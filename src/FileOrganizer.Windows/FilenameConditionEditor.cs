using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FileOrganizer.Core;

namespace FileOrganizer.Windows;

internal sealed class FilenameConditionEditor : Window
{
    public FilenameConditionSet? Result { get; private set; }

    public FilenameConditionEditor(Window owner, FilenameConditionSet? original = null)
    {
        Owner = owner; Title = "분류 조건 설정";
        SetResourceReference(StyleProperty, typeof(Window));
        Width = Math.Min(760, SystemParameters.WorkArea.Width); Height = Math.Min(780, SystemParameters.WorkArea.Height);
        MinWidth = Math.Min(520, Width); MinHeight = Math.Min(420, Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var shell = new DockPanel { Margin = new(24) }; Content = shell;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); shell.Children.Add(footer);
        var panel = new FilenameConditionPanel(original ?? FilenameConditionSet.Unrestricted());
        var cancel = EditorUi.Button("취소", (_, _) => Close()); cancel.IsCancel = true; footer.Children.Add(cancel);
        var apply = EditorUi.Button("조건 적용", (_, _) =>
        {
            var value = panel.Read(); FilenameGroups.Validate(value.ToOptions()); Result = value; DialogResult = true;
        }, primary: true);
        apply.IsEnabled = panel.IsValid; panel.Changed += (_, _) => apply.IsEnabled = panel.IsValid; footer.Children.Add(apply);
        shell.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 12, 0) });
    }
}

// 분석 화면과 규칙 편집 창이 같은 입력·미리보기·검증을 사용한다.
internal sealed class FilenameConditionPanel : StackPanel
{
    public event EventHandler? Changed;
    public bool IsValid { get; private set; }
    private readonly ComboBox brackets, limit, combine;
    private readonly CheckBox include;
    private readonly StackPanel rows = new(), filters = new();
    private readonly List<(ComboBox Kind, TextBox Value)> terms = [];
    private readonly TextBox sample, generated;
    private readonly TextBlock feedback = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
    private readonly TextBlock validation;
    private readonly Button add;
    private readonly List<Button> removeButtons = [];
    private bool ready;

    public FilenameConditionPanel(FilenameConditionSet original)
    {
        feedback.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var draft = Storage.Clone(original);
        var choices = new Grid();
        choices.ColumnDefinitions.Add(new ColumnDefinition()); choices.ColumnDefinitions.Add(new ColumnDefinition());
        var bracketField = new StackPanel { Margin = new(0, 0, 12, 0) };
        brackets = EditorUi.Select(["대괄호 [ ]", "소괄호 ( )", "중괄호 { }"], (int)draft.Brackets);
        EditorUi.Field(bracketField, "괄호 종류", brackets); choices.Children.Add(bracketField);
        var limitField = new StackPanel(); limit = EditorUi.Select(["제한 없음 · 모두 분류", "조건 지정"], draft.LimitContent ? 1 : 0);
        EditorUi.Field(limitField, "괄호 안 내용", limit); Grid.SetColumn(limitField, 1); choices.Children.Add(limitField); Children.Add(choices);
        combine = EditorUi.Select(["하나라도 만족 (또는)", "모두 만족 (그리고)"], draft.All ? 1 : 0);
        EditorUi.Field(filters, "조건 결합", combine);
        EditorUi.Hint(filters, "‘모두 만족’은 같은 괄호 안의 값이 모든 조건을 만족해야 합니다. 입력한 기호는 글자 그대로 찾습니다.");
        filters.Children.Add(rows);
        add = EditorUi.Button("조건 추가", (_, _) =>
        {
            if (terms.Count >= 10) return;
            var input = AddTerm(new FilenameCondition()); Refresh(); input.Focus(); input.BringIntoView();
        });
        add.Margin = new(0, 0, 0, 12); add.HorizontalAlignment = HorizontalAlignment.Left; filters.Children.Add(add);
        Children.Add(filters);
        include = new CheckBox { Content = "그룹 이름에 괄호 포함", IsChecked = draft.IncludeBrackets, Margin = new(0, 4, 0, 12) };
        include.ToolTip = "켜면 [업무], 끄면 업무로 묶습니다. 분석 결과에서 만든 규칙도 같은 기준을 사용합니다."; Children.Add(include);
        validation = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new(0, 0, 0, 10) };
        validation.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
        AutomationProperties.SetName(validation, "분류 조건 입력 오류"); AutomationProperties.SetLiveSetting(validation, AutomationLiveSetting.Polite); Children.Add(validation);
        sample = EditorUi.Input("[xyz] [x] [y] 보고서.pdf"); sample.MaxLength = 1000;
        EditorUi.Field(this, "시험할 파일명 (확장자 포함)", sample);
        AutomationProperties.SetName(feedback, "조건 시험 결과"); AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite); Children.Add(feedback);
        generated = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 14, MinHeight = 60, MaxHeight = 140,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 8, 0, 0) };
        AutomationProperties.SetName(generated, "조건으로 생성한 정규식 또는 제한 없음 처리 방식 · 복사 가능");
        Children.Add(new Expander { Header = "생성된 정규식 보기", Content = generated, FontSize = 14 });
        EditorUi.Hint(this, "내용 제한 없음은 빈·중첩·닫히지 않은 괄호를 제외합니다. 조건 지정은 정규식으로 맞는 괄호 쌍을 찾습니다. 대소문자·공백을 구분하며 실제 파일을 읽거나 이동하지 않습니다.");
        foreach (var term in draft.Terms ?? []) AddTerm(term);
        brackets.SelectionChanged += (_, _) => Refresh(); limit.SelectionChanged += (_, _) => Refresh(); combine.SelectionChanged += (_, _) => Refresh();
        include.Checked += (_, _) => Refresh(); include.Unchecked += (_, _) => Refresh(); sample.TextChanged += (_, _) => Refresh();
        ready = true; Refresh();
    }

    private TextBox AddTerm(FilenameCondition term)
    {
        var row = new Grid { Margin = new(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var kind = EditorUi.Select(["시작하는", "정확히 같은", "끝나는", "포함하는"], (int)term.Kind); kind.Margin = new(0, 0, 8, 0);
        var kindField = new StackPanel(); EditorUi.Field(kindField, "비교 방법", kind); row.Children.Add(kindField);
        var value = EditorUi.Input(term.Value); value.MaxLength = 100; value.Margin = new(0, 0, 8, 0);
        var valueField = new StackPanel(); EditorUi.Field(valueField, "조건 값 (1~100자)", value); Grid.SetColumn(valueField, 1); row.Children.Add(valueField);
        var remove = EditorUi.Button("삭제", (_, _) =>
        {
            var index = terms.FindIndex(t => t.Value == value);
            terms.RemoveAt(index); removeButtons.RemoveAt(index); rows.Children.Remove(row); Refresh(); add.Focus();
        });
        remove.VerticalAlignment = VerticalAlignment.Bottom; Grid.SetColumn(remove, 2); row.Children.Add(remove);
        removeButtons.Add(remove); terms.Add((kind, value)); rows.Children.Add(row);
        value.TextChanged += (_, _) => Refresh(); kind.SelectionChanged += (_, _) => Refresh(); return value;
    }

    public FilenameConditionSet Read() => new()
    {
        Brackets = (BracketKind)brackets.SelectedIndex, LimitContent = limit.SelectedIndex == 1, IncludeBrackets = include.IsChecked == true,
        All = combine.SelectedIndex == 1,
        Terms = terms.Select(t => new FilenameCondition { Kind = (FilenameConditionKind)t.Kind.SelectedIndex, Value = t.Value.Text }).ToList()
    };

    private void Refresh()
    {
        if (!ready) return;
        filters.Visibility = limit.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        add.IsEnabled = terms.Count < 10;
        for (var index = 0; index < terms.Count; index++)
        {
            AutomationProperties.SetName(terms[index].Value, $"조건 {index + 1} 값"); AutomationProperties.SetName(terms[index].Kind, $"조건 {index + 1} 비교 방법");
            AutomationProperties.SetName(removeButtons[index], $"조건 {index + 1} 삭제"); removeButtons[index].IsEnabled = terms.Count > 1;
        }
        AnalysisOptions options;
        try
        {
            options = Read().ToOptions(); FilenameGroups.Validate(options);
            generated.Text = options.CustomPattern.Length == 0 ? "내용 제한 없음: 정규식을 사용하지 않고 괄호를 분류합니다. 빈·중첩·닫히지 않은 괄호는 제외합니다." : options.CustomPattern;
            validation.Text = ""; IsValid = true;
        }
        catch (ArgumentException ex)
        {
            generated.Text = ""; validation.Text = ex.Message; feedback.Text = "위 조건 값을 확인하세요."; IsValid = false; Changed?.Invoke(this, EventArgs.Empty); return;
        }
        try
        {
            var result = FilenameGroups.Preview(sample.Text, options);
            feedback.Text = string.IsNullOrWhiteSpace(sample.Text) ? "시험할 파일명을 입력하세요." : result.Groups.Count == 0 ? "조건에 맞지 않음 · 그룹 없음"
                : "찾은 부분: " + string.Join(" · ", result.Matches) + "\n그룹: " + string.Join(" · ", result.Groups);
        }
        catch (ArgumentException ex) { feedback.Text = ex.Message; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
