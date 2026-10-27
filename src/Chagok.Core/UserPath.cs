namespace Chagok.Core;

public static class UserPath
{
    private static string Comparable(string entry)
    {
        var path = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"')).Replace('/', '\\');
        return path.Length == 3 && path[1] == ':' && path[2] == '\\' ? path : path.TrimEnd('\\');
    }

    public static bool Contains(string value, string directory) => value.Split(';')
        .Any(entry => string.Equals(Comparable(entry), Comparable(directory), StringComparison.OrdinalIgnoreCase));

    public static string Update(string value, string directory, bool add)
    {
        if (string.IsNullOrWhiteSpace(directory) || directory.IndexOfAny([';', '"', '\r', '\n']) >= 0)
            throw new ArgumentException("PATH에 등록할 수 없는 폴더 경로입니다.");
        if (add)
        {
            if (Contains(value, directory)) return value;
            return value + (value.Length == 0 || value.EndsWith(';') ? "" : ";") + directory;
        }
        return string.Join(';', value.Split(';').Where(entry =>
            !string.Equals(Comparable(entry), Comparable(directory), StringComparison.OrdinalIgnoreCase)));
    }
}
