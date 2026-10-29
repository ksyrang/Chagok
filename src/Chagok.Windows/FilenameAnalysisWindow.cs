using System.Windows;
using System.Windows.Controls;
using Chagok.Core;

namespace Chagok.Windows;

public partial class MainWindow
{
    private FilenameAnalysis? filenameAnalysis;
    private bool analyzing;
    private void InitializeFilenameAnalysis()
    {
        AnalysisView.AnalysisRequested += async (_, _) => await AnalyzeFilenamesAsync();
        AnalysisView.RulesRequested += (_, _) => { if (!busy) { ShowView(RulesView); PumpPending(); } };
        AnalysisView.RuleRequested += (_, _) => AddAnalysisRule();
    }
    private void BackToStart(object sender, RoutedEventArgs e)
    {
        if (busy || modal) return;
        InvalidatePlan(); RefreshRules(); ShowView(StartView); PumpPending();
    }
    private void StartRules(object sender, RoutedEventArgs e)
    {
        if (busy || modal || SelectedFolder is null) return;
        InvalidatePlan(); ShowView(RulesView); PumpPending();
    }
    private void StartReference(object sender, RoutedEventArgs e)
    {
        if (busy || modal || exiting) return;
        try
        {
            var folder = SelectedFolder ?? new FolderSpec { Name = "이번 검사" };
            var excluded = settings.Folders.SelectMany(f => f.Rules).Select(DestinationPaths.Resolve).Concat(storage.ProtectedDirectories).ToArray();
            ReferenceView.Content = new ReferenceOrganizationWindow(this, folder, excluded, Path.Combine(storage.DirectoryPath, "scan-cache"), storage.DirectoryPath);
            awaitingReview = true; ShowView(ReferenceView);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { ShowError("기준 폴더 정리를 준비하지 못했습니다.", ex); }
    }
    internal void ReferenceWorkState(bool value) => SetBusy(value);
    internal void RegisterReferenceCancellation(CancellationTokenSource source) => workCancellation?.Token.Register(source.Cancel);
    internal void CloseReference()
    {
        if (busy) return;
        (ReferenceView.Content as ReferenceOrganizationWindow)?.Release(); ReferenceView.Content = null;
        InvalidatePlan(); ShowView(StartView); PumpPending();
    }
    internal async Task ReferencePlanReady(ReferenceOrganizationWindow view)
    {
        var plan = view.TakePlan();
        if (plan is null || exiting || exitAfterWork) { plan?.Dispose(); return; }
        try
        {
            ClearFilenameAnalysis(); InvalidatePlan(); currentPlan = plan; plan = null; planExecutable = true;
            PreviewBackButton.Content = "← 분석 결과";
            SetBusy(true, "이동 계획을 불러오고 있습니다…");
            await LoadPageAsync(0);
            PreviewSubtitle.Text = $"{currentPlan.Header.FolderName} · 기준 폴더 정리 · 대상 {currentPlan.Count:N0}개";
            var warnings = currentPlan.ReadWarnings(); ScanWarnings.Text = string.Join("\n", warnings);
            ScanWarnings.Visibility = currentPlan.WarningCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowScanReady(currentPlan); awaitingReview = true; ShowView(PreviewView); UpdateSelection();
            SetStatus("이동 계획을 확인한 후 ‘선택 항목 실행’을 누르세요. 영구 규칙은 저장하지 않았습니다.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { InvalidatePlan(); awaitingReview = true; ShowError("기준 폴더 정리를 준비하지 못했습니다.", ex); }
        finally { plan?.Dispose(); if (busy) SetBusy(false); PumpPending(); }
    }
    private void OpenFilenameAnalysis(object sender, RoutedEventArgs e)
    {
        if (busy || modal || exiting) return;
        InvalidatePlan(); ShowView(AnalysisView);
        AnalysisView.Bind(filenameAnalysis, selectedFolderId);
    }
    private async void AnalyzeRegisteredFolders(object sender, RoutedEventArgs e)
    { if (!busy && !modal && !exiting) { InvalidatePlan(); ShowView(AnalysisView); await AnalyzeFilenamesAsync(); } }
    private void ClearFilenameAnalysis()
    {
        var old = filenameAnalysis; filenameAnalysis = null;
        AnalysisView.Bind(null, selectedFolderId); old?.Dispose();
        FolderList.Items.Refresh();
    }
    private void AnalysisFolderSummaryLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock { DataContext: FolderSpec folder } block) return;
        try { block.Text = filenameAnalysis?.Folders().FirstOrDefault(f => f.Id == folder.Id)?.Summary ?? "아직 분석하지 않음"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { block.Text = "분석 결과 조회 실패"; }
    }
    private async Task AnalyzeFilenamesAsync()
    {
        if (busy || settings.Folders.Count == 0) { SetStatus("먼저 검사할 폴더를 등록하세요."); return; }
        AnalysisOptions options;
        try { options = AnalysisView.Options; }
        catch (ArgumentException ex) { AnalysisView.ShowSettings(); ShowError("분류 규칙을 확인하세요.", ex); return; }
        var folders = settings.Folders.Select(Storage.Clone).ToArray();
        string[] destinations;
        try { destinations = settings.Folders.SelectMany(f => f.Rules).Select(DestinationPaths.Resolve).Concat(storage.ProtectedDirectories).ToArray(); }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        { AnalysisView.ShowSettings(); ShowError("파일명 분석 조건을 확인하세요.", ex); return; }
        if (folders.Any(f => storage.IsDataPath(f.Path)))
        { AnalysisView.ShowSettings(); ShowError("파일명 분석을 시작하지 못했습니다.", new ArgumentException("차곡 데이터 저장소는 분석할 수 없습니다.")); return; }
        ClearFilenameAnalysis(); analyzing = true;
        SetBusy(true, "등록 폴더의 파일명을 분석하고 있습니다. 파일은 이동하지 않습니다…");
        AnalysisView.BeginAnalysis(options);
        var token = workCancellation!.Token;
        FilenameAnalysis? result = null;
        try
        {
            var progress = new Progress<AnalysisProgress>(p => { if (analyzing && !exiting) AnalysisView.ShowProgress(p); });
            result = await Task.Run(() => FilenameAnalysis.Scan(folders, destinations,
                Path.Combine(storage.DirectoryPath, "analysis-cache"), options, token, progress));
            token.ThrowIfCancellationRequested();
            filenameAnalysis = result; result = null;
            AnalysisView.Bind(filenameAnalysis, selectedFolderId);
            FolderList.Items.Refresh();
            SetStatus(filenameAnalysis.WarningCount == 0 ? "파일명 분석 완료 · 파일은 이동하지 않았습니다."
                : $"파일명 분석 완료 · 일부 생략 · 경고 {filenameAnalysis.WarningCount}개. 파일은 이동하지 않았습니다.");
        }
        catch (OperationCanceledException) { SetStatus("파일명 분석을 취소했습니다. 파일은 변경하지 않았습니다."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { ShowError("파일명 분석을 완료하지 못했습니다.", ex); }
        finally
        {
            result?.Dispose(); analyzing = false; SetBusy(false); AnalysisView.EndAnalysis(); PumpPending();
        }
    }
    private void AddAnalysisRule()
    {
        if (busy || modal || filenameAnalysis is null || AnalysisView.SelectedKey is not { } key
            || SelectedFolder is not { } folder) return;
        var preset = FilenameGroups.Rule(folder, key, filenameAnalysis.Options);
        modal = true;
        try
        {
            var editor = new RuleEditor(this, folder, null, rulePreset: preset);
            if (editor.ShowDialog() != true || editor.Result is not { } rule) return;
            if (Commit(() => { folder.Rules.Add(rule); DestinationPaths.Remember(folder, rule); }))
            { RefreshRules(); ShowView(RulesView); SetStatus("구분값 규칙을 저장했습니다. 파일 이동은 검사 및 실행으로 확인하세요."); }
        }
        finally { modal = false; PumpPending(); }
    }
}
