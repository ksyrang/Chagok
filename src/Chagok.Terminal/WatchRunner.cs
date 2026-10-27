using System.Collections.Concurrent;
using System.Text.Json;
using Chagok.Core;
using Chagok.Windows;

namespace Chagok.Terminal;

internal static class WatchRunner
{
    public static async Task RunAsync(TerminalSession session, IRecycleBin recycle, CancellationToken token,
        Guid? folder, bool json, bool tui)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("프로그램 감지는 Windows에서 지원합니다.");
        await RunWindowsAsync(session, recycle, token, folder, json, tui);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task RunWindowsAsync(TerminalSession session, IRecycleBin recycle, CancellationToken token,
        Guid? folder, bool json, bool tui)
    {
        var settings = Storage.Clone(session.Settings);
        if (folder is { } id) settings.Folders.RemoveAll(f => f.Id != id);
        var paths = TriggerPlanner.ProgramPaths(settings);
        if (paths.Count == 0) throw new ArgumentException("켜진 규칙이 있는 폴더에 프로그램 조건을 먼저 설정하세요.");
        var signals = new ConcurrentQueue<ProgramSignal>();
        var notices = new ConcurrentQueue<(string Status, string Message)>();
        var pending = new List<TriggerBatch>();
        var gate = new object();
        var dropped = 0;
        using var monitor = new ProgramMonitor();
        monitor.Signaled += signal => { lock (gate) { if (signals.Count < 100) signals.Enqueue(signal); else Interlocked.Increment(ref dropped); } };
        monitor.FallbackStarted += message => { lock (gate) { if (notices.Count < 20) notices.Enqueue(("대체 감지", message)); } };
        monitor.Failed += message => { lock (gate) { if (notices.Count < 20) notices.Enqueue(("실패", message)); } };
        monitor.PathMismatch += message => { lock (gate) { if (notices.Count < 20) notices.Enqueue(("경로 확인", message)); } };
        monitor.Configure(paths);
        if (tui) TerminalText.Header("프로그램 감지");
        Emit(new { status = "watching", programs = paths, message = json
            ? "Ctrl+C: 종료. 확인 요청은 이벤트로만 표시하며 자동 조건만 실행합니다."
            : "Enter: 첫 확인 요청 검사 / D: 첫 요청 무시 / Q: 감지 종료 / Ctrl+C: 앱 종료. 자동 실행은 저장된 설정을 따릅니다." });
        while (!token.IsCancellationRequested)
        {
            while (notices.TryDequeue(out var notice))
            {
                session.Storage.Append(new(DateTime.UtcNow, "", "프로그램 감지", "", "", "감지", notice.Status, notice.Message));
                Emit(new { status = notice.Status, message = notice.Message });
            }
            while (signals.TryDequeue(out var signal))
            {
                foreach (var batch in TriggerPlanner.Build(settings, signal.Path, signal.Trigger))
                {
                    if (pending.Any(p => p.FolderId == batch.FolderId && p.Automatic == batch.Automatic && p.RuleIds.SetEquals(batch.RuleIds))) continue;
                    if (pending.Count < 100) pending.Add(batch);
                    else { Interlocked.Increment(ref dropped); break; }
                }
                session.Storage.Append(new(DateTime.UtcNow, "", "프로그램 감지", signal.Path, "", "감지", "감지됨", $"터미널 {signal.Trigger} · 대기 {pending.Count}개"));
                Emit(new { status = "detected", path = signal.Path, trigger = signal.Trigger.ToString(), pending = pending.Count });
            }
            var lost = Interlocked.Exchange(ref dropped, 0);
            if (lost > 0) Emit(new { status = "queue-full", dropped = lost, message = "대기 요청이 많아 일부를 생략했습니다." });
            if (pending.Count > 0 && pending[0].Automatic)
            {
                var request = pending[0]; pending.RemoveAt(0);
                var source = session.Folder(request.FolderId.ToString());
                using var plan = await Task.Run(() => session.Scan(source, request.RuleIds, token), token);
                if (plan.SelectedCount == 0)
                {
                    Emit(new { status = "no-executable-items", total = plan.Count, warnings = plan.WarningCount });
                    continue;
                }
                var report = await session.ExecuteAsync(plan, recycle, json || Console.IsOutputRedirected ? null : Commands.Progress, token);
                if (!json && !Console.IsOutputRedirected) Console.WriteLine();
                Emit(new { status = "finished", report.Title, report.CountsText, outcome = report.Outcome.ToString(), report.Error, warnings = plan.WarningCount });
                continue;
            }
            if (!json && !Console.IsInputRedirected && !Console.IsOutputRedirected && Console.KeyAvailable)
            {
                var key = Console.ReadKey(true).Key;
                if (key is ConsoleKey.Q or ConsoleKey.Escape) return;
                if (key == ConsoleKey.D && pending.Count > 0) { pending.RemoveAt(0); Emit(new { status = "dismissed", pending = pending.Count }); }
                if (key == ConsoleKey.Enter && pending.Count > 0)
                {
                    var request = pending[0]; pending.RemoveAt(0);
                    await Tui.PreviewAsync(session, recycle, session.Folder(request.FolderId.ToString()), request.RuleIds, token);
                    if (tui) TerminalText.Header("프로그램 감지 계속");
                    Emit(new { status = "watching", pending = pending.Count });
                }
            }
            await Task.Delay(50, token);
        }

        void Emit(object value) => Console.WriteLine(JsonSerializer.Serialize(value, json ? null : new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}
