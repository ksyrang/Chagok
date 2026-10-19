using System.Globalization;
using System.Text;

namespace Chagok.Terminal;

public static class TerminalText
{
    public static string Clean(string value) => string.Concat(value.EnumerateRunes()
        .Where(r => Rune.GetUnicodeCategory(r) is not (UnicodeCategory.Control or UnicodeCategory.Format))
        .Select(r => r.ToString()));
    public static int Width { get { try { return Math.Max(30, Console.WindowWidth - 1); } catch (IOException) { return 80; } } }
    public static int Height { get { try { return Math.Max(12, Console.WindowHeight); } catch (IOException) { return 24; } } }
    public static string Fit(string value, int cells)
    {
        var builder = new StringBuilder(); var count = 0;
        foreach (var rune in Clean(value).EnumerateRunes())
        {
            var size = RuneCells(rune);
            if (count + size > cells) break;
            builder.Append(rune); count += size;
        }
        return builder.ToString();
    }
    public static void Line(string value = "") => Console.WriteLine(Fit(value, Width));
    public static void Header(string title)
    {
        Console.Clear();
        Line(Dashboard.Top("차곡 · Chagok " + HelpCatalog.Version, Width));
        Line(Dashboard.Row(title, Width));
        Line(Dashboard.Bottom(Width));
    }
    public static void Status(string value)
    {
        if (Console.IsOutputRedirected) return;
        Console.Write('\r'); Console.Write(new string(' ', Width)); Console.Write('\r'); Console.Write(Fit(value, Width));
    }
    public static ConsoleKeyInfo Key(CancellationToken token)
    {
        while (!Console.KeyAvailable) { token.ThrowIfCancellationRequested(); Thread.Sleep(25); }
        token.ThrowIfCancellationRequested(); return Console.ReadKey(intercept: true);
    }
    public static int CellWidth(string value) => Clean(value).EnumerateRunes().Sum(RuneCells);
    private static int RuneCells(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark ? 0
            : rune.Value is >= 0x1100 and <= 0x115f or >= 0x2e80 and <= 0xa4cf or >= 0xac00 and <= 0xd7a3
                or >= 0xf900 and <= 0xfaff or >= 0xfe10 and <= 0xfe6f or >= 0xff01 and <= 0xff60
                or >= 0x1f300 and <= 0x1faff or >= 0x20000 and <= 0x3ffff ? 2 : 1;
    }
    private static int Previous(string value, int cursor)
        => StringInfo.ParseCombiningCharacters(value).LastOrDefault(i => i < cursor);
    private static int Next(string value, int cursor)
        => StringInfo.ParseCombiningCharacters(value).FirstOrDefault(i => i > cursor, value.Length);

    public static string Input(string label, string current, CancellationToken token, int? inputRow = null, int column = 0, int? inputCells = null)
    {
        Console.CursorVisible = true;
        try
        {
            void Prompt()
            {
                if (inputRow is int row) Console.SetCursorPosition(column, row);
                else Console.Write('\r');
                Console.Write(" > ");
            }
            if (inputRow is null) Console.WriteLine(Clean(label) + (current.Length == 0 ? "" : " [" + Clean(current) + "]"));
            Prompt();
            var value = ""; var cursor = 0; var first = 0;
            while (true)
            {
                var key = Key(token);
                if (key.Key == ConsoleKey.Escape) { if (inputRow is null) Console.WriteLine(); throw new FormCanceledException(); }
                if (key.Key == ConsoleKey.Enter)
                {
                    if (inputRow is null) Console.WriteLine();
                    if (string.Equals(value.Trim(), "/cancel", StringComparison.OrdinalIgnoreCase)) throw new FormCanceledException();
                    return value == "/clear" ? "" : value.Length == 0 ? current : value.Trim();
                }
                if (key.Key == ConsoleKey.LeftArrow) cursor = Previous(value, cursor);
                else if (key.Key == ConsoleKey.RightArrow) cursor = Next(value, cursor);
                else if (key.Key == ConsoleKey.Home) cursor = 0;
                else if (key.Key == ConsoleKey.End) cursor = value.Length;
                else if (key.Key == ConsoleKey.Backspace && cursor > 0)
                {
                    var previous = Previous(value, cursor);
                    value = value.Remove(previous, cursor - previous); cursor = previous;
                }
                else if (key.Key == ConsoleKey.Delete && cursor < value.Length)
                    value = value.Remove(cursor, Next(value, cursor) - cursor);
                else if (key.Key == ConsoleKey.U && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { value = ""; cursor = 0; }
                else if (!char.IsControl(key.KeyChar) && key.KeyChar != '\0')
                { value = value.Insert(cursor, key.KeyChar.ToString()); cursor++; }

                // 긴 경로는 한 줄 안에서 가로로 이동한다. 한글·결합 문자는 중간에서 지우지 않는다.
                first = Math.Min(first, cursor);
                var cells = inputCells ?? Math.Max(1, Console.WindowWidth - 5);
                while (CellWidth(value[first..cursor]) >= cells && first < cursor) first = Next(value, first);
                Prompt(); Console.Write(Fit(value[first..] + new string(' ', cells), cells));
                Prompt(); Console.Write(Clean(value[first..cursor]));
            }
        }
        finally { Console.CursorVisible = false; }
    }
}

internal sealed class FormCanceledException : Exception { }
