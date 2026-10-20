namespace FileOrganizer.Core;

public static class PathSafety
{
    public static StringComparer Comparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path))
            throw new ArgumentException("폴더는 드라이브를 포함한 전체 경로로 지정하세요.");
        var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        if (OperatingSystem.IsWindows() && (full.StartsWith(@"\\.\", StringComparison.Ordinal)
            || full.StartsWith(@"\\?\", StringComparison.Ordinal)))
            throw new ArgumentException("장치 경로는 사용할 수 없습니다.");
        return full;
    }

    public static bool IsWithin(string path, string parent)
    {
        path = Normalize(path);
        parent = Normalize(parent);
        return Comparer.Equals(path, parent) || path.StartsWith(
            System.IO.Path.EndsInDirectorySeparator(parent) ? parent : parent + System.IO.Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    public static void AssertNoLinks(string path)
    {
        string? current = Normalize(path);
        while (current is not null)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"링크·연결 폴더는 처리하지 않습니다: {current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = System.IO.Path.GetDirectoryName(current);
        }
    }

    public static List<string> NormalizeExclusions(string source, IEnumerable<string> paths)
    {
        source = Normalize(source);
        var result = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Normalize(p.Trim()))
            .Distinct(Comparer).ToList();
        if (result.Any(p => !IsWithin(p, source) || Comparer.Equals(p, source)))
            throw new ArgumentException("제외 폴더는 검사 폴더 안의 하위 폴더로 지정하세요.");
        return result;
    }

    public static void ValidateRule(FolderSpec folder, RuleSpec rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Name)) throw new ArgumentException("규칙 이름을 입력하세요.");
        var source = Normalize(folder.Path);
        var target = DestinationPaths.Resolve(rule);
        if (Comparer.Equals(source, target)) throw new ArgumentException("검사 폴더와 이동 폴더가 같습니다.");
        AssertNoLinks(source);
        AssertNoLinks(target);
        if (!Enum.IsDefined(rule.Match) || !Enum.IsDefined(rule.Conflict)
            || !Enum.IsDefined(rule.Trigger) || !Enum.IsDefined(rule.RunMode))
            throw new ArgumentException("지원하지 않는 규칙 옵션입니다.");
        if (rule.Match != NameMatch.Any && string.IsNullOrWhiteSpace(rule.Pattern))
            throw new ArgumentException("파일명 조건을 입력하거나 ‘제한 없음’을 선택하세요.");
        _ = RuleMatcher.ParseExtensions(rule.Extensions);
        if (rule.Match == NameMatch.Group)
        {
            if (rule.GroupConditions is { } conditions && !string.Equals(conditions.Compile(), rule.GroupExpression, StringComparison.Ordinal))
                throw new ArgumentException("저장할 분류 조건과 정규식이 다릅니다. 조건을 다시 적용하세요.");
            if (rule.GroupConditions is { } selected) FilenameGroups.Validate(selected.ToOptions());
            else if (rule.GroupExpression.Length == 0) _ = FilenameGroups.ValidateKey(rule.Pattern);
            else FilenameGroups.Validate(new AnalysisOptions(CustomPattern: rule.GroupExpression));
        }
        if (rule.Trigger != TriggerMode.Manual)
        {
            var program = Normalize(rule.ProgramPath);
            if (!program.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("감지할 프로그램의 .exe 파일을 선택하세요.");
        }
    }
}
