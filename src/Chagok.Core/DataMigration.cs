using System.Text.Json;

namespace Chagok.Core;

public static class DataMigration
{
    private static readonly string[] DataFiles = ["settings.json", "settings.json.backup", "history.jsonl"];
    private static bool HasData(string directory) => DataFiles.Append("legacy-import.json")
        .Any(name => File.Exists(Path.Combine(directory, name)) || Directory.Exists(Path.Combine(directory, name)));
    // 반환한 잠금은 새 앱이 실행되는 동안 보유해 다른 로그인 세션의 구버전도 차단한다.
    public static FileStream? AcquireLegacy(string legacyDirectory, string directory)
    {
        legacyDirectory = PathSafety.Normalize(legacyDirectory);
        directory = PathSafety.Normalize(directory);
        if (PathSafety.IsWithin(directory, legacyDirectory) || PathSafety.IsWithin(legacyDirectory, directory))
            throw new ArgumentException("기존 데이터 폴더와 새 데이터 폴더는 서로 겹칠 수 없습니다.");
        PathSafety.AssertNoLinks(legacyDirectory);
        PathSafety.AssertNoLinks(directory);
        if (File.Exists(legacyDirectory)) throw new IOException("기존 데이터 폴더 위치에 파일이 있습니다. 기존 파일은 보존했습니다.");
        if (!Directory.Exists(legacyDirectory)) return null;
        var lockPath = Path.Combine(legacyDirectory, "session.lock");
        PathSafety.AssertNoLinks(lockPath);
        FileStream lease;
        try { lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new SessionBusyException("기존 차곡 데이터가 사용 중이거나 잠금을 열 수 없습니다. 구버전 앱을 완전히 종료한 뒤 다시 실행하세요."); }
        try
        {
            if (!Directory.Exists(directory) || !Directory.EnumerateFileSystemEntries(directory).Any())
                Import(legacyDirectory, directory);
            else if (!HasData(directory) && HasData(legacyDirectory))
                throw new IOException("새 데이터 폴더에 다른 파일이 있어 자동 가져오기를 중단했습니다. 기존 데이터는 보존했습니다. 새 데이터 폴더를 확인하세요: " + directory);
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    private static void Import(string legacyDirectory, string directory)
    {
        var parent = Path.GetDirectoryName(directory)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".chagok-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var name in DataFiles)
            {
                var source = Path.Combine(legacyDirectory, name);
                PathSafety.AssertNoLinks(source);
                if (Directory.Exists(source)) throw new IOException("가져올 데이터 파일 위치에 폴더가 있습니다: " + name);
                if (!File.Exists(source)) continue;
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var output = new FileStream(Path.Combine(staging, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output); output.Flush(flushToDisk: true);
            }
            _ = new Storage(staging).Load();
            File.WriteAllText(Path.Combine(staging, "legacy-import.json"), "{\"version\":1,\"source\":\"FileOrganizer\"}");
            // 전체 복사·설정 검증이 끝난 뒤 새 위치를 공개한다. 기존 폴더는 삭제하지 않는다.
            if (Directory.Exists(directory)) Directory.Delete(directory); // 미리 만들어 둔 빈 폴더만 제거한다.
            Directory.Move(staging, directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            throw new IOException("기존 설정·기록을 가져오지 못했습니다. 원본은 보존했습니다. 문제를 해결한 뒤 다시 실행하세요.\n" + ex.Message, ex);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 원래 오류를 보존한다. */ }
        }
    }
}
