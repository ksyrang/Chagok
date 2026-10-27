using Chagok.Core;

static class DataMigrationChecks
{
    public static void Register(Action<string, Action> add)
    {
        void Check(bool value) { if (!value) throw new Exception("데이터 가져오기 검사 실패"); }
        void Throws<T>(Action action) where T : Exception
        { try { action(); } catch (T) { return; } throw new Exception(typeof(T).Name + " 예외 없음"); }

        add("데이터 가져오기 설정·ID·기록·백업 원문 보존과 캐시 제외", () =>
        {
            using var f = new Fixture(); var old = Path.Combine(f.Root, "FileOrganizer"); var next = Path.Combine(f.Root, "Chagok");
            var storage = new Storage(old); var settings = new AppSettings { Theme = ThemeMode.Light, Folders = [f.Folder] };
            storage.Save(settings); settings.Theme = ThemeMode.Dark; storage.Save(settings);
            storage.Append(new(DateTime.UtcNow, "폴더", "규칙", "원본", "대상", "이동", "완료", ""));
            File.AppendAllText(storage.HistoryPath, "{미완성 마지막 줄");
            Directory.CreateDirectory(Path.Combine(old, "scan-cache")); File.WriteAllText(Path.Combine(old, "scan-cache", "plan.db"), "캐시");
            Directory.CreateDirectory(Path.Combine(old, "shell-extension")); File.WriteAllText(Path.Combine(old, "unknown.txt"), "유지");
            var originals = new[] { "settings.json", "settings.json.backup", "history.jsonl" }
                .ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(old, name)));
            using var lease = DataMigration.AcquireLegacy(old, next);
            Check(lease is not null);
            foreach (var (name, bytes) in originals)
                Check(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(next, name))) && bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(old, name))));
            var imported = new Storage(next); var loaded = imported.Load();
            Check(loaded.Theme == ThemeMode.Dark && loaded.Folders[0].Id == f.Folder.Id && loaded.Folders[0].Rules[0].Id == f.Rule.Id);
            Check(imported.ReadHistory().Single().Status == "완료" && File.Exists(Path.Combine(next, "legacy-import.json")));
            Check(!Directory.Exists(Path.Combine(next, "scan-cache")) && !Directory.Exists(Path.Combine(next, "shell-extension")));
            Check(!File.Exists(Path.Combine(next, "unknown.txt")) && File.Exists(Path.Combine(old, "unknown.txt")));
        });
        add("데이터 가져오기 새 데이터가 있으면 다시 덮어쓰지 않음", () =>
        {
            using var f = new Fixture(); var old = Path.Combine(f.Root, "FileOrganizer"); var next = Path.Combine(f.Root, "Chagok");
            new Storage(old).Save(new() { Theme = ThemeMode.Light });
            using (DataMigration.AcquireLegacy(old, next)) { }
            var storage = new Storage(next); storage.Save(new() { Theme = ThemeMode.Dark });
            var before = File.ReadAllBytes(storage.SettingsPath);
            File.WriteAllText(Path.Combine(old, "settings.json"), "이후 손상된 구버전 설정");
            using (DataMigration.AcquireLegacy(old, next)) { }
            Check(before.SequenceEqual(File.ReadAllBytes(storage.SettingsPath)) && storage.Load().Theme == ThemeMode.Dark);
        });
        add("데이터 가져오기 미리 만든 빈 폴더에는 가져오고 관련 없는 파일은 보존", () =>
        {
            using var f = new Fixture(); var old = Path.Combine(f.Root, "FileOrganizer"); var next = Path.Combine(f.Root, "Chagok");
            new Storage(old).Save(new() { Theme = ThemeMode.Dark }); Directory.CreateDirectory(next);
            File.WriteAllText(Path.Combine(next, "Chagok.exe"), "기존 파일");
            Throws<IOException>(() => DataMigration.AcquireLegacy(old, next));
            Check(File.ReadAllText(Path.Combine(next, "Chagok.exe")) == "기존 파일" && !File.Exists(Path.Combine(next, "settings.json")));
            File.Delete(Path.Combine(next, "Chagok.exe"));
            using var lease = DataMigration.AcquireLegacy(old, next);
            Check(new Storage(next).Load().Theme == ThemeMode.Dark);
        });
        add("데이터 가져오기 손상된 설정은 중단하고 복구 후 재시도 가능", () =>
        {
            using var f = new Fixture(); var old = Path.Combine(f.Root, "FileOrganizer"); var next = Path.Combine(f.Root, "Chagok");
            Directory.CreateDirectory(old); var source = Path.Combine(old, "settings.json"); File.WriteAllText(source, "{손상");
            Throws<IOException>(() => DataMigration.AcquireLegacy(old, next));
            Check(!Directory.Exists(next) && File.ReadAllText(source) == "{손상");
            Check(!Directory.EnumerateDirectories(f.Root, ".chagok-import-*").Any());
            File.WriteAllText(source, "{\"Version\":2}");
            Throws<IOException>(() => DataMigration.AcquireLegacy(old, next)); Check(!Directory.Exists(next));
            new Storage(old).Save(new() { Theme = ThemeMode.Dark });
            using var lease = DataMigration.AcquireLegacy(old, next);
            Check(new Storage(next).Load().Theme == ThemeMode.Dark);
        });
        add("데이터 가져오기 복사 도중 오류가 나도 일부 데이터로 시작하지 않음", () =>
        {
            using var f = new Fixture(); var old = Path.Combine(f.Root, "FileOrganizer"); var next = Path.Combine(f.Root, "Chagok");
            var storage = new Storage(old); storage.Save(new()); var before = File.ReadAllBytes(storage.SettingsPath);
            Directory.CreateDirectory(storage.HistoryPath);
            Throws<IOException>(() => DataMigration.AcquireLegacy(old, next));
            Check(!Directory.Exists(next) && before.SequenceEqual(File.ReadAllBytes(storage.SettingsPath)));
            Check(!Directory.EnumerateDirectories(f.Root, ".chagok-import-*").Any());
        });
        add("데이터 가져오기 실행 중 구버전 잠금 차단과 종료 뒤 해제", () =>
        {
            using var f = new Fixture(); var old = Path.Combine(f.Root, "FileOrganizer"); var next = Path.Combine(f.Root, "Chagok");
            new Storage(old).Save(new());
            using (var busy = SessionLease.Acquire(old, "old"))
            {
                Throws<SessionBusyException>(() => DataMigration.AcquireLegacy(old, next)); Check(!Directory.Exists(next));
            }
            using (var imported = DataMigration.AcquireLegacy(old, next))
            {
                Throws<SessionBusyException>(() => SessionLease.Acquire(old, "old"));
                Throws<SessionBusyException>(() => DataMigration.AcquireLegacy(old, next));
            }
            using (var restarted = DataMigration.AcquireLegacy(old, next))
                Throws<SessionBusyException>(() => SessionLease.Acquire(old, "old"));
            using var released = SessionLease.Acquire(old, "old");
        });
        add("데이터 가져오기 원본 없음·겹친 경로·링크를 안전하게 처리", () =>
        {
            using var f = new Fixture(); var old = Path.Combine(f.Root, "FileOrganizer"); var next = Path.Combine(f.Root, "Chagok");
            Check(DataMigration.AcquireLegacy(old, next) is null && !Directory.Exists(next) && !Directory.Exists(old));
            Throws<ArgumentException>(() => DataMigration.AcquireLegacy(old, old));
            Throws<ArgumentException>(() => DataMigration.AcquireLegacy(old, Path.Combine(old, "nested")));
            if (OperatingSystem.IsWindows()) return;
            Directory.CreateDirectory(old); var original = Path.Combine(f.Root, "original.json"); File.WriteAllText(original, "{\"Version\":1}");
            File.CreateSymbolicLink(Path.Combine(old, "settings.json"), original);
            Throws<IOException>(() => DataMigration.AcquireLegacy(old, next));
            Check(!Directory.Exists(next) && File.ReadAllText(original) == "{\"Version\":1}");
        });
        add("데이터 가져오기 완료 표식은 설정 삭제 후 구버전 재가져오기를 방지", () =>
        {
            using var f = new Fixture(); var old = Path.Combine(f.Root, "FileOrganizer"); var next = Path.Combine(f.Root, "Chagok");
            new Storage(old).Save(new() { Theme = ThemeMode.Dark });
            using (DataMigration.AcquireLegacy(old, next)) { }
            File.Delete(Path.Combine(next, "settings.json"));
            using (DataMigration.AcquireLegacy(old, next)) { }
            Check(!File.Exists(Path.Combine(next, "settings.json")) && new Storage(next).Load().Theme == ThemeMode.System);
        });
        add("데이터 보호는 새 위치와 보존한 구버전 위치를 검사·분석에서 차단", () =>
        {
            using var f = new Fixture(); var storage = new Storage(Path.Combine(f.Root, "state"));
            Check(Path.GetFileName(SessionLease.DefaultDirectory) == "Chagok");
            foreach (var directory in storage.ProtectedDirectories)
            {
                Check(storage.IsDataPath(directory) && storage.IsDataPath(Path.Combine(directory, "nested")));
                var folder = new FolderSpec { Path = directory };
                var session = new Chagok.Terminal.TerminalSession(storage, new() { Folders = [folder] });
                Throws<ArgumentException>(() => session.Scan(folder, null, CancellationToken.None));
                Throws<ArgumentException>(() => session.Analyze(new(), CancellationToken.None));
            }
            Check(!storage.IsDataPath(f.Source));
        });
    }
}
