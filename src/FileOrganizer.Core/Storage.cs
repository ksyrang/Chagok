using System.Text;
using System.Text.Json;

namespace FileOrganizer.Core;

public sealed class Storage(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string DirectoryPath => directory;
    public string SettingsPath => Path.Combine(directory, "settings.json");
    public string HistoryPath => Path.Combine(directory, "history.jsonl");

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions)
            ?? throw new InvalidDataException("설정 파일이 비어 있습니다.");
        if (settings.Version != 1 || settings.Folders is null || !Enum.IsDefined(settings.Theme)
            || !Enum.IsDefined(settings.CloseAction)
            || settings.Folders.Any(f => f is null || f.Rules is null || f.ExcludedFolders is null
                || f.Name is null || f.Path is null || f.ProgramPath is null || f.RecentDestinations is null
                || f.RecentDestinations.Any(p => p is null)
                || !Enum.IsDefined(f.Trigger) || !Enum.IsDefined(f.RunMode) || f.ExcludedFolders.Any(p => p is null)
                || f.Rules.Any(r => r is null || r.Name is null || r.Pattern is null || r.Extensions is null
                    || r.Destination is null || r.ProgramPath is null || !Enum.IsDefined(r.Match)
                    || !Enum.IsDefined(r.Conflict) || !Enum.IsDefined(r.Trigger) || !Enum.IsDefined(r.RunMode))))
            throw new InvalidDataException("지원하지 않거나 손상된 설정 파일입니다. 기존 파일은 보존했습니다.");
        var folderIds = settings.Folders.Select(f => f.Id).ToArray();
        var ruleIds = settings.Folders.SelectMany(f => f.Rules).Select(r => r.Id).ToArray();
        if (folderIds.Contains(Guid.Empty) || ruleIds.Contains(Guid.Empty)
            || folderIds.Distinct().Count() != folderIds.Length || ruleIds.Distinct().Count() != ruleIds.Length)
            throw new InvalidDataException("설정의 폴더·규칙 ID가 중복되거나 비어 있습니다. 기존 파일은 보존했습니다.");
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(directory);
        var temp = SettingsPath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, settings, JsonOptions);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(SettingsPath)) File.Replace(temp, SettingsPath, SettingsPath + ".backup");
        else File.Move(temp, SettingsPath);
    }

    public void Append(OperationResult result)
    {
        Directory.CreateDirectory(directory);
        using var stream = new FileStream(HistoryPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        var data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result) + Environment.NewLine);
        stream.Write(data);
        stream.Flush(flushToDisk: true);
    }

    public IReadOnlyList<OperationResult> ReadHistory(int limit = 500)
    {
        if (!File.Exists(HistoryPath)) return [];
        var rows = new Queue<OperationResult>();
        foreach (var line in File.ReadLines(HistoryPath))
        {
            try
            {
                var item = JsonSerializer.Deserialize<OperationResult>(line);
                if (item is not null) rows.Enqueue(item);
                if (rows.Count > limit) rows.Dequeue();
            }
            catch (JsonException) { /* 비정상 종료로 마지막 줄이 끊겨도 이전 기록은 표시한다. */ }
        }
        return rows.Reverse().ToArray();
    }

    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
