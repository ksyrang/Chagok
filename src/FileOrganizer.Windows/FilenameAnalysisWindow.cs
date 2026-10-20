using System.Windows;
using System.Windows.Controls;
using FileOrganizer.Core;

namespace FileOrganizer.Windows;

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
    private async void OpenFilenameAnalysis(object sender, RoutedEventArgs e)
    {
        if (busy || modal || exiting) return;
        InvalidatePlan(); ShowView(AnalysisView);
        if (filenameAnalysis is null) await AnalyzeFilenamesAsync();
        else AnalysisView.Bind(filenameAnalysis, selectedFolderId);
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
        catch (ArgumentException ex) { ShowError("분류 규칙을 확인하세요.", ex); return; }
        var folders = settings.Folders.Select(Storage.Clone).ToArray();
        string[] destinations;
        try { destinations = settings.Folders.SelectMany(f => f.Rules).Select(DestinationPaths.Resolve).Append(storage.DirectoryPath).ToArray(); }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        { ShowError("파일명 분석 조건을 확인하세요.", ex); return; }
        if (folders.Any(f => PathSafety.IsWithin(f.Path, storage.DirectoryPath)))
        { ShowError("파일명 분석을 시작하지 못했습니다.", new ArgumentException("차곡 데이터 저장소는 분석할 수 없습니다.")); return; }
        ClearFilenameAnalysis(); analyzing = true;
        SetBusy(true, "등록 폴더의 파일명을 분석하고 있습니다. 파일은 이동하지 않습니다…");
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
            result?.Dispose(); analyzing = false; SetBusy(false); PumpPending();
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
