using RevitAgent.Gui.ViewModels;
using RevitAgent.Gui.Services;

namespace RevitAgent.Gui;

public partial class MainWindow
{
    internal System.Windows.FrameworkElement NavigationViewport => NavigationHost;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ThemeService.Apply(this);
            RootNavigation.Navigate(typeof(Views.ChatPage));
        };
    }

    // Pages are recreated on each navigation; hand each its ViewModel through a simple
    // locator (no DI container in v1). ViewModels themselves are singletons per app run.
    internal static ChatViewModel ChatViewModel { get; } = new();
    internal static SkillsViewModel SkillsViewModel { get; } = new();
    internal static SettingsViewModel SettingsViewModel { get; } = new();
}
