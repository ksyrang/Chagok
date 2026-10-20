using FileOrganizer.Core;

namespace Chagok.Terminal;

internal static class TuiConditionEditor
{
    internal const string Help = "괄호 종류 → 내용 제한 없음/조건 지정 → 그룹 이름에 괄호 포함 여부를 선택합니다. "
        + "제한 없음은 빈·중첩·닫히지 않은 괄호를 제외합니다. 조건 지정의 값은 정규식이 아닌 글자 그대로 입력하며 OR/AND로 결합합니다. "
        + "켜면 [업무], 끄면 업무가 그룹 이름입니다. 분석 결과와 그 결과로 만든 규칙에 같은 기준을 적용합니다. "
        + "대소문자·공백을 구분합니다. 조건은 1~10개, 값은 1~100자입니다. 제한 없음을 골라도 작성한 조건을 지우지 않습니다. "
        + "S 검토 후 Y 적용, Esc 취소입니다.";

    internal static FilenameConditionSet? Show(FilenameConditionSet? original, BracketKind brackets,
        Func<(string Name, string Path)> context, CancellationToken token)
    {
        var draft = original is null ? FilenameConditionSet.Unrestricted(brackets) : Storage.Clone(original);
        var sample = "[xyz][x][y] 보고서.pdf";
        var form = new TuiForm("분류 조건 선택", context, token);
        string Validation()
        {
            try { _ = draft.Compile(); return ""; }
            catch (ArgumentException ex) { return "입력 확인: " + ex.Message + " "; }
        }
        string Expression()
        {
            try { return draft.LimitContent ? draft.Compile() : "제한 없음 · 정규식 대신 괄호를 분류합니다. 빈·중첩·닫히지 않은 괄호는 제외합니다."; }
            catch (ArgumentException ex) { return "입력 확인: " + ex.Message; }
        }
        string Preview()
        {
            try
            {
                var result = FilenameGroups.Preview(sample, draft.ToOptions());
                return "일치 문자열: " + (result.Matches.Count == 0 ? "없음" : string.Join(" · ", result.Matches))
                    + " / 그룹: " + (result.Groups.Count == 0 ? "없음" : string.Join(" · ", result.Groups));
            }
            catch (ArgumentException ex) { return "입력 확인: " + ex.Message; }
        }
        IReadOnlyList<TuiForm.Field> Fields()
        {
            var fields = new List<TuiForm.Field>
            {
                TuiForm.Choice("대상 괄호", "01 · 비교 대상", () => draft.Brackets, value => draft.Brackets = value, ["[ ] 대괄호", "( ) 소괄호", "{ } 중괄호"])
                    with { Description = () => Help },
                new("괄호 안 내용", "01 · 비교 대상", () => draft.LimitContent ? "조건 지정" : "제한 없음 · 모두 분류",
                    Change: _ => draft.LimitContent = !draft.LimitContent, Description: () => Help),
                new("그룹 이름에 괄호 포함", "01 · 비교 대상", () => draft.IncludeBrackets ? "켜짐 · [업무]" : "꺼짐 · 업무",
                    Change: _ => draft.IncludeBrackets = !draft.IncludeBrackets, Description: () => Help)
            };
            if (draft.LimitContent)
            {
                fields.Add(new("조건 결합", "02 · 조건", () => draft.All ? "AND · 모두 만족" : "OR · 하나 이상 만족",
                    Change: _ => draft.All = !draft.All, Description: () => Help));
                for (var i = 0; i < draft.Terms.Count; i++)
                {
                    var term = draft.Terms[i];
                    fields.Add(TuiForm.Choice($"조건 {i + 1} 종류", "02 · 조건", () => term.Kind, value => term.Kind = value,
                        ["시작하는", "정확히 같은", "끝나는", "포함하는"]) with { Description = () => Help });
                    fields.Add(new($"조건 {i + 1} 값", "02 · 조건", () => term.Value, value => term.Value = value,
                        Description: () => Validation() + "괄호를 빼고 내용만 입력하세요. 기호도 글자 그대로 비교하며 대소문자·공백을 구분합니다. 1~100자.", ExactInput: true));
                    fields.Add(new($"조건 {i + 1} 삭제", "02 · 조건", () => "Enter로 이 조건 삭제",
                        Change: _ => draft.Terms.Remove(term), Enabled: () => draft.Terms.Count > 1,
                        Description: () => "선택한 조건만 지웁니다. 적용 전에는 기존 분석이나 규칙이 바뀌지 않습니다.",
                        DisabledReason: () => "조건은 하나 이상 필요합니다."));
                }
                fields.Add(new("조건 추가", "02 · 조건", () => $"{draft.Terms.Count}/10개 · Enter로 추가",
                    Change: _ => draft.Terms.Add(new FilenameCondition { Kind = FilenameConditionKind.Contains }),
                    Enabled: () => draft.Terms.Count < 10, Description: () => "새 조건의 종류와 값을 입력하세요.",
                    DisabledReason: () => "조건은 최대 10개입니다."));
            }
            fields.Add(new("시험할 파일명", "03 · 미리보기", () => sample, value => sample = value,
                Description: () => "실제 파일은 읽지 않습니다. 입력한 파일명에서 일치 문자열과 그룹을 확인합니다.", ExactInput: true));
            fields.Add(new("미리보기 결과", "03 · 미리보기", Preview, Description: () => Help));
            fields.Add(new("생성된 정규식", "04 · 생성식 조회", Expression,
                Description: () => "I로 생성식 또는 제한 없음 처리 방식을 확인합니다. 조건 값의 기호는 글자 그대로 비교합니다. " + Help));
            return fields;
        }
        try
        {
            return form.Run(Fields(), "이 분류 조건을 적용할까요?", width =>
                new[] { draft.Summary, "시험 파일명: " + sample, Preview(), "생성식: " + Expression() }
                    .SelectMany(line => TuiDetails.Wrap(line, width)).ToArray(),
                () => { var options = draft.ToOptions(); FilenameGroups.Validate(options); _ = FilenameGroups.Preview(sample, options); },
                "analyze", Fields) ? Storage.Clone(draft) : null;
        }
        catch (FormCanceledException) { return null; }
    }
}
