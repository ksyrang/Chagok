namespace Chagok.Core;

public static class DestinationPaths
{
    public const int RecentLimit = 10;

    public static string Initial(FolderSpec folder, string? preset = null)
        => preset ?? Recent(folder).FirstOrDefault() ?? "";

    public static string Resolve(RuleSpec rule)
    {
        var parent = PathSafety.Normalize(rule.Destination.Trim());
        if (!rule.AppendRuleName) return parent;
        var name = rule.Name.Trim();
        var stem = name.Split('.')[0].TrimEnd();
        if (name.Length == 0 || name.Length > 255 || name is "." or ".." || name.EndsWith('.')
            || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c))
            || new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
            || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3]))
            throw new ArgumentException("규칙 이름을 폴더 이름으로 사용할 수 없습니다. 경로 구분자, 금지 문자, 예약 이름과 끝의 마침표를 확인하세요.");
        return Path.Combine(parent, name);
    }

    // 화면에 표시된 최종 경로에서 이미 붙어 있는 규칙명 한 개만 제거한다.
    public static string BaseFromDisplay(string display, string name, bool append)
    {
        var path = display.Trim();
        if (!append || path.Length == 0) return path;
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        return PathSafety.Comparer.Equals(Path.GetFileName(trimmed), name.Trim())
            ? Path.GetDirectoryName(trimmed) ?? path : path;
    }

    public static bool CreatesDirectory(RuleSpec rule) => rule.AppendRuleName || rule.CreateDestination;

    public static string Preview(RuleSpec rule)
    {
        try { return Resolve(rule); }
        catch (ArgumentException ex) { return "입력 확인 필요: " + ex.Message; }
    }

    public static IReadOnlyList<string> Recent(FolderSpec folder) => folder.UseRecentDestinations
        ? folder.RecentDestinations.Where(Path.IsPathFullyQualified).Select(PathSafety.Normalize)
            .Distinct(PathSafety.Comparer).Take(RecentLimit).ToArray() : [];

    public static void Remember(FolderSpec folder, RuleSpec rule)
    {
        if (!folder.UseRecentDestinations) return;
        // 규칙명 하위 폴더를 반복해서 붙이지 않도록 사용자가 고른 기본 위치만 기록한다.
        var parent = PathSafety.Normalize(rule.Destination.Trim());
        folder.RecentDestinations = new[] { parent }.Concat(folder.RecentDestinations)
            .Distinct(PathSafety.Comparer).Take(RecentLimit).ToList();
    }
}
