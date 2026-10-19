using System.Text;

namespace FileOrganizer.Core;

public sealed class SessionBusyException(string message) : IOException(message);

// 창·명령·대화형 터미널 전체 수명 동안 보유한다. 설정을 읽기 전에 획득한다.
public sealed class SessionLease : IDisposable
{
    public const string LegacyMutexName = @"Local\FileOrganizer-15193BE9-9D6D-4D58-9A90-23910F1544AE";
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileOrganizer");
    private readonly FileStream stream;
    private readonly Mutex? legacy;
    private SessionLease(FileStream stream, Mutex? legacy) { this.stream = stream; this.legacy = legacy; }

    public static SessionLease Acquire(string directory, string client)
    {
        Mutex? legacy = null;
        FileStream? stream = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // 구 GUI도 같은 이름의 개체가 이미 존재하면 실행을 거부한다.
                legacy = new Mutex(false, LegacyMutexName, out var created);
                if (!created) throw new SessionBusyException("차곡 GUI·CLI·TUI가 이미 실행 중입니다. GUI가 트레이에 있다면 ‘종료’한 뒤 다시 실행하세요.");
            }
            directory = PathSafety.Normalize(directory);
            PathSafety.AssertNoLinks(directory);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "session.lock");
            PathSafety.AssertNoLinks(path);
            try { stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new SessionBusyException("이 설정을 다른 차곡 GUI·CLI·TUI가 사용 중이거나 잠금 파일을 열 수 없습니다. 기존 앱을 완전히 종료한 뒤 다시 실행하세요."); }
            stream.SetLength(0);
            var data = Encoding.UTF8.GetBytes($"{client} · PID {Environment.ProcessId} · {DateTime.UtcNow:O}");
            stream.Write(data); stream.Flush(flushToDisk: true);
            return new(stream, legacy);
        }
        catch { stream?.Dispose(); legacy?.Dispose(); throw; }
    }

    public void Dispose() { stream.Dispose(); legacy?.Dispose(); }
}
