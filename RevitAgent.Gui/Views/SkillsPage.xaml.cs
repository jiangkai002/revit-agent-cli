using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RevitAgent.Cli;
using RevitAgent.Gui.ViewModels;

namespace RevitAgent.Gui.Views;

public partial class SkillsPage : NavigationPage
{
    public SkillsPage()
    {
        InitializeComponent();
        DataContext = MainWindow.SkillsViewModel;
    }

    private void ViewSkill_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SkillManifest skill })
            new SkillViewWindow(skill.Name) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private async void InstallLocalZip_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择技能 ZIP 压缩包",
            Filter = "技能压缩包 (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is SkillsViewModel viewModel)
            await viewModel.InstallLocalZipAsync(dialog.FileName);
    }
}
