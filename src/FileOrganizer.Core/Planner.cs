namespace FileOrganizer.Core;

public sealed class Planner
{
    public ScanPlan Scan(FolderSpec folder, IReadOnlyList<string> allDestinations,
        IReadOnlySet<Guid>? ruleIds = null, CancellationToken cancellationToken = default)
    {
        var sink = new MemorySink();
        ScanInto(folder, allDestinations, sink, ruleIds, cancellationToken);
        return new(folder.Id, folder.Name, DateTime.UtcNow, sink.Entries, sink.Warnings);
    }

    public DiskPlanStore ScanToDisk(FolderSpec folder, IReadOnlyList<string> allDestinations, string cacheRoot,
        IReadOnlySet<Guid>? ruleIds = null, CancellationToken cancellationToken = default)
    {
        var store = new DiskPlanStore(cacheRoot, folder);
        try
        {
            if (PathSafety.IsWithin(folder.Path, store.CacheRoot))
                throw new ArgumentException("검사 폴더는 임시 결과 저장소 안에 둘 수 없습니다.");
            ScanInto(folder, allDestinations.Append(store.CacheRoot).ToArray(), store, ruleIds, cancellationToken);
            store.CompleteScan();
            return store;
        }
        catch { store.Dispose(); throw; }
    }

    private static void ScanInto(FolderSpec folder, IReadOnlyList<string> allDestinations, IScanSink sink,
        IReadOnlySet<Guid>? ruleIds, CancellationToken cancellationToken)
    {
        var source = PathSafety.Normalize(folder.Path);
        PathSafety.AssertNoLinks(source);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("검사 폴더가 없거나 드라이브가 연결되지 않았습니다.");
        var rules = folder.Rules.Where(r => r.Enabled && (ruleIds is null || ruleIds.Contains(r.Id))).ToArray();
        foreach (var rule in rules) PathSafety.ValidateRule(folder, rule);
        var excluded = folder.ExcludedFolders.Concat(allDestinations.Where(p =>
            PathSafety.IsWithin(p, source) && !PathSafety.Comparer.Equals(PathSafety.Normalize(p), source)))
            .Select(PathSafety.Normalize).ToArray();
        if (excluded.Any(p => PathSafety.IsWithin(source, p)))
            throw new ArgumentException("검사 폴더가 제외 폴더 또는 이동 폴더 안에 있습니다. 폴더 설정을 확인하세요.");
        Walk(source, 0);

        void Walk(string directory, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var paths = sink.Paths(directory, cancellationToken).GetEnumerator();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path;
                try { if (!paths.MoveNext()) break; path = paths.Current; }
                catch (Exception ex) when (ex is not ScanStorageException && (ex is IOException or UnauthorizedAccessException))
                { sink.AddWarning($"검사 생략: {directory} — {ex.Message}"); break; }
                var baseName = System.IO.Path.GetFileName(path);
                if (baseName.StartsWith("._", StringComparison.Ordinal) || baseName == ".DS_Store") continue;
                if (excluded.Any(p => PathSafety.IsWithin(path, p))) continue;
                try
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    { sink.AddWarning($"링크 제외: {path}"); continue; }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (rules.Any(r => r.Recursive)) Walk(path, depth + 1);
                        continue;
                    }
                    if (System.IO.Path.GetFileName(path).StartsWith(".fileorganizer-", StringComparison.Ordinal)) continue;
                    var rule = rules.FirstOrDefault(r => RuleMatcher.Matches(r, path, depth));
                    if (rule is null) continue;
                    sink.AddEntry(PlanFile(path, rule, sink));
                }
                catch (Exception ex) when (ex is not ScanStorageException and not FilenamePatternTimeoutException && (ex is IOException or UnauthorizedAccessException or ArgumentException))
                { sink.AddWarning($"검사 생략: {path} — {ex.Message}"); }
            }
        }
    }

    internal static PlanEntry PlanFile(string path, RuleSpec rule, IScanSink sink, FileStamp? expected = null)
    {
        var stamp = FileStamp.Read(path);
        if (expected is not null && stamp != expected) throw new IOException("분석 이후 원본이 변경되었습니다. 다시 분석하세요.");
        var destination = DestinationPaths.Resolve(rule);
        var target = System.IO.Path.Combine(destination, System.IO.Path.GetFileName(path));
        var action = PlanAction.Move;
        var reason = "파일명·확장자 조건 일치";
        FileStamp? targetStamp = null;
        if (PathSafety.Comparer.Equals(path, target))
            return Entry(PlanAction.Skip, "이미 이동 위치에 있습니다.");
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return Entry(PlanAction.Blocked, "파일이 사용 중이거나 읽기 권한이 없습니다. " + ex.Message); }
        if (!DestinationPaths.CreatesDirectory(rule) && !Directory.Exists(destination))
            return Entry(PlanAction.Blocked, "이동 폴더가 없습니다. 자동 생성이 꺼져 있습니다.");
        if (Directory.Exists(target)) return Entry(PlanAction.Blocked, "대상 위치에 같은 이름의 폴더가 있습니다.");
        if (File.Exists(target) || sink.IsReserved(target))
        {
            if (rule.Conflict == ConflictMode.Skip) return Entry(PlanAction.Skip, "같은 이름의 파일이 있어 건너뜁니다.");
            if (rule.Conflict == ConflictMode.Replace)
            {
                if (sink.IsReserved(target)) return Entry(PlanAction.Blocked, "여러 파일의 이동 위치가 같습니다. 이름 변경 옵션을 사용하세요.");
                PathSafety.AssertNoLinks(target);
                targetStamp = FileStamp.Read(target);
                action = PlanAction.Replace;
                reason = "같은 이름의 기존 파일을 휴지통으로 옮긴 뒤 교체";
            }
            else
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(target);
                var extension = System.IO.Path.GetExtension(target);
                for (var number = 1; File.Exists(target) || Directory.Exists(target) || sink.IsReserved(target); number++)
                    target = System.IO.Path.Combine(destination, $"{name} ({number}){extension}");
                action = PlanAction.Rename;
                reason = "이름 충돌을 피하도록 번호 추가";
            }
        }
        sink.Reserve(target);
        return Entry(action, reason);

        PlanEntry Entry(PlanAction kind, string explanation) => new(Guid.NewGuid(), path, target,
            rule.Name, kind, explanation, stamp, targetStamp, DestinationPaths.CreatesDirectory(rule));
    }

    private sealed class MemorySink : IScanSink
    {
        public List<PlanEntry> Entries { get; } = [];
        public List<string> Warnings { get; } = [];
        private readonly HashSet<string> reserved = new(PathSafety.Comparer);
        public void AddEntry(PlanEntry entry) => Entries.Add(entry);
        public void AddWarning(string message) => Warnings.Add(message);
        public bool IsReserved(string path) => reserved.Contains(path);
        public void Reserve(string path) => reserved.Add(path);
        public IEnumerable<string> Paths(string directory, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var paths = Directory.GetFileSystemEntries(directory);
            Array.Sort(paths, PathSafety.Comparer);
            foreach (var path in paths) yield return path;
        }
    }

}
