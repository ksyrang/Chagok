using FileOrganizer.Core;

namespace Chagok.Terminal;

public static class Dashboard
{
    public sealed record Selection(int Line, int Column, int Width, bool Active);
    public static void Draw(IReadOnlyList<FolderSpec> folders, int folderIndex, int ruleIndex, int pane, string status = "대기 중")
        => Render(Build(TerminalText.Width, TerminalText.Height, folders, folderIndex, ruleIndex, pane, status), brand: true, selections: Selections(TerminalText.Width, TerminalText.Height, folders, folderIndex, ruleIndex, pane));

    public static IReadOnlyList<string> Build(int width, int height, IReadOnlyList<FolderSpec> folders,
        int folderIndex, int ruleIndex, int pane, string status = "대기 중")
    {
        var lines = new List<string>(BrandBanner.Build(width)) { "차곡 · CHAGOK · by ksyrang  /  " + status };
        var folder = folders.ElementAtOrDefault(folderIndex);
        var rule = folder?.Rules.ElementAtOrDefault(ruleIndex);
        var count = Math.Max(1, height - 21);
        var left = width >= 80 ? Math.Clamp(width / 4, 20, 28) : 0;
        var right = left == 0 ? width : width - left - 1;
        string Join(string a, string b) => left == 0 ? b : a + " " + b;
        lines.Add(Join(Top((pane == 0 ? "▶ " : "") + "검사 폴더", left), Top((pane == 1 ? "▶ " : "") + "규칙 · " + (folder?.Name ?? "폴더를 추가하세요"), right)));
        lines.Add(Join(Row("F 추가 · C 복제", left), Row($"규칙 {folder?.Rules.Count ?? 0}개 · N 규칙 추가", right)));
        var folderFirst = folderIndex / count * count;
        var ruleFirst = ruleIndex / count * count;
        for (var i = 0; i < count; i++)
        {
            var fi = folderFirst + i; var ri = ruleFirst + i;
            var f = fi < folders.Count ? (fi == folderIndex ? pane == 0 ? "> " : "· " : "  ") + folders[fi].Name : "";
            var r = folder?.Rules.ElementAtOrDefault(ri);
            var text = r is null ? i == 0 ? "규칙 없음 · N으로 추가" : ""
                : (ri == ruleIndex ? pane == 1 ? "> " : "· " : "  ") + (r.Enabled ? "[켜짐] " : "[꺼짐] ") + r.Name;
            if (left == 0 && pane == 0) text = f;
            lines.Add(Join(Row(f, left), Row(text, right)));
        }
        lines.Add(Join(Bottom(left), Bottom(right)));
        lines.Add(Top("선택 규칙 · 조건과 이동", width));
        lines.Add(Row("검사 위치  " + (folder?.Path ?? "F: 새 폴더 추가"), width));
        lines.Add(Row("선택 규칙  " + (rule?.Name ?? "없음"), width));
        lines.Add(Row("조건       " + (rule is null ? "—" : MatchLabel(rule) + " · 확장자 " + (rule.Extensions.Length == 0 ? "전체" : rule.Extensions)), width));
        lines.Add(Row("이동       " + (rule is null ? "—" : DestinationPaths.Preview(rule)), width));
        lines.Add(Bottom(width));
        lines.Add("[R 이 규칙만 검사 및 실행]  ·  이동 전 목록 확인");
        lines.Add("[A 폴더 전체 검사 및 실행]  [E 편집]");
        lines.Add("Tab 영역 · ↑↓ 선택 · Enter 규칙/검사 · Space 켜기/끄기");
        lines.Add("N 규칙 · F 폴더 · C 복제 · D 제거 · I 상세");
        lines.Add("F1/? 기능 도움말 · H 기록 · S 설정 · W 감지 · Q 종료");
        return lines;
    }
    public static IReadOnlyList<Selection> Selections(int width, int height, IReadOnlyList<FolderSpec> folders,
        int folderIndex, int ruleIndex, int pane)
    {
        var result = new List<Selection>();
        var count = Math.Max(1, height - 21);
        var left = width >= 80 ? Math.Clamp(width / 4, 20, 28) : 0;
        var folder = folders.ElementAtOrDefault(folderIndex);
        if (folder is not null && (left > 0 || pane == 0))
            result.Add(new(BrandBanner.Height + 3 + folderIndex % count, 2, (left > 0 ? left : width) - 4, pane == 0));
        if (folder?.Rules.ElementAtOrDefault(ruleIndex) is not null && (left > 0 || pane == 1))
            result.Add(new(BrandBanner.Height + 3 + ruleIndex % count, left > 0 ? left + 3 : 2,
                (left > 0 ? width - left - 1 : width) - 4, pane == 1));
        return result;
    }

    // Cell columns differ from string offsets when Korean names are displayed.
    public static (string Before, string Selected, string After) SplitSelection(string line, Selection selection)
    {
        var before = TerminalText.Fit(line, selection.Column);
        var selected = TerminalText.Fit(line[before.Length..], selection.Width);
        return (before, selected, line[(before.Length + selected.Length)..]);
    }

    private static string MatchLabel(RuleSpec rule) => rule.Match switch
    { NameMatch.Any => "파일명 제한 없음", NameMatch.Contains => "포함: " + rule.Pattern,
      NameMatch.StartsWith => "시작: " + rule.Pattern, _ => "패턴: " + rule.Pattern };

    internal static void Preview(FolderSpec folder, IReadOnlyList<StoredPlanEntry> rows, int selected,
        long page, DiskPlanStore plan, string scope)
    {
        var width = TerminalText.Width;
        var lines = new List<string> { "차곡  CHAGOK  /  검사 및 실행", Top(scope + " / " + folder.Name, width),
            Row($"대상 {plan.Count} · 선택 {plan.SelectedCount} · 교체 {plan.SelectedReplaceCount} · 경고 {plan.WarningCount}", width),
            Row("검사만으로 파일을 이동하지 않습니다.", width), Bottom(width), Top("이동 계획 · Space 선택", width) };
        var nameWidth = Math.Clamp((width - 6) / 3, 12, 40);
        lines.Add(Row(Pad("선택 / 파일", nameWidth) + " │ 처리 / 이동 위치", width));
        var count = Math.Max(1, TerminalText.Height - 17);
        var first = selected / count * count;
        for (var i = first; i < Math.Min(rows.Count, first + count); i++)
        {
            var row = rows[i];
            lines.Add(Row(Pad((i == selected ? ">" : " ") + (row.Selected ? "[✓] " : "[ ] ") + Path.GetFileName(row.Entry.Source), nameWidth)
                + " │ " + row.Entry.Action + " → " + row.Entry.Target, width));
        }
        for (var i = Math.Min(rows.Count - first, count); i < count; i++) lines.Add(Row(i == 0 && rows.Count == 0 ? "조건에 맞는 파일이 없습니다." : "", width));
        lines.Add(Bottom(width));
        lines.Add(rows.Count == 0 ? "" : "선택 파일: " + rows[selected].Entry.Source);
        lines.Add(rows.Count == 0 ? "" : "판단: " + rows[selected].Entry.Reason);
        lines.Add(plan.WarningCount == 0 ? "" : "검사 경고: " + string.Join(" / ", plan.ReadWarnings(2)));
        lines.Add($"페이지 {page + 1}/{Math.Max(1, (plan.Count + 99) / 100)} · 선택 {(rows.Count == 0 ? 0 : selected + 1)}/{rows.Count} · 전체 페이지 선택 유지");
        lines.Add("[Enter 선택 항목 이동]  [Esc 돌아가기]  [F1/? 기능 설명]");
        lines.Add("↑↓ 이동 · Space 선택 · A 전체 선택 · U 해제 · N/P 페이지 · I 상세");
        Render(lines, selections: rows.Count == 0 ? [] : [new Selection(7 + selected - first, 2, width - 4, true)]);
    }
    public static string Pad(string value, int cells) => TerminalText.Fit(TerminalText.Clean(value) + new string(' ', Math.Max(0, cells)), Math.Max(0, cells));
    public static string Top(string title, int width)
    {
        if (width < 4) return "";
        var label = TerminalText.Fit(" " + title + " ", width - 4);
        return "╭─" + label + new string('─', Math.Max(0, width - 4 - TerminalText.CellWidth(label))) + "─╮";
    }
    public static string Row(string text, int width) => width < 4 ? "" : "│ " + Pad(text, width - 4) + " │";
    public static string Bottom(int width) => width < 2 ? "" : "╰" + new string('─', width - 2) + "╯";

    internal static void Render(IReadOnlyList<string> lines, bool brand = false, IReadOnlyList<Selection>? selections = null)
    {
        var height = TerminalText.Height - 1; var width = TerminalText.Width;
        if (Console.IsOutputRedirected) { foreach (var line in lines) TerminalText.Line(line); return; }
        Console.SetCursorPosition(0, 0);
        var foreground = Console.ForegroundColor; var background = Console.BackgroundColor;
        var light = background is ConsoleColor.White or ConsoleColor.Gray or ConsoleColor.Yellow;
        var color = Environment.GetEnvironmentVariable("NO_COLOR") is null;
        try
        {
            for (var i = 0; i < height; i++)
            {
                var line = Pad(i < lines.Count ? lines[i] : "", width);
                if (color && brand && i < BrandBanner.Height)
                {
                    if (width >= 80)
                    {
                        Console.ForegroundColor = light ? ConsoleColor.DarkCyan : ConsoleColor.Cyan;
                        Console.Write(line[..BrandBanner.SymbolWidth]);
                        Console.ForegroundColor = light ? ConsoleColor.DarkBlue : ConsoleColor.Blue;
                        Console.Write(line[BrandBanner.SymbolWidth..]);
                    }
                    else { Console.ForegroundColor = light ? ConsoleColor.DarkBlue : ConsoleColor.Blue; Console.Write(line); }
                }
                else
                {
                    if (color && (line.Contains('╭') || line.Contains('╰')))
                        Console.ForegroundColor = light ? ConsoleColor.DarkCyan : ConsoleColor.Cyan;
                    var offset = 0;
                    foreach (var selection in (selections ?? []).Where(s => s.Line == i).OrderBy(s => s.Column))
                    {
                        var parts = SplitSelection(line, selection);
                        Console.Write(parts.Before[offset..]);
                        if (color)
                        {
                            Console.ForegroundColor = selection.Active ? ConsoleColor.White : light ? ConsoleColor.DarkCyan : ConsoleColor.Cyan;
                            Console.BackgroundColor = selection.Active ? ConsoleColor.DarkCyan : background;
                        }
                        Console.Write(parts.Selected);
                        Console.ForegroundColor = foreground; Console.BackgroundColor = background;
                        offset = parts.Before.Length + parts.Selected.Length;
                    }
                    Console.Write(line[offset..]);
                }
                Console.ForegroundColor = foreground; Console.BackgroundColor = background;
                if (i < height - 1) Console.WriteLine();
            }
        }
        finally { Console.ForegroundColor = foreground; Console.BackgroundColor = background; }
    }
}
