using System.Diagnostics;
using System.Management;
using Chagok.Core;

namespace Chagok.Windows;

internal sealed record ProgramSignal(string Path, TriggerMode Trigger);

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class ProgramMonitor : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<int, string> active = [];
    private readonly HashSet<string> targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> reportedPaths = new(StringComparer.OrdinalIgnoreCase);
    private ManagementEventWatcher? started;
    private ManagementEventWatcher? stopped;
    private CancellationTokenSource? polling;
    private bool disposed;
    public event Action<ProgramSignal>? Signaled;
    public event Action<string>? Failed;
    public event Action<string>? FallbackStarted;
    public event Action<string>? PathMismatch;

    public void Configure(IEnumerable<string> programPaths)
    {
        Stop();
        var paths = programPaths.Select(PathSafety.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) return;
        var stage = "감지 초기화";
        try
        {
            lock (gate)
            {
                disposed = false; targets.UnionWith(paths);
                using var current = Process.GetCurrentProcess();
                var session = current.SessionId;
                started = new ManagementEventWatcher(new WqlEventQuery($"SELECT * FROM Win32_ProcessStartTrace WHERE SessionID = {session}"));
                stopped = new ManagementEventWatcher(new WqlEventQuery($"SELECT * FROM Win32_ProcessStopTrace WHERE SessionID = {session}"));
                started.EventArrived += OnStarted; stopped.EventArrived += OnStopped;
                stage = "시작 이벤트 등록"; started.Start();
                stage = "종료 이벤트 등록"; stopped.Start();
                var snapshot = ProcessReader.Snapshot(paths, session);
                ReportOtherPaths(snapshot.Processes);
                foreach (var process in snapshot.Processes)
                    if (process.ExecutablePath is { } path && targets.Contains(path)) active[process.ProcessId] = path;
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            Stop();
            var cancellation = new CancellationTokenSource();
            using var current = Process.GetCurrentProcess();
            var session = current.SessionId;
            var tracker = new ProcessPresenceTracker(paths);
            var initial = ProcessReader.Snapshot(paths, session);
            tracker.Observe(initial.Processes, initial.Complete);
            lock (gate) { disposed = false; targets.UnionWith(paths); polling = cancellation; }
            ReportOtherPaths(initial.Processes);
            FallbackStarted?.Invoke($"{stage} 거부 또는 오류({ex.HResult:X8}): {ex.Message} — 250ms 간격의 프로그램 확인으로 전환했습니다. 표본 사이에 끝나는 짧은 실행은 놓칠 수 있습니다.");
            _ = Task.Run(() => PollAsync(paths, session, tracker, cancellation));
        }
    }

    private async Task PollAsync(string[] paths, int session, ProcessPresenceTracker tracker, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            var unreadableCount = 0; var reported = false;
            while (await timer.WaitForNextTickAsync(token))
            {
                var snapshot = ProcessReader.Snapshot(paths, session);
                var transitions = tracker.Observe(snapshot.Processes, snapshot.Complete);
                lock (gate)
                {
                    if (disposed || !ReferenceEquals(polling, cancellation)) return;
                    ReportOtherPaths(snapshot.Processes);
                    foreach (var transition in transitions) Signaled?.Invoke(new(transition.Path, transition.Trigger));
                    unreadableCount = snapshot.Unreadable || !snapshot.Complete ? unreadableCount + 1 : 0;
                    if (unreadableCount >= 12 && !reported)
                    {
                        reported = true;
                        Failed?.Invoke("일부 프로그램 경로를 읽지 못해 해당 감지를 보류합니다. 경로를 확인할 수 없는 상태를 종료로 판단하지 않습니다.");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        { if (!token.IsCancellationRequested) Failed?.Invoke("대체 프로그램 감지가 중단되었습니다. " + ex.Message); }
        finally
        {
            lock (gate)
            { if (ReferenceEquals(polling, cancellation)) polling = null; cancellation.Dispose(); }
        }
    }

    private void OnStarted(object sender, EventArrivedEventArgs args)
    {
        try
        {
            var pid = Convert.ToInt32(args.NewEvent["ProcessID"]);
            var name = Convert.ToString(args.NewEvent["ProcessName"]) ?? "";
            lock (gate)
            {
                if (disposed || !ReferenceEquals(sender, started)
                    || !targets.Any(p => string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase))) return;
            }
            var path = ProcessReader.ReadPath(pid);
            if (string.IsNullOrWhiteSpace(path))
            { Failed?.Invoke($"실행 경로를 확인할 수 없어 감지를 생략했습니다: {name}"); return; }
            bool first;
            lock (gate)
            {
                if (disposed || !ReferenceEquals(sender, started)) return;
                ReportOtherPaths([new(pid, path)]);
                if (!targets.Contains(path) || active.ContainsKey(pid)) return;
                first = !active.Values.Contains(path, StringComparer.OrdinalIgnoreCase); active[pid] = path;
            }
            if (first) Signaled?.Invoke(new(path, TriggerMode.ProgramStarted));
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { Failed?.Invoke("프로그램 시작 정보를 읽지 못했습니다. " + ex.Message); }
    }

    private void OnStopped(object sender, EventArrivedEventArgs args)
    {
        var pid = Convert.ToInt32(args.NewEvent["ProcessID"]);
        string? path;
        lock (gate)
        {
            if (disposed || !ReferenceEquals(sender, stopped) || !active.Remove(pid, out path)) return;
            if (active.Values.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        }
        Signaled?.Invoke(new(path, TriggerMode.ProgramStopped));
    }

    private void ReportOtherPaths(IEnumerable<ProcessObservation> observations)
    {
        lock (gate)
        {
            if (disposed) return;
            foreach (var observation in observations)
            {
                if (observation.ExecutablePath is not { } path || targets.Contains(path) || reportedPaths.Count >= 16) continue;
                var configured = targets.Where(p => string.Equals(Path.GetFileName(p), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)).ToArray();
                if (configured.Length == 0 || !reportedPaths.Add(path)) continue;
                PathMismatch?.Invoke("같은 파일명으로 실행됐지만 등록 경로와 달라 감지 대상에서 제외했습니다. 등록: "
                    + string.Join(", ", configured) + " / 실제 실행: " + path);
            }
        }
    }

    private void Stop()
    {
        ManagementEventWatcher? start, stop;
        CancellationTokenSource? cancellation;
        lock (gate)
        {
            disposed = true; start = started; stop = stopped; cancellation = polling;
            started = null; stopped = null; polling = null; active.Clear(); targets.Clear(); reportedPaths.Clear();
            cancellation?.Cancel();
        }
        foreach (var watcher in new[] { start, stop })
        {
            if (watcher is null) continue;
            try { watcher.Stop(); }
            catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { }
            watcher.Dispose();
        }
    }

    public void Dispose() => Stop();
}
