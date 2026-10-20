using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using FileOrganizer.Core;

namespace FileOrganizer.Windows;

internal sealed class AnalysisPatternHelpWindow : Window
{
    public string? SelectedPattern { get; private set; }
    public string? SelectedSample { get; private set; }
    private readonly RegexPracticePanel practice;
    private readonly TabControl tabs;

    public AnalysisPatternHelpWindow(Window owner)
    {
        Owner = owner; Title = "분류 기준 입력 방법";
        SetResourceReference(StyleProperty, typeof(Window));
        Width = Math.Min(820, SystemParameters.WorkArea.Width); Height = Math.Min(820, SystemParameters.WorkArea.Height);
        MinWidth = Math.Min(520, Width); MinHeight = Math.Min(420, Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Resources.Add(typeof(TabItem), XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="TabItem">
             <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="Background" Value="{DynamicResource SurfaceBrush}"/>
             <Setter Property="FontSize" Value="15"/><Setter Property="FontWeight" Value="SemiBold"/>
             <Setter Property="Padding" Value="18,10"/><Setter Property="Margin" Value="0,0,8,0"/><Setter Property="MinHeight" Value="44"/>
             <Setter Property="Template"><Setter.Value>
              <ControlTemplate TargetType="TabItem">
               <Border Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{DynamicResource LineBrush}" BorderThickness="2" CornerRadius="6" Padding="{TemplateBinding Padding}">
                <ContentPresenter ContentSource="Header" HorizontalAlignment="Center" VerticalAlignment="Center" TextElement.Foreground="{TemplateBinding Foreground}"/>
               </Border>
               <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SoftBrush}"/></Trigger>
                <Trigger Property="IsSelected" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SoftBrush}"/><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger>
               </ControlTemplate.Triggers>
              </ControlTemplate>
             </Setter.Value></Setter>
            </Style>
            """));
        var shell = new DockPanel { Margin = new(24) }; Content = shell;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 14, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); shell.Children.Add(footer);
        practice = new RegexPracticePanel();
        var use = EditorUi.Button("연습한 식 사용", (_, _) => practice.Apply(), primary: true);
        use.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(use, "연습한 정규식과 첫 번째 파일명을 분석 입력에 적용");
        footer.Children.Add(use);
        var close = EditorUi.Button("닫기", (_, _) => Close()); close.IsCancel = true; footer.Children.Add(close);
        tabs = new TabControl { BorderThickness = new(0), Padding = new(0) };
        tabs.SetResourceReference(BackgroundProperty, "PageBrush"); tabs.SetResourceReference(ForegroundProperty, "TextBrush");
        tabs.Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="TabControl">
             <Grid>
              <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
              <Border BorderBrush="{DynamicResource LineBrush}" BorderThickness="0,0,0,1" Padding="2,2,2,12">
               <StackPanel Orientation="Horizontal" IsItemsHost="True"/>
              </Border>
              <Border Grid.Row="1" Background="{TemplateBinding Background}" Padding="0,14,0,0">
               <ContentPresenter Name="PART_SelectedContentHost" ContentSource="SelectedContent"/>
              </Border>
             </Grid>
            </ControlTemplate>
            """);
        AutomationProperties.SetName(tabs, "정규식 입력 방법 · 예시와 직접 연습");
        var examples = new StackPanel();
        examples.Children.Add(Text("어떤 이름끼리 묶을까요?", 24, bold: true));
        examples.Children.Add(Text("‘직접 연습’을 누르면 이 예시의 식과 파일명을 연습 탭에 채웁니다.", 15, sub: true));
        for (var index = 0; index < FilenameRegexLessons.All.Count; index++) AddExample(examples, index);
        tabs.Items.Add(new TabItem { Header = "예시", Content = Scroll(examples) });
        tabs.Items.Add(new TabItem { Header = "직접 연습", Content = Scroll(practice) });
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != tabs) return;
            var active = tabs.SelectedIndex == 1; use.Visibility = active ? Visibility.Visible : Visibility.Collapsed; practice.SetActive(active);
        };
        practice.ApplyRequested += (pattern, sample) => { SelectedPattern = pattern; SelectedSample = sample; DialogResult = true; };
        practice.BusyChanged += busy => { use.IsEnabled = !busy; examples.IsEnabled = !busy; };
        Closed += (_, _) => practice.Deactivate();
        shell.Children.Add(tabs); tabs.SelectedIndex = 0;
    }

    private void AddExample(Panel parent, int index)
    {
        var lesson = FilenameRegexLessons.All[index]; var card = new StackPanel();
        card.Children.Add(Text(lesson.Title, 17, bold: true)); card.Children.Add(Text(lesson.Description, 14, sub: true));
        var code = new TextBox { Text = lesson.Pattern, IsReadOnly = true, FontFamily = new("Consolas"), FontSize = 15,
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
        AutomationProperties.SetName(code, lesson.Title + " 예시 정규식 · 복사 가능"); card.Children.Add(code);
        card.Children.Add(Text("파일명  " + lesson.Sample, 15));
        var result = Text("그룹     " + lesson.ExpectedGroup, 16, bold: true); result.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); card.Children.Add(result);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var tryIt = EditorUi.Button("직접 연습", (_, _) => { practice.SelectLesson(index); tabs.SelectedIndex = 1; practice.FocusEditable(); });
        var use = EditorUi.Button("이 예시 사용", (_, _) => { SelectedPattern = lesson.Pattern; SelectedSample = lesson.Sample; DialogResult = true; });
        AutomationProperties.SetName(tryIt, lesson.Title + " 직접 연습 탭으로 이동");
        AutomationProperties.SetName(use, lesson.Title + " 예시 사용");
        actions.Children.Add(tryIt); actions.Children.Add(use); card.Children.Add(actions);
        var border = new Border { Child = card, CornerRadius = new(8), BorderThickness = new(1), Padding = new(16), Margin = new(0, 6, 0, 10) };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); border.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); parent.Children.Add(border);
    }

    private static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 12, 0) };
    internal static TextBlock Text(string value, double size, bool bold = false, bool sub = false)
    {
        var block = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.6,
            Margin = new(0, 0, 0, 8), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        if (sub) block.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush"); return block;
    }
}
