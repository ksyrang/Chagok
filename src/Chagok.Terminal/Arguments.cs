namespace Chagok.Terminal;

public sealed class Arguments
{
    public List<string> Words { get; } = [];
    public Dictionary<string, string> Options { get; } = new(StringComparer.Ordinal);
    public bool Json => Options.ContainsKey("json");
    public bool Yes => Options.ContainsKey("yes");
    public string? Get(string name) => Options.GetValueOrDefault(name);
    public string Require(string name) => Get(name) ?? throw new ArgumentException($"--{name} 값을 지정하세요.");
    public int Number(string name, int fallback, int min = 1, int max = 500)
    {
        if (Get(name) is not { } value) return fallback;
        if (!int.TryParse(value, out var number) || number < min || number > max)
            throw new ArgumentException($"--{name} 값은 {min}~{max} 정수여야 합니다.");
        return number;
    }
    public bool Boolean(string name, bool fallback) => Get(name) switch
    { null => fallback, "on" or "true" => true, "off" or "false" => false, _ => throw new ArgumentException($"--{name} 값은 on/off입니다.") };
    public void Allow(params string[] names)
    {
        var allowed = names.Concat(["data-dir", "json"]).ToHashSet(StringComparer.Ordinal);
        foreach (var key in Options.Keys)
            if (!allowed.Contains(key)) throw new ArgumentException($"지원하지 않는 옵션: --{key}");
    }
    public static Arguments Parse(string[] args)
    {
        var parsed = new Arguments(); var help = false;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--help" or "-h") { help = true; continue; }
            if (arg == "--version") { parsed.Words.Add("version"); continue; }
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (arg.StartsWith('-')) throw new ArgumentException("지원하지 않는 옵션: " + arg);
                parsed.Words.Add(arg); continue;
            }
            var key = arg[2..];
            var value = "true";
            if (key is not ("yes" or "json"))
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"--{key} 값이 없습니다.");
                value = args[i];
            }
            if (!parsed.Options.TryAdd(key, value)) throw new ArgumentException("옵션 중복: --" + key);
        }
        if (help && (parsed.Words.FirstOrDefault() != "help" || parsed.Words.Count == 1)) parsed.Words.Insert(0, "help");
        return parsed;
    }
}
