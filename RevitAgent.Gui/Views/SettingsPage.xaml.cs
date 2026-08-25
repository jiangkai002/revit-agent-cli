using System.Windows.Controls;
using RevitAgent.Gui.ViewModels;

namespace RevitAgent.Gui.Views;

public partial class SettingsPage : NavigationPage
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = MainWindow.SettingsViewModel;
    }
}
