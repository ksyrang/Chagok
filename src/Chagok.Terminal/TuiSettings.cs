using Chagok.Core;

namespace Chagok.Terminal;

public static class TuiSettings
{
    private static readonly int[] FieldRows = [4, 8, 9, 13];
    private static readonly string[][] Descriptions =
    [
        ["GUI에서 사용할 테마를 선택합니다.", "TUI 색상은 터미널 설정을 따릅니다."],
        ["GUI 창을 닫을 때의 동작을 선택합니다.", "트레이 유지 또는 프로그램 종료입니다."],
        ["GUI 종료 확인창을 표시할지 선택합니다.", "창 닫기 동작이 ‘종료’일 때 적용됩니다."],
        ["다음 GUI 시작 때 탐색기 메뉴를 확인합니다.", "이 화면에서는 메뉴를 등록하지 않습니다."],
    ];

    public static void Change(AppSettings draft, int field, int direction)
    {
        switch (field)
        {
            case 0: draft.Theme = (ThemeMode)(((int)draft.Theme + direction + 3) % 3); break;
            case 1: draft.CloseAction = draft.CloseAction == WindowCloseAction.Exit ? WindowCloseAction.HideToTray : WindowCloseAction.Exit; break;
            case 2: draft.ConfirmExitOnClose = !draft.ConfirmExitOnClose; break;
            case 3: draft.AutoRegisterExplorerMenu = !draft.AutoRegisterExplorerMenu; break;
        }
    }

    public static bool Changed(AppSettings saved, AppSettings draft) => saved.Theme != draft.Theme
        || saved.CloseAction != draft.CloseAction || saved.ConfirmExitOnClose != draft.ConfirmExitOnClose
        || saved.AutoRegisterExplorerMenu != draft.AutoRegisterExplorerMenu;

    public static IReadOnlyList<string> Build(int width, AppSettings draft, int selected, bool changed, string error = "")
    {
        width = Math.Min(100, width);
        string Field(int index, string label, string value) => Dashboard.Row((index == selected ? "> " : "  ")
            + Dashboard.Pad($"{index + 1}. {label}", 30) + "  ‹ " + value + " ›", width);
        var description = error.Length == 0 ? Descriptions[selected] : TuiDetails.Wrap("저장 실패: " + error, width - 4).Take(2).ToArray();
        var rows = new List<string>
        {
            Dashboard.Top("공유 설정 · 항목 선택", width),
            Dashboard.Row(changed ? "● 저장하지 않은 변경이 있습니다." : "저장된 설정 · 항목을 골라 바로 변경하세요.", width),
            Dashboard.Bottom(width),
            Dashboard.Top("01 · 테마", width),
            Field(0, "GUI 테마", draft.Theme switch { ThemeMode.Light => "라이트", ThemeMode.Dark => "다크", _ => "시스템" }),
            Dashboard.Bottom(width), "",
            Dashboard.Top("02 · 창 닫기", width),
            Field(1, "GUI 창 닫기", draft.CloseAction == WindowCloseAction.Exit ? "종료" : "트레이 유지"),
            Field(2, "GUI 종료 확인", draft.ConfirmExitOnClose ? "켜짐" : "꺼짐"),
            Dashboard.Bottom(width), "",
            Dashboard.Top("03 · 탐색기 연동", width),
            Field(3, "GUI 시작 시 메뉴 확인", draft.AutoRegisterExplorerMenu ? "켜짐" : "꺼짐"),
            Dashboard.Bottom(width), "",
            Dashboard.Top("선택 항목 설명", width),
            Dashboard.Row(description.ElementAtOrDefault(0) ?? "", width),
            Dashboard.Row(description.ElementAtOrDefault(1) ?? "", width),
            Dashboard.Bottom(width),
            "↑↓ / Tab 항목 선택 · 1~4 바로 선택",
            "←→ / Enter / Space 값 변경 · S 저장",
            "Esc 취소하고 돌아가기 · F1/? 기능 설명",
        };
        return rows;
    }

    internal static string Show(TerminalSession session, CancellationToken token)
    {
        var saved = session.Settings;
        var draft = Storage.Clone(saved);
        var selected = 0; var error = "";
        while (true)
        {
            var width = TerminalText.Width;
            Dashboard.Render(Build(width, draft, selected, Changed(saved, draft), error),
                selections: [new Dashboard.Selection(FieldRows[selected], 2, Math.Min(100, width) - 4, true)]);
            var input = TerminalText.Key(token);
            if (TuiHelp.IsHelp(input)) { TuiHelp.Show(token, "settings set"); continue; }
            switch (input.Key)
            {
                case ConsoleKey.Escape or ConsoleKey.Q:
                    return Changed(saved, draft) ? "설정 변경 취소 · 저장하지 않았습니다." : "설정 화면을 닫았습니다.";
                case ConsoleKey.UpArrow: selected = (selected + 3) % 4; break;
                case ConsoleKey.DownArrow: selected = (selected + 1) % 4; break;
                case ConsoleKey.Tab: selected = (selected + (input.Modifiers.HasFlag(ConsoleModifiers.Shift) ? 3 : 1)) % 4; break;
                case ConsoleKey.LeftArrow: Change(draft, selected, -1); error = ""; break;
                case ConsoleKey.RightArrow or ConsoleKey.Enter or ConsoleKey.Spacebar: Change(draft, selected, 1); error = ""; break;
                case ConsoleKey.Home: selected = 0; break;
                case ConsoleKey.End: selected = 3; break;
                case ConsoleKey.S:
                    if (!Changed(saved, draft)) return "설정 변경 없음 · 기존 값을 유지했습니다.";
                    try { session.Save(draft); return "설정 저장 완료"; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    { error = ex.Message; }
                    break;
                default:
                    if (input.KeyChar is >= '1' and <= '4') selected = input.KeyChar - '1';
                    break;
            }
        }
    }
}
