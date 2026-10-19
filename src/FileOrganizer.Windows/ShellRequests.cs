using System.Windows;
using FileOrganizer.Core;

namespace FileOrganizer.Windows;

public partial class MainWindow
{
    private readonly Queue<string> shellDestinations = [];

    public bool EnqueueDestination(string destination)
    {
        if (exiting || exitAfterWork || shellDestinations.Count >= 20) return false;
        try { destination = ShellLaunch.ValidateDestination(destination); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return false; }
        shellDestinations.Enqueue(destination);
        Reveal(); SetStatus($"탐색기 규칙 추가 요청 {shellDestinations.Count}개가 대기 중입니다.");
        Dispatcher.BeginInvoke(new Action(ProcessShellRequests));
        return true;
    }

    public void ReportShellError(string message) => SetStatus("탐색기 요청을 받지 못합니다. 차곡을 다시 실행하세요. " + message);

    public void ReportShellRegistrationError(string message)
    {
        SetStatus("탐색기 메뉴 등록을 확인하지 못했습니다. " + message);
        try { storage.Append(new(DateTime.UtcNow, "", "탐색기 연동", "", "", "메뉴 등록", "실패", message)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus(message + " / 기록 저장 실패: " + ex.Message); }
    }

    private void PersistExplorerAutoRegistration(bool enabled)
    {
        var copy = Storage.Clone(settings); copy.AutoRegisterExplorerMenu = enabled;
        storage.Save(copy); settings.AutoRegisterExplorerMenu = enabled;
    }

    private void ProcessShellRequests()
    {
        if (busy || modal || exiting || exitAfterWork || !shellDestinations.TryDequeue(out var destination)) return;
        modal = true;
        try
        {
            if (!Directory.Exists(destination)) throw new ArgumentException("탐색기에서 선택한 폴더가 없습니다.");
            PathSafety.AssertNoLinks(destination);
            Reveal();
            var start = new QuickRuleStartDialog(this, settings, selectedFolderId, destination);
            if (start.ShowDialog() != true) return;
            FolderSpec? folder;
            if (start.CreateFolderRequested)
            {
                var folderEditor = new FolderEditor(this, settings, null);
                if (folderEditor.ShowDialog() != true || folderEditor.Result is not { } newFolder) return;
                if (!Commit(() => { settings.Folders.Add(newFolder); selectedFolderId = newFolder.Id; })) return;
                RefreshFolders(); folder = SelectedFolder;
            }
            else folder = settings.Folders.FirstOrDefault(f => f.Id == start.FolderId);
            if (folder is null) return;
            var editor = new RuleEditor(this, folder, null, start.Destination);
            if (editor.ShowDialog() != true || editor.Result is not { } rule) return;
            if (Commit(() => { folder.Rules.Add(rule); selectedFolderId = folder.Id; }))
            { RefreshFolders(); ShowView(RulesView); SetStatus("탐색기에서 요청한 이동 규칙을 저장했습니다. 파일은 이동하지 않았습니다."); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { ShowError("탐색기 규칙 추가 요청을 처리하지 못했습니다.", ex); }
        finally
        {
            modal = false;
            if (shellDestinations.Count > 0) Dispatcher.BeginInvoke(new Action(ProcessShellRequests));
            else PumpPending();
        }
    }
}
