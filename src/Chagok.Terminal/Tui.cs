using FileOrganizer.Core;

namespace Chagok.Terminal;

internal sealed class Tui(TerminalSession session, IRecycleBin recycle, CancellationToken token)
{
    public async Task RunAsync()
    {
        var visible = OperatingSystem.IsWindows() ? Console.CursorVisible : true;
        try
        {
            Console.CursorVisible = false;
            var folderIndex = 0; var ruleIndex = 0; var pane = 0;
            var status = "대기 중";
            void SelectFolder(Guid? id)
            {
                if (id is null) return;
                folderIndex = session.Settings.Folders.FindIndex(f => f.Id == id);
                ruleIndex = 0; pane = 0;
                status = "폴더 저장 완료";
            }
            void SelectRule(Guid? id, Guid folderId)
            {
                if (id is null) return;
                folderIndex = session.Settings.Folders.FindIndex(f => f.Id == folderId);
                ruleIndex = session.Folder(folderId.ToString()).Rules.FindIndex(r => r.Id == id);
                pane = 1; status = "규칙 저장 완료 · 새 목록에 반영했습니다";
            }
            while (true)
            {
                var folders = session.Settings.Folders;
                folderIndex = Math.Clamp(folderIndex, 0, Math.Max(0, folders.Count - 1));
                var folder = folders.ElementAtOrDefault(folderIndex);
                ruleIndex = Math.Clamp(ruleIndex, 0, Math.Max(0, (folder?.Rules.Count ?? 0) - 1));
                var rule = folder?.Rules.ElementAtOrDefault(ruleIndex);
                if (!ViewportReady(token)) return;
                Dashboard.Draw(folders, folderIndex, ruleIndex, pane, status);
                var input = TerminalText.Key(token);
                var key = input.Key;
                try
                {
                    if (TuiHelp.IsHelp(input)) { TuiHelp.Show(token, pane == 0 ? "folders add" : "rules add"); continue; }
                    if (key == ConsoleKey.Q) return;
                    if (key == ConsoleKey.Escape) { if (pane == 0) return; pane = 0; continue; }
                    if (key is ConsoleKey.Tab or ConsoleKey.LeftArrow or ConsoleKey.RightArrow) { pane = 1 - pane; continue; }
                    if (key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
                    {
                        var delta = key == ConsoleKey.UpArrow ? -1 : 1;
                        if (pane == 0) { folderIndex = Math.Clamp(folderIndex + delta, 0, Math.Max(0, folders.Count - 1)); ruleIndex = 0; }
                        else ruleIndex = Math.Clamp(ruleIndex + delta, 0, Math.Max(0, (folder?.Rules.Count ?? 0) - 1));
                        continue;
                    }
                    if (key == ConsoleKey.H) History();
                    if (key == ConsoleKey.S) { status = TuiSettings.Show(session, token); continue; }
                    if (key == ConsoleKey.F) { SelectFolder(EditFolder(null, false)); continue; }
                    if (key == ConsoleKey.N)
                    {
                        if (folder is null) Notice("먼저 F로 검사 폴더를 추가하세요.");
                        else SelectRule(EditRule(folder, null), folder.Id);
                        continue;
                    }
                    if (key == ConsoleKey.W && Confirm("저장된 자동 실행 조건은 확인 없이 이동할 수 있습니다. 프로그램 감지를 시작할까요?"))
                        await WatchRunner.RunAsync(session, recycle, token, null, false, true);
                    if (folder is null) continue;
                    if (key == ConsoleKey.Enter && pane == 0) { pane = 1; continue; }
                    if (key == ConsoleKey.E)
                    {
                        if (pane == 0) SelectFolder(EditFolder(folder, false));
                        else if (rule is not null) SelectRule(EditRule(folder, rule), folder.Id);
                        continue;
                    }
                    if (key == ConsoleKey.C) { SelectFolder(EditFolder(folder, true)); continue; }
                    if (key == ConsoleKey.D)
                    {
                        if (pane == 0 && Confirm($"‘{folder.Name}’과 규칙 {folder.Rules.Count}개 설정을 제거할까요? 실제 파일은 유지합니다.")) session.RemoveFolder(folder.Id);
                        if (pane == 1 && rule is not null && Confirm($"‘{rule.Name}’ 규칙 설정을 제거할까요?")) session.RemoveRule(folder.Id, rule.Id);
                    }
                    if (key == ConsoleKey.Spacebar && pane == 1 && rule is not null)
                    { var copy = Storage.Clone(rule); copy.Enabled = !copy.Enabled; session.PutRule(folder.Id, copy); }
                    if (key == ConsoleKey.I) TuiDetails.Show(folder, pane == 1 ? rule : null, token);
                    if (key == ConsoleKey.A)
                        await PreviewAsync(session, recycle, folder, null, token);
                    else if (key == ConsoleKey.R || key == ConsoleKey.Enter && pane == 1)
                    {
                        if (rule is null) { Notice("규칙이 없습니다. N으로 규칙을 추가하세요."); continue; }
                        pane = 1;
                        if (!rule.Enabled) { Notice("꺼진 규칙입니다. Space로 켠 뒤 검사하세요."); continue; }
                        await PreviewAsync(session, recycle, folder, new HashSet<Guid> { rule.Id }, token);
                    }
                }
                catch (FormCanceledException) { status = "입력 취소 · 저장하지 않고 돌아왔습니다"; }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { Notice(ex.Message); }
            }
        }
        finally { Console.CursorVisible = visible; Console.ResetColor(); Console.WriteLine(); }
    }

    internal static async Task PreviewAsync(TerminalSession session, IRecycleBin recycle, FolderSpec folder,
        IReadOnlySet<Guid>? rules, CancellationToken token, bool automatic = false)
    {
        TerminalText.Header("폴더 검사 중"); TerminalText.Line(folder.Name + " · 아직 파일을 이동하지 않습니다. Ctrl+C로 취소");
        using var plan = await Task.Run(() => session.Scan(folder, rules, token), token);
        if (automatic)
        {
            if (plan.SelectedCount > 0) await ExecuteAsync(session, recycle, plan, token);
            return;
        }
        long page = 0; var index = 0;
        while (true)
        {
            var rows = plan.ReadPage(page * 100);
            index = Math.Clamp(index, 0, Math.Max(0, rows.Count - 1));
            if (!ViewportReady(token)) return;
            var scope = rules is null ? "폴더 전체 · 켜진 규칙" : rules.Count == 1
                ? "선택 규칙 · " + folder.Rules.First(r => rules.Contains(r.Id)).Name : $"선택 규칙 {rules.Count}개";
            Dashboard.Preview(folder, rows, index, page, plan, scope);
            var input = TerminalText.Key(token);
            if (TuiHelp.IsHelp(input)) { TuiHelp.Show(token, "run"); continue; }
            var key = input.Key;
            if (key is ConsoleKey.Escape or ConsoleKey.Q) return;
            if (key == ConsoleKey.UpArrow) index = Math.Max(0, index - 1);
            if (key == ConsoleKey.DownArrow) index = Math.Min(Math.Max(0, rows.Count - 1), index + 1);
            if (key == ConsoleKey.Spacebar && rows.Count > 0) plan.SetSelected(rows[index].Position, !rows[index].Selected);
            if (key == ConsoleKey.A) plan.SetAllSelected(true);
            if (key == ConsoleKey.U) plan.SetAllSelected(false);
            if (key == ConsoleKey.N && (page + 1) * 100 < plan.Count) { page++; index = 0; }
            if (key == ConsoleKey.P && page > 0) { page--; index = 0; }
            if (key == ConsoleKey.I && rows.Count > 0) ShowEntry(rows[index], token);
            if (key == ConsoleKey.Enter && plan.SelectedCount > 0 && Confirm($"전체 페이지의 선택 {plan.SelectedCount}개를 이동합니다. 기존 대상 교체 {plan.SelectedReplaceCount}개. 실행할까요?", token))
            {
                var report = await ExecuteAsync(session, recycle, plan, token);
                long resultPage = 0; var resultIndex = 0;
                while (true)
                {
                    TerminalText.Header("파일별 처리 결과");
                    TerminalText.Line(report.Title);
                    TerminalText.Line(report.CountsText);
                    if (report.Error is { } error) TerminalText.Line(error);
                    TerminalText.Line("↑↓ 항목 · N/P 페이지 · I 상세 · Esc/Enter 돌아가기");
                    var results = plan.ReadPage(resultPage * 100);
                    DrawList(results.Select(row => ($"{row.Result?.Status ?? "미실행/미확인"} · {Path.GetFileName(row.Entry.Source)}", row.Result?.Message ?? row.Entry.Reason)).ToArray(), resultIndex);
                    var resultKey = TerminalText.Key(token).Key;
                    if (resultKey is ConsoleKey.Escape or ConsoleKey.Enter or ConsoleKey.Q) return;
                    if (resultKey == ConsoleKey.UpArrow) resultIndex = Math.Max(0, resultIndex - 1);
                    if (resultKey == ConsoleKey.DownArrow) resultIndex = Math.Min(Math.Max(0, results.Count - 1), resultIndex + 1);
                    if (resultKey == ConsoleKey.N && (resultPage + 1) * 100 < plan.Count) { resultPage++; resultIndex = 0; }
                    if (resultKey == ConsoleKey.P && resultPage > 0) { resultPage--; resultIndex = 0; }
                    if (resultKey == ConsoleKey.I && results.Count > 0) ShowEntry(results[resultIndex], token);
                }
            }
        }
    }

    private static async Task<ExecutionReport> ExecuteAsync(TerminalSession session, IRecycleBin recycle, DiskPlanStore plan, CancellationToken token)
    {
        TerminalText.Header("파일 이동 중 · Ctrl+C 안전하게 중단");
        var report = await session.ExecuteAsync(plan, recycle, Commands.Progress, token);
        Console.WriteLine(); TerminalText.Line(report.Title); TerminalText.Line(report.CountsText);
        if (report.Error is { } error) TerminalText.Line(error);
        TerminalText.Line("완료한 이동은 되돌리지 않습니다. 아무 키나 눌러 결과 확인");
        if (!token.IsCancellationRequested) TerminalText.Key(token);
        return report;
    }

    private Guid? EditFolder(FolderSpec? original, bool duplicate)
    {
        var folder = original is null ? new FolderSpec() : duplicate ? FolderDuplicator.Create(original) : Storage.Clone(original);
        var form = new TuiForm(duplicate ? "검사 폴더 복제" : original is null ? "검사 폴더 추가" : "검사 폴더 편집", () => (folder.Name, folder.Path), token);
        var rebasePath = folder.Path;
        var fields = new TuiForm.Field[]
        {
            new("표시 이름", "01 · 기본 정보", () => folder.Name, value => folder.Name = value),
            new("검사할 폴더 전체 경로", "01 · 기본 정보", () => folder.Path, value =>
            {
                var exclusions = rebasePath.Length > 0 && value.Length > 0 && !PathSafety.Comparer.Equals(rebasePath, value)
                    ? FolderDuplicator.RebaseExclusions(rebasePath, value, folder.ExcludedFolders) : folder.ExcludedFolders;
                folder.Path = value; folder.ExcludedFolders = exclusions;
                if (value.Length > 0) rebasePath = value;
            }),
            new("제외할 하위 폴더 (; 구분)", "02 · 제외 폴더", () => string.Join(';', folder.ExcludedFolders),
                value => folder.ExcludedFolders = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()),
            TuiForm.Choice("실행 조건", "04 · 실행 조건", () => folder.Trigger, value => folder.Trigger = value, ["수동", "프로그램 시작", "프로그램 종료"]),
            new("감지할 .exe 전체 경로", "04 · 실행 조건", () => folder.ProgramPath, value => folder.ProgramPath = value, Enabled: () => folder.Trigger != TriggerMode.Manual),
            TuiForm.Choice("감지 후 처리", "04 · 실행 조건", () => folder.RunMode, value => folder.RunMode = value,
                ["목록 확인 후 실행", "자동 실행"], () => folder.Trigger != TriggerMode.Manual),
        };
        return form.Run(fields, duplicate ? "이동 목적지는 원본과 같습니다. 폴더 설정과 규칙을 복제할까요?" : "폴더 설정을 저장할까요?",
            width => TuiDetails.FolderLines(folder, width), () => session.PutFolder(folder), original is null ? "folders add" : "folders update") ? folder.Id : null;
    }

    private Guid? EditRule(FolderSpec folder, RuleSpec? original)
    {
        var rule = original is null ? new RuleSpec { Match = NameMatch.Any } : Storage.Clone(original);
        var form = new TuiForm(original is null ? "규칙 추가" : "규칙 편집", () => (folder.Name, folder.Path), token);
        var fields = new TuiForm.Field[]
        {
            new("규칙 이름", "01 · 기본 정보", () => rule.Name, value => rule.Name = value),
            TuiForm.Toggle("규칙 사용", "01 · 기본 정보", () => rule.Enabled, value => rule.Enabled = value),
            TuiForm.Toggle("하위 폴더도 검사", "02 · 파일 조건", () => rule.Recursive, value => rule.Recursive = value),
            TuiForm.Choice("파일명 조건", "02 · 파일 조건", () => rule.Match, value => rule.Match = value, ["단어 포함", "특정 단어로 시작", "패턴 (*, ?)", "제한 없음"]),
            new("단어 또는 패턴", "02 · 파일 조건", () => rule.Pattern, value => rule.Pattern = value, Enabled: () => rule.Match != NameMatch.Any),
            new("확장자 (.pdf,.jpg)", "02 · 파일 조건", () => rule.Extensions, value => rule.Extensions = value),
            new("이동할 폴더 전체 경로", "03 · 이동 동작", () => rule.Destination, value => rule.Destination = value),
            TuiForm.Toggle("대상 폴더 자동 생성", "03 · 이동 동작", () => rule.CreateDestination, value => rule.CreateDestination = value),
            TuiForm.Choice("같은 이름의 파일", "03 · 이동 동작", () => rule.Conflict, value => rule.Conflict = value, ["번호 추가", "휴지통으로 보내고 교체", "건너뛰기"]),
            TuiForm.Choice("실행 조건", "04 · 실행 조건", () => rule.Trigger, value => rule.Trigger = value, ["수동", "프로그램 시작", "프로그램 종료"]),
            new("감지할 .exe 전체 경로", "04 · 실행 조건", () => rule.ProgramPath, value => rule.ProgramPath = value, Enabled: () => rule.Trigger != TriggerMode.Manual),
            TuiForm.Choice("감지 후 처리", "04 · 실행 조건", () => rule.RunMode, value => rule.RunMode = value,
                ["목록 확인 후 실행", "자동 실행"], () => rule.Trigger != TriggerMode.Manual),
        };
        return form.Run(fields, "위 검사 폴더에 규칙을 저장할까요?", width => TuiDetails.RuleLines(folder, rule, width),
            () => session.PutRule(folder.Id, rule), original is null ? "rules add" : "rules update") ? rule.Id : null;
    }
    private void History()
    {
        var history = session.Storage.ReadHistory(); var index = 0;
        while (true)
        {
            TerminalText.Header("실행 기록 / 최근 500개");
            TerminalText.Line("↑↓ 선택 · Enter 상세 · Esc 돌아가기");
            DrawList(history.Select(r => ($"{r.TimeUtc.ToLocalTime():MM-dd HH:mm:ss} {r.Status}", r.Rule + " · " + r.Source)).ToArray(), index);
            var key = TerminalText.Key(token).Key;
            if (key is ConsoleKey.Escape or ConsoleKey.Q) return;
            if (key == ConsoleKey.UpArrow) index = Math.Max(0, index - 1);
            if (key == ConsoleKey.DownArrow) index = Math.Min(Math.Max(0, history.Count - 1), index + 1);
            if (key == ConsoleKey.Enter && history.Count > 0)
            {
                var row = history[index]; TerminalText.Header("실행 기록 상세");
                Console.WriteLine(TerminalText.Clean($"{row.TimeUtc.ToLocalTime():O} · {row.Folder} · {row.Rule}"));
                foreach (var value in new[] { row.Status, row.Source, row.Target, row.Message }) Console.WriteLine(TerminalText.Clean(value));
                TerminalText.Key(token);
            }
        }
    }
    private static bool ViewportReady(CancellationToken token)
    {
        while (TerminalText.Width < 60 || TerminalText.Height < 24)
        {
            TerminalText.Header("터미널 크기 조정");
            TerminalText.Line("최소 60열 × 24줄이 필요합니다.");
            TerminalText.Line("창을 늘린 뒤 아무 키나 누르세요.");
            TerminalText.Line("Q / Esc: 돌아가기");
            if (TerminalText.Key(token).Key is ConsoleKey.Q or ConsoleKey.Escape) return false;
        }
        return true;
    }

    private static void DrawList((string Title, string Description)[] items, int selected)
    {
        var count = Math.Max(1, (TerminalText.Height - 14) / 2);
        TerminalText.Line(Dashboard.Top("목록", TerminalText.Width));
        var first = selected / count * count;
        foreach (var (item, index) in items.Select((v, i) => (v, i)).Skip(first).Take(count))
        {
            if (index == selected) { Console.ForegroundColor = ConsoleColor.White; Console.BackgroundColor = ConsoleColor.DarkCyan; }
            TerminalText.Line(Dashboard.Row((index == selected ? "> " : "  ") + item.Title, TerminalText.Width)); Console.ResetColor();
            TerminalText.Line(Dashboard.Row("  " + item.Description, TerminalText.Width));
        }
        TerminalText.Line(Dashboard.Bottom(TerminalText.Width));
        if (items.Length > 0) TerminalText.Line($"{selected + 1}/{items.Length}");
    }
    private static void ShowEntry(StoredPlanEntry row, CancellationToken token)
    {
        TerminalText.Header("파일 상세 · 전체 경로");
        foreach (var text in new[] { "원본: " + row.Entry.Source, "대상: " + row.Entry.Target,
            "규칙: " + row.Entry.RuleName, "예정 작업: " + row.Entry.Action, "판단: " + row.Entry.Reason,
            "선택: " + row.Selected, "처리 결과: " + (row.Result?.Status ?? "미실행/미확인"), row.Result?.Message ?? "" })
            Console.WriteLine(TerminalText.Clean(text));
        TerminalText.Line("아무 키나 누르면 목록으로 돌아갑니다."); TerminalText.Key(token);
    }
    private bool Confirm(string message) => Confirm(message, token);
    internal static bool Confirm(string message, CancellationToken token)
    {
        TerminalText.Line(message); TerminalText.Line("Y: 확인 · 다른 키: 취소"); return TerminalText.Key(token).Key == ConsoleKey.Y;
    }
    private void Notice(string message) { TerminalText.Header("확인 필요"); Console.WriteLine(TerminalText.Clean(message)); TerminalText.Line("아무 키나 누르면 돌아갑니다."); TerminalText.Key(token); }
}
