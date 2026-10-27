using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using Chagok.Core;

namespace Chagok.Windows;

internal sealed class RegexPracticePanel : StackPanel
{
    public event Action<string, string>? ApplyRequested;
    public event Action<bool>? BusyChanged;
    private readonly ComboBox lessons = new();
    private readonly TextBox fragment = new() { FontFamily = new("Consolas"), FontSize = 16, TextWrapping = TextWrapping.Wrap, BorderThickness = new(2), Margin = new(0, 0, 0, 10) };
    private readonly TextBox raw = new() { FontFamily = new("Consolas"), FontSize = 15, TextWrapping = TextWrapping.Wrap, MaxLength = 2000 };
    private readonly TextBox names = new() { AcceptsReturn = true, MinHeight = 115, MaxHeight = 180, MaxLength = 4000, FontSize = 14,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 0, 0, 10) };
    private readonly TextBlock highlighted = AnalysisPatternHelpWindow.Text("", 16);
    private readonly TextBlock editHint = AnalysisPatternHelpWindow.Text("", 14);
    private readonly TextBlock feedback = AnalysisPatternHelpWindow.Text("", 14, sub: true);
    private readonly TextBlock summary = AnalysisPatternHelpWindow.Text("", 15, bold: true);
    private readonly WrapPanel choices = new() { Margin = new(0, 0, 0, 8) };
    private readonly DataGrid grid = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, MaxHeight = 260,
        Visibility = Visibility.Collapsed, Margin = new(0, 8, 0, 8) };
    private readonly Button run, reset;
    private readonly List<(Button Button, FilenameRegexChoice Choice)> choiceButtons = [];
    private bool syncing, running, inactive;
    private FilenameRegexLesson Lesson => FilenameRegexLessons.All[Math.Max(0, lessons.SelectedIndex)];
    private sealed record PracticeRow(string Name, string Matched, string Groups);

    public RegexPracticePanel()
    {
        Children.Add(AnalysisPatternHelpWindow.Text("강조한 부분을 바꾸며 연습하세요", 23, bold: true));
        Children.Add(AnalysisPatternHelpWindow.Text("고정 문법은 그대로 두고 ‘찾을 내용’만 수정합니다. 아래 바꿔 보기 버튼으로 변화부터 확인해 보세요.", 15, sub: true));
        lessons.ItemsSource = FilenameRegexLessons.All.Select(l => l.Title).ToArray(); lessons.FontSize = 15;
        AutomationProperties.SetName(lessons, "연습할 분류 예시");
        reset = EditorUi.Button("예시 초기화", (_, _) => SelectLesson(lessons.SelectedIndex));
        var select = new DockPanel { Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(reset, Dock.Right); select.Children.Add(reset); select.Children.Add(lessons); Children.Add(select);
        Children.Add(AnalysisPatternHelpWindow.Text("1  전체 식에서 ‘수정할 내용 조건’ 확인", 16, bold: true));
        highlighted.FontFamily = new("Consolas"); highlighted.LineHeight = 27; highlighted.Margin = new(0);
        AutomationProperties.SetName(highlighted, "전체 정규식 · 굵고 강조된 부분이 수정할 내용 조건");
        var frame = new Border { Child = highlighted, CornerRadius = new(6), BorderThickness = new(1), Padding = new(14), Margin = new(0, 0, 0, 8) };
        frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); frame.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); Children.Add(frame);
        Children.Add(AnalysisPatternHelpWindow.Text("일반 글자: 예시의 고정 문법  /  굵은 강조 글자: 아래에서 수정할 내용 조건", 13, sub: true));
        Children.Add(AnalysisPatternHelpWindow.Text("수정할 내용 조건 · 위 강조 부분과 연결됩니다", 14, bold: true));
        fragment.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
        AutomationProperties.SetName(fragment, "수정할 내용 조건 · 전체 식에서 강조되는 부분만 편집"); Children.Add(fragment);
        Children.Add(choices); AutomationProperties.SetLiveSetting(editHint, AutomationLiveSetting.Polite); Children.Add(editHint);
        var advanced = new Expander { Header = "전체 정규식 직접 수정 · 고정 문법도 바꾸려면 펼치세요", Content = raw, FontSize = 14, Margin = new(0, 4, 0, 16) };
        advanced.SetResourceReference(Control.ForegroundProperty, "TextBrush"); AutomationProperties.SetName(raw, "전체 정규식 직접 수정"); Children.Add(advanced);
        Children.Add(AnalysisPatternHelpWindow.Text("2  파일명을 넣고 결과 확인", 16, bold: true));
        Children.Add(AnalysisPatternHelpWindow.Text("폴더 경로를 뺀 파일 이름과 확장자를 한 줄에 하나씩 입력하세요. 최대 10줄이며 실제 파일은 필요하지 않습니다.", 14, sub: true));
        AutomationProperties.SetName(names, "연습할 파일명 · 한 줄을 한 파일로 가정, 최대 10개"); Children.Add(names);
        run = EditorUi.Button("연습 결과 보기", async (_, _) => await RunAsync(), primary: true); run.HorizontalAlignment = HorizontalAlignment.Left; run.Margin = new(0, 0, 0, 8); Children.Add(run);
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite); Children.Add(feedback);
        var cell = new Style(typeof(TextBlock)); cell.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap)); cell.Setters.Add(new Setter(TextBlock.FontSizeProperty, 14d));
        foreach (var (header, property, width) in new[] { ("파일명", "Name", 2d), ("찾은 부분", "Matched", 1d), ("그룹 이름", "Groups", 1d) })
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property), Width = new DataGridLength(width, DataGridLengthUnitType.Star), ElementStyle = cell });
        AutomationProperties.SetName(grid, "연습 결과 · 파일명, 찾은 부분, 그룹 이름"); Children.Add(grid);
        summary.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); Children.Add(summary);
        Children.Add(AnalysisPatternHelpWindow.Text("원하는 결과가 나오면 창 아래 ‘연습한 식 사용’을 누르세요. 현재 식과 첫 번째 파일명만 분석 화면에 채웁니다. 실제 파일은 이동하지 않습니다.", 14, sub: true));
        lessons.SelectionChanged += (_, _) => { if (!syncing) SelectLesson(lessons.SelectedIndex); };
        fragment.TextChanged += (_, _) =>
        {
            if (syncing) return;
            syncing = true; raw.Text = Lesson.Compose(fragment.Text); syncing = false;
            RefreshHighlight(); editHint.Text = "강조한 부분만 수정 중입니다. " + Lesson.Description; Changed();
        };
        raw.TextChanged += (_, _) =>
        {
            if (syncing) return;
            syncing = true; var split = Lesson.TryGetFragment(raw.Text, out var value); fragment.Text = split ? value : ""; syncing = false;
            fragment.IsEnabled = split && !running; RefreshHighlight();
            editHint.Text = split ? Lesson.Description : "전체 식이 이 예시의 구조와 달라 수정할 부분을 자동으로 구분하지 않습니다. 전체 식으로 연습하거나 ‘예시 초기화’를 눌러 강조 편집으로 돌아가세요.";
            Changed();
        };
        names.TextChanged += (_, _) => { if (!syncing) Changed(); };
        foreach (var input in new[] { fragment, raw }) input.KeyDown += async (_, e) =>
        { if (e.Key == Key.Enter) { e.Handled = true; await RunAsync(); } };
        SelectLesson(1);
    }

    public void SelectLesson(int index)
    {
        if (running || inactive) return;
        index = Math.Clamp(index, 0, FilenameRegexLessons.All.Count - 1);
        syncing = true; lessons.SelectedIndex = index; var lesson = Lesson;
        fragment.MaxLength = 2000 - lesson.Prefix.Length - lesson.Suffix.Length;
        raw.Text = lesson.Pattern; fragment.Text = lesson.Fragment; names.Text = lesson.PracticeFiles; fragment.IsEnabled = true;
        syncing = false; choices.Children.Clear(); choiceButtons.Clear();
        foreach (var choice in lesson.Choices)
        {
            var button = EditorUi.Button(choice.Title, async (_, _) =>
            { if (running) return; fragment.Text = choice.Fragment; editHint.Text = choice.Explanation; await RunAsync(); });
            button.Margin = new(0, 0, 8, 8); AutomationProperties.SetName(button, "강조 부분을 " + choice.Title + " 조건으로 바꾸고 연습 결과 보기");
            choices.Children.Add(button); choiceButtons.Add((button, choice));
        }
        editHint.Text = lesson.Description + "\n" + lesson.Choices[0].Explanation; RefreshHighlight(); Changed();
    }
    private void RefreshHighlight()
    {
        highlighted.Inlines.Clear();
        if (Lesson.TryGetFragment(raw.Text, out var value))
        {
            var before = new Run(Lesson.Prefix); before.SetResourceReference(TextElement.ForegroundProperty, "SubBrush");
            var editable = new Run(value) { FontWeight = FontWeights.Bold, TextDecorations = TextDecorations.Underline };
            editable.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush"); editable.SetResourceReference(TextElement.BackgroundProperty, "SoftBrush");
            var after = new Run(Lesson.Suffix); after.SetResourceReference(TextElement.ForegroundProperty, "SubBrush");
            highlighted.Inlines.Add(before); highlighted.Inlines.Add(editable); highlighted.Inlines.Add(after);
        }
        else highlighted.Inlines.Add(new Run(raw.Text));
        foreach (var (button, choice) in choiceButtons) button.Content = (fragment.IsEnabled && fragment.Text == choice.Fragment ? "✓ " : "") + choice.Title;
    }
    private void Changed() { grid.Visibility = Visibility.Collapsed; summary.Text = ""; feedback.Text = "입력을 확인하려면 ‘연습 결과 보기’를 누르세요."; }
    public void FocusEditable() => Dispatcher.BeginInvoke(new Action(() => { if (!inactive && fragment.IsEnabled) { fragment.Focus(); fragment.SelectAll(); } }));
    public void SetActive(bool active) => run.IsDefault = active;
    public void Deactivate() => inactive = true;
    private string[] ReadNames()
    {
        var values = names.Text.Replace("\r", "").Split('\n').Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
        if (values.Length is < 1 or > 10) throw new ArgumentException("연습할 파일명을 한 줄에 하나씩 1~10개 입력하세요.");
        return values;
    }
    private void Busy(bool value)
    {
        running = value; if (inactive) return;
        lessons.IsEnabled = reset.IsEnabled = choices.IsEnabled = run.IsEnabled = names.IsEnabled = raw.IsEnabled = !value;
        fragment.IsEnabled = !value && Lesson.TryGetFragment(raw.Text, out _);
        run.Content = value ? "확인 중…" : "연습 결과 보기"; BusyChanged?.Invoke(value);
    }
    private async Task RunAsync()
    {
        if (running || inactive) return;
        Busy(true); grid.Visibility = Visibility.Collapsed; summary.Text = "";
        feedback.Text = "입력한 글자만 확인합니다. 실제 파일을 읽거나 옮기지 않습니다.";
        try
        {
            var values = ReadNames(); var pattern = raw.Text;
            var results = await Task.Run(() => values.Select(name => (Name: name, Preview: FilenameGroups.Preview(name, pattern))).ToArray());
            if (inactive) return;
            grid.ItemsSource = results.Select(row => new PracticeRow(row.Name, row.Preview.Matches.Count == 0 ? "일치 없음"
                : string.Join(" · ", row.Preview.Matches.Select(m => m.Length == 0 ? "(빈 일치)" : m)), row.Preview.Groups.Count == 0 ? "—" : string.Join(" · ", row.Preview.Groups))).ToArray();
            grid.Visibility = Visibility.Visible;
            var groups = results.SelectMany(row => row.Preview.Groups).GroupBy(k => k, StringComparer.Ordinal).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToArray();
            summary.Text = groups.Length == 0 ? "그룹으로 묶이는 파일명이 없습니다." : "그룹별 개수  " + string.Join(" · ", groups.Select(g => $"{g.Key}  {g.Count()}개"));
            feedback.Text = $"{values.Length}줄 연습 완료 · 찾은 부분과 그룹 이름을 비교해 보세요.";
        }
        catch (ArgumentException ex) { if (!inactive) feedback.Text = "입력 확인: " + ex.Message; }
        finally { Busy(false); }
    }
    public void Apply()
    {
        if (running || inactive) return;
        try { var first = ReadNames()[0]; _ = FilenameGroups.Preview(first, raw.Text); ApplyRequested?.Invoke(raw.Text, first); }
        catch (ArgumentException ex) { feedback.Text = "입력 확인: " + ex.Message; }
    }
}
