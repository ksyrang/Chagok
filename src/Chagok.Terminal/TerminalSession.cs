using FileOrganizer.Core;

namespace Chagok.Terminal;

public sealed class TerminalSession(Storage storage, AppSettings settings)
{
    public Storage Storage { get; } = storage;
    public AppSettings Settings { get; private set; } = settings;
    public FolderSpec Folder(string value) => Find(Settings.Folders, value, f => f.Id, f => f.Name, "폴더");
    public static RuleSpec Rule(FolderSpec folder, string value) => Find(folder.Rules, value, r => r.Id, r => r.Name, "규칙");
    private static T Find<T>(IEnumerable<T> items, string value, Func<T, Guid> id, Func<T, string> name, string kind)
    {
        var matches = items.Where(item => Guid.TryParse(value, out var guid) ? id(item) == guid
            : string.Equals(name(item), value, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length switch
        { 1 => matches[0], 0 => throw new ArgumentException($"{kind}을 찾을 수 없습니다: {value}"), _ => throw new ArgumentException($"이름이 같은 {kind}이 여러 개입니다. 전체 ID를 지정하세요.") };
    }
    public void Save(AppSettings next) { Storage.Save(next); Settings = next; }
    public void PutFolder(FolderSpec folder)
    {
        var next = FileOrganizer.Core.Storage.Clone(Settings);
        SettingsValidation.Folder(next, folder);
        if (PathSafety.IsWithin(folder.Path, PathSafety.Normalize(Storage.DirectoryPath)))
            throw new ArgumentException("차곡 데이터 저장소는 검사 폴더로 사용할 수 없습니다.");
        var index = next.Folders.FindIndex(f => f.Id == folder.Id);
        if (index < 0) next.Folders.Add(folder); else next.Folders[index] = folder;
        Save(next);
    }
    public void PutRule(Guid folderId, RuleSpec rule)
    {
        var folder = FileOrganizer.Core.Storage.Clone(Folder(folderId.ToString()));
        SettingsValidation.Rule(folder, rule);
        var index = folder.Rules.FindIndex(r => r.Id == rule.Id);
        if (index < 0) folder.Rules.Add(rule); else folder.Rules[index] = rule;
        DestinationPaths.Remember(folder, rule);
        PutFolder(folder);
    }
    public void RemoveFolder(Guid id)
    {
        var next = FileOrganizer.Core.Storage.Clone(Settings); next.Folders.RemoveAll(f => f.Id == id); Save(next);
    }
    public void RemoveRule(Guid folderId, Guid id)
    {
        var folder = FileOrganizer.Core.Storage.Clone(Folder(folderId.ToString()));
        folder.Rules.RemoveAll(r => r.Id == id); PutFolder(folder);
    }
    public FilenameAnalysis Analyze(AnalysisOptions options, CancellationToken token, Guid? scope = null)
    {
        var folders = Settings.Folders.Where(f => scope is null || f.Id == scope).Select(FileOrganizer.Core.Storage.Clone).ToArray();
        if (folders.Any(f => PathSafety.IsWithin(f.Path, Storage.DirectoryPath)))
            throw new ArgumentException("차곡 데이터 저장소는 분석 폴더로 사용할 수 없습니다.");
        var destinations = Settings.Folders.SelectMany(f => f.Rules).Select(DestinationPaths.Resolve).Append(Storage.DirectoryPath).ToArray();
        return FilenameAnalysis.Scan(folders, destinations, Path.Combine(Storage.DirectoryPath, "analysis-cache"), options, token);
    }
    public DiskPlanStore Scan(FolderSpec folder, IReadOnlySet<Guid>? rules, CancellationToken token)
    {
        if (PathSafety.IsWithin(folder.Path, PathSafety.Normalize(Storage.DirectoryPath)))
            throw new ArgumentException("차곡 데이터 저장소는 검사 폴더로 사용할 수 없습니다.");
        var destinations = Settings.Folders.SelectMany(f => f.Rules).Select(DestinationPaths.Resolve)
            .Append(PathSafety.Normalize(Storage.DirectoryPath)).ToArray();
        return new Planner().ScanToDisk(FileOrganizer.Core.Storage.Clone(folder), destinations,
            Path.Combine(Storage.DirectoryPath, "scan-cache"), rules, token);
    }
    public async Task<ExecutionReport> ExecuteAsync(DiskPlanStore plan, IRecycleBin recycle,
        Action<ExecutionReport, PlanEntry, TransferProgress?>? progress, CancellationToken token)
    {
        var report = new ExecutionReport(plan.SelectedCount);
        var executor = new Executor(recycle); long cursor = 0; string? error = null;
        try
        {
            plan.Flush();
            while (!token.IsCancellationRequested)
            {
                var page = plan.ReadSelectedPage(cursor);
                if (page.Count == 0) break;
                foreach (var row in page)
                {
                    if (token.IsCancellationRequested) break;
                    var entry = row.Entry;
                    progress?.Invoke(report, entry, null);
                    Storage.Append(new(DateTime.UtcNow, plan.Header.FolderName, entry.RuleName, entry.Source, entry.Target,
                        entry.Action.ToString(), "시작", "터미널 실행 전 기록"));
                    var result = await executor.ExecuteAsync(plan.Header, entry, token,
                        new InlineProgress(p => progress?.Invoke(report, entry, p)));
                    report.Record(result);
                    Storage.Append(result); plan.StoreResult(row.Position, result); cursor = row.Position;
                    progress?.Invoke(report, entry, null);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { error = "목록·기록 오류로 중단했습니다. 이미 이동한 파일의 위치도 확인하세요. " + ex.Message; }
        finally { report.Finish(token.IsCancellationRequested, error); }
        return report;
    }
    private sealed class InlineProgress(Action<TransferProgress> action) : IProgress<TransferProgress>
    { public void Report(TransferProgress value) => action(value); }
}
