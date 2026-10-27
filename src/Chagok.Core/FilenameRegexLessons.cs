namespace Chagok.Core;

public sealed record FilenameRegexChoice(string Title, string Fragment, string Explanation, string Input, string ExpectedGroup);

public sealed record FilenameRegexLesson(string Title, string Prefix, string Fragment, string Suffix,
    string Sample, string ExpectedGroup, string Description, string PracticeFiles, IReadOnlyList<FilenameRegexChoice> Choices)
{
    public string Pattern => Compose(Fragment);
    public string Compose(string fragment)
    {
        var pattern = Prefix + fragment + Suffix;
        if (pattern.Length > 2000) throw new ArgumentException("정규식은 고정 문법을 포함해 최대 2,000자입니다.");
        return pattern;
    }
    // 알려진 예시 구조만 분리한다. 전체 식의 문법을 임의로 추측해서 강조하지 않는다.
    public bool TryGetFragment(string pattern, out string fragment)
    {
        fragment = "";
        if (pattern.Length < Prefix.Length + Suffix.Length || !pattern.StartsWith(Prefix, StringComparison.Ordinal)
            || !pattern.EndsWith(Suffix, StringComparison.Ordinal)) return false;
        fragment = pattern.Substring(Prefix.Length, pattern.Length - Prefix.Length - Suffix.Length); return true;
    }
}

public static class FilenameRegexLessons
{
    private const string BracketFiles = "[xyz] 문서.pdf\n[x] 짧은 이름.pdf\n[y] 문서.pdf\n[yellow] 문서.pdf\n[공고문] 채용.pdf";
    public static IReadOnlyList<FilenameRegexLesson> All { get; } = Array.AsReadOnly(new[]
    {
        new FilenameRegexLesson("01  괄호를 포함해 묶기", @"\[(?:", @"[^\[\]]+", @")\]",
            "[공고문] 채용 안내.pdf", "[공고문]", "강조한 부분은 괄호 안에서 찾을 내용입니다. 그룹 이름에는 괄호까지 포함합니다.", BracketFiles, BracketChoices(true)),
        new FilenameRegexLesson("02  괄호 안의 글자만 묶기", @"\[(?<group>", @"[^\[\]]+", @")\]",
            "[공고문] 채용 안내.pdf", "공고문", "강조한 부분만 바꾸세요. 앞의 여는 괄호 문법과 뒤의 닫는 괄호 문법은 그대로 둡니다. 그룹 이름에서는 괄호가 빠집니다.", BracketFiles, BracketChoices(false)),
        new FilenameRegexLesson("03  밑줄 앞의 이름으로 묶기", @"^(?<group>", @"[^_]+", ")_",
            "업무_2026_계획.pdf", "업무", "강조한 부분은 첫 번째 밑줄 앞에서 찾을 내용입니다. 밑줄 뒤쪽 파일명이 달라도 같은 접두사로 묶습니다.",
            "업무_2026_계획.pdf\n업무지원_회의록.pdf\n개인_사진.jpg", new FilenameRegexChoice[]
            {
                new("밑줄 앞 전체", @"[^_]+", "[^_]+는 밑줄이 아닌 글자를 한 글자 이상 찾습니다. 밑줄 앞 내용을 모두 그룹으로 만듭니다.", "업무_계획.pdf", "업무"),
                new("업무로 시작", @"업무[^_]*", "강조 칸 맨 앞에 업무를 넣고 뒤를 [^_]*로 두면 업무와 업무지원 등을 찾습니다. 업무를 다른 글자로 바꿔 보세요.", "업무지원_회의.pdf", "업무지원"),
                new("개인만", "개인", "강조 칸을 개인으로 바꾸면 밑줄 바로 앞 내용이 개인인 파일을 찾습니다. 개인을 원하는 이름으로 바꿔 보세요.", "개인_사진.jpg", "개인")
            }),
        new FilenameRegexLesson("04  날짜에서 연도만 묶기", @"(?<group>", @"\d{4}", @")-\d{2}-\d{2}",
            "회의록_2026-10-20.pdf", "2026", "강조한 부분은 연도 조건입니다. 뒤의 -월-일 문법은 그대로 두고 연도 부분만 바꿉니다.",
            "회의록_2026-10-20.pdf\n보고서_2026-11-01.pdf\n계획_2025-01-01.pdf", new FilenameRegexChoice[]
            {
                new("모든 연도", @"\d{4}", @"\d{4}는 숫자 네 자리입니다. 연도가 다른 파일도 찾고 연도별로 나눕니다.", "회의록_2026-10-20.pdf", "2026"),
                new("2026년만", "2026", "강조 칸을 2026으로 바꾸면 2026년 날짜만 찾습니다. 2026을 원하는 연도로 바꿔 보세요.", "회의록_2026-10-20.pdf", "2026"),
                new("2025 또는 2026", "2025|2026", "|는 또는입니다. 강조 칸에 2025|2026을 넣으면 두 연도를 찾습니다. 뒤의 월·일 문법은 바꾸지 않습니다.", "계획_2025-01-01.pdf", "2025")
            })
    });

    private static IReadOnlyList<FilenameRegexChoice> BracketChoices(bool includeBrackets)
    {
        string Key(string text) => includeBrackets ? "[" + text + "]" : text;
        return new FilenameRegexChoice[]
        {
            new("내용 전체", @"[^\[\]]+", @"[^\[\]]+는 괄호가 아닌 글자를 한 글자 이상 찾습니다. 특정 시작 글자를 요구하지 않습니다.", "[공고문] 채용.pdf", Key("공고문")),
            new("x로 시작", @"x[^\[\]]*", @"강조 칸을 x[^\[\]]*로 바꿉니다. 앞의 x가 시작 글자이고 *는 뒤의 글자가 없어도 된다는 뜻입니다. x를 원하는 시작 글자로 바꿔 보세요.", "[xyz] 문서.pdf", Key("xyz")),
            new("y와 정확히 같음", "y", "강조 칸을 y만 남기면 내용이 정확히 y인 경우만 찾습니다. yellow는 찾지 않습니다. y를 원하는 내용으로 바꿔 보세요.", "[y] 문서.pdf", Key("y")),
            new("x 시작 또는 y", @"x[^\[\]]*|y", @"강조 칸에 x[^\[\]]*|y를 넣습니다. |는 또는이므로 xyz·x·y를 찾고 yellow는 찾지 않습니다. x와 y를 다른 글자로 바꿔 보세요.", "[x] 문서.pdf", Key("x"))
        };
    }
}
