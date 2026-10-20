using System.IO.Enumeration;

namespace FileOrganizer.Core;

public static class RuleMatcher
{
    public static string[] ParseExtensions(string text)
    {
        var parts = text.Split([',', ';', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Select(part =>
        {
            var value = part.Trim().TrimStart('*');
            if (!value.StartsWith('.')) value = "." + value;
            if (value.Length < 2 || value.IndexOfAny(['*', '?', '/', '\\', ':']) >= 0)
                throw new ArgumentException("확장자는 .pdf, .jpg처럼 쉼표로 구분하세요.");
            return value;
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool Matches(RuleSpec rule, string path, int depth)
    {
        if (!rule.Enabled || (!rule.Recursive && depth > 0)) return false;
        var name = System.IO.Path.GetFileName(path);
        var matched = rule.Match switch
        {
            NameMatch.Any => true,
            NameMatch.Contains => name.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase),
            NameMatch.StartsWith => name.StartsWith(rule.Pattern, StringComparison.OrdinalIgnoreCase),
            NameMatch.Glob => FileSystemName.MatchesSimpleExpression(rule.Pattern, name, ignoreCase: true),
            NameMatch.Group => (rule.GroupExpression.Length == 0
                ? FilenameGroups.Extract(name, FilenameGroups.ValidateKey(rule.Pattern))
                : FilenameGroups.Extract(name, new AnalysisOptions(CustomPattern: rule.GroupExpression))).Contains(rule.Pattern),
            _ => false
        };
        var extensions = ParseExtensions(rule.Extensions);
        return matched && (extensions.Length == 0 || extensions.Contains(
            System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
    }
}
