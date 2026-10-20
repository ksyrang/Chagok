using FileOrganizer.Core;

namespace Chagok.Terminal;

internal static class TuiReference
{
    private enum DestinationKind { Existing, New }
    public static async Task ShowAsync(TerminalSession session, IRecycleBin recycle, FolderSpec? selected, CancellationToken token)
    {
        var path = selected?.Path ?? ""; var root = ""; var recursive = false;
        var bracket = BracketKind.Square; var expression = "";
        var targets = new List<ReferenceTarget>(); var targetIndex = 0;
        ReferenceOrganization? result = null;
        var configuration = true; var scan = false;
        void Settings()
        {
            var nextPath = path; var nextRoot = root; var nextRecursive = recursive; var nextBracket = bracket; var nextExpression = expression;
            new TuiForm("기준 폴더 정리 설정", () => ("이번 검사 · 등록 설정 변경 없음", nextPath), token).Run([
                new("검사할 폴더 전체 경로", "01 · 정리할 파일", () => nextPath, value => nextPath = value, Description: () => "미등록 위치도 이번에만 사용합니다. 등록된 폴더와 규칙은 변경하지 않습니다."),
                new("정리된 기준 폴더 전체 경로", "02 · 분류와 이동 목적지", () => nextRoot, value => nextRoot = value),
                TuiForm.Toggle("검사 하위 폴더 포함", "01 · 정리할 파일", () => nextRecursive, value => nextRecursive = value),
                TuiForm.Choice("새 그룹 괄호 기준", "03 · 목적지 없는 파일", () => nextBracket, value => nextBracket = value, ["대괄호 [ ]", "소괄호 ( )", "중괄호 { }"]),
                new("새 그룹 정규식 (선택)", "03 · 목적지 없는 파일", () => nextExpression, value => nextExpression = value,
                    Description: () => "입력하면 괄호 기준 대신 사용합니다. (?<group>...) 캡처, 최대 2,000자/파일당 100ms. /clear로 비웁니다.", ExactInput: true)
            ], "이 조건으로 하위 목적지 폴더를 가져올까요? 파일은 이동하지 않습니다.",
                _ => ["검사: " + nextPath, "기준: " + nextRoot, "임시 검사 · 영구 규칙/설정 저장 없음"], () =>
                {
                    var source = ReferenceOrganization.PrepareSource(selected, nextPath, session.Storage.DirectoryPath);
                    ValidateRoot(nextRoot, source, session.Storage.DirectoryPath);
                    FilenameGroups.Validate(new(nextBracket, nextRecursive, nextExpression));
                    var loaded = ReferenceOrganization.ReadTargets(nextRoot, token)
                        .Where(t => !PathSafety.IsWithin(t.Path, session.Storage.DirectoryPath))
                        .Select(t => targets.FirstOrDefault(old => PathSafety.Comparer.Equals(old.Path, t.Path)) ?? t).ToList();
                    if (loaded.Count == 0) throw new ArgumentException("기준 폴더에 링크가 아닌 하위 폴더가 없습니다.");
                    path = source.Path; root = PathSafety.Normalize(nextRoot); recursive = nextRecursive; bracket = nextBracket; expression = nextExpression;
                    targets = loaded; targetIndex = 0;
                }, "tui");
        }
        try
        {
            Settings();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (configuration)
                {
                    var target = targets[targetIndex];
                    var labels = targets.Select(t => Path.GetFileName(t.Path) + " · " + (t.Match == ReferenceMatch.Contains ? "포함" : "괄호 일치") + " · " + t.Keywords.Replace('\n', ';')).ToArray();
                    Draw("기준 폴더 · 목적지 검색어", path, labels, targetIndex, $"목적지 {targets.Count}개 · 기준: {root}",
                        "↑↓ 선택 · E 검색어 편집 · I 전체 경로/검색어", "R 분석 · C 검사 조건 · Esc 시작 화면");
                    var input = TerminalText.Key(token); var key = input.Key;
                    if (TuiHelp.IsHelp(input)) { TuiHelp.Show(token); continue; }
                    if (key is ConsoleKey.Escape or ConsoleKey.Q) return;
                    if (key == ConsoleKey.UpArrow) targetIndex = Math.Max(0, targetIndex - 1);
                    if (key == ConsoleKey.DownArrow) targetIndex = Math.Min(targets.Count - 1, targetIndex + 1);
                    if (key == ConsoleKey.PageUp) targetIndex = Math.Max(0, targetIndex - 10);
                    if (key == ConsoleKey.PageDown) targetIndex = Math.Min(targets.Count - 1, targetIndex + 10);
                    if (key == ConsoleKey.I) TuiAnalysis.Details(target.Path + "\n" + target.Match + "\n" + target.Keywords, token);
                    if (key == ConsoleKey.C) { try { Settings(); } catch (FormCanceledException) { } }
                    if (key == ConsoleKey.E)
                    {
                        var words = target.Keywords.Replace('\n', ';'); var match = target.Match;
                        try
                        {
                            new TuiForm("목적지 검색어 편집", () => (Path.GetFileName(target.Path), target.Path), token).Run([
                                TuiForm.Choice("검색 방법", "검색 기준", () => match, value => match = value, ["파일명에 포함 (대소문자 무시)", "괄호 값 일치 (대소문자 구분)"]),
                                new("검색어·별칭 (; 구분)", "검색 기준", () => words, value => words = value,
                                    Description: () => "한 개 이상 입력합니다. 괄호 일치는 [A], (A), {A} 형식입니다. ;로 여러 검색어를 구분합니다.")
                            ], "이번 분석에 검색어를 적용할까요?", _ => [target.Path, words], () =>
                            {
                                var values = words.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                                if (values.Length == 0) throw new ArgumentException("검색어를 입력하세요.");
                                if (match == ReferenceMatch.Group) foreach (var value in values) FilenameGroups.ValidateKey(value);
                                targets[targetIndex] = target with { Keywords = string.Join('\n', values), Match = match };
                            }, "tui");
                        }
                        catch (FormCanceledException) { }
                    }
                    if (key == ConsoleKey.R) { configuration = false; scan = true; }
                    if (configuration) continue;
                }
                var folder = ReferenceOrganization.PrepareSource(selected, path, session.Storage.DirectoryPath);
                if (scan)
                {
                    result?.Dispose(); result = null;
                    TerminalText.Header("기준 폴더 분석 중 · 파일 이동 없음"); TerminalText.Line("Ctrl+C 안전하게 중단 · 설정은 저장하지 않습니다.");
                    var exclusions = session.Settings.Folders.SelectMany(f => f.Rules).Select(DestinationPaths.Resolve).Append(session.Storage.DirectoryPath).ToArray();
                    result = await Task.Run(() => ReferenceOrganization.Scan(folder, root, targets, exclusions,
                        Path.Combine(session.Storage.DirectoryPath, "scan-cache"), new(bracket, recursive, expression), token), token);
                    scan = false;
                }
                var next = await ReviewAsync(session, recycle, folder, result!, targets, token);
                if (next == ConsoleKey.Escape) return;
                if (next == ConsoleKey.C) configuration = true;
                if (next == ConsoleKey.R) scan = true;
            }
        }
        finally { result?.Dispose(); }
    }
    private static void ValidateRoot(string root, FolderSpec source, string data)
    {
        if (PathSafety.IsWithin(root, data)) throw new ArgumentException("차곡 데이터 저장소는 기준 폴더로 사용할 수 없습니다.");
        if (PathSafety.IsWithin(source.Path, root)) throw new ArgumentException("검사 위치는 기준 폴더와 같거나 그 안에 둘 수 없습니다.");
    }
    private static async Task<ConsoleKey> ReviewAsync(TerminalSession session, IRecycleBin recycle, FolderSpec folder,
        ReferenceOrganization result, IReadOnlyList<ReferenceTarget> targets, CancellationToken token)
    {
        var pane = 0; var index = 0; long page = 0; var filter = ReferenceFilter.All; string? groupKey = null;
        while (true)
        {
            var groups = pane == 1 ? result.Groups(page) : [];
            var files = pane == 0 ? result.Files(groupKey is null ? filter : ReferenceFilter.Unmatched, page, groupKey) : [];
            var count = pane == 1 ? result.GroupCount : groupKey is not null ? result.GroupFileCount(groupKey) : result.Count(filter);
            var pages = Math.Max(1, (count + 99) / 100);
            if (page >= pages) { page = pages - 1; index = 0; continue; }
            var labels = pane == 1 ? groups.Select(g => g.Key + $" · {g.Count}개").ToArray()
                : files.Select(f => f.Name + " · " + f.Status + (f.Destination.Length > 0 ? " → " + Path.GetFileName(f.Destination) : "")).ToArray();
            index = Math.Clamp(index, 0, Math.Max(0, labels.Length - 1));
            var summary = $"전체 {result.Count()} · 결정 {result.Count(ReferenceFilter.Decided)} · 선택 필요 {result.Count(ReferenceFilter.Ambiguous)} · 목적지 없음 {result.Count(ReferenceFilter.Unmatched)} · 경고 {result.WarningCount}";
            Draw(pane == 1 ? "새 그룹 · 목적지 없는 파일" : "기준 폴더 정리 결과 · " + (groupKey is null ? FilterLabel(filter) : groupKey), folder.Path, labels, index,
                summary, $"{page + 1}/{pages}페이지 · Tab 파일/그룹 · ↑↓ 선택 · N/P 페이지 · I 상세",
                "D 목적지 지정 · H 보류 · F 필터/그룹 해제 · W 경고", "V 이동 계획 검토 · R 재분석 · C 조건/검색어 · Esc 시작");
            var input = TerminalText.Key(token); var key = input.Key;
            if (TuiHelp.IsHelp(input)) { TuiHelp.Show(token); continue; }
            if (key is ConsoleKey.Escape or ConsoleKey.Q or ConsoleKey.R or ConsoleKey.C) return key == ConsoleKey.Q ? ConsoleKey.Escape : key;
            if (key == ConsoleKey.Tab) { pane = 1 - pane; page = 0; index = 0; groupKey = null; }
            if (key == ConsoleKey.UpArrow) index = Math.Max(0, index - 1);
            if (key == ConsoleKey.DownArrow) index = Math.Min(Math.Max(0, labels.Length - 1), index + 1);
            if (key == ConsoleKey.N && page + 1 < pages) { page++; index = 0; }
            if (key == ConsoleKey.P && page > 0) { page--; index = 0; }
            if (key == ConsoleKey.F) { if (groupKey is null) filter = (ReferenceFilter)(((int)filter + 1) % 4); groupKey = null; page = 0; index = 0; }
            if (key == ConsoleKey.W) TuiAnalysis.Details($"분석 경고 {result.WarningCount}개 · 최대 20개 표시\n" + string.Join('\n', result.Warnings), token);
            var file = files.ElementAtOrDefault(index); var group = groups.ElementAtOrDefault(index);
            if (key == ConsoleKey.Enter && pane == 1 && group is not null) { groupKey = group.Key; pane = 0; page = 0; index = 0; }
            if (key == ConsoleKey.I)
            {
                if (pane == 0 && file is not null) TuiAnalysis.Details(file.Path + "\n최종 목적지: " + file.Destination + "\n" + file.Reason + "\n" + string.Join('\n', result.Candidates(file.Id).Select(c => c.Destination + " · " + c.Reason)), token);
                if (pane == 1 && group is not null) TuiAnalysis.Details(group.Key + $" · 미분류 {group.Count}개\nEnter로 멤버 확인 · D로 모든 페이지의 미분류 멤버 목적지 지정", token);
            }
            if (key == ConsoleKey.H && pane == 0 && file is not null) result.ClearAssignment(file.Id);
            if (key == ConsoleKey.D && (pane == 0 && file is not null || pane == 1 && group is not null))
            {
                try { Assign(result, targets, pane == 0 ? file : null, pane == 1 ? group : null, token); }
                catch (FormCanceledException) { }
            }
            if (key == ConsoleKey.V)
            {
                var decided = result.Count(ReferenceFilter.Decided);
                if (decided == 0) { TuiAnalysis.Details("목적지가 결정된 파일이 없습니다. D로 목적지를 지정하세요.", token); continue; }
                if (!Tui.Confirm($"목적지 결정 {decided}개를 검토합니다. 미결정 {result.Count() - decided}개는 제외합니다. 계속할까요?", token)) continue;
                using var plan = await Task.Run(() => result.CreatePlan(Path.Combine(session.Storage.DirectoryPath, "scan-cache"), token), token);
                await Tui.ReviewPlanAsync(session, recycle, folder, plan, "기준 폴더 · 확정 목적지", token);
                return ConsoleKey.Escape;
            }
        }
    }
    private static void Assign(ReferenceOrganization result, IReadOnlyList<ReferenceTarget> targets, ReferenceFile? file, ReferenceGroup? group, CancellationToken token)
    {
        var kind = group is null ? DestinationKind.Existing : DestinationKind.New; var selected = 0;
        var name = group?.Key ?? "";
        if (result.GroupOptions.CustomPattern.Length == 0 && name.Length > 1) name = name[1..^1];
        if (file is not null) selected = Math.Max(0, targets.ToList().FindIndex(t => PathSafety.Comparer.Equals(t.Path, file.Destination)));
        string Destination() => kind == DestinationKind.New ? ReferenceOrganization.NewDestination(result.Root, name) : targets[selected].Path;
        new TuiForm("목적지 지정", () => (group?.Key ?? file!.Name, group is null ? file!.Path : result.Root), token).Run([
            TuiForm.Choice("목적지 종류", "이동 목적지", () => kind, value => kind = value, ["기존 하위 폴더", "새 폴더명"]),
            new("기존 목적지 (←→ 선택)", "이동 목적지", () => targets[selected].Path, Change: delta => selected = (selected + delta + targets.Count) % targets.Count, Enabled: () => kind == DestinationKind.Existing),
            new("새 폴더명", "이동 목적지", () => name, value => name = value, Enabled: () => kind == DestinationKind.New)
        ], group is null ? "이 파일의 목적지를 지정할까요? 이동은 다음 검토에서 실행합니다." : $"그룹의 미분류 {group.Count}개 전체 목적지를 지정할까요? 기존 결정은 유지합니다.",
            _ => [group?.Key ?? file!.Path, Destination()], () =>
            { if (group is not null) result.AssignGroup(group.Key, Destination()); else result.Assign(file!.Id, Destination()); }, "tui");
    }
    private static string FilterLabel(ReferenceFilter filter) => filter switch
    { ReferenceFilter.All => "전체", ReferenceFilter.Decided => "목적지 결정", ReferenceFilter.Ambiguous => "선택 필요", _ => "목적지 없음" };
    private static void Draw(string title, string path, IReadOnlyList<string> labels, int index, string summary, params string[] controls)
    {
        var width = TerminalText.Width; var count = Math.Max(1, TerminalText.Height - controls.Length - 7);
        var first = index / count * count;
        var rows = new List<string> { title, "검사: " + path, summary, Dashboard.Top("목록 · 파일 이동 없음", width) };
        for (var i = first; i < first + count; i++) rows.Add(Dashboard.Row((i == index ? "> " : "  ") + (labels.ElementAtOrDefault(i) ?? ""), width));
        rows.Add(Dashboard.Bottom(width)); rows.AddRange(controls);
        Dashboard.Render(rows, selections: labels.Count == 0 ? [] : [new(4 + index - first, 2, width - 4, true)]);
    }
}
