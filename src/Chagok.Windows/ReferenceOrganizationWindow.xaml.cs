using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Chagok.Core;

namespace Chagok.Windows;

public partial class ReferenceOrganizationWindow : Window
{
    private sealed record TargetRow(string Path, string Keywords, ReferenceMatch Match)
    {
        public string Name => System.IO.Path.GetFileName(Path);
        public ReferenceTarget Spec => new(Path, Keywords, Match);
    }
    private FolderSpec folder;
    private readonly FolderSpec originalFolder;
    private readonly string[] exclusions;
    private readonly string cacheRoot;
    private readonly string protectedDirectory;
    private readonly ObservableCollection<TargetRow> targets = [];
    private ReferenceOrganization? result;
    private TargetRow? editing;
    private CancellationTokenSource? cancellation;
    private bool working, changing, loading, closeAfterWork, changed;
    private long filePage, groupPage, decidedCount;
    private string? selectedGroup;
    public DiskPlanStore? Plan { get; private set; }

    public ReferenceOrganizationWindow(Window owner, FolderSpec folder, string[] exclusions, string cacheRoot, string protectedDirectory)
    {
        InitializeComponent(); Owner = owner; SetResourceReference(StyleProperty, typeof(Window));
        this.folder = Storage.Clone(folder); originalFolder = Storage.Clone(folder); this.exclusions = exclusions; this.cacheRoot = cacheRoot; this.protectedDirectory = protectedDirectory;
        MinHeight = Math.Min(MinHeight, SystemParameters.WorkArea.Height);
        MinWidth = Math.Min(MinWidth, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        SourceInput.Text = folder.Path;
        SourceText.Text = "검사할 폴더: " + (folder.Path.Length > 0 ? folder.Path : "아래에서 선택하세요.");
        TargetsGrid.ItemsSource = targets; DestinationInput.ItemsSource = targets;
        RootInput.TextChanged += (_, _) => MarkChanged();
        KeywordsInput.TextChanged += (_, _) => { if (!loading) MarkChanged(); };
        MatchInput.SelectionChanged += (_, _) => { if (!loading) MarkChanged(); };
        RecursiveInput.Click += (_, _) => MarkChanged(); GroupPattern.TextChanged += (_, _) => MarkChanged();
        Closing += OnClosing;
        Closed += (_, _) => { result?.Dispose(); result = null; cancellation?.Dispose(); cancellation = null; };
    }
    private void BrowseSource(object sender, RoutedEventArgs e)
    {
        var path = EditorUi.SelectFolder(this, "이번에 검사할 폴더 선택", SourceInput.Text);
        if (path is not null) SourceInput.Text = path;
    }
    private void SourceChanged(object sender, TextChangedEventArgs e)
    {
        if (SourceText is not null) SourceText.Text = "검사할 폴더: " + SourceInput.Text;
        if (PlanButton is not null) MarkChanged();
    }
    private void MarkChanged()
    {
        if (result is null) return;
        changed = true; PlanButton.IsEnabled = false;
        ResultsSummary.Text = "이전 결과 · 설정 변경됨, 재분석 필요";
    }
    private void ShowSettings(bool edit)
    {
        changing = true;
        SettingsSection.IsExpanded = edit; ResultsSection.IsExpanded = !edit;
        SettingsRow.Height = edit ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        ResultsRow.Height = edit ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        changing = false;
    }
    private void SettingsExpanded(object sender, RoutedEventArgs e) { if (!changing && IsInitialized && e.Source == SettingsSection) ShowSettings(true); }
    private void SettingsCollapsed(object sender, RoutedEventArgs e) { if (!changing && IsInitialized && e.Source == SettingsSection) ShowSettings(result is null || working); }
    private void ResultsExpanded(object sender, RoutedEventArgs e) { if (!changing && IsInitialized && e.Source == ResultsSection) ShowSettings(false); }
    private void ResultsCollapsed(object sender, RoutedEventArgs e) { if (!changing && IsInitialized && e.Source == ResultsSection) ShowSettings(true); }
    private void SetWorking(bool value, string message = "")
    {
        working = value; SettingsSection.IsEnabled = !value; ResultsSection.IsEnabled = !value && result is not null;
        PlanButton.IsEnabled = !value && !changed && result is not null && decidedCount > 0;
        CancelWorkButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        Progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        if (message.Length > 0) StatusText.Text = message;
        cancellation?.Dispose(); cancellation = value ? new CancellationTokenSource() : null;
    }
    private void SaveKeywords()
    {
        if (editing is null) return;
        var next = editing with { Keywords = KeywordsInput.Text, Match = MatchInput.SelectedIndex == 1 ? ReferenceMatch.Group : ReferenceMatch.Contains };
        if (next == editing) return;
        var index = targets.IndexOf(editing); if (index < 0) return;
        loading = true; targets[index] = next; editing = next; TargetsGrid.SelectedItem = next; loading = false;
    }
    private void TargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || KeywordsInput is null) return;
        var selected = TargetsGrid.SelectedItem as TargetRow;
        SaveKeywords(); editing = selected;
        loading = true;
        TargetPath.Text = selected?.Path ?? "목적지를 선택하세요.";
        KeywordsInput.Text = selected?.Keywords ?? ""; MatchInput.SelectedIndex = selected?.Match == ReferenceMatch.Group ? 1 : 0;
        TargetsGrid.SelectedItem = selected;
        loading = false;
    }
    private void BrowseRoot(object sender, RoutedEventArgs e)
    {
        var path = EditorUi.SelectFolder(this, "정리된 기준 폴더 선택", RootInput.Text);
        if (path is null || PathSafety.Comparer.Equals(path, RootInput.Text)) return;
        RootInput.Text = path; editing = null; targets.Clear(); KeywordsInput.Clear(); TargetPath.Text = "하위 폴더를 가져오세요.";
    }
    private async void LoadTargets(object sender, RoutedEventArgs e)
    {
        if (working) return;
        SaveKeywords();
        var previous = targets.ToDictionary(t => t.Path, PathSafety.Comparer);
        var root = RootInput.Text;
        try { ValidateRoot(root); } catch (Exception ex) when (ex is ArgumentException or IOException) { Error(ex); return; }
        SetWorking(true, "기준 폴더의 하위 폴더를 읽고 있습니다…"); var token = cancellation!.Token;
        try
        {
            var rows = await Task.Run(() => ReferenceOrganization.ReadTargets(root, token)); token.ThrowIfCancellationRequested();
            editing = null; targets.Clear(); foreach (var row in rows.Where(r => !new Storage(protectedDirectory).IsDataPath(r.Path))) targets.Add(previous.TryGetValue(row.Path, out var draft) ? draft : new(row.Path, row.Keywords, row.Match));
            TargetsGrid.SelectedIndex = targets.Count > 0 ? 0 : -1;
            MarkChanged(); StatusText.Text = $"하위 폴더 {targets.Count}개 · 검색어를 확인한 후 분석하세요.";
        }
        catch (OperationCanceledException) { StatusText.Text = "폴더 가져오기를 취소했습니다."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Error(ex); }
        finally { SetWorking(false); ShowSettings(true); FinishClose(); }
    }
    private void ValidateRoot(string root)
    {
        if (new Storage(protectedDirectory).IsDataPath(root)) throw new ArgumentException("차곡 데이터 저장소는 기준 폴더로 사용할 수 없습니다.");
        if (PathSafety.IsWithin(PathSafety.Normalize(SourceInput.Text.Trim()), root)) throw new ArgumentException("검사 폴더는 기준 폴더와 같거나 그 안에 있을 수 없습니다.");
    }
    private AnalysisOptions ReadOptions()
    {
        var options = GroupInput.SelectedIndex == 3
            ? new AnalysisOptions(Recursive: RecursiveInput.IsChecked == true, CustomPattern: GroupPattern.Text)
            : new AnalysisOptions((BracketKind)GroupInput.SelectedIndex, RecursiveInput.IsChecked == true);
        if (GroupInput.SelectedIndex == 3 && string.IsNullOrWhiteSpace(GroupPattern.Text)) throw new ArgumentException("새 그룹 분류 정규식을 입력하세요.");
        FilenameGroups.Validate(options); return options;
    }
    private void GroupOptionsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupPattern is null) return;
        GroupPattern.Visibility = GroupInput.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed; MarkChanged();
    }
    private async void Analyze(object sender, RoutedEventArgs e)
    {
        if (working) return;
        AnalysisOptions options; ReferenceTarget[] specs; string root;
        try { folder = ReferenceOrganization.PrepareSource(originalFolder.Path.Length == 0 ? null : originalFolder, SourceInput.Text, protectedDirectory); SaveKeywords(); options = ReadOptions(); specs = targets.Select(t => t.Spec).ToArray(); root = PathSafety.Normalize(RootInput.Text); ValidateRoot(root); }
        catch (Exception ex) when (ex is ArgumentException or IOException) { Error(ex); ShowSettings(true); return; }
        SetWorking(true, "파일명을 분석하고 있습니다. 파일은 이동하지 않습니다.");
        SettingsSummary.Text = $"{root} · 목적지 {specs.Length}개 · {FilenameGroups.Description(options)} · 하위 폴더 {(options.Recursive ? "포함" : "제외")}";
        ShowSettings(false); var token = cancellation!.Token; ReferenceOrganization? scanned = null;
        try
        {
            var progress = new Progress<long>(count => { if (working) StatusText.Text = $"파일 {count:N0}개 분석 중 · 파일 이동 없음"; });
            scanned = await Task.Run(() => ReferenceOrganization.Scan(folder, root, specs, exclusions, cacheRoot, options, token, progress)); token.ThrowIfCancellationRequested();
            result?.Dispose(); result = scanned; scanned = null; changed = false;
            selectedGroup = null; filePage = groupPage = 0; FilterInput.SelectedIndex = 0; RefreshResults();
            StatusText.Text = $"분석 완료 · 파일 이동 없음 · 경고 {result.WarningCount}개" + (result.WarningCount > 0 ? "\n" + string.Join("\n", result.Warnings) : "");
        }
        catch (OperationCanceledException) { changed = true; ResultsSummary.Text = "이전 결과 · 분석 취소됨, 재분석 필요"; StatusText.Text = "분석을 취소했습니다. 파일은 변경하지 않았습니다."; ShowSettings(true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { changed = true; ResultsSummary.Text = "이전 결과 · 분석 실패, 재분석 필요"; Error(ex); ShowSettings(true); }
        finally { scanned?.Dispose(); SetWorking(false); FinishClose(); }
    }
    private void RefreshResults()
    {
        if (result is null) return;
        var decided = result.Count(ReferenceFilter.Decided); decidedCount = decided;
        ResultsSummary.Text = $"전체 {result.Count():N0} · 목적지 결정 {decided:N0} · 선택 필요 {result.Count(ReferenceFilter.Ambiguous):N0} · 목적지 없음 {result.Count(ReferenceFilter.Unmatched):N0}";
        if (changed) ResultsSummary.Text = "이전 결과 · 설정 변경됨, 재분석 필요 · " + ResultsSummary.Text;
        groupPage = Math.Clamp(groupPage, 0, Math.Max(0, (result.GroupCount - 1) / 100));
        loading = true; GroupsGrid.ItemsSource = result.Groups(groupPage); loading = false;
        GroupPageText.Text = $"{groupPage + 1}/{Math.Max(1, (result.GroupCount + 99) / 100)}";
        LoadFiles(); PlanButton.IsEnabled = !working && !changed && decided > 0;
    }
    private void LoadFiles()
    {
        if (result is null) return;
        var filter = selectedGroup is null ? (ReferenceFilter)FilterInput.SelectedIndex : ReferenceFilter.Unmatched;
        var count = selectedGroup is null ? result.Count(filter) : result.GroupFileCount(selectedGroup);
        filePage = Math.Clamp(filePage, 0, Math.Max(0, (count - 1) / 100));
        FilesGrid.ItemsSource = result.Files(filter, filePage, selectedGroup); FilesGrid.SelectedIndex = -1;
        FileDetails.Clear(); FilePageText.Text = $"{count:N0}개 · {filePage + 1}/{Math.Max(1, (count + 99) / 100)} · 100개씩";
        GroupFilterText.Text = selectedGroup is null ? "" : "그룹: " + selectedGroup;
    }
    private void FilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || result is null) return;
        selectedGroup = null; filePage = 0; loading = true; GroupsGrid.SelectedIndex = -1; loading = false; TryRead(LoadFiles);
    }
    private void GroupChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || GroupsGrid.SelectedItem is not ReferenceGroup group) return;
        selectedGroup = group.Key; filePage = 0; NewDestinationOption.IsChecked = true;
        try { NewNameInput.Text = result!.GroupOptions.CustomPattern.Length == 0 && FilenameGroups.ValidateKey(group.Key) == result!.GroupOptions.Brackets ? group.Key[1..^1] : group.Key; }
        catch (ArgumentException) { NewNameInput.Text = group.Key; }
        TryRead(LoadFiles);
    }
    private void ClearGroup(object sender, RoutedEventArgs e) { selectedGroup = null; GroupsGrid.SelectedIndex = -1; filePage = 0; TryRead(LoadFiles); }
    private void FileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (result is null || FilesGrid.SelectedItem is not ReferenceFile file) return;
        TryRead(() =>
        {
            FileDetails.Text = file.Path + "\n" + (file.Destination.Length > 0 ? "최종 목적지: " + file.Destination + "\n" : "")
                + string.Join("\n", result.Candidates(file.Id).Select(c => c.Destination + " · " + c.Reason));
            if (selectedGroup is null) { NewNameInput.Clear(); ExistingDestinationOption.IsChecked = true; }
            DestinationInput.SelectedItem = targets.FirstOrDefault(t => PathSafety.Comparer.Equals(t.Path, file.Destination));
        });
    }
    private string Destination()
    {
        if (result is null || changed) throw new ArgumentException("설정이 변경되었습니다. 다시 분석하세요.");
        if (NewDestinationOption.IsChecked == true) return ReferenceOrganization.NewDestination(result.Root, NewNameInput.Text);
        return DestinationInput.SelectedItem is TargetRow row ? row.Path : throw new ArgumentException("기존 목적지를 선택하거나 새 폴더명을 입력하세요.");
    }
    private void AssignFile(object sender, RoutedEventArgs e) => TryRead(() =>
    {
        if (FilesGrid.SelectedItem is not ReferenceFile file) throw new ArgumentException("파일을 선택하세요.");
        result!.Assign(file.Id, Destination()); RefreshResults();
    });
    private void AssignGroup(object sender, RoutedEventArgs e) => TryRead(() =>
    {
        if (selectedGroup is null) throw new ArgumentException("새 그룹을 선택하세요.");
        var destination = Destination(); var count = result!.GroupFileCount(selectedGroup);
        if (MessageBox.Show(this, $"그룹 ‘{selectedGroup}’의 미분류 파일 {count:N0}개를 다음 목적지로 지정합니다.\n{destination}\n파일 이동은 다음 검토 화면에서 실행합니다.", "그룹 목적지 지정", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        result.AssignGroup(selectedGroup, destination); RefreshResults();
    });
    private void HoldFile(object sender, RoutedEventArgs e) => TryRead(() => { if (FilesGrid.SelectedItem is ReferenceFile file) { result!.ClearAssignment(file.Id); RefreshResults(); } });
    private void PreviousFile(object sender, RoutedEventArgs e) { filePage = Math.Max(0, filePage - 1); TryRead(LoadFiles); }
    private void NextFile(object sender, RoutedEventArgs e) { filePage++; TryRead(LoadFiles); }
    private void PreviousGroup(object sender, RoutedEventArgs e) { groupPage = Math.Max(0, groupPage - 1); TryRead(RefreshResults); }
    private void NextGroup(object sender, RoutedEventArgs e) { groupPage++; TryRead(RefreshResults); }
    private async void ReviewPlan(object sender, RoutedEventArgs e)
    {
        if (working || changed || result is null) return;
        SetWorking(true, "목적지와 원본 상태를 확인하고 이동 계획을 만들고 있습니다…"); var token = cancellation!.Token;
        DiskPlanStore? plan = null;
        try
        {
            plan = await Task.Run(() => result.CreatePlan(cacheRoot, token)); token.ThrowIfCancellationRequested();
            Plan = plan; plan = null; working = false; DialogResult = true;
        }
        catch (OperationCanceledException) { StatusText.Text = "이동 계획 검토를 취소했습니다. 파일은 이동하지 않았습니다."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Error(ex); }
        finally { plan?.Dispose(); SetWorking(false); FinishClose(); }
    }
    private void TryRead(Action action)
    {
        if (working) return;
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Error(ex); }
    }
    private void Error(Exception ex)
    {
        StatusText.Text = ex.Message;
        if (ex is IOException or UnauthorizedAccessException) { changed = true; PlanButton.IsEnabled = false; }
    }
    private void CancelWork(object sender, RoutedEventArgs e) => cancellation?.Cancel();
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!working) return;
        e.Cancel = true; closeAfterWork = true; cancellation?.Cancel(); StatusText.Text = "작업을 안전하게 중단한 뒤 닫습니다…";
    }
    private void FinishClose() { if (closeAfterWork && !working) Close(); }
}
