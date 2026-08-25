using System.Windows;
using RevitAgent.Gui.Services;

namespace RevitAgent.Gui;

public partial class App : Application
{
    // Surface unhandled UI-thread exceptions instead of dying silently — without this a
    // startup/XAML failure just makes the process vanish with no window and no message.
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeService.Apply();

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.ToString(), "RevitAgent 发生未处理异常",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);
    }
}
