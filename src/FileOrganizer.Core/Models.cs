using System.Text.Json.Serialization;

namespace FileOrganizer.Core;

[JsonConverter(typeof(JsonStringEnumConverter<ThemeMode>))]
public enum ThemeMode { System, Light, Dark }
[JsonConverter(typeof(JsonStringEnumConverter<WindowCloseAction>))]
public enum WindowCloseAction { HideToTray, Exit }
[JsonConverter(typeof(JsonStringEnumConverter<NameMatch>))]
public enum NameMatch { Contains, StartsWith, Glob, Any, Group }
[JsonConverter(typeof(JsonStringEnumConverter<ConflictMode>))]
public enum ConflictMode { Rename, Replace, Skip }
[JsonConverter(typeof(JsonStringEnumConverter<TriggerMode>))]
public enum TriggerMode { Manual, ProgramStarted, ProgramStopped }
[JsonConverter(typeof(JsonStringEnumConverter<RunMode>))]
public enum RunMode { Confirm, Automatic }
public enum PlanAction { Move, Rename, Replace, Skip, Blocked }

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public WindowCloseAction CloseAction { get; set; } = WindowCloseAction.HideToTray;
    public bool ConfirmExitOnClose { get; set; } = true;
    public bool AutoRegisterExplorerMenu { get; set; } = true;
    public List<FolderSpec> Folders { get; set; } = [];
}

public sealed class FolderSpec
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public TriggerMode Trigger { get; set; } = TriggerMode.Manual;
    public string ProgramPath { get; set; } = "";
    public RunMode RunMode { get; set; } = RunMode.Confirm;
    public List<string> ExcludedFolders { get; set; } = [];
    public bool UseRecentDestinations { get; set; }
    public List<string> RecentDestinations { get; set; } = [];
    public List<RuleSpec> Rules { get; set; } = [];
}

public sealed class RuleSpec
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "새 규칙";
    public bool Enabled { get; set; } = true;
    public bool Recursive { get; set; }
    public NameMatch Match { get; set; } = NameMatch.Contains;
    public string Pattern { get; set; } = "";
    public string GroupExpression { get; set; } = "";
    public FilenameConditionSet? GroupConditions { get; set; }
    public string Extensions { get; set; } = "";
    public string Destination { get; set; } = "";
    public bool AppendRuleName { get; set; }
    public bool CreateDestination { get; set; } = true;
    public ConflictMode Conflict { get; set; } = ConflictMode.Rename;
    public TriggerMode Trigger { get; set; } = TriggerMode.Manual;
    public string ProgramPath { get; set; } = "";
    public RunMode RunMode { get; set; } = RunMode.Confirm;
}

public sealed record FileStamp(long Length, DateTime LastWriteUtc, DateTime CreatedUtc)
{
    public static FileStamp Read(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists) throw new FileNotFoundException("파일이 없습니다.", path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("바로 가기·링크 파일은 처리하지 않습니다.");
        return new(info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc);
    }
}

public sealed record PlanEntry(
    Guid Id, string Source, string Target, string RuleName, PlanAction Action,
    string Reason, FileStamp? SourceStamp, FileStamp? TargetStamp, bool CreateDestination)
{
    public bool CanExecute => Action is PlanAction.Move or PlanAction.Rename or PlanAction.Replace;
}

public sealed record ScanPlan(Guid FolderId, string FolderName, DateTime CreatedUtc,
    IReadOnlyList<PlanEntry> Entries, IReadOnlyList<string> Warnings);

public sealed record OperationResult(DateTime TimeUtc, string Folder, string Rule, string Source,
    string Target, string Action, string Status, string Message);

public interface IRecycleBin
{
    Task RecycleAsync(string path, CancellationToken cancellationToken);
}
