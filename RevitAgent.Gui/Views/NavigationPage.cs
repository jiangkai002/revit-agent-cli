using System.Windows;
using System.Windows.Controls;

namespace RevitAgent.Gui.Views;

/// <summary>
/// Keeps a page constrained to the finite viewport supplied by the main navigation host.
/// WPF-UI's NavigationView can measure frame content with infinite height; without this,
/// a long transcript becomes the page's desired height and the whole page is clipped.
/// </summary>
public class NavigationPage : Page
{
    private FrameworkElement? _viewport;

    public NavigationPage()
    {
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not MainWindow mainWindow)
            return;

        _viewport = mainWindow.NavigationViewport;
        WeakEventManager<FrameworkElement, SizeChangedEventArgs>.AddHandler(
            _viewport, nameof(FrameworkElement.SizeChanged), OnViewportSizeChanged);
        ConstrainToViewport();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewport is null) return;
        WeakEventManager<FrameworkElement, SizeChangedEventArgs>.RemoveHandler(
            _viewport, nameof(FrameworkElement.SizeChanged), OnViewportSizeChanged);
        _viewport = null;
    }

    private void OnViewportSizeChanged(object? sender, SizeChangedEventArgs e) =>
        ConstrainToViewport();

    private void ConstrainToViewport()
    {
        if (_viewport is null || _viewport.ActualHeight <= 0) return;
        Height = _viewport.ActualHeight;
        MaxHeight = _viewport.ActualHeight;
    }
}
