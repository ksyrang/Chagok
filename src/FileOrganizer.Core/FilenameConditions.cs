using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FileOrganizer.Core;

[JsonConverter(typeof(JsonStringEnumConverter<FilenameConditionKind>))]
public enum FilenameConditionKind { StartsWith, Equal, EndsWith, Contains }

public sealed class FilenameCondition
{
    public FilenameConditionKind Kind { get; set; }
    public string Value { get; set; } = "";
}

public sealed class FilenameConditionSet
{
    public BracketKind Brackets { get; set; } = BracketKind.Square;
    // 기존 조건 JSON은 내용 제한 사용·괄호 없는 그룹으로 읽는다.
    public bool LimitContent { get; set; } = true;
    public bool IncludeBrackets { get; set; }
    public bool All { get; set; }
    public List<FilenameCondition> Terms { get; set; } =
        [new() { Kind = FilenameConditionKind.StartsWith, Value = "x" }, new() { Kind = FilenameConditionKind.Equal, Value = "y" }];

    public static string KindLabel(FilenameConditionKind kind) => kind switch
    {
        FilenameConditionKind.StartsWith => "시작하는", FilenameConditionKind.Equal => "정확히 같은",
        FilenameConditionKind.EndsWith => "끝나는", FilenameConditionKind.Contains => "포함하는",
        _ => throw new ArgumentException("지원하지 않는 분류 조건입니다.")
    };

    public static FilenameConditionSet Unrestricted(BracketKind brackets = BracketKind.Square) =>
        new() { Brackets = brackets, LimitContent = false, IncludeBrackets = true };

    public string Summary => $"{FilenameGroups.Pair(Brackets).Open} {FilenameGroups.Pair(Brackets).Close} 안 · "
        + (LimitContent ? string.Join(All ? " 그리고 " : " 또는 ", Terms.Select(t => $"‘{t.Value}’ {KindLabel(t.Kind)}")) : "내용 제한 없음")
        + (IncludeBrackets ? " · 괄호 포함" : " · 내용만");

    public string Compile()
    {
        var (open, close) = FilenameGroups.Pair(Brackets);
        // 제한 없음은 기존 괄호 파서로 중첩·닫히지 않은 괄호를 제외한다.
        if (!LimitContent) return "";
        if (Terms is null || Terms.Count is < 1 or > 10) throw new ArgumentException("분류 조건은 1~10개로 설정하세요.");
        var unit = Brackets switch { BracketKind.Square => @"[^\[\]]", BracketKind.Round => @"[^()]", _ => @"[^{}]" };
        var left = Regex.Escape(open.ToString()); var right = Regex.Escape(close.ToString());
        var parts = new List<string>();
        foreach (var term in Terms)
        {
            if (term is null || string.IsNullOrWhiteSpace(term.Value) || term.Value.Length > 100
                || term.Value.Any(char.IsControl) || term.Value.Contains(open) || term.Value.Contains(close))
                throw new ArgumentException("조건 값은 괄호 안의 내용만 1~100자로 입력하세요. 대상 괄호·줄바꿈·빈 값은 사용할 수 없습니다.");
            var value = Regex.Escape(term.Value);
            parts.Add(term.Kind switch
            {
                FilenameConditionKind.StartsWith => value + unit + "*",
                FilenameConditionKind.Equal => value,
                FilenameConditionKind.EndsWith => unit + "*" + value,
                FilenameConditionKind.Contains => unit + "*" + value + unit + "*",
                _ => throw new ArgumentException("지원하지 않는 분류 조건입니다.")
            });
        }
        // 각 조건의 끝을 같은 닫는 괄호에 고정해 서로 다른 구분값을 AND로 합치지 않는다.
        var checks = All ? string.Concat(parts.Select(p => "(?=" + p + right + ")"))
            : "(?=(?:" + string.Join("|", parts) + ")" + right + ")";
        var pattern = IncludeBrackets ? "(?<group>" + left + checks + unit + "+" + right + ")"
            : left + "(?<group>" + checks + unit + "+)" + right;
        if (pattern.Length > 2000) throw new ArgumentException("조건으로 만든 식이 2,000자를 넘었습니다. 조건이나 값을 줄이세요.");
        return pattern;
    }

    public AnalysisOptions ToOptions(bool recursive = false) => new(Brackets, recursive, Compile(), Storage.Clone(this));
}
