using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace FileOrganizer.Core;

[JsonConverter(typeof(JsonStringEnumConverter<BracketKind>))]
public enum BracketKind { Square, Round, Curly }
public sealed record AnalysisOptions(BracketKind Brackets = BracketKind.Square, bool Recursive = false, string CustomPattern = "");
public sealed record AnalysisFolder(Guid Id, string Name, string Path, long Total, long Matched, long Groups, long Warnings)
{
    public long Unmatched => Total - Matched;
    public string Summary => $"패턴 일치 {Matched}개 · {Groups}개 그룹" + (Warnings > 0 ? $" · 경고 {Warnings}" : "");
}
public sealed record AnalysisGroup(string Key, long Count);
public sealed record AnalysisFile(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Extension => System.IO.Path.GetExtension(Path);
}
public sealed record AnalysisProgress(string Folder, long Files);
public sealed record FilenamePatternPreview(IReadOnlyList<string> Matches, IReadOnlySet<string> Groups);
public sealed class FilenamePatternTimeoutException(string message, Exception inner) : ArgumentException(message, inner);

public static class FilenameGroups
{
    public const string PatternExample = @"\[(?<group>[^\]]+)\]";
    public const string PatternHelp = "파일명 전체(확장자 포함)에 .NET 정규식을 적용합니다. 대소문자를 구분합니다. "
        + "(?<group>...)가 있으면 그 안의 값으로, 없으면 일치한 전체 문자열로 묶습니다. "
        + "빈 값은 제외하고 한 파일의 같은 값은 한 번만 셉니다. (?i)는 찾을 때만 대소문자 구분을 끕니다. 추출한 값은 대소문자·공백을 구분합니다.\n"
        + @"예: \[(?<group>[^\]]+)\] → [a] 보고서.pdf에서 a" + "\n"
        + @"예: ^(?<group>[^_]+)_ → 업무_2026.pdf에서 업무" + "\n"
        + @"예: (?<group>\d{4})-\d{2}-\d{2} → 2026-10-20.pdf에서 2026" + "\n"
        + "정규식은 최대 2,000자입니다. 파일 하나에서 100ms를 넘기면 분석/검사를 중단합니다.";

    public static void Validate(AnalysisOptions options)
    {
        _ = Pair(options.Brackets);
        if (options.CustomPattern.Length > 0) _ = Expression(options.CustomPattern);
    }
    private static Regex Expression(string pattern)
    {
        if (pattern.Length > 2000 || string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("분류 정규식은 공백이 아닌 1~2,000자로 입력하세요.");
        try { return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
        catch (ArgumentException ex) { throw new ArgumentException("분류 정규식을 확인하세요. " + ex.Message, ex); }
    }
    public static string Description(AnalysisOptions options) => options.CustomPattern.Length > 0
        ? "직접 입력: " + options.CustomPattern : $"{Pair(options.Brackets).Open} {Pair(options.Brackets).Close}";

    public static IReadOnlySet<string> Extract(string name, AnalysisOptions options)
    {
        if (options.CustomPattern.Length == 0) return Extract(name, options.Brackets);
        return ExtractRegex(name, options.CustomPattern);
    }

    public static FilenamePatternPreview Preview(string name, string pattern)
    {
        _ = Expression(pattern);
        var matches = new List<string>();
        return new(matches, ExtractRegex(name, pattern, matches));
    }

    private static IReadOnlySet<string> ExtractRegex(string name, string pattern, List<string>? matches = null)
    {
        // 정적 Regex API의 크기가 제한된 캐시를 사용한다. 파일마다 정규식을 다시 컴파일하지 않는다.
        var result = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (Match match in Regex.Matches(name, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            {
                matches?.Add(match.Value);
                var group = match.Groups.ContainsKey("group") ? match.Groups["group"] : match.Groups[0];
                foreach (Capture capture in group.Captures)
                    if (!string.IsNullOrWhiteSpace(capture.Value)) result.Add(capture.Value);
            }
        }
        catch (RegexMatchTimeoutException ex)
        { throw new FilenamePatternTimeoutException("분류 정규식 처리가 100ms를 넘었습니다. 더 단순한 식으로 바꾸세요.", ex); }
        return result;
    }

    public static (char Open, char Close) Pair(BracketKind kind) => kind switch
    {
        BracketKind.Square => ('[', ']'), BracketKind.Round => ('(', ')'), BracketKind.Curly => ('{', '}'),
        _ => throw new ArgumentException("지원하지 않는 괄호입니다.")
    };

    public static IReadOnlySet<string> Extract(string name, BracketKind kind)
    {
        var (open, close) = Pair(kind);
        var result = new HashSet<string>(StringComparer.Ordinal);
        var depth = 0; var start = 0; var nested = false;
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == open)
            {
                if (depth == 0) { start = i; nested = false; } else nested = true;
                depth++;
            }
            else if (name[i] == close && depth > 0 && --depth == 0 && !nested
                && !string.IsNullOrWhiteSpace(name[(start + 1)..i])) result.Add(name[start..(i + 1)]);
        }
        return result;
    }

    public static BracketKind ValidateKey(string key)
    {
        foreach (var kind in Enum.GetValues<BracketKind>())
        {
            var groups = Extract(key, kind);
            if (groups.Count == 1 && groups.Contains(key)) return kind;
        }
        throw new ArgumentException("구분값은 [a], (a), {a}처럼 비어 있지 않은 괄호 한 쌍으로 입력하세요.");
    }

    public static RuleSpec Rule(FolderSpec folder, string key, AnalysisOptions options)
    {
        Validate(options);
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("구분값은 비워 둘 수 없습니다.");
        if (options.CustomPattern.Length == 0 && ValidateKey(key) != options.Brackets) throw new ArgumentException("분석 조건과 구분값의 괄호가 다릅니다.");
        return new() { Name = options.CustomPattern.Length == 0 ? key[1..^1] : key, Match = NameMatch.Group, Pattern = key,
            GroupExpression = options.CustomPattern,
            Recursive = options.Recursive, Destination = DestinationPaths.Initial(folder) };
    }
}

// 파일·그룹은 메모리에 누적하지 않는다. 기존 디스크 순회·캐시 임대/정리 기능을 재사용한다.
public sealed class FilenameAnalysis : IDisposable
{
    private readonly DiskPlanStore scratch;
    private readonly SqliteConnection connection;
    private SqliteTransaction? transaction;
    private int writes;
    private bool disposed;
    public AnalysisOptions Options { get; }
    public DateTime CreatedUtc { get; private set; }
    public string DirectoryPath => scratch.DirectoryPath;
    public long WarningCount => scratch.WarningCount;

    private FilenameAnalysis(string cacheRoot, AnalysisOptions options)
    {
        FilenameGroups.Validate(options);
        Options = options;
        scratch = new DiskPlanStore(cacheRoot, new FolderSpec { Name = "파일명 분석" });
        try
        {
            connection = new(new SqliteConnectionStringBuilder { DataSource = Path.Combine(DirectoryPath, "analysis.db"), Pooling = false }.ToString());
            connection.Open();
            connection.CreateCollation("PATH", PathSafety.Comparer.Compare);
            Execute("""
                PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-2048; PRAGMA temp_store=FILE;
                CREATE TABLE folders(id TEXT PRIMARY KEY, name TEXT, path TEXT, total INTEGER, matched INTEGER, warnings INTEGER, groups INTEGER DEFAULT 0);
                CREATE TABLE members(folder TEXT, key TEXT COLLATE BINARY, path TEXT COLLATE PATH,
                    PRIMARY KEY(folder,key,path)) WITHOUT ROWID;
                CREATE TABLE groups(folder TEXT, key TEXT COLLATE BINARY, count INTEGER, PRIMARY KEY(folder,key)) WITHOUT ROWID;
                CREATE INDEX group_counts ON groups(folder,count DESC,key);
                CREATE TABLE pending(id INTEGER PRIMARY KEY,path TEXT);
                """);
        }
        catch (Exception ex) when (ex is SqliteException or DllNotFoundException or BadImageFormatException or TypeInitializationException)
        { Dispose(); throw new ScanStorageException("파일명 분석 저장소를 만들지 못했습니다. " + ex.Message, ex); }
        catch { Dispose(); throw; }
    }

    private T Sql<T>(Func<T> action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try { return action(); }
        catch (SqliteException ex) { throw new ScanStorageException("파일명 분석 저장소 오류: " + ex.Message, ex); }
    }
    private SqliteCommand Command(string sql, params (string Key, object Value)[] args)
    {
        var cmd = connection.CreateCommand(); cmd.Transaction = transaction; cmd.CommandText = sql;
        foreach (var (key, value) in args) cmd.Parameters.AddWithValue(key, value);
        return cmd;
    }
    private void Execute(string sql, params (string Key, object Value)[] args) => Sql(() =>
    { using var cmd = Command(sql, args); return cmd.ExecuteNonQuery(); });
    private void Write(string sql, params (string Key, object Value)[] args)
    {
        Sql(() => { transaction ??= connection.BeginTransaction(); return 0; });
        Execute(sql, args); if (++writes >= 500) Flush();
    }
    private void Flush() => Sql(() =>
    { transaction?.Commit(); transaction?.Dispose(); transaction = null; writes = 0; return 0; });
    private long Number(string sql, params (string Key, object Value)[] args) => Sql(() =>
    { using var cmd = Command(sql, args); return Convert.ToInt64(cmd.ExecuteScalar()); });

    public static FilenameAnalysis Scan(IReadOnlyList<FolderSpec> folders, IReadOnlyList<string> excludedDestinations,
        string cacheRoot, AnalysisOptions options, CancellationToken token = default, IProgress<AnalysisProgress>? progress = null)
    {
        var store = new FilenameAnalysis(cacheRoot, options);
        try
        {
            var watch = Stopwatch.StartNew();
            foreach (var folder in folders)
            {
                token.ThrowIfCancellationRequested();
                long total = 0, matched = 0; var beforeWarnings = store.WarningCount;
                var id = folder.Id.ToString();
                store.Write("INSERT INTO folders(id,name,path,total,matched,warnings) VALUES($id,$n,$p,0,0,0)", ("$id", id), ("$n", folder.Name), ("$p", folder.Path));
                var exclusions = folder.ExcludedFolders.Concat(excludedDestinations).Append(cacheRoot).Select(PathSafety.Normalize).Distinct(PathSafety.Comparer).ToArray();
                try
                {
                    var source = PathSafety.Normalize(folder.Path);
                    if (PathSafety.IsWithin(source, cacheRoot)) throw new IOException("분석 폴더는 임시 저장소 안에 둘 수 없습니다.");
                    PathSafety.AssertNoLinks(source);
                    if (!Directory.Exists(source)) throw new DirectoryNotFoundException("분석할 폴더가 없거나 드라이브가 연결되지 않았습니다.");
                    store.Write("INSERT INTO pending(path) VALUES($p)", ("$p", source));
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        var directory = store.NextDirectory(); if (directory is null) break;
                        try
                        {
                            PathSafety.AssertNoLinks(directory);
                            foreach (var path in store.scratch.Paths(directory, token))
                            {
                                token.ThrowIfCancellationRequested();
                                var name = Path.GetFileName(path);
                                if (name.StartsWith("._", StringComparison.Ordinal) || name == ".DS_Store"
                                    || name.StartsWith(".fileorganizer-", StringComparison.Ordinal)) continue;
                                if (exclusions.Any(e => !PathSafety.Comparer.Equals(e, source) && PathSafety.IsWithin(path, e))) continue;
                                try
                                {
                                    var attrs = File.GetAttributes(path);
                                    if ((attrs & FileAttributes.ReparsePoint) != 0) { store.scratch.AddWarning("링크 제외: " + path); continue; }
                                    if ((attrs & FileAttributes.Directory) != 0)
                                    { if (options.Recursive) store.Write("INSERT INTO pending(path) VALUES($p)", ("$p", path)); continue; }
                                    total++;
                                    var keys = FilenameGroups.Extract(name, options);
                                    if (keys.Count > 0) matched++;
                                    foreach (var key in keys)
                                        store.Write("INSERT OR IGNORE INTO members VALUES($f,$k,$p)", ("$f", id), ("$k", key), ("$p", path));
                                    if (watch.ElapsedMilliseconds >= 250)
                                    { progress?.Report(new(folder.Name, total)); watch.Restart(); }
                                }
                                catch (Exception ex) when (ex is not ScanStorageException && ex is IOException or UnauthorizedAccessException)
                                { store.scratch.AddWarning($"분석 생략: {path} — {ex.Message}"); }
                            }
                        }
                        catch (Exception ex) when (ex is not ScanStorageException && ex is IOException or UnauthorizedAccessException)
                        { store.scratch.AddWarning($"분석 생략: {directory} — {ex.Message}"); }
                    }
                }
                catch (Exception ex) when (ex is not ScanStorageException && ex is IOException or UnauthorizedAccessException)
                { store.scratch.AddWarning($"분석 생략: {folder.Path} — {ex.Message}"); }
                store.Write("UPDATE folders SET total=$t,matched=$m,warnings=$w WHERE id=$id", ("$id", id), ("$t", total), ("$m", matched), ("$w", store.WarningCount - beforeWarnings));
                progress?.Report(new(folder.Name, total));
            }
            token.ThrowIfCancellationRequested();
            store.Execute("INSERT INTO groups SELECT folder,key,count(*) FROM members GROUP BY folder,key");
            store.Execute("UPDATE folders SET groups=(SELECT count(*) FROM groups WHERE folder=folders.id)");
            token.ThrowIfCancellationRequested();
            store.Flush(); store.scratch.Flush(); store.CreatedUtc = DateTime.UtcNow;
            return store;
        }
        catch { store.Dispose(); throw; }
    }

    private string? NextDirectory() => Sql(() =>
    {
        long id; string path;
        using (var cmd = Command("SELECT id,path FROM pending ORDER BY id LIMIT 1"))
        using (var reader = cmd.ExecuteReader())
        { if (!reader.Read()) return null; id = reader.GetInt64(0); path = reader.GetString(1); }
        Write("DELETE FROM pending WHERE id=$id", ("$id", id)); return path;
    });

    public IReadOnlyList<AnalysisFolder> Folders() => Sql(() =>
    {
        var result = new List<AnalysisFolder>();
        using var cmd = Command("SELECT id,name,path,total,matched,groups,warnings FROM folders ORDER BY rowid");
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6)));
        return result;
    });
    public long GroupCount(Guid? folder, int minimum = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimum, 1);
        return Number(folder is null ? "SELECT count(*) FROM (SELECT key FROM groups GROUP BY key HAVING sum(count)>=$m)"
            : "SELECT count(*) FROM groups WHERE folder=$f AND count>=$m", ("$f", folder?.ToString() ?? ""), ("$m", minimum));
    }
    public IReadOnlyList<AnalysisGroup> Groups(Guid? folder, int minimum = 1, long page = 0) => Sql(() =>
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page); ArgumentOutOfRangeException.ThrowIfLessThan(minimum, 1);
        var result = new List<AnalysisGroup>();
        using var cmd = Command(folder is null
            ? "SELECT key,sum(count) AS n FROM groups GROUP BY key HAVING n>=$m ORDER BY n DESC,key COLLATE BINARY LIMIT 100 OFFSET $o"
            : "SELECT key,count FROM groups WHERE folder=$f AND count>=$m ORDER BY count DESC,key COLLATE BINARY LIMIT 100 OFFSET $o",
            ("$f", folder?.ToString() ?? ""), ("$m", minimum), ("$o", checked(page * 100)));
        using var reader = cmd.ExecuteReader(); while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetInt64(1))); return result;
    });
    public long FileCount(Guid? folder, string key) => Number(folder is null ? "SELECT count(*) FROM members WHERE key=$k"
        : "SELECT count(*) FROM members WHERE folder=$f AND key=$k", ("$f", folder?.ToString() ?? ""), ("$k", key));
    public IReadOnlyList<AnalysisFile> Files(Guid? folder, string key, long page = 0) => Sql(() =>
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        var result = new List<AnalysisFile>();
        using var cmd = Command(folder is null ? "SELECT path FROM members WHERE key=$k ORDER BY path COLLATE PATH LIMIT 100 OFFSET $o"
            : "SELECT path FROM members WHERE folder=$f AND key=$k ORDER BY path COLLATE PATH LIMIT 100 OFFSET $o",
            ("$f", folder?.ToString() ?? ""), ("$k", key), ("$o", checked(page * 100)));
        using var reader = cmd.ExecuteReader(); while (reader.Read()) result.Add(new(reader.GetString(0))); return result;
    });
    public IReadOnlyList<string> Warnings() => scratch.ReadWarnings();
    public void Dispose()
    {
        if (disposed) return;
        try { transaction?.Dispose(); }
        catch (SqliteException) { /* 중단된 임시 트랜잭션은 재사용하지 않는다. */ }
        finally
        {
            try { connection?.Dispose(); }
            catch (SqliteException) { /* 사용하지 않는 캐시는 다음 분석에서 정리한다. */ }
            finally { disposed = true; scratch.Dispose(); }
        }
    }
}
