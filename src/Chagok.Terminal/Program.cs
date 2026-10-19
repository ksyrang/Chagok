using System.Text;
using System.Text.Json;
using FileOrganizer.Core;
using FileOrganizer.Windows;

namespace Chagok.Terminal;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        var json = args.Contains("--json");
        try
        {
            var parsed = Arguments.Parse(args);
            if (parsed.Words.FirstOrDefault() == "help")
            {
                parsed.Allow();
                var topic = string.Join(' ', parsed.Words.Skip(1));
                Console.WriteLine(parsed.Json ? HelpCatalog.Serialize(topic) : HelpCatalog.Text(topic));
                return 0;
            }
            if (parsed.Words.SequenceEqual(["version"])) { parsed.Allow(); Console.WriteLine(parsed.Json ? JsonSerializer.Serialize(new { program = "Chagok", version = HelpCatalog.Version }) : "Chagok " + HelpCatalog.Version); return 0; }
            var directory = PathSafety.Normalize(parsed.Get("data-dir") ?? SessionLease.DefaultDirectory);
            using var lease = SessionLease.Acquire(directory, parsed.Words.Count == 0 || parsed.Words[0] == "tui" ? "TUI" : "CLI");
            var storage = new Storage(directory);
            var session = new TerminalSession(storage, storage.Load());
            IRecycleBin recycle = OperatingSystem.IsWindows() ? new WindowsRecycleBin() : new UnavailableRecycleBin();
            return await new Commands(session, recycle, cancellation.Token).RunAsync(parsed);
        }
        catch (OperationCanceledException) { return Error("작업을 취소했습니다. 이미 완료한 이동은 유지됩니다.", 130); }
        catch (SessionBusyException ex) { return Error(ex.Message, 3); }
        catch (ArgumentException ex) { return Error(ex.Message, 2); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or PlatformNotSupportedException
            or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        { return Error(ex.Message, 1); }
        finally { Console.CancelKeyPress -= cancel; }

        int Error(string message, int code)
        {
            if (json) Console.Error.WriteLine(JsonSerializer.Serialize(new { error = message, exitCode = code }));
            else Console.Error.WriteLine("차곡: " + TerminalText.Clean(message));
            return code;
        }
    }
    private sealed class UnavailableRecycleBin : IRecycleBin
    {
        public Task RecycleAsync(string path, CancellationToken cancellationToken)
            => throw new IOException("이 환경에서는 Windows 휴지통 교체를 지원하지 않습니다. 기존 파일은 삭제하지 않았습니다.");
    }
}
