namespace Chagok.Terminal;

internal static class TuiHelp
{
    internal static bool IsHelp(ConsoleKeyInfo key) => key.Key == ConsoleKey.F1 || key.KeyChar == '?';
    internal static void Show(CancellationToken token, string topic = "tui")
    {
        var index = HelpCatalog.Commands.ToList().FindIndex(c => c.Name == topic);
        index = Math.Max(0, index); var offset = 0;
        while (true)
        {
            var width = TerminalText.Width; var height = TerminalText.Height;
            var command = HelpCatalog.Commands[index];
            var description = HelpCatalog.Describe(command).AsEnumerable();
            if (command.Name is "tui" or "help") description = description.Concat(["", "공통 안전 기준"]).Concat(HelpCatalog.Safety);
            var text = description.SelectMany(s => Wrap(s, width - 4)).ToArray();
            var count = Math.Max(1, height - 7);
            offset = Math.Clamp(offset, 0, Math.Max(0, text.Length - count));
            var rows = new List<string> { "차곡  CHAGOK  /  기능 도움말",
                Dashboard.Top($"{index + 1}/{HelpCatalog.Commands.Count} · {command.Title} · {command.Name}", width) };
            rows.AddRange(text.Skip(offset).Take(count).Select(s => Dashboard.Row(s, width)));
            while (rows.Count < count + 2) rows.Add(Dashboard.Row("", width));
            rows.Add(Dashboard.Bottom(width));
            rows.Add($"설명 {offset + 1}~{Math.Min(text.Length, offset + count)} / {text.Length}줄");
            rows.Add("←→ 기능 선택 · ↑↓ 스크롤 · PgUp/PgDn 페이지");
            rows.Add("Esc 돌아가기 · CLI: help 명령 --json (AI용 설명)");
            Dashboard.Render(rows);
            var key = TerminalText.Key(token).Key;
            if (key is ConsoleKey.Escape or ConsoleKey.Q) return;
            if (key == ConsoleKey.LeftArrow) { index = (index + HelpCatalog.Commands.Count - 1) % HelpCatalog.Commands.Count; offset = 0; }
            if (key == ConsoleKey.RightArrow) { index = (index + 1) % HelpCatalog.Commands.Count; offset = 0; }
            if (key == ConsoleKey.UpArrow) offset--;
            if (key == ConsoleKey.DownArrow) offset++;
            if (key == ConsoleKey.PageUp) offset -= count;
            if (key == ConsoleKey.PageDown) offset += count;
        }
    }
    private static IEnumerable<string> Wrap(string text, int width)
    {
        text = TerminalText.Clean(text);
        if (text.Length == 0) { yield return ""; yield break; }
        while (text.Length > 0)
        {
            var line = TerminalText.Fit(text, Math.Max(1, width));
            if (line.Length == 0) yield break;
            yield return line; text = text[line.Length..];
        }
    }
}
