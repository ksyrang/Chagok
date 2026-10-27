using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Chagok.Core;

namespace Chagok.Windows;

internal sealed class QuickRuleStartDialog : Window
{
    public Guid? FolderId { get; private set; }
    public bool CreateFolderRequested { get; private set; }
    public string Destination { get; private set; } = "";

    public QuickRuleStartDialog(Window owner, AppSettings settings, Guid? selectedFolder, string destination)
    {
        Owner = owner; Title = "차곡에 추가";
        SetResourceReference(StyleProperty, typeof(Window));
        Width = 620; Height = Math.Min(450, SystemParameters.WorkArea.Height); MinWidth = 480; MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var shell = new DockPanel { Margin = new(24) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); shell.Children.Add(actions);
        var form = new StackPanel();
        shell.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(0, 0, 12, 0) });
        Content = shell;
        form.Children.Add(new TextBlock { Text = "이 폴더로 이동하는 규칙 추가", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 16) });
        var target = EditorUi.Input(destination);
        form.Children.Add(new TextBlock { Text = "이동할 폴더", Margin = new(0, 0, 0, 6) });
        AutomationProperties.SetName(target, "규칙의 이동할 폴더");
        form.Children.Add(EditorUi.Browse(target, (_, _) =>
        {
            var path = EditorUi.SelectFolder(this, "이동할 폴더 선택", target.Text);
            if (path is not null) target.Text = path;
        }));
        var sources = new ComboBox { ItemsSource = settings.Folders, DisplayMemberPath = nameof(FolderSpec.Name), Margin = new(0, 0, 0, 12) };
        sources.SelectedItem = settings.Folders.FirstOrDefault(f => f.Id == selectedFolder) ?? settings.Folders.FirstOrDefault();
        EditorUi.Field(form, "규칙을 추가할 검사 폴더", sources);
        var sourcePath = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
        sourcePath.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush");
        void ShowSourcePath() => sourcePath.Text = (sources.SelectedItem as FolderSpec)?.Path ?? "검사 폴더를 새로 등록하세요.";
        sources.SelectionChanged += (_, _) => ShowSourcePath(); ShowSourcePath(); form.Children.Add(sourcePath);
        EditorUi.Hint(form, "이 창에서는 파일을 이동하지 않습니다. 다음 화면에서 파일명 조건과 동작을 정해 규칙을 저장합니다.");
        bool ReadDestination()
        {
            try
            {
                Destination = ShellLaunch.ValidateDestination(target.Text.Trim());
                if (!Directory.Exists(Destination)) throw new ArgumentException("이동할 폴더가 없습니다.");
                PathSafety.AssertNoLinks(Destination); return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { EditorUi.Error(this, ex); return false; }
        }
        var create = EditorUi.Button("검사 폴더 새로 등록", (_, _) =>
        {
            if (!ReadDestination()) return;
            CreateFolderRequested = true; DialogResult = true;
        });
        create.Margin = new(0); create.HorizontalAlignment = HorizontalAlignment.Left;
        form.Children.Add(create);
        EditorUi.Hint(form, "새 검사 폴더 등록은 별도로 저장됩니다.");
        var cancel = EditorUi.Button("취소", (_, _) => Close()); cancel.IsCancel = true; actions.Children.Add(cancel);
        var next = EditorUi.Button("규칙 추가", (_, _) =>
        {
            if (sources.SelectedItem is not FolderSpec folder || !ReadDestination()) return;
            FolderId = folder.Id; DialogResult = true;
        }, primary: true);
        next.IsEnabled = settings.Folders.Count > 0; actions.Children.Add(next);
    }
}
