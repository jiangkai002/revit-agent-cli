using System.Windows;
using RevitAgent.Cli;
using WpfUiWindow = Wpf.Ui.Controls.FluentWindow;

namespace RevitAgent.Gui.Views;

public partial class SkillViewWindow : WpfUiWindow
{
    public SkillViewWindow(string skillName)
    {
        InitializeComponent();
        Title = $"技能 · {skillName}";
        SkillBodyViewer.Markdown = SkillStore.Show(skillName) ?? $"未找到技能: {skillName}";
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
