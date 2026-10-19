namespace FileOrganizer.Core;

public sealed record TriggerBatch(Guid FolderId, HashSet<Guid> RuleIds, bool Automatic);

public static class TriggerPlanner
{
    public static IReadOnlyList<string> ProgramPaths(AppSettings settings) => settings.Folders
        .Where(f => f.Rules.Any(r => r.Enabled))
        .SelectMany(f => (f.Trigger == TriggerMode.Manual ? Enumerable.Empty<string>() : [f.ProgramPath])
            .Concat(f.Rules.Where(r => r.Enabled && r.Trigger != TriggerMode.Manual).Select(r => r.ProgramPath)))
        .Select(PathSafety.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<TriggerBatch> Build(AppSettings settings, string programPath, TriggerMode trigger)
    {
        if (trigger == TriggerMode.Manual) return [];
        var path = PathSafety.Normalize(programPath);
        var result = new List<TriggerBatch>();
        foreach (var folder in settings.Folders)
        {
            var folderMatch = Matches(folder.Trigger, folder.ProgramPath, trigger, path);
            var rules = folder.Rules.Where(r => r.Enabled).ToArray();
            if (folderMatch)
            {
                // 폴더 전체는 한 계획으로 검사해 첫 일치 규칙의 순서를 유지한다.
                var needsConfirm = folder.RunMode == RunMode.Confirm || rules.Any(r =>
                    r.RunMode == RunMode.Confirm && Matches(r.Trigger, r.ProgramPath, trigger, path));
                if (rules.Length > 0) result.Add(new(folder.Id, rules.Select(r => r.Id).ToHashSet(), !needsConfirm));
                continue;
            }
            var automatic = new HashSet<Guid>();
            var confirm = new HashSet<Guid>();
            foreach (var rule in rules)
            {
                var ruleMatch = Matches(rule.Trigger, rule.ProgramPath, trigger, path);
                if (!ruleMatch) continue;
                if (rule.RunMode == RunMode.Confirm)
                    confirm.Add(rule.Id);
                else automatic.Add(rule.Id);
            }
            if (confirm.Count > 0) result.Add(new(folder.Id, confirm, false));
            if (automatic.Count > 0) result.Add(new(folder.Id, automatic, true));
        }
        return result;
    }

    private static bool Matches(TriggerMode configured, string configuredPath, TriggerMode trigger, string path)
        => configured != TriggerMode.Manual && configured == trigger
            && string.Equals(PathSafety.Normalize(configuredPath), path, StringComparison.OrdinalIgnoreCase);
}
