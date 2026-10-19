namespace FileOrganizer.Core;

public static class SettingsValidation
{
    public static void Folder(AppSettings settings, FolderSpec folder)
    {
        folder.Name = folder.Name.Trim();
        if (folder.Name.Length == 0) throw new ArgumentException("표시 이름을 입력하세요.");
        folder.Path = PathSafety.Normalize(folder.Path.Trim());
        if (!Directory.Exists(folder.Path)) throw new ArgumentException("검사할 폴더가 없습니다.");
        PathSafety.AssertNoLinks(folder.Path);
        if (settings.Folders.Any(f => f.Id != folder.Id &&
            (PathSafety.IsWithin(folder.Path, f.Path) || PathSafety.IsWithin(f.Path, folder.Path))))
            throw new ArgumentException("기존 검사 폴더와 범위가 겹칩니다.");
        folder.ExcludedFolders = PathSafety.NormalizeExclusions(folder.Path, folder.ExcludedFolders);
        Program(folder.Trigger, folder.ProgramPath);
        foreach (var rule in folder.Rules) Rule(folder, rule);
    }

    public static void Rule(FolderSpec folder, RuleSpec rule)
    {
        rule.Name = rule.Name.Trim();
        rule.Destination = PathSafety.Normalize(rule.Destination.Trim());
        PathSafety.ValidateRule(folder, rule);
        Program(rule.Trigger, rule.ProgramPath);
    }

    private static void Program(TriggerMode trigger, string path)
    {
        if (trigger == TriggerMode.Manual) return;
        path = PathSafety.Normalize(path);
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new ArgumentException("감지할 실제 .exe 파일이 없습니다.");
    }
}
