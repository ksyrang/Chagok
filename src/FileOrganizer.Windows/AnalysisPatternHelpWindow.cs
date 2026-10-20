using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Input;
using FileOrganizer.Core;

namespace FileOrganizer.Windows;

internal sealed class AnalysisPatternHelpWindow : Window
{
    public string? SelectedPattern { get; private set; }
    public string? SelectedSample { get; private set; }

    public AnalysisPatternHelpWindow(Window owner)
    {
        Owner = owner; Title = "분류 기준 입력 방법";
        SetResourceReference(StyleProperty, typeof(Window));
        Width = Math.Min(760, SystemParameters.WorkArea.Width);
        Height = Math.Min(780, SystemParameters.WorkArea.Height);
        MinWidth = Math.Min(500, Width); MinHeight = Math.Min(420, Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var shell = new DockPanel { Margin = new(24) };
        var footer = new DockPanel { Margin = new(0, 16, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); shell.Children.Add(footer);
        var close = EditorUi.Button("닫기", (_, _) => Close());
        close.IsCancel = true; close.IsDefault = true; close.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Children.Add(close);
        var body = new StackPanel();
        shell.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 12, 0) });
        Content = shell;
        body.Children.Add(Text("어떤 이름끼리 묶을까요?", 25, bold: true));
        body.Children.Add(Text("가까운 예시에서 ‘직접 연습’을 눌러 내 파일명으로 시험해 보세요.", 15, sub: true));

        Example(body, "01  괄호를 포함해 묶기", @"\[[^\[\]]+\]", "[공고문] 채용 안내.pdf", "[공고문]",
            "[공고문]과 [보고서]를 각각 다른 그룹으로 표시합니다.",
            "파일명의 [공고문]을 [보고서]로 바꿔 보세요. 그룹별 개수가 어떻게 달라지는지 확인할 수 있습니다.",
            "[공고문] 채용 안내.pdf\n[보고서] 사업 결과.pdf\n[공고문] 추가 안내.pdf");
        Example(body, "02  괄호 안의 글자만 묶기", @"\[(?<group>[^\[\]]+)\]", "[공고문] 채용 안내.pdf", "공고문",
            "괄호를 빼고 안쪽 글자만 그룹 이름으로 표시합니다.",
            "괄호 안의 글자를 바꿔 보세요. 찾은 부분에는 괄호가 있고, 그룹 이름에는 괄호가 빠지는지 확인하세요.",
            "[공고문] 채용 안내.pdf\n[보고서] 사업 결과.pdf\n[공고문] 추가 안내.pdf");
        Example(body, "03  밑줄 앞의 이름으로 묶기", @"^(?<group>[^_]+)_", "업무_2026_계획.pdf", "업무",
            "파일명 시작부터 첫 번째 밑줄 앞까지 같은 파일을 묶습니다.",
            "첫 번째 밑줄 앞의 ‘업무’를 다른 이름으로 바꿔 보세요. 밑줄 뒤쪽만 바꾸면 같은 그룹으로 남습니다.",
            "업무_2026_계획.pdf\n개인_사진.jpg\n업무_회의록.pdf");
        Example(body, "04  날짜에서 연도만 묶기", @"(?<group>\d{4})-\d{2}-\d{2}", "회의록_2026-10-20.pdf", "2026",
            "날짜가 달라도 연도가 같으면 한 그룹으로 묶습니다.",
            "월·일을 바꾼 뒤 연도도 바꿔 보세요. 그룹이 달라지는 경우를 비교해 보세요.",
            "회의록_2026-10-20.pdf\n보고서_2026-11-01.pdf\n계획_2025-01-01.pdf");

        var advanced = new StackPanel();
        advanced.Children.Add(Text("(?<group>...)  →  이 안에서 찾은 글자를 그룹 이름으로 사용합니다.\n이 표시가 없으면 정규식에 일치한 전체 문자열을 사용합니다.", 15));
        advanced.Children.Add(Text(@"^  파일명 시작     \d  숫자     {4}  네 자리" + "\n"
            + @"+  한 글자 이상     \[ 와 \]  실제 괄호 문자", 15));
        advanced.Children.Add(Text("확장자를 포함한 파일명에 적용합니다. 추출한 값의 대소문자·공백은 구분합니다. 빈 값은 제외하고 한 파일의 같은 값은 한 번만 셉니다.", 15));
        advanced.Children.Add(Text("정규식은 최대 2,000자입니다. 파일 하나에서 100ms를 넘기면 중단합니다.", 14, sub: true));
        var expander = new Expander { Header = "직접 바꾸고 싶다면 · 기호 설명과 집계 기준", Content = advanced,
            FontSize = 15, Margin = new(0, 8, 0, 8) };
        expander.SetResourceReference(ForegroundProperty, "TextBrush");
        body.Children.Add(expander);
        body.Children.Add(Text("예시 사용 → 시험할 파일명으로 확인 → ‘다시 분석’\n예시를 선택하는 것만으로 파일을 분석하거나 이동하지 않습니다.", 14, sub: true));
    }

    private void Example(Panel parent, string title, string pattern, string sample, string group, string explanation, string practiceTip, string practiceFiles)
    {
        var card = new StackPanel();
        card.Children.Add(Text(title, 17, bold: true));
        card.Children.Add(Text(explanation, 14, sub: true));
        card.Children.Add(Text("예시 정규식 · 선택해서 복사할 수 있습니다", 13, sub: true));
        var input = new TextBox { Text = pattern, IsReadOnly = true, FontFamily = new FontFamily("Consolas"),
            FontSize = 15, TextWrapping = TextWrapping.Wrap, MinHeight = 40, Margin = new(0, 0, 0, 10) };
        AutomationProperties.SetName(input, title + " 정규식 · 선택 후 Ctrl+C로 복사");
        card.Children.Add(input);
        card.Children.Add(Text("파일명    " + sample, 15));
        var result = Text("그룹       " + group, 16, bold: true);
        result.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        card.Children.Add(result);
        var practice = new StackPanel { Margin = new(0, 12, 0, 0) };
        var toggle = EditorUi.Button("직접 연습", (_, _) => { });
        var draftPattern = new TextBox { Text = pattern, MaxLength = 2000, FontFamily = new FontFamily("Consolas"),
            FontSize = 15, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
        var names = new TextBox { Text = practiceFiles, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            MinHeight = 94, MaxHeight = 160, MaxLength = 4000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14, Margin = new(0, 0, 0, 10) };
        AutomationProperties.SetName(draftPattern, title + " 연습할 정규식");
        AutomationProperties.SetName(names, title + " 연습할 파일명 · 한 줄에 하나, 최대 10개");
        practice.Children.Add(Text("1  아래 파일명을 내 파일명으로 바꿔 보세요.", 15, bold: true));
        practice.Children.Add(Text(practiceTip, 14, sub: true));
        practice.Children.Add(Text("연습할 파일명 · 한 줄을 파일 하나로 가정, 최대 10개", 13, sub: true));
        practice.Children.Add(names);
        practice.Children.Add(Text("폴더 경로를 뺀 파일 이름과 확장자를 입력하세요. 실제 파일을 준비할 필요는 없습니다.", 13, sub: true));
        practice.Children.Add(Text("2  필요하면 정규식도 바꿔 볼 수 있습니다.", 15, bold: true));
        practice.Children.Add(Text("이 칸에는 파일명이 아닌 찾을 규칙을 넣습니다. 처음에는 예시의 식을 그대로 두고 파일명부터 바꿔 보세요.", 14, sub: true));
        practice.Children.Add(draftPattern);
        var feedback = Text("‘연습 결과 보기’를 누르면 찾은 부분과 그룹별 개수가 나옵니다.", 14, sub: true);
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false,
            MaxHeight = 240, MinHeight = 100, Visibility = Visibility.Collapsed, Margin = new(0, 8, 0, 8) };
        AutomationProperties.SetName(grid, title + " 연습 결과 · 파일명, 찾은 부분, 그룹 이름");
        var cell = new Style(typeof(TextBlock));
        cell.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        cell.Setters.Add(new Setter(TextBlock.FontSizeProperty, 14d));
        foreach (var (header, property, width) in new[] { ("파일명", "Name", 2d), ("찾은 부분", "Matched", 1d), ("그룹 이름", "Groups", 1d) })
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property),
                Width = new DataGridLength(width, DataGridLengthUnitType.Star), ElementStyle = cell });
        var summary = Text("", 15, bold: true);
        summary.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var run = EditorUi.Button("연습 결과 보기", (_, _) => { }, primary: true);
        run.HorizontalAlignment = HorizontalAlignment.Left; run.Margin = new(0, 0, 0, 8);
        AutomationProperties.SetName(run, title + " 연습 결과 보기");
        draftPattern.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; if (run.IsEnabled) run.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        };
        var use = EditorUi.Button("이 예시 사용", (_, _) => { });
        var opened = false; var running = false;
        string[] ReadNames()
        {
            var values = names.Text.Replace("\r", "").Split('\n').Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
            if (values.Length == 0) throw new ArgumentException("연습할 파일명을 한 줄 이상 입력하세요.");
            if (values.Length > 10) throw new ArgumentException("파일명은 최대 10개까지 연습할 수 있습니다. 한 줄에 하나씩 입력하세요.");
            return values;
        }
        void Changed()
        {
            grid.Visibility = Visibility.Collapsed; summary.Text = "";
            feedback.Text = "입력이 바뀌었습니다. ‘연습 결과 보기’를 눌러 확인하세요.";
        }
        names.TextChanged += (_, _) => Changed(); draftPattern.TextChanged += (_, _) => Changed();
        run.Click += async (_, _) =>
        {
            if (running) return;
            running = true; run.IsEnabled = use.IsEnabled = names.IsEnabled = draftPattern.IsEnabled = false;
            run.Content = "확인 중…"; feedback.Text = "입력한 이름만 확인합니다. 실제 파일은 읽거나 옮기지 않습니다.";
            grid.Visibility = Visibility.Collapsed; summary.Text = "";
            try
            {
                var values = ReadNames(); var expression = draftPattern.Text;
                var result = await Task.Run(() => values.Select(value => (Name: value, Preview: FilenameGroups.Preview(value, expression))).ToArray());
                grid.ItemsSource = result.Select(row => new PracticeRow(row.Name,
                    row.Preview.Matches.Count == 0 ? "일치 없음" : string.Join(" · ", row.Preview.Matches.Select(m => m.Length == 0 ? "(빈 일치)" : m)),
                    row.Preview.Groups.Count == 0 ? "—" : string.Join(" · ", row.Preview.Groups))).ToArray();
                grid.Visibility = Visibility.Visible;
                var groups = result.SelectMany(row => row.Preview.Groups).GroupBy(key => key, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToArray();
                summary.Text = groups.Length == 0 ? "그룹으로 묶이는 파일명이 없습니다."
                    : "그룹별 개수  " + string.Join(" · ", groups.Select(g => $"{g.Key}  {g.Count()}개"));
                feedback.Text = $"{values.Length}개 이름 연습 완료 · ‘찾은 부분’ 중 실제로 묶을 값이 ‘그룹 이름’입니다.";
            }
            catch (ArgumentException ex) { feedback.Text = ex.Message; }
            finally
            {
                running = false; run.IsEnabled = use.IsEnabled = names.IsEnabled = draftPattern.IsEnabled = true;
                run.Content = "연습 결과 보기";
            }
        };
        use.Click += (_, _) =>
        {
            try
            {
                var expression = opened ? draftPattern.Text : pattern;
                var filename = opened ? ReadNames()[0] : sample;
                _ = FilenameGroups.Preview(filename, expression);
                SelectedPattern = expression; SelectedSample = filename; DialogResult = true;
            }
            catch (ArgumentException ex) { feedback.Text = ex.Message; }
        };
        practice.Children.Add(run); practice.Children.Add(feedback); practice.Children.Add(grid); practice.Children.Add(summary);
        practice.Children.Add(Text("3  원하는 그룹이 나오면 ‘연습한 식 사용’을 누르세요.\n분석 화면에는 이 식과 첫 번째 파일명을 채웁니다. 실제 폴더 분석은 ‘다시 분석’으로 실행합니다.", 14, sub: true));
        var practiceBox = new Border { Child = practice, BorderThickness = new(0, 1, 0, 0),
            Margin = new(0, 12, 0, 0), Visibility = Visibility.Collapsed };
        practiceBox.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        toggle.Click += (_, _) =>
        {
            opened = !opened; practiceBox.Visibility = opened ? Visibility.Visible : Visibility.Collapsed;
            toggle.Content = opened ? "연습 접기" : "직접 연습";
            use.Content = opened ? "연습한 식 사용" : "이 예시 사용";
            if (opened) { names.Focus(); names.BringIntoView(); }
        };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 4, 0, 0) };
        toggle.Margin = new(0, 0, 8, 8); use.Margin = new(0, 0, 0, 8);
        AutomationProperties.SetName(toggle, title + " 직접 연습 열기·접기");
        AutomationProperties.SetName(use, title + " 예시 또는 연습한 식 사용");
        actions.Children.Add(toggle); actions.Children.Add(use); card.Children.Add(practiceBox); card.Children.Add(actions);
        var border = new Border { Child = card, CornerRadius = new(8), BorderThickness = new(1),
            Padding = new(16), Margin = new(0, 6, 0, 8) };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        parent.Children.Add(border);
    }

    private sealed record PracticeRow(string Name, string Matched, string Groups);

    private static TextBlock Text(string value, double size, bool bold = false, bool sub = false)
    {
        var block = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
            LineHeight = size * 1.6, Margin = new(0, 0, 0, 8), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        if (sub) block.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush");
        return block;
    }
}
