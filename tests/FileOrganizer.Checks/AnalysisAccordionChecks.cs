using FileOrganizer.Core;
using FileOrganizer.Windows;

static class AnalysisAccordionChecks
{
    public static void Register(Action<string, Action> add)
    {
        void Check(bool value) { if (!value) throw new Exception("분석 아코디언 전환 오류"); }
        add("AnalysisAccordion 처음에는 설정만 펼침", () =>
        {
            var s = new AnalysisAccordionState();
            Check(s.Editing && !s.HasResults && !s.Running); s.ShowResults(); Check(s.Editing);
        });
        add("AnalysisAccordion 분석 시작은 설정을 접고 진행 화면을 표시", () =>
        {
            var s = new AnalysisAccordionState(); s.Begin();
            Check(!s.Editing && s.Running && !s.HasResults); s.Edit(); Check(!s.Editing);
        });
        add("AnalysisAccordion 성공 뒤 결과·설정 왕복은 결과를 유지", () =>
        {
            var s = new AnalysisAccordionState(); s.Begin(); s.Loaded(); s.Finish();
            Check(!s.Running && s.HasResults && !s.Editing);
            s.Edit(); Check(s.Editing && s.HasResults); s.ShowResults(); Check(!s.Editing && s.HasResults);
        });
        add("AnalysisAccordion 취소·오류는 빈 결과 대신 설정 복원", () =>
        {
            var s = new AnalysisAccordionState(); s.Begin(); s.Finish();
            Check(s.Editing && !s.HasResults && !s.Running);
            s.Loaded(); s.Clear(); Check(s.Editing && !s.HasResults);
        });
        add("AnalysisAccordion 변경 표시와 재분석 완료는 구분", () =>
        {
            var s = new AnalysisAccordionState(); s.Loaded(); s.Edit(); s.MarkChanged(true);
            Check(s.HasResults && s.Changed); s.ShowResults(); Check(s.Changed);
            s.MarkChanged(false); Check(!s.Changed); s.Begin(); s.Loaded(); Check(!s.Changed);
        });
        add("AnalysisAccordion 실제 분석 기준 비교는 숨긴 초안·시험명과 분리", () =>
        {
            var native = FilenameConditionSet.Unrestricted(); var options = native.ToOptions();
            var same = Storage.Clone(native); same.Terms[0].Value = "숨긴 초안";
            Check(AnalysisAccordionState.SameConditions(options, same.ToOptions()));
            same.IncludeBrackets = false; Check(!AnalysisAccordionState.SameConditions(options, same.ToOptions()));
            Check(!AnalysisAccordionState.SameConditions(options, options with { Recursive = true }));
            Check(!AnalysisAccordionState.SameConditions(options, options with { Brackets = BracketKind.Round }));
            Check(!AnalysisAccordionState.SameConditions(options, new AnalysisOptions(CustomPattern: "x")));
            var limited = new FilenameConditionSet().ToOptions();
            Check(AnalysisAccordionState.SameConditions(limited, limited with { Conditions = null }));
            var round = new FilenameConditionSet { Brackets = BracketKind.Round }.ToOptions();
            Check(AnalysisAccordionState.SameConditions(round, new AnalysisOptions(CustomPattern: round.CustomPattern)));
        });
    }
}
