namespace Chagok.Terminal;

internal sealed class TuiForm(string title, Func<(string Name, string Path)> context, CancellationToken token)
{
    internal sealed record Field(string Label, string Section, Func<string> Value, Action<string>? Set = null,
        Action<int>? Change = null, Func<bool>? Enabled = null, Func<string>? Description = null,
        Func<string>? DisabledReason = null, Func<string>? DisabledValue = null, bool ExactInput = false);

    private static string Explanation(Field field) => (field.Enabled?.Invoke() == false
        ? (field.DisabledReason?.Invoke() ?? "현재 조건에서는 사용하지 않습니다. 기존 값은 유지합니다.") + " " : "")
        + (field.Description?.Invoke() ?? "Enter로 값을 수정하고 I로 전체 값을 확인합니다.");

    internal static Field Choice<T>(string label, string section, Func<T> read, Action<T> write, string[] labels, Func<bool>? enabled = null) where T : struct, Enum
        => new(label, section, () => labels[Convert.ToInt32(read())], Change: direction =>
            write((T)Enum.ToObject(typeof(T), (Convert.ToInt32(read()) + direction + labels.Length) % labels.Length)), Enabled: enabled);

    internal static Field Toggle(string label, string section, Func<bool> read, Action<bool> write)
        => new(label, section, () => read() ? "켜짐" : "꺼짐", Change: _ => write(!read()));

    private string status = "아직 저장하지 않았습니다.";

    internal bool Run(IReadOnlyList<Field> fields, string confirmation, Func<int, IReadOnlyList<string>> review, Action save, string help,
        Func<IReadOnlyList<Field>>? refreshFields = null)
    {
        var selected = 0; var first = 0;
        while (true)
        {
            if (refreshFields is not null) fields = refreshFields();
            selected = Math.Clamp(selected, 0, fields.Count - 1);
            var width = Math.Min(100, TerminalText.Width);
            var rows = Header(width);
            var count = Math.Max(1, TerminalText.Height - rows.Count - 11);
            if (selected < first) first = selected;
            if (selected >= first + count) first = selected - count + 1;
            first = Math.Clamp(first, 0, Math.Max(0, fields.Count - count));
            rows.Add(Dashboard.Top("항목 선택 · " + fields[selected].Section, width));
            var selectedRow = rows.Count + selected - first;
            for (var i = first; i < first + count; i++)
            {
                if (i >= fields.Count) { rows.Add(Dashboard.Row("", width)); continue; }
                var field = fields[i];
                var value = field.Enabled?.Invoke() == false ? field.DisabledValue?.Invoke() ?? "사용 안 함" : field.Value();
                rows.Add(Dashboard.Row((i == selected ? "> " : "  ") + Dashboard.Pad($"{i + 1}. {field.Label}", Math.Min(30, width / 2))
                    + "  " + (value.Length == 0 ? "미입력" : value), width));
            }
            rows.Add(Dashboard.Bottom(width));
            rows.Add(Dashboard.Top("선택 항목 · " + fields[selected].Label, width));
            var details = TuiDetails.Wrap(Explanation(fields[selected]), width - 4).ToArray();
            rows.Add(Dashboard.Row(details.ElementAtOrDefault(0) ?? "", width));
            rows.Add(Dashboard.Row(details.ElementAtOrDefault(1) ?? "", width));
            rows.Add(Dashboard.Row(details.Length > 3 ? "… I: 전체 값과 설명 보기" : details.ElementAtOrDefault(2) ?? "", width));
            rows.Add(Dashboard.Bottom(width));
            rows.Add("↑↓/Tab 선택 · 1~9 바로 선택 · Home/End 처음/끝");
            rows.Add("Enter/E 편집 · ←→ 값 변경 · I 전체 값 보기");
            rows.Add("S 검토·저장 · Esc 전체 취소 · F1/? 도움말");
            Dashboard.Render(rows, selections: [new Dashboard.Selection(selectedRow, 2, width - 4, true)]);
            var input = TerminalText.Key(token);
            if (TuiHelp.IsHelp(input)) { TuiHelp.Show(token, help); continue; }
            var fieldToEdit = fields[selected];
            try
            {
                switch (input.Key)
                {
                    case ConsoleKey.Escape: throw new FormCanceledException();
                    case ConsoleKey.UpArrow: selected = (selected + fields.Count - 1) % fields.Count; break;
                    case ConsoleKey.DownArrow: selected = (selected + 1) % fields.Count; break;
                    case ConsoleKey.Tab: selected = (selected + (input.Modifiers.HasFlag(ConsoleModifiers.Shift) ? fields.Count - 1 : 1)) % fields.Count; break;
                    case ConsoleKey.Home: selected = 0; break;
                    case ConsoleKey.End: selected = fields.Count - 1; break;
                    case ConsoleKey.I: ShowValue(fieldToEdit); break;
                    case ConsoleKey.LeftArrow or ConsoleKey.RightArrow:
                        if (fieldToEdit.Enabled?.Invoke() != false && fieldToEdit.Change is not null)
                        { fieldToEdit.Change(input.Key == ConsoleKey.LeftArrow ? -1 : 1); status = "수정 중 · S 검토·저장 전에는 적용되지 않습니다."; }
                        break;
                    case ConsoleKey.Enter or ConsoleKey.E or ConsoleKey.Spacebar:
                        if (fieldToEdit.Enabled?.Invoke() == false) { status = fieldToEdit.DisabledReason?.Invoke() ?? "현재 조건에서는 이 항목을 사용하지 않습니다."; break; }
                        if (fieldToEdit.Change is not null) fieldToEdit.Change(1);
                        else if (fieldToEdit.Set is not null)
                        {
                            try { fieldToEdit.Set(Edit(fieldToEdit)); }
                            catch (FormCanceledException) { status = "항목 편집 취소 · 기존 값을 유지했습니다."; break; }
                        }
                        else { ShowValue(fieldToEdit); break; }
                        status = "수정 중 · S 검토·저장 전에는 적용되지 않습니다.";
                        break;
                    case ConsoleKey.S:
                        if (Confirm(confirmation, review)) { save(); return true; }
                        status = "저장 확인 취소 · 항목을 계속 수정할 수 있습니다.";
                        break;
                    default:
                        if (input.KeyChar is >= '1' and <= '9' && input.KeyChar - '1' < fields.Count) selected = input.KeyChar - '1';
                        break;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            { status = "처리 실패: " + ex.Message; }
        }
    }

    private string Edit(Field field)
    {
        var width = Math.Min(100, TerminalText.Width);
        var rows = Header(width);
        rows.Add(Dashboard.Top("입력 · " + field.Label, width));
        rows.Add(Dashboard.Row("현재 값 · " + (field.Value().Length == 0 ? "미입력" : field.Value()), width));
        var inputRow = rows.Count;
        rows.Add(Dashboard.Row("", width));
        rows.Add(Dashboard.Bottom(width));
        rows.Add("Enter 적용 / 빈 Enter 기존 값 · /clear 비우기");
        rows.Add("Esc 또는 /cancel + Enter 항목 편집만 취소");
        Dashboard.Render(rows);
        return TerminalText.Input(field.Label, field.Value(), token, inputRow, 2, width - 8, field.ExactInput);
    }

    private void ShowValue(Field field)
    {
        var offset = 0;
        while (true)
        {
            var width = Math.Min(100, TerminalText.Width);
            var values = TuiDetails.Wrap("현재 값: " + (field.Value().Length == 0 ? "미입력" : field.Value()), width - 4)
                .Concat(new[] { "", "기능과 연계 항목" }).Concat(TuiDetails.Wrap(Explanation(field), width - 4)).ToArray();
            var count = Math.Max(1, TerminalText.Height - 6);
            offset = Math.Clamp(offset, 0, Math.Max(0, values.Length - count));
            var rows = new List<string> { Dashboard.Top("전체 값 · " + field.Label, width) };
            rows.AddRange(values.Skip(offset).Take(count).Select(v => Dashboard.Row(v, width)));
            rows.Add(Dashboard.Bottom(width));
            rows.Add("↑↓/PgUp/PgDn 스크롤 · Esc/Enter 항목 목록");
            Dashboard.Render(rows);
            var key = TerminalText.Key(token).Key;
            if (key is ConsoleKey.Escape or ConsoleKey.Enter) return;
            if (key == ConsoleKey.UpArrow) offset--;
            if (key == ConsoleKey.DownArrow) offset++;
            if (key == ConsoleKey.PageUp) offset -= count;
            if (key == ConsoleKey.PageDown) offset += count;
            if (key == ConsoleKey.Home) offset = 0;
            if (key == ConsoleKey.End) offset = values.Length;
        }
    }

    private List<string> Header(int width)
    {
        var (name, path) = context();
        var rows = new List<string> { Dashboard.Top(title, width), Dashboard.Row(status, width), Dashboard.Bottom(width),
            Dashboard.Top("검사 폴더", width), Dashboard.Row("선택한 검사 폴더: " + (name.Length == 0 ? "입력 중" : name), width) };
        var paths = TuiDetails.Wrap("검사 경로: " + (path.Length == 0 ? "입력 중" : path), width - 4).ToArray();
        rows.AddRange(paths.Take(2).Select((line, i) => Dashboard.Row(i == 1 && paths.Length > 2 ? TerminalText.Fit(line, width - 5) + "…" : line, width)));
        rows.Add(Dashboard.Bottom(width));
        return rows;
    }

    private bool Confirm(string message, Func<int, IReadOnlyList<string>> content)
    {
        var offset = 0;
        while (true)
        {
            var width = Math.Min(100, TerminalText.Width);
            var rows = Header(width);
            var body = content(width);
            var headerCount = rows.Count;
            var count = Math.Max(1, TerminalText.Height - headerCount - 5);
            offset = Math.Clamp(offset, 0, Math.Max(0, body.Count - count));
            rows.AddRange(body.Skip(offset).Take(count));
            while (rows.Count < count + headerCount) rows.Add("");
            rows.Add(TerminalText.Fit(message, TerminalText.Width));
            rows.Add("Y 저장 · Esc/다른 키 항목 목록 · 파일 이동 없음");
            rows.Add($"↑↓/PgUp/PgDn 검토 · {offset + 1}~{Math.Min(body.Count, offset + count)}/{body.Count}줄");
            Dashboard.Render(rows);
            var key = TerminalText.Key(token).Key;
            switch (key)
            {
                case ConsoleKey.UpArrow: offset--; break;
                case ConsoleKey.DownArrow: offset++; break;
                case ConsoleKey.PageUp: offset -= count; break;
                case ConsoleKey.PageDown: offset += count; break;
                case ConsoleKey.Home: offset = 0; break;
                case ConsoleKey.End: offset = body.Count; break;
                default: return key == ConsoleKey.Y;
            }
        }
    }
}
