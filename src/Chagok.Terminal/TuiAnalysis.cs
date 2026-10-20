using FileOrganizer.Core;

namespace Chagok.Terminal;

internal sealed record AnalysisRuleRequest(Guid FolderId, RuleSpec Rule);
internal static class TuiAnalysis
{
    public static async Task<AnalysisRuleRequest?> ShowAsync(TerminalSession session, Guid? selected, CancellationToken token)
    {
        if (session.Settings.Folders.Count == 0) throw new ArgumentException("먼저 F로 검사 폴더를 추가하세요.");
        var options = FilenameConditionSet.Unrestricted().ToOptions();
        FilenameAnalysis? store = null;
        var folderIndex = Math.Max(0, session.Settings.Folders.FindIndex(f => f.Id == selected));
        var pane = 0; var groupIndex = 0; var fileIndex = 0; long groupPage = 0, filePage = 0;
        var minimum = 2; var rescan = true;
        IReadOnlyList<AnalysisFolder> folders = [];
        IReadOnlyList<AnalysisGroup> groups = [];
        IReadOnlyList<AnalysisFile> files = [];
        long groupCount = 0, fileCount = 0;
        void LoadGroups()
        {
            var id = folders[folderIndex].Id;
            groupCount = store!.GroupCount(id, minimum);
            groups = store.Groups(id, minimum, groupPage);
            groupIndex = Math.Clamp(groupIndex, 0, Math.Max(0, groups.Count - 1));
            LoadFiles();
        }
        void LoadFiles()
        {
            var group = groups.ElementAtOrDefault(groupIndex);
            fileCount = group is null ? 0 : store!.FileCount(folders[folderIndex].Id, group.Key);
            files = group is null ? [] : store!.Files(folders[folderIndex].Id, group.Key, filePage);
            fileIndex = Math.Clamp(fileIndex, 0, Math.Max(0, files.Count - 1));
        }
        try
        {
            while (true)
            {
                if (rescan)
                {
                    store?.Dispose(); store = null;
                    TerminalText.Header("파일명 분석 중 · 파일 이동 없음");
                    TerminalText.Line("등록 폴더의 이름을 분석합니다. Ctrl+C 취소");
                    store = await Task.Run(() => session.Analyze(options, token), token);
                    folders = store.Folders();
                    folderIndex = Math.Clamp(folderIndex, 0, folders.Count - 1);
                    groupPage = filePage = 0; groupIndex = fileIndex = 0;
                    LoadGroups(); rescan = false;
                }
                var folder = folders[folderIndex];
                var group = groups.ElementAtOrDefault(groupIndex);
                var width = TerminalText.Width; var height = TerminalText.Height;
                var count = Math.Max(1, height - 12);
                var classification = FilenameGroups.Description(options);
                var rows = new List<string>
                {
                    "차곡 · 파일명 분석 · 파일 이동 없음",
                    $"분류 {classification} · 하위 폴더 {(options.Recursive ? "포함" : "제외")} · {(minimum == 2 ? "2개 이상만" : "전체 그룹")} · 경고 {store!.WarningCount}개",
                    TerminalText.Fit($"{folder.Name} · 전체 {folder.Total} · 패턴 일치 {folder.Matched} · 그룹 {folder.Groups} · 패턴 없음 {folder.Unmatched}", width)
                };
                var selections = new List<Dashboard.Selection>();
                var left = width >= 100 ? 23 : 0; var center = left > 0 ? 27 : 0;
                var right = width - (left > 0 ? left + center + 2 : 0);
                string Cell(int p, string text) => p switch
                { 0 => Dashboard.Row(text, left > 0 ? left : width), 1 => Dashboard.Row(text, center > 0 ? center : width), _ => Dashboard.Row(text, right) };
                string Title(int p) => p switch
                { 0 => "폴더 · 일치/그룹", 1 => $"구분값 · {groupCount}그룹", _ => group is null ? "파일 목록" : $"{group.Key} · {fileCount}개" };
                rows.Add(left > 0 ? Dashboard.Top(Title(0), left) + " " + Dashboard.Top(Title(1), center) + " " + Dashboard.Top(Title(2), right) : Dashboard.Top(Title(pane), width));
                for (var i = 0; i < count; i++)
                {
                    var fi = folderIndex / count * count + i;
                    var gi = groupIndex / count * count + i;
                    var pi = fileIndex / count * count + i;
                    string Prefix(int p, bool chosen) => chosen ? pane == p ? "> " : "· " : "  ";
                    var ft = folders.ElementAtOrDefault(fi) is { } f ? Prefix(0, fi == folderIndex) + f.Name + $" {f.Matched}/{f.Groups}" : "";
                    var gt = groups.ElementAtOrDefault(gi) is { } g ? Prefix(1, gi == groupIndex) + g.Key + $"  {g.Count}개" : groups.Count == 0 && i == 0 ? "표시할 그룹 없음" : "";
                    var pt = files.ElementAtOrDefault(pi) is { } file ? Prefix(2, pi == fileIndex) + file.Name : files.Count == 0 && i == 0 ? "그룹을 선택하세요" : "";
                    rows.Add(left > 0 ? Cell(0, ft) + " " + Cell(1, gt) + " " + Cell(2, pt) : Cell(pane, pane == 0 ? ft : pane == 1 ? gt : pt));
                    var current = pane == 0 ? folderIndex % count : pane == 1 ? groupIndex % count : fileIndex % count;
                    var hasRow = pane == 0 || pane == 1 && groups.Count > 0 || pane == 2 && files.Count > 0;
                    if (i == current && hasRow)
                    {
                        var column = left == 0 || pane == 0 ? 2 : pane == 1 ? left + 3 : left + center + 4;
                        var span = left == 0 ? width : pane == 0 ? left : pane == 1 ? center : right;
                        selections.Add(new(rows.Count - 1, column, span - 4, true));
                    }
                }
                rows.Add(left > 0 ? Dashboard.Bottom(left) + " " + Dashboard.Bottom(center) + " " + Dashboard.Bottom(right) : Dashboard.Bottom(width));
                rows.Add(TerminalText.Fit("선택 폴더: " + folder.Path, width));
                rows.Add($"그룹 {groupPage + 1}/{Math.Max(1, (groupCount + 99) / 100)} · 파일 {filePage + 1}/{Math.Max(1, (fileCount + 99) / 100)} · 100개씩 읽기");
                rows.Add("Tab/←→ 영역 · ↑↓ 선택 · Enter 파일 목록 · N/P 페이지 · I 전체 값");
                rows.Add("M 2개 이상/전체 · G 조건으로 선택 · E 직접 입력 (정규식) · S 하위 폴더 · R 다시 분석");
                rows.Add("C 이 그룹으로 규칙 만들기 · W 경고 · Esc 목록 복귀 · F1 도움말");
                Dashboard.Render(rows, selections: selections);
                var input = TerminalText.Key(token); var key = input.Key;
                if (TuiHelp.IsHelp(input)) { TuiHelp.Show(token, "analyze"); continue; }
                if (key is ConsoleKey.Escape or ConsoleKey.Q) return null;
                if (key == ConsoleKey.Tab || key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
                { pane = (pane + (key == ConsoleKey.LeftArrow ? 2 : 1)) % 3; continue; }
                if (key == ConsoleKey.Enter) { pane = Math.Min(2, pane + 1); continue; }
                if (key == ConsoleKey.UpArrow || key == ConsoleKey.DownArrow)
                {
                    var delta = key == ConsoleKey.UpArrow ? -1 : 1;
                    if (pane == 0) { folderIndex = Math.Clamp(folderIndex + delta, 0, folders.Count - 1); groupPage = filePage = 0; groupIndex = fileIndex = 0; LoadGroups(); }
                    else if (pane == 1) { groupIndex = Math.Clamp(groupIndex + delta, 0, Math.Max(0, groups.Count - 1)); filePage = 0; fileIndex = 0; LoadFiles(); }
                    else fileIndex = Math.Clamp(fileIndex + delta, 0, Math.Max(0, files.Count - 1));
                }
                if (key == ConsoleKey.M) { minimum = minimum == 2 ? 1 : 2; groupPage = filePage = 0; groupIndex = fileIndex = 0; LoadGroups(); }
                if (key == ConsoleKey.S) { options = options with { Recursive = !options.Recursive }; rescan = true; }
                if (key is ConsoleKey.G or ConsoleKey.B)
                {
                    var conditions = TuiConditionEditor.Show(options.Conditions, options.Brackets, () => (folder.Name, folder.Path), token);
                    if (conditions is not null) { options = conditions.ToOptions(options.Recursive); rescan = true; }
                }
                if (key == ConsoleKey.E)
                {
                    var expression = options.CustomPattern.Length == 0 ? FilenameGroups.PatternExample : options.CustomPattern;
                    var sample = "[a] 보고서.pdf";
                    var form = new TuiForm("분류 정규식", () => (folder.Name, folder.Path), token);
                    string Preview()
                    {
                        try
                        {
                            var draft = options with { CustomPattern = expression, Conditions = null };
                            if (expression.Length == 0) return "정규식을 입력하세요.";
                            FilenameGroups.Validate(draft);
                            var keys = FilenameGroups.Extract(sample, draft);
                            return keys.Count == 0 ? "일치하는 구분값 없음" : string.Join(" · ", keys);
                        }
                        catch (ArgumentException ex) { return ex.Message; }
                    }
                    try
                    {
                        if (form.Run([
                            new("분류 정규식", "직접 입력", () => expression, value => expression = value, Description: () => FilenameGroups.PatternHelp, ExactInput: true),
                            new("시험할 파일명", "미리보기", () => sample, value => sample = value, Description: () => "실제 파일은 읽지 않습니다. 아래 미리보기에서 추출할 그룹을 확인하세요.", ExactInput: true),
                            new("미리보기 그룹", "미리보기", Preview, Description: () => FilenameGroups.PatternHelp)
                        ], "이 식으로 등록 폴더를 다시 분석할까요?", _ => [expression, sample, Preview()], () =>
                        {
                            if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException("분류 정규식을 입력하세요.");
                            var draft = options with { CustomPattern = expression, Conditions = null };
                            FilenameGroups.Validate(draft); _ = FilenameGroups.Extract(sample, draft); options = draft;
                        }, "analyze")) rescan = true;
                    }
                    catch (FormCanceledException) { }
                }
                if (key == ConsoleKey.R) rescan = true;
                if (key == ConsoleKey.N || key == ConsoleKey.P)
                {
                    var delta = key == ConsoleKey.N ? 1 : -1;
                    if (pane == 1 && groupPage + delta >= 0 && (groupPage + delta) * 100 < groupCount)
                    { groupPage += delta; groupIndex = fileIndex = 0; filePage = 0; LoadGroups(); }
                    if (pane == 2 && filePage + delta >= 0 && (filePage + delta) * 100 < fileCount)
                    { filePage += delta; fileIndex = 0; LoadFiles(); }
                }
                if (key == ConsoleKey.C && group is not null)
                    return new(folder.Id, FilenameGroups.Rule(session.Folder(folder.Id.ToString()), group.Key, options));
                if (key == ConsoleKey.I || key == ConsoleKey.W)
                {
                    var text = key == ConsoleKey.W ? string.Join("\n", store.Warnings().Prepend($"분석 경고 {store.WarningCount}개 · 최대 20개 표시"))
                        : pane == 0 ? folder.Path + "\n" + folder.Summary
                        : pane == 1 ? group?.Key ?? "그룹 없음" : files.ElementAtOrDefault(fileIndex)?.Path ?? "파일 없음";
                    Details(text, token);
                }
            }
        }
        finally { store?.Dispose(); }
    }
    internal static void Details(string text, CancellationToken token)
    {
        var offset = 0;
        while (true)
        {
            var width = TerminalText.Width; var count = Math.Max(1, TerminalText.Height - 5);
            var content = text.Split('\n').SelectMany(s => TuiDetails.Wrap(s, width - 4)).ToArray();
            offset = Math.Clamp(offset, 0, Math.Max(0, content.Length - count));
            var rows = new List<string> { Dashboard.Top("분석 상세 · 파일 이동 없음", width) };
            rows.AddRange(content.Skip(offset).Take(count).Select(s => Dashboard.Row(s, width)));
            rows.Add(Dashboard.Bottom(width)); rows.Add("↑↓ 스크롤 · Esc/Enter 돌아가기"); Dashboard.Render(rows);
            var key = TerminalText.Key(token).Key;
            if (key is ConsoleKey.Escape or ConsoleKey.Enter) return;
            if (key == ConsoleKey.UpArrow) offset--;
            if (key == ConsoleKey.DownArrow) offset++;
        }
    }
}
