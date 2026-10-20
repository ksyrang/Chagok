using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Collections.ObjectModel;
using FileOrganizer.Core;

namespace FileOrganizer.Windows;

internal static class EditorUi
{
    public static TextBlock Heading(string text) => new() { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new(0, 18, 0, 12) };
    public static TextBox Input(string text) => new() { Text = text, Margin = new(0, 0, 0, 12) };
    public static void Field(Panel parent, string label, Control control)
    {
        var caption = new TextBlock { Text = label, FontSize = 12, Margin = new(0, 0, 0, 6) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        parent.Children.Add(caption);
        AutomationProperties.SetName(control, label);
        parent.Children.Add(control);
    }
    public static ComboBox Select(string[] labels, int index)
        => new() { ItemsSource = labels, SelectedIndex = index, Margin = new(0, 0, 0, 12) };
    public static CheckBox Check(string label, bool value) => new() { Content = label, IsChecked = value, Margin = new(0, 0, 0, 12) };
    public static Button Button(string text, RoutedEventHandler handler, bool primary = false)
    {
        var button = new Button { Content = text, Margin = new(8, 0, 0, 0), MinWidth = 75 };
        if (primary) button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        button.Click += handler;
        return button;
    }
    public static DockPanel Browse(TextBox input, RoutedEventHandler handler, string buttonText = "찾아보기")
    {
        var row = new DockPanel();
        var button = Button(buttonText, handler);
        button.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(button, Dock.Right);
        row.Children.Add(button); row.Children.Add(input);
        return row;
    }
    public static string? SelectFolder(Window owner, string title, string initialPath)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title, Multiselect = false };
        if (Directory.Exists(initialPath)) dialog.InitialDirectory = initialPath;
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }
    public static DockPanel ProgramInput(Window owner, TextBox input) => Browse(input, (_, _) =>
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "감지할 프로그램 선택", Filter = "실행 파일 (*.exe)|*.exe",
            DefaultExt = ".exe", CheckFileExists = true, Multiselect = false
        };
        if (File.Exists(input.Text))
        { dialog.InitialDirectory = Path.GetDirectoryName(input.Text); dialog.FileName = Path.GetFileName(input.Text); }
        if (dialog.ShowDialog(owner) == true) input.Text = dialog.FileName;
    });
    public static void Hint(Panel panel, string text)
    {
        var block = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush");
        panel.Children.Add(block);
    }
    public static void Error(Window owner, Exception error) => MessageBox.Show(owner, error.Message, "설정을 확인하세요", MessageBoxButton.OK, MessageBoxImage.Warning);
}

internal sealed class RuleEditor : Window
{
    public RuleSpec? Result { get; private set; }
    public bool DeleteRequested { get; private set; }
    public RuleEditor(Window owner, FolderSpec folder, RuleSpec? original, string? destinationPreset = null, RuleSpec? rulePreset = null)
    {
        Owner = owner; Title = original is null ? "규칙 추가" : "규칙 편집";
        SetResourceReference(StyleProperty, typeof(Window));
        Width = 640; Height = Math.Min(760, SystemParameters.WorkArea.Height); MinWidth = 520; MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var rule = original is not null ? Storage.Clone(original) : rulePreset is not null ? Storage.Clone(rulePreset)
            : new RuleSpec { Destination = DestinationPaths.Initial(folder, destinationPreset) };
        var shell = new DockPanel { Margin = new(24) };
        var actions = new DockPanel { Margin = new(0, 18, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(right, Dock.Right);
        actions.Children.Add(right);
        shell.Children.Add(actions);
        var form = new StackPanel();
        shell.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(0, 0, 12, 0) });
        Content = shell;

        form.Children.Add(new TextBlock { Text = Title, FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 8) });
        EditorUi.Hint(form, $"{folder.Name} 폴더에 적용합니다. 규칙은 목록 위에서부터 판단하며 한 파일은 첫 일치 규칙으로 처리합니다.");
        var name = EditorUi.Input(rule.Name);
        EditorUi.Field(form, "규칙 이름", name);
        var appendName = EditorUi.Check("규칙명 폴더 사용", rule.AppendRuleName);
        form.Children.Add(appendName);
        var enabled = EditorUi.Check("규칙 사용", rule.Enabled);
        var recursive = EditorUi.Check("하위 폴더도 검사", rule.Recursive);
        form.Children.Add(enabled); form.Children.Add(recursive);
        form.Children.Add(EditorUi.Heading("조건"));
        var match = EditorUi.Select(["파일명에 특정 단어 포함", "파일명이 특정 단어로 시작", "파일명 패턴 일치 (*, ?)", "파일명 제한 없음", "분류 구분값 일치 (대소문자 구분)"], (int)rule.Match);
        EditorUi.Field(form, "파일명 조건", match);
        var pattern = EditorUi.Input(rule.Pattern);
        EditorUi.Field(form, "단어 또는 패턴", pattern);
        match.SelectionChanged += (_, _) => pattern.IsEnabled = match.SelectedIndex != (int)NameMatch.Any;
        pattern.IsEnabled = rule.Match != NameMatch.Any;
        var expression = EditorUi.Input(rule.GroupExpression);
        var groupConditions = rule.GroupConditions;
        var updatingExpression = false;
        var expressionFields = new StackPanel();
        EditorUi.Field(expressionFields, "분류 정규식 (조건 선택 시 자동 설정)", expression);
        expression.ToolTip = FilenameGroups.PatternHelp;
        EditorUi.Hint(expressionFields, "추출한 값이 위 구분값과 정확히 같은 파일을 찾습니다. 선택한 조건은 아래 요약에서 확인하세요. 조건 정보 없이 식을 비우면 [a] 같은 괄호 구분값으로 비교합니다.");
        var conditionSummary = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new(0, 0, 0, 8) };
        AutomationProperties.SetName(conditionSummary, "규칙의 괄호 안 분류 조건");
        void UpdateConditionSummary() => conditionSummary.Text = groupConditions is null
            ? "조건 선택으로 식을 만들거나, 위 정규식을 직접 입력할 수 있습니다. 직접 입력한 식은 조건으로 자동 변환하지 않습니다."
            : "선택한 조건: " + groupConditions.Summary;
        UpdateConditionSummary();
        expression.TextChanged += (_, _) =>
        {
            if (updatingExpression) return;
            groupConditions = null; UpdateConditionSummary();
        };
        expressionFields.Children.Add(conditionSummary);
        var editConditions = EditorUi.Button("조건 설정", (_, _) =>
        {
            var editor = new FilenameConditionEditor(this, groupConditions);
            if (editor.ShowDialog() != true || editor.Result is not { } value) return;
            updatingExpression = true;
            try { expression.Text = value.Compile(); groupConditions = value; }
            finally { updatingExpression = false; }
            UpdateConditionSummary();
        });
        editConditions.HorizontalAlignment = HorizontalAlignment.Left; editConditions.Margin = new(0, 0, 0, 12);
        AutomationProperties.SetName(editConditions, "규칙의 괄호 안 분류 조건 설정");
        expressionFields.Children.Add(editConditions);
        EditorUi.Hint(expressionFields, "조건을 적용하면 이 규칙의 분류 정규식을 바꿉니다. 위 구분값과 정확히 같은 그룹만 규칙에 포함합니다. 정규식을 직접 바꾸면 선택한 조건 정보는 해제됩니다.");
        form.Children.Add(expressionFields);
        void UpdateExpression() => expressionFields.Visibility = match.SelectedIndex == (int)NameMatch.Group ? Visibility.Visible : Visibility.Collapsed;
        match.SelectionChanged += (_, _) => UpdateExpression(); UpdateExpression();
        var extensions = EditorUi.Input(rule.Extensions);
        EditorUi.Field(form, "확장자 (.pdf, .jpg처럼 구분 / 비우면 전체)", extensions);
        EditorUi.Hint(form, "파일명 조건과 확장자 조건을 모두 만족하는 파일을 찾습니다. 괄호 구분값은 [a]처럼 입력하며 대소문자를 구분합니다. 파일 내용은 검색하지 않습니다.");
        form.Children.Add(EditorUi.Heading("동작"));
        var baseDestination = rule.Destination;
        Action refreshDestination = () => { };
        var destination = EditorUi.Input(rule.Destination);
        var destinationRow = EditorUi.Browse(destination, (_, _) =>
        {
            var selected = EditorUi.SelectFolder(this, "이동할 폴더 선택", baseDestination);
            if (selected is not null) { baseDestination = selected; refreshDestination(); }
        });
        form.Children.Add(new TextBlock { Text = "이동할 폴더", FontSize = 12, Margin = new(0, 0, 0, 6) });
        AutomationProperties.SetName(destination, "이동할 폴더");
        form.Children.Add(destinationRow);
        if (folder.UseRecentDestinations)
        {
            var recent = new ComboBox { ItemsSource = DestinationPaths.Recent(folder), Margin = new(0, 0, 0, 12) };
            EditorUi.Field(form, "최근 사용한 이동 위치 (이 검사 폴더)", recent);
            recent.SelectionChanged += (_, _) =>
            {
                if (recent.SelectedItem is string path) { baseDestination = path; refreshDestination(); }
            };
            if (recent.Items.Count == 0) EditorUi.Hint(form, "규칙을 저장하면 최근 이동 위치가 여기에 표시됩니다.");
        }
        var finalPath = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 0, 0, 12) };
        finalPath.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush");
        AutomationProperties.SetName(finalPath, "최종 이동 폴더");
        form.Children.Add(finalPath);
        var create = EditorUi.Check("대상 폴더가 없으면 자동 생성", rule.CreateDestination);
        form.Children.Add(create);
        var createChoice = rule.CreateDestination;
        create.Checked += (_, _) => { if (create.IsEnabled) createChoice = true; };
        create.Unchecked += (_, _) => { if (create.IsEnabled) createChoice = false; };
        var updatingDestination = false;
        void UpdateDestination()
        {
            if (updatingDestination) return;
            updatingDestination = true;
            var append = appendName.IsChecked == true;
            var resolved = DestinationPaths.Preview(new RuleSpec
                { Name = name.Text, Destination = baseDestination, AppendRuleName = append });
            var draft = new RuleSpec { Name = name.Text, Destination = baseDestination, AppendRuleName = append };
            try { destination.Text = DestinationPaths.Resolve(draft); }
            catch (ArgumentException) { destination.Text = baseDestination; }
            destination.ToolTip = destination.Text;
            finalPath.Text = "최종 이동 폴더: " + resolved;
            create.IsEnabled = false;
            create.IsChecked = append || createChoice;
            create.IsEnabled = !append;
            updatingDestination = false;
        }
        name.TextChanged += (_, _) => UpdateDestination();
        refreshDestination = UpdateDestination;
        destination.TextChanged += (_, _) =>
        {
            if (updatingDestination) return;
            baseDestination = DestinationPaths.BaseFromDisplay(destination.Text, name.Text, appendName.IsChecked == true);
            finalPath.Text = "최종 이동 폴더: " + DestinationPaths.Preview(new RuleSpec
                { Name = name.Text, Destination = baseDestination, AppendRuleName = appendName.IsChecked == true });
        };
        destination.LostKeyboardFocus += (_, _) => UpdateDestination();
        appendName.Checked += (_, _) => UpdateDestination();
        appendName.Unchecked += (_, _) => UpdateDestination();
        UpdateDestination();
        EditorUi.Hint(form, "규칙명 폴더 옵션을 켜면 최종 폴더가 없을 때 실행 중 자동으로 생성합니다. 경로는 이 칸에서 직접 수정할 수 있습니다. 규칙명이 끝에 없으면 입력을 마칠 때 붙입니다. 찾아보기·최근 위치도 규칙명 폴더를 붙여 표시합니다.");
        var conflict = EditorUi.Select(["이름 변경 후 이동", "기존 파일을 휴지통으로 옮기고 교체", "건너뛰기"], (int)rule.Conflict);
        EditorUi.Field(form, "이동 위치에 같은 이름이 있을 때", conflict);
        EditorUi.Hint(form, "같은 이름의 파일만 판단합니다. 파일 내용이 같은지는 비교하지 않습니다. 휴지통 이동이 실패하면 교체를 중단합니다.");
        form.Children.Add(EditorUi.Heading("실행 조건"));
        var trigger = EditorUi.Select(["수동 실행", "특정 프로그램 실행 시", "특정 프로그램 종료 시"], (int)rule.Trigger);
        EditorUi.Field(form, "실행 방식", trigger);
        var programFields = new StackPanel();
        var program = EditorUi.Input(rule.ProgramPath);
        programFields.Children.Add(new TextBlock { Text = "대상 프로그램 실행 파일", FontSize = 12, Margin = new(0, 0, 0, 6) });
        AutomationProperties.SetName(program, "대상 프로그램 실행 파일");
        programFields.Children.Add(EditorUi.Browse(program, (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "감지할 프로그램 선택", Filter = "실행 파일 (*.exe)|*.exe",
                DefaultExt = ".exe", CheckFileExists = true, Multiselect = false
            };
            if (File.Exists(program.Text))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(program.Text);
                dialog.FileName = Path.GetFileName(program.Text);
            }
            if (dialog.ShowDialog(this) == true) program.Text = dialog.FileName;
        }));
        var mode = EditorUi.Select(["목록 확인 후 실행", "저장된 조건으로 자동 실행"], (int)rule.RunMode);
        EditorUi.Field(programFields, "감지 후 처리", mode);
        EditorUi.Hint(programFields, "프로그램 감지에는 트레이 상주가 필요합니다. 실행은 첫 실행본 시작, 종료는 마지막 실행본 종료를 기준으로 합니다. 대상 앱이 백그라운드에 남아 있으면 창을 다시 여는 것은 새 시작이 아닙니다. 대체 감지는 250ms 간격으로 확인하며 표본 사이에 끝나는 짧은 실행은 놓칠 수 있습니다.");
        form.Children.Add(programFields);
        void UpdateTrigger() => programFields.Visibility = trigger.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        trigger.SelectionChanged += (_, _) => UpdateTrigger(); UpdateTrigger();

        right.Children.Add(EditorUi.Button("취소", (_, _) => Close()));
        var save = EditorUi.Button("규칙 저장", (_, _) =>
        {
            try
            {
                rule.Name = name.Text.Trim(); rule.Enabled = enabled.IsChecked == true;
                rule.Recursive = recursive.IsChecked == true; rule.Match = (NameMatch)match.SelectedIndex;
                rule.Pattern = rule.Match == NameMatch.Group ? pattern.Text : pattern.Text.Trim(); rule.GroupExpression = expression.Text; rule.Extensions = extensions.Text.Trim();
                rule.GroupConditions = rule.Match == NameMatch.Group ? groupConditions : null;
                rule.Destination = PathSafety.Normalize(baseDestination.Trim());
                rule.AppendRuleName = appendName.IsChecked == true;
                rule.CreateDestination = createChoice; rule.Conflict = (ConflictMode)conflict.SelectedIndex;
                rule.Trigger = (TriggerMode)trigger.SelectedIndex; rule.ProgramPath = program.Text.Trim();
                rule.RunMode = (RunMode)mode.SelectedIndex;
                PathSafety.ValidateRule(folder, rule);
                if (rule.Trigger != TriggerMode.Manual && !File.Exists(rule.ProgramPath))
                    throw new ArgumentException("감지할 프로그램 파일이 없습니다. .exe 파일을 다시 선택하세요.");
                Result = rule; DialogResult = true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { EditorUi.Error(this, ex); }
        }, primary: true);
        right.Children.Add(save);
        if (original is not null)
        {
            var delete = EditorUi.Button("규칙 삭제", (_, _) =>
            {
                if (MessageBox.Show(this, $"‘{original.Name}’ 규칙을 삭제할까요? 실제 파일은 삭제하지 않습니다.", "규칙 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                DeleteRequested = true; DialogResult = true;
            });
            delete.HorizontalAlignment = HorizontalAlignment.Left;
            actions.Children.Add(delete);
        }
    }
}

internal sealed class FolderEditor : Window
{
    public FolderSpec? Result { get; private set; }
    public bool DeleteRequested { get; private set; }
    public FolderEditor(Window owner, AppSettings settings, FolderSpec? original, bool duplicate = false)
    {
        Owner = owner; Title = duplicate ? "검사 폴더 복제" : original is null ? "검사 폴더 추가" : "폴더 설정";
        SetResourceReference(StyleProperty, typeof(Window));
        Width = 730; Height = Math.Min(720, SystemParameters.WorkArea.Height); MinWidth = 600; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var folder = original is null ? new FolderSpec() : duplicate ? FolderDuplicator.Create(original) : Storage.Clone(original);
        var shell = new DockPanel { Margin = new(24) };
        var actions = new DockPanel { Margin = new(0, 18, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); shell.Children.Add(actions);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(right, Dock.Right); actions.Children.Add(right);
        var form = new StackPanel();
        shell.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(0, 0, 12, 0) });
        Content = shell;
        form.Children.Add(new TextBlock { Text = Title, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 20) });
        if (duplicate) EditorUi.Hint(form, $"‘{original!.Name}’의 규칙 {folder.Rules.Count}개를 함께 복제합니다. 새 검사 폴더를 지정하세요. 규칙의 이동 목적지는 유지하며 제외 폴더는 새 검사 폴더 안의 같은 하위 경로로 바꿉니다.");
        var name = EditorUi.Input(folder.Name);
        EditorUi.Field(form, "표시 이름", name);
        var path = EditorUi.Input(folder.Path);
        form.Children.Add(new TextBlock { Text = "검사할 폴더", FontSize = 12, Margin = new(0, 0, 0, 6) });
        AutomationProperties.SetName(path, "검사할 폴더");
        form.Children.Add(EditorUi.Browse(path, (_, _) =>
        {
            var selected = EditorUi.SelectFolder(this, "검사할 폴더 선택", path.Text);
            if (selected is null) return;
            path.Text = selected;
            if (string.IsNullOrWhiteSpace(name.Text)) name.Text = Path.GetFileName(path.Text.TrimEnd(Path.DirectorySeparatorChar));
        }));
        var useRecent = EditorUi.Check("규칙에서 최근 사용한 이동 위치 표시", folder.UseRecentDestinations);
        form.Children.Add(useRecent);
        EditorUi.Hint(form, "이 검사 폴더에서 저장한 규칙의 기본 이동 위치를 최근 10개까지 기록합니다. 끄면 표시와 새 기록을 중단하며, 기존 기록은 유지합니다.");
        form.Children.Add(EditorUi.Heading("검사에서 제외할 폴더"));
        var excluded = new ObservableCollection<ExcludedFolderRow>(folder.ExcludedFolders.Select(p => new ExcludedFolderRow(p)));
        var exclusionSource = folder.Path;
        var table = new DataGrid { ItemsSource = excluded, IsReadOnly = true, Height = 150, SelectionMode = DataGridSelectionMode.Extended };
        table.Columns.Add(new DataGridTextColumn { Header = "폴더 이름", Binding = new Binding(nameof(ExcludedFolderRow.Name)), Width = 140 });
        table.Columns.Add(new DataGridTextColumn { Header = "전체 경로", Binding = new Binding(nameof(ExcludedFolderRow.Path)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        AutomationProperties.SetName(table, "제외 폴더 목록");
        form.Children.Add(table);
        var addPath = EditorUi.Input("");
        addPath.Margin = new(0);
        AutomationProperties.SetName(addPath, "추가할 제외 폴더 전체 경로");
        void AddExcluded(string value)
        {
            var current = duplicate
                ? FolderDuplicator.RebaseExclusions(exclusionSource, path.Text, excluded.Select(r => r.Path))
                : excluded.Select(r => r.Path);
            var normalized = PathSafety.NormalizeExclusions(path.Text, current.Append(value));
            excluded.Clear(); foreach (var item in normalized) excluded.Add(new(item));
            exclusionSource = PathSafety.Normalize(path.Text);
            addPath.Clear();
        }
        var inputRow = EditorUi.Browse(addPath, (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(addPath.Text)) throw new ArgumentException("제외할 폴더 경로를 입력하세요.");
                AddExcluded(addPath.Text);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { EditorUi.Error(this, ex); }
        }, "경로 추가");
        inputRow.Margin = new(0, 10, 0, 12); form.Children.Add(inputRow);
        var excludeActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 12) };
        excludeActions.Children.Add(EditorUi.Button("폴더 선택하여 추가", (_, _) =>
        {
            try
            {
                var source = PathSafety.Normalize(path.Text);
                var selected = EditorUi.SelectFolder(this, "제외할 하위 폴더 선택", source);
                if (selected is not null) AddExcluded(selected);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { EditorUi.Error(this, ex); }
        }));
        var remove = EditorUi.Button("선택 제거", (_, _) =>
        { foreach (var item in table.SelectedItems.Cast<ExcludedFolderRow>().ToArray()) excluded.Remove(item); });
        remove.IsEnabled = false;
        table.SelectionChanged += (_, _) => remove.IsEnabled = table.SelectedItems.Count > 0;
        excludeActions.Children.Add(remove); form.Children.Add(excludeActions);
        EditorUi.Hint(form, "이동할 폴더는 검사에서 자동 제외합니다. 링크·연결 폴더는 따라가지 않습니다.");
        form.Children.Add(EditorUi.Heading("폴더 실행 조건"));
        var trigger = EditorUi.Select(["수동 실행", "특정 프로그램 실행 시", "특정 프로그램 종료 시"], (int)folder.Trigger);
        EditorUi.Field(form, "폴더 실행 방식", trigger);
        var triggerFields = new StackPanel();
        var program = EditorUi.Input(folder.ProgramPath);
        triggerFields.Children.Add(new TextBlock { Text = "대상 프로그램 실행 파일", FontSize = 12, Margin = new(0, 0, 0, 6) });
        AutomationProperties.SetName(program, "폴더 실행 조건의 대상 프로그램");
        triggerFields.Children.Add(EditorUi.ProgramInput(this, program));
        var mode = EditorUi.Select(["목록 확인 후 실행", "저장된 조건으로 자동 실행"], (int)folder.RunMode);
        EditorUi.Field(triggerFields, "폴더 감지 후 처리", mode);
        EditorUi.Hint(triggerFields, "조건이 맞으면 이 폴더의 켜진 규칙을 모두 검사합니다. 규칙 자체의 실행 조건도 독립적으로 동작합니다. 같은 이벤트가 겹쳐도 한 목록으로 검사합니다. 확인이 필요한 규칙이 있으면 폴더 목록 전체를 확인 후 실행합니다.");
        form.Children.Add(triggerFields);
        void UpdateFolderTrigger() => triggerFields.Visibility = trigger.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        trigger.SelectionChanged += (_, _) => UpdateFolderTrigger(); UpdateFolderTrigger();
        right.Children.Add(EditorUi.Button("취소", (_, _) => Close()));
        right.Children.Add(EditorUi.Button(duplicate ? "복제" : "저장", (_, _) =>
        {
            try
            {
                folder.Name = name.Text.Trim(); folder.Path = PathSafety.Normalize(path.Text.Trim());
                folder.UseRecentDestinations = useRecent.IsChecked == true;
                folder.Trigger = (TriggerMode)trigger.SelectedIndex;
                folder.ProgramPath = program.Text.Trim(); folder.RunMode = (RunMode)mode.SelectedIndex;
                if (folder.Trigger != TriggerMode.Manual)
                {
                    folder.ProgramPath = PathSafety.Normalize(folder.ProgramPath);
                    if (!File.Exists(folder.ProgramPath) || !folder.ProgramPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("폴더 실행 조건의 .exe 파일을 다시 선택하세요.");
                }
                if (folder.Name.Length == 0) throw new ArgumentException("표시 이름을 입력하세요.");
                if (!Directory.Exists(folder.Path)) throw new ArgumentException("검사할 폴더가 없습니다.");
                PathSafety.AssertNoLinks(folder.Path);
                if (settings.Folders.Any(f => f.Id != folder.Id && (PathSafety.IsWithin(folder.Path, f.Path) || PathSafety.IsWithin(f.Path, folder.Path))))
                    throw new ArgumentException("기존 검사 폴더와 범위가 겹칩니다. 하위 폴더 포함 옵션을 사용하세요.");
                var exclusions = excluded.Select(r => r.Path).Append(addPath.Text).Where(p => !string.IsNullOrWhiteSpace(p));
                folder.ExcludedFolders = duplicate
                    ? FolderDuplicator.RebaseExclusions(exclusionSource, folder.Path, exclusions)
                    : PathSafety.NormalizeExclusions(folder.Path, exclusions);
                foreach (var rule in folder.Rules) PathSafety.ValidateRule(folder, rule);
                Result = folder; DialogResult = true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { EditorUi.Error(this, ex); }
        }, primary: true));
        if (original is not null && !duplicate)
            actions.Children.Add(EditorUi.Button("폴더 제거", (_, _) =>
            {
                if (MessageBox.Show(this, $"‘{original.Name}’ 폴더와 연결된 규칙을 목록에서 제거할까요? 실제 파일과 폴더는 삭제하지 않습니다.", "폴더 제거", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                DeleteRequested = true; DialogResult = true;
            }));
    }
}

internal sealed record ExcludedFolderRow(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
}
