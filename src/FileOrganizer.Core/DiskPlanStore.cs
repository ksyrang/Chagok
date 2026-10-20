using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace FileOrganizer.Core;

public sealed class ScanStorageException(string message, Exception inner) : IOException(message, inner);

public sealed record StoredPlanEntry(long Position, PlanEntry Entry, bool Selected, OperationResult? Result = null);

// 한 검사에만 사용하는 저장소다. UI와 작업 스레드는 동시에 접근하지 않는다.
public sealed class DiskPlanStore : IDisposable, IScanSink
{
    public const int BatchSize = 500;
    private readonly SqliteConnection connection;
    private readonly FileStream lease;
    private SqliteTransaction? transaction;
    private int writes;
    private long nextDirectory;
    private bool disposed;
    public string CacheRoot { get; }
    public string DirectoryPath { get; }
    public ScanPlan Header { get; private set; }
    public long Count { get; private set; }
    public long ExecutableCount { get; private set; }
    public long SelectedCount { get; private set; }
    public long SelectedReplaceCount { get; private set; }
    public long WarningCount { get; private set; }

    public DiskPlanStore(string cacheRoot, FolderSpec folder)
    {
        CacheRoot = PathSafety.Normalize(cacheRoot);
        PathSafety.AssertNoLinks(CacheRoot);
        Directory.CreateDirectory(CacheRoot);
        CleanAbandoned(CacheRoot);
        DirectoryPath = Path.Combine(CacheRoot, "scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        lease = new FileStream(Path.Combine(DirectoryPath, "lease"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        Header = new(folder.Id, folder.Name, DateTime.UtcNow, [], []);
        try
        {
            connection = new(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(DirectoryPath, "plan.db"), Pooling = false
            }.ToString());
            connection.Open();
            connection.CreateCollation("PATH", PathSafety.Comparer.Compare);
            Execute("PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-2048; PRAGMA temp_store=FILE;");
            Execute("""
                CREATE TABLE entries(position INTEGER PRIMARY KEY, entry TEXT NOT NULL,
                    executable INTEGER NOT NULL, selected INTEGER NOT NULL, action INTEGER NOT NULL, result TEXT);
                CREATE INDEX selection ON entries(selected, position);
                CREATE TABLE warnings(position INTEGER PRIMARY KEY, message TEXT NOT NULL);
                CREATE TABLE reserved(path TEXT COLLATE PATH PRIMARY KEY) WITHOUT ROWID;
                CREATE TABLE directory_paths(directory_id INTEGER, path TEXT COLLATE PATH,
                    PRIMARY KEY(directory_id, path)) WITHOUT ROWID;
                """);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or DllNotFoundException or BadImageFormatException or TypeInitializationException)
        {
            Dispose();
            throw new ScanStorageException("검사 결과의 임시 저장소를 만들지 못했습니다. " + ex.Message, ex);
        }
    }

    private T Sql<T>(Func<T> action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try { return action(); }
        catch (SqliteException ex) { throw new ScanStorageException("검사 결과를 읽거나 저장하지 못했습니다. " + ex.Message, ex); }
        catch (JsonException ex) { throw new InvalidDataException("검사 항목이 손상되었습니다. 다시 검사하세요.", ex); }
    }

    private SqliteCommand Command(string sql, params (string Name, object Value)[] values)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value);
        return command;
    }

    private int Execute(string sql, params (string Name, object Value)[] values) => Sql(() =>
    { using var command = Command(sql, values); return command.ExecuteNonQuery(); });

    private void Write(string sql, params (string Name, object Value)[] values)
    {
        Sql(() => { transaction ??= connection.BeginTransaction(); return 0; });
        Execute(sql, values);
        if (++writes >= BatchSize) Flush();
    }

    public void Flush() => Sql(() =>
    {
        transaction?.Commit(); transaction?.Dispose(); transaction = null; writes = 0;
        return 0;
    });

    public void CompleteScan() { Flush(); Header = Header with { CreatedUtc = DateTime.UtcNow }; }

    public void AddEntry(PlanEntry entry)
    {
        Write("INSERT INTO entries(entry,executable,selected,action) VALUES($e,$x,$x,$a)",
            ("$e", JsonSerializer.Serialize(entry)), ("$x", entry.CanExecute ? 1 : 0), ("$a", (int)entry.Action));
        Count++;
        if (entry.CanExecute) { ExecutableCount++; SelectedCount++; }
        if (entry.Action == PlanAction.Replace) SelectedReplaceCount++;
    }

    public void AddWarning(string message)
    { Write("INSERT INTO warnings(message) VALUES($m)", ("$m", message)); WarningCount++; }

    public bool IsReserved(string path) => Sql(() =>
    {
        using var command = Command("SELECT 1 FROM reserved WHERE path=$p", ("$p", path));
        return command.ExecuteScalar() is not null;
    });
    public void Reserve(string path) => Write("INSERT OR IGNORE INTO reserved(path) VALUES($p)", ("$p", path));

    public IReadOnlyList<StoredPlanEntry> ReadPage(long offset, int size = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return ReadEntries("WHERE position>$p ORDER BY position LIMIT $n", offset, size);
    }
    public IReadOnlyList<StoredPlanEntry> ReadSelectedPage(long after, int size = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(after);
        return ReadEntries("WHERE selected=1 AND position>$p ORDER BY position LIMIT $n", after, size);
    }
    private IReadOnlyList<StoredPlanEntry> ReadEntries(string clause, long after, int size) => Sql(() =>
    {
        if (size is < 1 or > BatchSize) throw new ArgumentOutOfRangeException(nameof(size));
        var rows = new List<StoredPlanEntry>(size);
        using var command = Command("SELECT position,entry,selected,result FROM entries " + clause, ("$p", after), ("$n", size));
        using var reader = command.ExecuteReader();
        while (reader.Read()) rows.Add(new(reader.GetInt64(0),
            JsonSerializer.Deserialize<PlanEntry>(reader.GetString(1)) ?? throw new InvalidDataException("검사 항목이 손상되었습니다."),
            reader.GetBoolean(2), reader.IsDBNull(3) ? null : JsonSerializer.Deserialize<OperationResult>(reader.GetString(3))));
        return rows;
    });

    public void StoreResult(long position, OperationResult result)
    {
        if (Execute("UPDATE entries SET result=$r WHERE position=$p AND result IS NULL",
            ("$r", JsonSerializer.Serialize(result)), ("$p", position)) != 1)
            throw new InvalidDataException("실행 결과를 저장할 항목이 없거나 이미 처리한 항목입니다.");
    }

    public IReadOnlyList<string> ReadWarnings(int limit = 20) => Sql(() =>
    {
        if (limit is < 1 or > BatchSize) throw new ArgumentOutOfRangeException(nameof(limit));
        var rows = new List<string>(limit);
        using var command = Command("SELECT message FROM warnings ORDER BY position LIMIT $n", ("$n", limit));
        using var reader = command.ExecuteReader();
        while (reader.Read()) rows.Add(reader.GetString(0));
        return rows;
    });

    public void SetSelected(long position, bool selected) => Sql(() =>
    {
        using var command = Command("UPDATE entries SET selected=$s WHERE position=$p AND executable=1 AND selected<>$s RETURNING action",
            ("$s", selected ? 1 : 0), ("$p", position));
        var action = command.ExecuteScalar();
        if (action is not null)
        {
            SelectedCount += selected ? 1 : -1;
            if (Convert.ToInt32(action) == (int)PlanAction.Replace) SelectedReplaceCount += selected ? 1 : -1;
        }
        return 0;
    });

    public void SetAllSelected(bool selected)
    {
        Flush();
        Execute("UPDATE entries SET selected=$s WHERE executable=1", ("$s", selected ? 1 : 0));
        SelectedCount = selected ? ExecutableCount : 0;
        SelectedReplaceCount = selected ? Sql(() =>
        {
            using var command = Command("SELECT count(*) FROM entries WHERE action=$a", ("$a", (int)PlanAction.Replace));
            return (long)command.ExecuteScalar()!;
        }) : 0;
    }

    // 폴더 하나에 파일이 많아도 전체 이름 배열을 만들지 않는다. 정렬은 디스크 인덱스에서 한다.
    public IEnumerable<string> Paths(string directory, CancellationToken token)
    {
        var id = ++nextDirectory;
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            token.ThrowIfCancellationRequested();
            Write("INSERT INTO directory_paths(directory_id,path) VALUES($d,$p)", ("$d", id), ("$p", path));
        }
        string? after = null;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var rows = Sql(() =>
            {
                var page = new List<string>(BatchSize);
                using var command = after is null
                    ? Command("SELECT path FROM directory_paths WHERE directory_id=$d ORDER BY path LIMIT $n", ("$d", id), ("$n", BatchSize))
                    : Command("SELECT path FROM directory_paths WHERE directory_id=$d AND path>$p ORDER BY path LIMIT $n", ("$d", id), ("$p", after), ("$n", BatchSize));
                using var reader = command.ExecuteReader();
                while (reader.Read()) page.Add(reader.GetString(0));
                return page;
            });
            if (rows.Count == 0) break;
            foreach (var path in rows) { token.ThrowIfCancellationRequested(); yield return path; }
            after = rows[^1];
        }
        Write("DELETE FROM directory_paths WHERE directory_id=$d", ("$d", id));
    }

    public void Dispose()
    {
        if (disposed) return;
        try { transaction?.Dispose(); }
        catch (SqliteException) { /* 실패한 임시 트랜잭션은 재사용하지 않는다. */ }
        finally
        {
            try { connection?.Dispose(); }
            catch (SqliteException) { /* 캐시는 다음 검사에서 정리할 수 있다. */ }
            finally { lease.Dispose(); disposed = true; TryDelete(DirectoryPath); }
        }
    }

    private static void CleanAbandoned(string cacheRoot)
    {
        foreach (var directory in Directory.EnumerateDirectories(cacheRoot, "scan-*"))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory)[5..], "N", out _)) continue;
            try
            {
                PathSafety.AssertNoLinks(directory);
                using (new FileStream(Path.Combine(directory, "lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                TryDelete(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 사용 중인 검사 또는 정리할 수 없는 캐시는 보존한다. */ }
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            PathSafety.AssertNoLinks(directory);
            var known = new HashSet<string>(StringComparer.Ordinal) { "lease", "plan.db", "plan.db-journal", "plan.db-wal", "plan.db-shm", "analysis.db", "analysis.db-journal", "analysis.db-wal", "analysis.db-shm" };
            if (Directory.EnumerateDirectories(directory).Any() || Directory.EnumerateFiles(directory).Any(p => !known.Contains(Path.GetFileName(p)))) return;
            foreach (var file in Directory.EnumerateFiles(directory)) { PathSafety.AssertNoLinks(file); File.Delete(file); }
            Directory.Delete(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 다음 검사에서 다시 정리한다. */ }
    }
}

internal interface IScanSink
{
    void AddEntry(PlanEntry entry);
    void AddWarning(string message);
    bool IsReserved(string path);
    void Reserve(string path);
    IEnumerable<string> Paths(string directory, CancellationToken token);
}
