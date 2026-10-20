using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileOrganizer.Core;

namespace FileOrganizer.Windows;

public partial class FilenameAnalysisView : UserControl
{
    public event EventHandler? AnalysisRequested;
    public event EventHandler? RulesRequested;
    public event EventHandler? RuleRequested;
    private FilenameAnalysis? store;
    private Guid? folderId;
    private long groupPage, filePage, groupCount, fileCount;
    private bool loading;
    public AnalysisOptions Options
    {
        get
        {
            if (Brackets.SelectedIndex == 3 && string.IsNullOrWhiteSpace(CustomPattern.Text))
                throw new ArgumentException("분류 정규식을 입력하세요. ‘입력 방법’에서 예시를 확인할 수 있습니다.");
            var options = new AnalysisOptions(Brackets.SelectedIndex == 3 ? BracketKind.Square : (BracketKind)Brackets.SelectedIndex,
                Recursive.IsChecked == true, Brackets.SelectedIndex == 3 ? CustomPattern.Text : "");
            FilenameGroups.Validate(options); return options;
        }
    }
    public string? SelectedKey => (GroupGrid.SelectedItem as GroupDisplay)?.Key;
    public Guid? SelectedFolderId => folderId;
    private sealed record GroupDisplay(string Key, long Count, double Ratio);

    public FilenameAnalysisView()
    {
        InitializeComponent(); CustomPattern.Text = FilenameGroups.PatternExample;
        CustomPattern.ToolTip = "‘입력 방법’에서 파일명과 그룹 결과가 있는 예시를 골라 사용할 수 있습니다."; PreviewPattern();
    }
    private void PatternHelpClick(object sender, RoutedEventArgs e)
    {
        var help = new AnalysisPatternHelpWindow(Window.GetWindow(this));
        if (help.ShowDialog() != true || help.SelectedPattern is not { } pattern) return;
        CustomPattern.Text = pattern; SampleName.Text = help.SelectedSample ?? "";
        PreviewPattern();
    }
    private void PatternChanged(object sender, TextChangedEventArgs e)
    { if (PatternPreview is not null) { PreviewPattern(); OptionsChanged(sender, e); } }
    private void PreviewPattern()
    {
        try
        {
            var options = new AnalysisOptions(CustomPattern: CustomPattern.Text);
            if (string.IsNullOrWhiteSpace(options.CustomPattern)) { PatternPreview.Text = "정규식을 입력하세요."; return; }
            FilenameGroups.Validate(options);
            var keys = FilenameGroups.Extract(SampleName.Text, options);
            PatternPreview.Text = keys.Count == 0 ? "미리보기: 일치하는 구분값 없음" : "미리보기 그룹: " + string.Join(" · ", keys);
        }
        catch (ArgumentException ex) { PatternPreview.Text = ex.Message; }
    }
    public void WorkState(bool busy)
    { IsEnabled = !busy; Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; }
    public void ShowProgress(AnalysisProgress value) => HintText.Text = $"분석 중 · {value.Folder} · 파일 {value.Files:N0}개 · 파일은 이동하지 않습니다.";
    public void Bind(FilenameAnalysis? value, Guid? selected)
    {
        store = value; folderId = selected; groupPage = filePage = 0;
        var folder = store?.Folders().FirstOrDefault(f => f.Id == selected);
        TitleText.Text = folder?.Name ?? "파일명 분석"; PathText.Text = folder?.Path ?? "등록 폴더 분석으로 결과를 불러오세요.";
        TotalText.Text = folder is null ? "—" : $"{folder.Total:N0}개";
        MatchedText.Text = folder is null ? "—" : $"{folder.Matched:N0}개";
        GroupsText.Text = folder is null ? "—" : $"{folder.Groups:N0}개";
        UnmatchedText.Text = folder is null ? "" : $"패턴 없는 파일 {folder.Unmatched:N0}개";
        if (store is null) { HintText.Text = "분석은 이름만 읽습니다. 파일을 이동하거나 규칙을 실행하지 않습니다."; ResultText.Text = "아직 분석 결과가 없습니다."; }
        else
        {
            HintText.Text = $"결과 조건: {FilenameGroups.Description(store.Options)} · 하위 폴더 {(store.Options.Recursive ? "포함" : "제외")} · 같은 값은 파일당 1회";
            ResultText.Text = $"{store.CreatedUtc.ToLocalTime():HH:mm:ss} 분석 시점 결과 · 여러 값이 있는 파일은 각 그룹에서 집계합니다. 파일은 이동하지 않았습니다."
                + (store.WarningCount > 0 ? $"\n일부 경로 생략 · 경고 {store.WarningCount}개\n" + string.Join("\n", store.Warnings().Take(2)) : "");
            ResultText.ToolTip = string.Join("\n", store.Warnings());
        }
        LoadGroups();
    }
    private void Read(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { CreateRuleButton.IsEnabled = false; ResultText.Text = "분석 결과 조회 실패 · 다시 분석하세요. " + ex.Message; }
    }
    private void LoadGroups() => Read(() =>
    {
        loading = true;
        try
        {
            groupCount = store is null || folderId is null ? 0 : store.GroupCount(folderId, RepeatedOnly.IsChecked == true ? 2 : 1);
            groupPage = Math.Clamp(groupPage, 0, Math.Max(0, (groupCount - 1) / 100));
            var rows = groupCount == 0 ? [] : store!.Groups(folderId, RepeatedOnly.IsChecked == true ? 2 : 1, groupPage);
            var maximum = groupCount == 0 ? 1 : store!.Groups(folderId, RepeatedOnly.IsChecked == true ? 2 : 1)[0].Count;
            GroupGrid.ItemsSource = rows.Select(g => new GroupDisplay(g.Key, g.Count, (double)g.Count / maximum * 100)).ToArray();
            GroupGrid.SelectedIndex = rows.Count > 0 ? 0 : -1;
            GroupPrevious.IsEnabled = groupPage > 0; GroupNext.IsEnabled = (groupPage + 1) * 100 < groupCount;
            GroupPageText.Text = $"{groupCount:N0}그룹 · {groupPage + 1}/{Math.Max(1, (groupCount + 99) / 100)}";
        }
        finally { loading = false; }
        filePage = 0; LoadFiles();
    });
    private void LoadFiles() => Read(() =>
    {
        var key = SelectedKey;
        fileCount = key is null || store is null ? 0 : store.FileCount(folderId, key);
        FileGrid.ItemsSource = key is null || store is null ? Array.Empty<AnalysisFile>() : store.Files(folderId, key, filePage);
        FileTitle.Text = key is null ? "그룹을 선택하세요" : $"{key} 파일 {fileCount:N0}개";
        FileTitle.ToolTip = key;
        CreateRuleButton.IsEnabled = key is not null && folderId is not null;
        FilePrevious.IsEnabled = filePage > 0; FileNext.IsEnabled = (filePage + 1) * 100 < fileCount;
        FilePageText.Text = $"{fileCount:N0}개 · {filePage + 1}/{Math.Max(1, (fileCount + 99) / 100)}";
    });
    private void GroupChanged(object sender, SelectionChangedEventArgs e) { if (!loading) { filePage = 0; LoadFiles(); } }
    private void FilterChanged(object sender, RoutedEventArgs e) { groupPage = 0; LoadGroups(); }
    private void OptionsChanged(object sender, RoutedEventArgs e)
    {
        if (CustomFields is not null) CustomFields.Visibility = Brackets.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        if (!IsInitialized || HintText is null || store is null) return;
        HintText.Text = "조건 변경 · ‘다시 분석’을 눌러 새 조건을 적용하세요.";
    }
    private void PreviousGroups(object sender, RoutedEventArgs e) { if (groupPage > 0) { groupPage--; LoadGroups(); } }
    private void NextGroups(object sender, RoutedEventArgs e) { if ((groupPage + 1) * 100 < groupCount) { groupPage++; LoadGroups(); } }
    private void PreviousFiles(object sender, RoutedEventArgs e) { if (filePage > 0) { filePage--; LoadFiles(); } }
    private void NextFiles(object sender, RoutedEventArgs e) { if ((filePage + 1) * 100 < fileCount) { filePage++; LoadFiles(); } }
    private void AnalyzeClick(object sender, RoutedEventArgs e) => AnalysisRequested?.Invoke(this, EventArgs.Empty);
    private void RulesClick(object sender, RoutedEventArgs e) => RulesRequested?.Invoke(this, EventArgs.Empty);
    private void RuleClick(object sender, RoutedEventArgs e) => RuleRequested?.Invoke(this, EventArgs.Empty);
    private void FileDetails(object sender, MouseButtonEventArgs e)
    { if (FileGrid.SelectedItem is AnalysisFile file) MessageBox.Show(Window.GetWindow(this), file.Path, "분석 파일 전체 경로", MessageBoxButton.OK, MessageBoxImage.Information); }
}
