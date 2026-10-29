using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Chagok.Core;

namespace Chagok.Windows;

public partial class ReferenceOrganizationWindow : UserControl
{
    private sealed record TargetRow(string Path, string Keywords, ReferenceMatch Match)
    {
        public string Name => System.IO.Path.GetFileName(Path);
        public ReferenceTarget Spec => new(Path, Keywords, Match);
    }
    public sealed record FileRow(ReferenceFile File, bool Existing)
    {
        public string Name => File.Name;
        public string Destination => File.Destination;
        public string Reason => File.Reason;
        public string Status => File.Destination.Length == 0 ? File.Reason == "사용자 보류" ? "보류" : File.Status : Existing ? "기존 폴더" : "새 이름 지정";
    }
    public sealed record ProposalRow(string Key, string Name, string Path, string Kind, long Count);
    public sealed record DestinationRow(string Path, string Kind, long Count);
    private FolderSpec folder;
    private readonly FolderSpec originalFolder;
    private readonly string[] exclusions;
    private readonly string cacheRoot;
    private readonly string protectedDirectory;
    private readonly ObservableCollection<TargetRow> targets = [];
    private ReferenceOrganization? result;
    private TargetRow? editing;
    private CancellationTokenSource? cancellation;
    private bool working, changing, loading, changed;
    private readonly MainWindow owner;
    private long filePage, groupPage, destinationPage, decidedCount;
    private string? selectedGroup, selectedDestination;
    public DiskPlanStore? Plan { get; private set; }

    public ReferenceOrganizationWindow(MainWindow owner, FolderSpec folder, string[] exclusions, string cacheRoot, string protectedDirectory)
    {
        InitializeComponent(); this.owner = owner;
        this.folder = Storage.Clone(folder); originalFolder = Storage.Clone(folder); this.exclusions = exclusions; this.cacheRoot = cacheRoot; this.protectedDirectory = protectedDirectory;
        SourceInput.Text = folder.Path;
        SourceText.Text = "검사할 폴더: " + (folder.Path.Length > 0 ? folder.Path : "아래에서 선택하세요.");
        TargetsGrid.ItemsSource = targets; DestinationInput.ItemsSource = targets;
        RootInput.TextChanged += (_, _) => MarkChanged();
        KeywordsInput.TextChanged += (_, _) => { if (!loading) MarkChanged(); };
        MatchInput.SelectionChanged += (_, _) => { if (!loading) MarkChanged(); };
        RecursiveInput.Click += (_, _) => MarkChanged(); GroupPattern.TextChanged += (_, _) => MarkChanged();

    }
    private void BrowseSource(object sender, RoutedEventArgs e)
    {
        var path = EditorUi.SelectFolder(owner, "이번에 검사할 폴더 선택", SourceInput.Text);
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
        changing = false;
        PageScroll.ScrollToTop();
    }
    private void PagePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node is not null && node != PageScroll;
            node = node is Visual or Visual3D ? VisualTreeHelper.GetParent(node) : (node as FrameworkContentElement)?.Parent)
        {
            if (node is not ScrollViewer inner) continue;
            var canScroll = e.Delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight;
            if (!canScroll)
            {
                PageScroll.ScrollToVerticalOffset(PageScroll.VerticalOffset - e.Delta / 3.0);
                e.Handled = true;
            }
            return;
        }
    }
    private void SettingsExpanded(object sender, RoutedEventArgs e) { if (!changing && IsInitialized && e.Source == SettingsSection) ShowSettings(true); }
    private void SettingsCollapsed(object sender, RoutedEventArgs e) { if (!changing && IsInitialized && e.Source == SettingsSection) ShowSettings(result is null || working); }
    private void ResultsExpanded(object sender, RoutedEventArgs e) { if (!changing && IsInitialized && e.Source == ResultsSection) ShowSettings(false); }
    private void ResultsCollapsed(object sender, RoutedEventArgs e) { if (!changing && IsInitialized && e.Source == ResultsSection) ShowSettings(true); }
    private void SetWorking(bool value, string message = "")
    {
        working = value; owner.ReferenceWorkState(value); AnalyzeButton.IsEnabled = CloseButton.IsEnabled = !value; SettingsSection.IsEnabled = !value; ResultsSection.IsEnabled = !value && result is not null;
        PlanButton.IsEnabled = !value && !changed && result is not null && decidedCount > 0;
        CancelWorkButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        Progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        if (message.Length > 0) StatusText.Text = message;
        cancellation?.Dispose(); cancellation = value ? new CancellationTokenSource() : null;
        if (value) owner.RegisterReferenceCancellation(cancellation!);
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
        var path = EditorUi.SelectFolder(owner, "정리된 기준 폴더 선택", RootInput.Text);
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
        finally { SetWorking(false); ShowSettings(true); }
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
        ResultsSummary.Text = "분석 중입니다. 완료되면 파일과 이동할 폴더를 표시합니다.";
        ShowSettings(false); var token = cancellation!.Token; ReferenceOrganization? scanned = null;
        try
        {
            var progress = new Progress<long>(count => { if (working) StatusText.Text = $"파일 {count:N0}개 분석 중 · 파일 이동 없음"; });
            scanned = await Task.Run(() => ReferenceOrganization.Scan(folder, root, specs, exclusions, cacheRoot, options, token, progress)); token.ThrowIfCancellationRequested();
            result?.Dispose(); result = scanned; scanned = null; changed = false;
            selectedGroup = selectedDestination = null; filePage = groupPage = destinationPage = 0; FilterInput.SelectedIndex = 0; RefreshResults();
            StatusText.Text = $"분석 완료 · 파일 이동 없음 · 경고 {result.WarningCount}개" + (result.WarningCount > 0 ? "\n" + string.Join("\n", result.Warnings) : "");
        }
        catch (OperationCanceledException) { changed = true; ResultsSummary.Text = result is null ? "분석이 취소되었습니다. 다시 분석하세요." : "이전 결과 · 분석 취소됨, 재분석 필요"; StatusText.Text = "분석을 취소했습니다. 파일은 변경하지 않았습니다."; ShowSettings(true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { changed = true; ResultsSummary.Text = result is null ? "분석에 실패했습니다. 아래 오류를 확인하세요." : "이전 결과 · 분석 실패, 재분석 필요"; Error(ex); ShowSettings(true); }
        finally { scanned?.Dispose(); SetWorking(false); }
    }
    private void RefreshResults()
    {
        if (result is null) return;
        var decided = result.Count(ReferenceFilter.Decided); decidedCount = decided;
        ResultsSummary.Text = $"전체 {result.Count():N0}개 · 이동 폴더 지정 {decided:N0}개 · 새 폴더 제안 {result.ProposedFileCount:N0}개 · 기존 폴더 선택 필요 {result.Count(ReferenceFilter.CandidateChoice):N0}개 · 여러 그룹 선택 필요 {result.Count(ReferenceFilter.GroupOverlap):N0}개 · 제안 없음 {result.Count(ReferenceFilter.NoGroup):N0}개 · 보류 {result.Count(ReferenceFilter.Held):N0}개";
        if (changed) ResultsSummary.Text = "이전 결과 · 설정 변경됨, 재분석 필요 · " + ResultsSummary.Text;
        destinationPage = Math.Clamp(destinationPage, 0, Math.Max(0, (result.DestinationCount - 1) / 100));
        loading = true;
        var destinations = result.Destinations(destinationPage)
            .Select(d => new DestinationRow(d.Path, Directory.Exists(d.Path) ? "기존 폴더" : "생성 예정", d.Count)).ToArray();
        DestinationsGrid.ItemsSource = destinations;
        DestinationsGrid.SelectedItem = destinations.FirstOrDefault(d => PathSafety.Comparer.Equals(d.Path, selectedDestination));
        if (selectedDestination is not null && DestinationsGrid.SelectedItem is null) selectedDestination = null;
        loading = false;
        DestinationPageText.Text = $"{destinationPage + 1}/{Math.Max(1, (result.DestinationCount + 99) / 100)} · 100개씩";
        groupPage = Math.Clamp(groupPage, 0, Math.Max(0, (result.ProposalCount - 1) / 100));
        loading = true;
        var groups = result.Proposals(groupPage).Select(g =>
        {
            var name = ProposalName(g.Key); var path = System.IO.Path.Combine(result.Root, name);
            return new ProposalRow(g.Key, name, path, Directory.Exists(path) ? "기존 폴더" : "새 폴더", g.Count);
        }).ToArray();
        GroupsGrid.ItemsSource = groups;
        GroupsGrid.SelectedItem = groups.FirstOrDefault(g => g.Key == selectedGroup);
        if (selectedGroup is not null && GroupsGrid.SelectedItem is null) selectedGroup = null;
        loading = false;
        GroupPageText.Text = $"{groupPage + 1}/{Math.Max(1, (result.ProposalCount + 99) / 100)} · 100개씩";
        LoadFiles(); PlanButton.IsEnabled = !working && !changed && decided > 0;
    }
    private void LoadFiles()
    {
        if (result is null) return;
        var filter = selectedGroup is null ? FilterInput.SelectedIndex == 2 ? ReferenceFilter.CandidateChoice : (ReferenceFilter)FilterInput.SelectedIndex : ReferenceFilter.Unmatched;
        var count = selectedDestination is not null ? result.DestinationFileCount(selectedDestination)
            : selectedGroup is null ? result.Count(filter) : result.ProposalFileCount(selectedGroup);
        filePage = Math.Clamp(filePage, 0, Math.Max(0, (count - 1) / 100));
        var files = selectedDestination is not null ? result.DestinationFiles(selectedDestination, filePage)
            : result.Files(filter, filePage, selectedGroup, proposalOnly: selectedGroup is not null);
        FilesGrid.ItemsSource = files.Select(f => new FileRow(f, targets.Any(t => PathSafety.Comparer.Equals(t.Path, f.Destination)))).ToArray(); FilesGrid.SelectedIndex = -1;
        FileDetails.Clear(); FilePageText.Text = $"{count:N0}개 · {filePage + 1}/{Math.Max(1, (count + 99) / 100)} · 100개씩";
        GroupFilterText.Text = selectedDestination is not null ? "폴더: " + selectedDestination : selectedGroup is null ? "" : "제안: " + selectedGroup;
        UpdateSelectionHint();
    }
    private void UpdateSelectionHint()
    {
        var file = (FilesGrid.SelectedItem as FileRow)?.File;
        AssignGroupButton.IsEnabled = HoldGroupButton.IsEnabled = selectedGroup is not null;
        AssignFileButton.IsEnabled = HoldFileButton.IsEnabled = file is not null;
        SelectionHint.Text = file is not null
            ? $"선택 파일: {file.Name}" + (selectedGroup is null ? "" : $" · 그룹: {selectedGroup}")
            : selectedGroup is not null ? $"선택 그룹: {selectedGroup} · 새 폴더 이름을 확인하고 그룹에 적용하세요."
            : "아직 선택한 그룹이나 파일이 없습니다. 아래 목록에서 선택하세요.";
    }
    private void FilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || result is null) return;
        selectedGroup = selectedDestination = null; filePage = 0; loading = true; GroupsGrid.SelectedIndex = -1; DestinationsGrid.SelectedIndex = -1; loading = false; TryRead(LoadFiles);
    }
    private string ProposalName(string key)
    {
        try { return result!.GroupOptions.CustomPattern.Length == 0 && FilenameGroups.ValidateKey(key) == result.GroupOptions.Brackets ? key[1..^1] : key; }
        catch (ArgumentException) { return key; }
    }
    private void GroupChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || GroupsGrid.SelectedItem is not ProposalRow group) return;
        selectedGroup = group.Key; selectedDestination = null; loading = true; DestinationsGrid.SelectedIndex = -1; loading = false; filePage = 0; NewDestinationOption.IsChecked = true;
        NewNameInput.Text = group.Name;
        TryRead(LoadFiles);
        AssignGroupButton.BringIntoView();
    }
    private void DestinationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || DestinationsGrid.SelectedItem is not DestinationRow row) return;
        selectedDestination = row.Path; selectedGroup = null; filePage = 0;
        loading = true; GroupsGrid.SelectedIndex = -1; loading = false;
        TryRead(LoadFiles);
        FilesGrid.BringIntoView();
    }
    private void ClearGroup(object sender, RoutedEventArgs e) { selectedGroup = null; GroupsGrid.SelectedIndex = -1; filePage = 0; TryRead(LoadFiles); }
    private void FileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectionHint is null) return;
        UpdateSelectionHint();
        if (result is null || FilesGrid.SelectedItem is not FileRow row) return;
        var file = row.File;
        TryRead(() =>
        {
            FileDetails.Text = file.Path + "\n" + (file.Destination.Length > 0 ? "최종 목적지: " + file.Destination + "\n" : "")
                + string.Join("\n", result.Candidates(file.Id).Select(c => c.Destination + " · " + c.Reason));
            if (selectedGroup is null) { NewNameInput.Clear(); ExistingDestinationOption.IsChecked = true; }
            DestinationInput.SelectedItem = targets.FirstOrDefault(t => PathSafety.Comparer.Equals(t.Path, file.Destination));
        });
        AssignFileButton.BringIntoView();
    }
    private string Destination()
    {
        if (result is null || changed) throw new ArgumentException("설정이 변경되었습니다. 다시 분석하세요.");
        if (NewDestinationOption.IsChecked == true) return ReferenceOrganization.NewDestination(result.Root, NewNameInput.Text);
        return DestinationInput.SelectedItem is TargetRow row ? row.Path : throw new ArgumentException("기존 목적지를 선택하거나 새 폴더명을 입력하세요.");
    }
    private void AssignFile(object sender, RoutedEventArgs e) => TryRead(() =>
    {
        if (FilesGrid.SelectedItem is not FileRow row) throw new ArgumentException("파일을 선택하세요.");
        result!.Assign(row.File.Id, Destination()); RefreshResults();
        StatusText.Text = "선택 파일의 이동할 폴더를 지정했습니다. 파일은 아직 이동하지 않았습니다.";
    });
    private void AssignGroup(object sender, RoutedEventArgs e) => TryRead(() =>
    {
        if (selectedGroup is null) throw new ArgumentException("새 그룹을 선택하세요.");
        var destination = Destination(); var count = result!.ProposalFileCount(selectedGroup);
        if (MessageBox.Show(owner, $"그룹 ‘{selectedGroup}’의 미분류 파일 {count:N0}개를 다음 목적지로 지정합니다.\n{destination}\n파일 이동은 다음 검토 화면에서 실행합니다.", "그룹 목적지 지정", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        result.AssignProposal(selectedGroup, destination); RefreshResults();
        StatusText.Text = $"선택 그룹의 미분류 파일 {count:N0}개에 폴더를 지정했습니다. 파일은 아직 이동하지 않았습니다.";
    });
    private void HoldGroup(object sender, RoutedEventArgs e) => TryRead(() =>
    {
        if (selectedGroup is null) return;
        var count = result!.ProposalFileCount(selectedGroup);
        result.HoldProposal(selectedGroup); RefreshResults();
        StatusText.Text = $"제안의 파일 {count:N0}개를 보류했습니다. 파일은 이동하지 않았습니다.";
    });
    private void HoldFile(object sender, RoutedEventArgs e) => TryRead(() => { if (FilesGrid.SelectedItem is FileRow row) { result!.ClearAssignment(row.File.Id); RefreshResults(); } });
    private void PreviousFile(object sender, RoutedEventArgs e) { filePage = Math.Max(0, filePage - 1); TryRead(LoadFiles); }
    private void NextFile(object sender, RoutedEventArgs e) { filePage++; TryRead(LoadFiles); }
    private void PreviousGroup(object sender, RoutedEventArgs e) { groupPage = Math.Max(0, groupPage - 1); TryRead(RefreshResults); }
    private void NextGroup(object sender, RoutedEventArgs e) { groupPage++; TryRead(RefreshResults); }
    private void PreviousDestination(object sender, RoutedEventArgs e) { destinationPage = Math.Max(0, destinationPage - 1); TryRead(RefreshResults); }
    private void NextDestination(object sender, RoutedEventArgs e) { destinationPage++; TryRead(RefreshResults); }
    private async void ReviewPlan(object sender, RoutedEventArgs e)
    {
        if (working || changed || result is null) return;
        SetWorking(true, "목적지와 원본 상태를 확인하고 이동 계획을 만들고 있습니다…"); var token = cancellation!.Token;
        DiskPlanStore? plan = null;
        try
        {
            plan = await Task.Run(() => result.CreatePlan(cacheRoot, token)); token.ThrowIfCancellationRequested();
            Plan = plan; plan = null;
        }
        catch (OperationCanceledException) { StatusText.Text = "이동 계획 검토를 취소했습니다. 파일은 이동하지 않았습니다."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Error(ex); }
        finally { plan?.Dispose(); SetWorking(false); }
        if (Plan is not null) await owner.ReferencePlanReady(this);
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
    private void CloseClick(object sender, RoutedEventArgs e) => owner.CloseReference();
    public void RequireReanalysis() => MarkChanged();
    public void Release() { result?.Dispose(); result = null; cancellation?.Dispose(); cancellation = null; Plan?.Dispose(); Plan = null; }
    public DiskPlanStore? TakePlan() { var plan = Plan; Plan = null; return plan; }
}
