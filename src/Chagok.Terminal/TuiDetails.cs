using FileOrganizer.Core;

namespace Chagok.Terminal;

public static class TuiDetails
{
    public static IReadOnlyList<string> FolderLines(FolderSpec folder, int width)
    {
        var lines = new List<string>();
        Section(lines, width, "01 · 검사 위치", ("폴더 이름", folder.Name), ("전체 경로", folder.Path));
        Section(lines, width, "02 · 등록된 규칙", ("규칙 수", $"전체 {folder.Rules.Count}개 · 켜짐 {folder.Rules.Count(r => r.Enabled)}개"));
        Section(lines, width, "03 · 제외 폴더", folder.ExcludedFolders.Count == 0
            ? [("제외 경로", "등록된 제외 폴더 없음")]
            : folder.ExcludedFolders.Select((path, i) => ($"제외 {i + 1}", path)).ToArray());
        Trigger(lines, width, folder.Trigger, folder.ProgramPath, folder.RunMode);
        Section(lines, width, "05 · 설정 식별자", ("폴더 ID", folder.Id.ToString()));
        Section(lines, width, "06 · 최근 이동 위치", ("표시·기록", folder.UseRecentDestinations ? "켜짐 · 최근 10개" : "꺼짐"));
        if (folder.UseRecentDestinations)
            Section(lines, width, "최근 위치 목록", DestinationPaths.Recent(folder).Select((p, i) => ($"위치 {i + 1}", p)).ToArray());
        return lines;
    }

    public static IReadOnlyList<string> RuleLines(FolderSpec folder, RuleSpec rule, int width)
    {
        var lines = new List<string>();
        Section(lines, width, "01 · 규칙", ("규칙 이름", rule.Name), ("사용 상태", rule.Enabled ? "켜짐" : "꺼짐"),
            ("검사 폴더", folder.Name), ("검사 경로", folder.Path));
        Section(lines, width, "02 · 파일 조건", ("파일명 조건", rule.Match switch
        { NameMatch.Contains => "특정 단어 포함", NameMatch.StartsWith => "특정 단어로 시작", NameMatch.Glob => "패턴 일치 (*, ?)", NameMatch.Group => "분류 구분값 일치", _ => "파일명 제한 없음" }),
            ("단어 / 패턴", rule.Match == NameMatch.Any ? "사용하지 않음" : rule.Pattern),
            ("확장자", string.IsNullOrWhiteSpace(rule.Extensions) ? "전체" : rule.Extensions),
            ("하위 폴더 검사", rule.Recursive ? "포함" : "포함하지 않음"));
        if (rule.Match == NameMatch.Group && rule.GroupExpression.Length > 0)
            Section(lines, width, "분류 정규식", ("입력한 식", rule.GroupExpression));
        if (rule.Match == NameMatch.Group && rule.GroupConditions is { } conditions)
            Section(lines, width, "분류 조건 선택", ("조건 요약", conditions.Summary), ("정확히 비교할 그룹", rule.Pattern));
        Section(lines, width, "03 · 이동 동작", ("기본 이동 위치", rule.Destination),
            ("규칙명 폴더 추가", rule.AppendRuleName ? "켜짐" : "꺼짐"),
            ("최종 이동 폴더", DestinationPaths.Preview(rule)),
            ("대상 폴더 자동 생성", DestinationPaths.CreatesDirectory(rule) ? "켜짐" : "꺼짐"),
            ("같은 이름의 파일", rule.Conflict switch
            { ConflictMode.Replace => "기존 파일을 휴지통으로 옮기고 교체", ConflictMode.Skip => "건너뛰기", _ => "번호를 붙여 이름 변경 후 이동" }));
        Trigger(lines, width, rule.Trigger, rule.ProgramPath, rule.RunMode);
        Section(lines, width, "05 · 설정 식별자", ("규칙 ID", rule.Id.ToString()));
        return lines;
    }

    private static void Trigger(List<string> lines, int width, TriggerMode trigger, string program, RunMode mode)
    {
        var fields = new List<(string, string)> { ("실행 조건", trigger switch
        { TriggerMode.ProgramStarted => "프로그램 시작 시", TriggerMode.ProgramStopped => "프로그램 종료 시", _ => "수동 실행" }) };
        if (trigger != TriggerMode.Manual)
        {
            fields.Add(("감지할 프로그램", program));
            fields.Add(("감지 후 동작", mode == RunMode.Automatic ? "자동 실행" : "목록 확인 후 실행"));
        }
        Section(lines, width, "04 · 실행 조건", fields.ToArray());
    }

    private static void Section(List<string> lines, int width, string title, params (string Label, string Value)[] fields)
    {
        if (lines.Count > 0) lines.Add("");
        lines.Add(Dashboard.Top(title, width));
        foreach (var (label, value) in fields)
        {
            lines.Add(Dashboard.Row("▪ " + label, width));
            foreach (var part in Wrap(string.IsNullOrEmpty(value) ? "—" : value, width - 6))
                lines.Add(Dashboard.Row("  " + part, width));
        }
        lines.Add(Dashboard.Bottom(width));
    }

    public static IEnumerable<string> Wrap(string text, int width)
    {
        text = TerminalText.Clean(text);
        if (text.Length == 0) { yield return ""; yield break; }
        while (text.Length > 0)
        {
            var part = TerminalText.Fit(text, Math.Max(2, width));
            yield return part; text = text[part.Length..];
        }
    }

    internal static void Show(FolderSpec folder, RuleSpec? rule, CancellationToken token)
    {
        var offset = 0;
        while (true)
        {
            var width = Math.Min(100, TerminalText.Width);
            var content = rule is null ? FolderLines(folder, width) : RuleLines(folder, rule, width);
            var count = Math.Max(1, TerminalText.Height - 8);
            offset = Math.Clamp(offset, 0, Math.Max(0, content.Count - count));
            var rows = new List<string> { Dashboard.Top(rule is null ? "폴더 상세" : "규칙 상세", width) };
            rows.Add(Dashboard.Row(rule?.Name ?? folder.Name, width));
            rows.Add(Dashboard.Bottom(width));
            rows.AddRange(content.Skip(offset).Take(count));
            while (rows.Count < count + 3) rows.Add("");
            rows.Add($"{offset + 1}~{Math.Min(offset + count, content.Count)} / {content.Count}줄 · 상세보기는 설정을 변경하지 않습니다.");
            rows.Add("↑↓ 스크롤 · PgUp/PgDn 페이지 · Home/End 처음/끝");
            rows.Add("Esc / Enter 목록으로 · F1/? 기능 설명");
            Dashboard.Render(rows);
            var input = TerminalText.Key(token);
            if (TuiHelp.IsHelp(input)) { TuiHelp.Show(token, rule is null ? "folders update" : "rules update"); continue; }
            switch (input.Key)
            {
                case ConsoleKey.Escape or ConsoleKey.Enter or ConsoleKey.Q: return;
                case ConsoleKey.UpArrow: offset--; break;
                case ConsoleKey.DownArrow: offset++; break;
                case ConsoleKey.PageUp: offset -= count; break;
                case ConsoleKey.PageDown: offset += count; break;
                case ConsoleKey.Home: offset = 0; break;
                case ConsoleKey.End: offset = content.Count; break;
            }
        }
    }
}
