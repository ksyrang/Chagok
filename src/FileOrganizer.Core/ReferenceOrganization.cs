using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace FileOrganizer.Core;

public enum ReferenceMatch { Contains, Group }
public enum ReferenceFilter { All, Decided, Ambiguous, Unmatched }
public sealed record ReferenceTarget(string Path, string Keywords, ReferenceMatch Match = ReferenceMatch.Contains);
public sealed record ReferenceCandidate(string Destination, string Reason);
public sealed record ReferenceFile(long Id, string Path, string Destination, string Reason, int Candidates)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Status => Destination.Length > 0 ? "목적지 결정" : Candidates > 1 ? "선택 필요" : "목적지 없음";
}
public sealed record ReferenceGroup(string Key, long Count);

// 결과·후보·그룹·선택을 디스크에 보관한다. UI와 작업 스레드는 번갈아 접근한다.
public sealed class ReferenceOrganization : IDisposable
{
    private readonly DiskPlanStore scratch;
    private readonly SqliteConnection db;
    private SqliteTransaction? transaction;
    private int writes;
    private bool disposed;
    private readonly FolderSpec folder;
    public string Root { get; }
    public AnalysisOptions GroupOptions { get; }
    public long WarningCount => scratch.WarningCount;
    public IReadOnlyList<string> Warnings => scratch.ReadWarnings();

    private ReferenceOrganization(FolderSpec folder, string root, string cacheRoot, AnalysisOptions options)
    {
        this.folder = Storage.Clone(folder); Root = root; GroupOptions = options;
        scratch = new(cacheRoot, folder);
        try
        {
            db = new(new SqliteConnectionStringBuilder { DataSource = Path.Combine(scratch.DirectoryPath, "analysis.db"), Pooling = false }.ToString());
            db.Open(); db.CreateCollation("PATH", PathSafety.Comparer.Compare);
            Execute("""
                PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-2048; PRAGMA temp_store=FILE;
                CREATE TABLE files(id INTEGER PRIMARY KEY,path TEXT COLLATE PATH UNIQUE,stamp TEXT NOT NULL,destination TEXT NOT NULL DEFAULT '',reason TEXT NOT NULL DEFAULT '',candidates INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE candidates(file INTEGER,destination TEXT COLLATE PATH,reason TEXT,PRIMARY KEY(file,destination)) WITHOUT ROWID;
                CREATE TABLE groups(file INTEGER,key TEXT COLLATE BINARY,PRIMARY KEY(file,key)) WITHOUT ROWID;
                CREATE INDEX group_keys ON groups(key,file);
                CREATE TABLE pending(id INTEGER PRIMARY KEY,path TEXT);
                """);
        }
        catch { Dispose(); throw; }
    }
    private T Sql<T>(Func<T> action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try { return action(); }
        catch (SqliteException ex) { throw new ScanStorageException("기준 폴더 분석 저장소 오류: " + ex.Message, ex); }
        catch (JsonException ex) { throw new InvalidDataException("분석 항목이 손상되었습니다. 다시 분석하세요.", ex); }
    }
    private SqliteCommand Command(string sql, params (string Key, object Value)[] args)
    {
        var command = db.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var (key, value) in args) command.Parameters.AddWithValue(key, value);
        return command;
    }
    private int Execute(string sql, params (string Key, object Value)[] args) => Sql(() => { using var c = Command(sql, args); return c.ExecuteNonQuery(); });
    private void Write(string sql, params (string Key, object Value)[] args)
    {
        Sql(() => { transaction ??= db.BeginTransaction(); return 0; });
        Execute(sql, args); if (++writes >= 500) Flush();
    }
    private void Flush() => Sql(() => { transaction?.Commit(); transaction?.Dispose(); transaction = null; writes = 0; return 0; });
    private long Number(string sql, params (string Key, object Value)[] args) => Sql(() => { using var c = Command(sql, args); return Convert.ToInt64(c.ExecuteScalar()); });

    public static FolderSpec PrepareSource(FolderSpec? registered, string path, string protectedDirectory)
    {
        var source = PathSafety.Normalize(path.Trim()); PathSafety.AssertNoLinks(source);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("검사할 폴더가 없습니다.");
        if (PathSafety.IsWithin(source, protectedDirectory)) throw new ArgumentException("차곡 데이터 저장소는 검사 폴더로 사용할 수 없습니다.");
        if (registered is not null && PathSafety.Comparer.Equals(source, PathSafety.Normalize(registered.Path)))
            return Storage.Clone(registered);
        var name = Path.GetFileName(source);
        return new FolderSpec { Name = name.Length > 0 ? name : source, Path = source };
    }
    public static IReadOnlyList<ReferenceTarget> ReadTargets(string root, CancellationToken token = default)
    {
        root = PathSafety.Normalize(root); PathSafety.AssertNoLinks(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("기준 폴더가 없습니다.");
        var targets = new List<ReferenceTarget>();
        foreach (var path in Directory.EnumerateDirectories(root))
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
            targets.Add(new(path, Path.GetFileName(path)));
        }
        return targets.OrderBy(t => t.Path, PathSafety.Comparer).ToArray();
    }
    public static string NewDestination(string root, string name)
    {
        var rule = new RuleSpec { Name = name, Destination = root, AppendRuleName = true };
        // 운영체제에 관계없이 Windows 폴더명 제한을 적용한다.
        var result = DestinationPaths.Resolve(rule); PathSafety.AssertNoLinks(result); return result;
    }
    private static string[] Keywords(ReferenceTarget target)
    {
        if (!Enum.IsDefined(target.Match)) throw new ArgumentException("검색 방법을 확인하세요.");
        var words = target.Keywords.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (words.Length == 0) throw new ArgumentException("목적지마다 검색어를 한 개 이상 입력하세요.");
        foreach (var word in words)
        {
            if (word.Length > 2000) throw new ArgumentException("검색어는 2,000자 이하로 입력하세요.");
            if (target.Match == ReferenceMatch.Group) FilenameGroups.ValidateKey(word);
        }
        return words;
    }
    public static ReferenceOrganization Scan(FolderSpec folder, string root, IReadOnlyList<ReferenceTarget> targets,
        IReadOnlyList<string> exclusions, string cacheRoot, AnalysisOptions groupOptions,
        CancellationToken token = default, IProgress<long>? progress = null)
    {
        root = PathSafety.Normalize(root); var source = PathSafety.Normalize(folder.Path);
        PathSafety.AssertNoLinks(source); PathSafety.AssertNoLinks(root); FilenameGroups.Validate(groupOptions);
        if (!Directory.Exists(source) || !Directory.Exists(root)) throw new DirectoryNotFoundException("검사 폴더 또는 기준 폴더가 없습니다.");
        if (PathSafety.IsWithin(root, cacheRoot)) throw new ArgumentException("임시 저장소는 기준 폴더로 사용할 수 없습니다.");
        if (PathSafety.IsWithin(source, root) || PathSafety.IsWithin(source, cacheRoot))
            throw new ArgumentException("검사 폴더는 기준 폴더 또는 임시 저장소 안에 둘 수 없습니다.");
        if (targets.Count == 0) throw new ArgumentException("기준 폴더의 하위 폴더를 가져오세요.");
        var prepared = targets.Select(t => (Target: t with { Path = PathSafety.Normalize(t.Path) }, Words: Keywords(t))).ToArray();
        foreach (var (target, _) in prepared)
        {
            if (!PathSafety.Comparer.Equals(Path.GetDirectoryName(target.Path), root) || !Directory.Exists(target.Path))
                throw new ArgumentException("검색 목적지는 기준 폴더의 바로 아래에 있는 폴더여야 합니다.");
            PathSafety.AssertNoLinks(target.Path);
        }
        var excluded = folder.ExcludedFolders.Concat(exclusions.Where(p => PathSafety.IsWithin(p, source) && !PathSafety.Comparer.Equals(PathSafety.Normalize(p), source))).Append(root).Append(cacheRoot).Select(PathSafety.Normalize).Distinct(PathSafety.Comparer).ToArray();
        if (excluded.Any(e => PathSafety.IsWithin(source, e))) throw new ArgumentException("검사 폴더가 제외 경로 안에 있습니다.");
        ReferenceOrganization? result = null;
        try
        {
            result = new(folder, root, cacheRoot, groupOptions);
            result.Write("INSERT INTO pending(path) VALUES($p)", ("$p", source));
            long count = 0; var watch = Stopwatch.StartNew();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var next = result.NextDirectory(); if (next is null) break;
                try
                {
                    PathSafety.AssertNoLinks(next);
                    foreach (var path in result.scratch.Paths(next, token))
                    {
                        token.ThrowIfCancellationRequested(); var name = Path.GetFileName(path);
                        if (name.StartsWith("._", StringComparison.Ordinal) || name == ".DS_Store" || name.StartsWith(".fileorganizer-", StringComparison.Ordinal)) continue;
                        if (excluded.Any(e => PathSafety.IsWithin(path, e))) continue;
                        try
                        {
                            var attrs = File.GetAttributes(path);
                            if ((attrs & FileAttributes.ReparsePoint) != 0) { result.scratch.AddWarning("링크 제외: " + path); continue; }
                            if ((attrs & FileAttributes.Directory) != 0)
                            {
                                if (groupOptions.Recursive) result.Write("INSERT INTO pending(path) VALUES($p)", ("$p", path));
                                continue;
                            }
                            var stamp = FileStamp.Read(path);
                            result.Write("INSERT INTO files(path,stamp) VALUES($p,$s)", ("$p", path), ("$s", JsonSerializer.Serialize(stamp)));
                            var id = result.Number("SELECT id FROM files WHERE path=$p", ("$p", path));
                            foreach (var (target, words) in prepared)
                            {
                                var word = words.FirstOrDefault(w => target.Match == ReferenceMatch.Contains
                                    ? name.Contains(w, StringComparison.OrdinalIgnoreCase)
                                    : FilenameGroups.Extract(name, FilenameGroups.ValidateKey(w)).Contains(w));
                                if (word is null) continue;
                                result.Write("INSERT OR IGNORE INTO candidates VALUES($f,$d,$r)", ("$f", id), ("$d", target.Path),
                                    ("$r", (target.Match == ReferenceMatch.Contains ? "파일명 포함: " : "괄호 안 구분값 일치: ") + word));
                            }
                            var candidates = result.Number("SELECT count(*) FROM candidates WHERE file=$f", ("$f", id));
                            result.Write("UPDATE files SET candidates=$n WHERE id=$f", ("$n", candidates), ("$f", id));
                            if (candidates == 1)
                                result.Write("UPDATE files SET destination=(SELECT destination FROM candidates WHERE file=$f),reason=(SELECT reason FROM candidates WHERE file=$f) WHERE id=$f", ("$f", id));
                            else if (candidates == 0)
                                foreach (var key in FilenameGroups.Extract(name, groupOptions))
                                    result.Write("INSERT INTO groups VALUES($f,$k)", ("$f", id), ("$k", key));
                            count++;
                            if (watch.ElapsedMilliseconds >= 250) { progress?.Report(count); watch.Restart(); }
                        }
                        catch (Exception ex) when (ex is not ScanStorageException and not FilenamePatternTimeoutException && ex is IOException or UnauthorizedAccessException)
                        { result.scratch.AddWarning($"분석 생략: {path} — {ex.Message}"); }
                    }
                }
                catch (Exception ex) when (ex is not ScanStorageException and not FilenamePatternTimeoutException && ex is IOException or UnauthorizedAccessException)
                { result.scratch.AddWarning($"폴더 읽기 생략: {next} — {ex.Message}"); }
            }
            result.Flush(); result.scratch.CompleteScan(); return result;
        }
        catch (SqliteException ex) { result?.Dispose(); throw new ScanStorageException("기준 폴더 분석 저장소 오류: " + ex.Message, ex); }
        catch { result?.Dispose(); throw; }
    }
    private string? NextDirectory() => Sql(() =>
    {
        using var c = Command("SELECT id,path FROM pending ORDER BY id LIMIT 1");
        long id; string path;
        using (var r = c.ExecuteReader()) { if (!r.Read()) return null; id = r.GetInt64(0); path = r.GetString(1); }
        Write("DELETE FROM pending WHERE id=$i", ("$i", id)); return path;
    });
    private static string Where(ReferenceFilter filter) => filter switch
    {
        ReferenceFilter.All => "1=1", ReferenceFilter.Decided => "destination<>''",
        ReferenceFilter.Ambiguous => "destination='' AND candidates>1", ReferenceFilter.Unmatched => "destination='' AND candidates<=1",
        _ => throw new ArgumentException("결과 필터를 확인하세요.")
    };
    public long Count(ReferenceFilter filter = ReferenceFilter.All) => Number("SELECT count(*) FROM files WHERE " + Where(filter));
    public IReadOnlyList<ReferenceFile> Files(ReferenceFilter filter, long page, string? group = null) => Sql(() =>
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        var rows = new List<ReferenceFile>(100);
        using var c = Command("SELECT id,path,destination,reason,candidates FROM files WHERE " + Where(filter)
            + (group is null ? "" : " AND id IN (SELECT file FROM groups WHERE key=$g)") + " ORDER BY id LIMIT 100 OFFSET $o", ("$g", group ?? ""), ("$o", page * 100));
        using var r = c.ExecuteReader(); while (r.Read()) rows.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4))); return rows;
    });
    public long GroupFileCount(string group) => Number("SELECT count(*) FROM groups JOIN files ON files.id=groups.file WHERE key=$g AND destination=''", ("$g", group));
    public long GroupCount => Number("SELECT count(DISTINCT key) FROM groups JOIN files ON files.id=groups.file WHERE destination=''");
    public IReadOnlyList<ReferenceGroup> Groups(long page = 0) => Sql(() =>
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page); var rows = new List<ReferenceGroup>(100);
        using var c = Command("SELECT key,count(*) FROM groups JOIN files ON files.id=groups.file WHERE destination='' GROUP BY key ORDER BY count(*) DESC,key LIMIT 100 OFFSET $o", ("$o", page * 100));
        using var r = c.ExecuteReader(); while (r.Read()) rows.Add(new(r.GetString(0), r.GetInt64(1))); return rows;
    });
    public IReadOnlyList<ReferenceCandidate> Candidates(long file) => Sql(() =>
    {
        var rows = new List<ReferenceCandidate>(); using var c = Command("SELECT destination,reason FROM candidates WHERE file=$f ORDER BY destination", ("$f", file));
        using var r = c.ExecuteReader(); while (r.Read()) rows.Add(new(r.GetString(0), r.GetString(1))); return rows;
    });
    private string ValidateDestination(string destination)
    {
        destination = PathSafety.Normalize(destination); PathSafety.AssertNoLinks(destination);
        if (!PathSafety.Comparer.Equals(Path.GetDirectoryName(destination), Root)) throw new ArgumentException("목적지는 기준 폴더의 바로 아래로 지정하세요.");
        _ = NewDestination(Root, Path.GetFileName(destination));
        if (File.Exists(destination)) throw new ArgumentException("목적지와 같은 이름의 파일이 있습니다.");
        return destination;
    }
    public void Assign(long file, string destination)
    {
        destination = ValidateDestination(destination);
        if (Execute("UPDATE files SET destination=$d,reason='사용자 목적지 선택' WHERE id=$f", ("$d", destination), ("$f", file)) != 1)
            throw new ArgumentException("선택한 파일이 없습니다.");
    }
    public void AssignGroup(string group, string destination)
    {
        destination = ValidateDestination(destination);
        Execute("UPDATE files SET destination=$d,reason=$r WHERE destination='' AND id IN (SELECT file FROM groups WHERE key=$g)",
            ("$d", destination), ("$r", "사용자 그룹 분류: " + group), ("$g", group));
    }
    public void ClearAssignment(long file)
    {
        Execute("UPDATE files SET destination='',reason='사용자 보류' WHERE id=$f", ("$f", file));
    }
    public DiskPlanStore CreatePlan(string cacheRoot, CancellationToken token = default)
    {
        var plan = new DiskPlanStore(cacheRoot, folder);
        try
        {
            long after = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var rows = Sql(() =>
                {
                    var page = new List<(long Id, string Path, string Destination, string Reason, FileStamp Stamp)>(100);
                    using var c = Command("SELECT id,path,destination,reason,stamp FROM files WHERE destination<>'' AND id>$i ORDER BY id LIMIT 100", ("$i", after));
                    using var r = c.ExecuteReader();
                    while (r.Read()) page.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), JsonSerializer.Deserialize<FileStamp>(r.GetString(4))!));
                    return page;
                });
                if (rows.Count == 0) break;
                foreach (var row in rows)
                {
                    token.ThrowIfCancellationRequested();
                    var rule = new RuleSpec { Name = "기준 폴더 정리", Match = NameMatch.Any, Destination = ValidateDestination(row.Destination), Conflict = ConflictMode.Rename };
                    PathSafety.ValidateRule(folder, rule);
                    try
                    {
                        PathSafety.AssertNoLinks(row.Path);
                        if (FileStamp.Read(row.Path) != row.Stamp) throw new IOException("분석 이후 원본이 변경되었습니다. 다시 분석하세요.");
                        var entry = Planner.PlanFile(row.Path, rule, plan, row.Stamp);
                        plan.AddEntry(entry with { Reason = row.Reason + " · " + entry.Reason });
                    }
                    catch (Exception ex) when (ex is not ScanStorageException && ex is IOException or UnauthorizedAccessException)
                    {
                        plan.AddEntry(new(Guid.NewGuid(), row.Path, Path.Combine(rule.Destination, Path.GetFileName(row.Path)), rule.Name,
                            PlanAction.Blocked, ex.Message, row.Stamp, null, true));
                    }
                }
                after = rows[^1].Id;
            }
            foreach (var warning in Warnings) plan.AddWarning(warning);
            if (WarningCount > Warnings.Count) plan.AddWarning($"분석 경고 총 {WarningCount}개 · 처음 {Warnings.Count}개만 표시");
            plan.CompleteScan(); return plan;
        }
        catch { plan.Dispose(); throw; }
    }
    public void Dispose()
    {
        if (disposed) return;
        try { transaction?.Dispose(); }
        catch (SqliteException) { /* 실패한 임시 트랜잭션은 재사용하지 않는다. */ }
        finally
        {
            try { db?.Dispose(); }
            catch (SqliteException) { /* 캐시는 다음 검사에서 정리할 수 있다. */ }
            finally { scratch.Dispose(); disposed = true; }
        }
    }
}
