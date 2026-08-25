using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using RevitAgent.Gui.Models;
using RevitAgent.Gui.ViewModels;

namespace RevitAgent.Gui.Views;

public partial class ChatPage : NavigationPage
{
    public ChatPage()
    {
        InitializeComponent();
        KeepAlive = true; // keep this page instance (scroll position) across navigations
        DataContext = MainWindow.ChatViewModel;

        // The VM outlives this page; weak subscription avoids leaking page instances.
        System.Windows.WeakEventManager<ObservableCollection<ChatItem>, NotifyCollectionChangedEventArgs>
            .AddHandler(((ChatViewModel)DataContext).Items, nameof(ObservableCollection<ChatItem>.CollectionChanged),
                (_, _) => AutoScroll());
    }

    /// <summary>Follow the tail only when the user is already near the bottom (never yank
    /// someone who scrolled up to read).</summary>
    private void AutoScroll()
    {
        if (Transcript.ExtentHeight - Transcript.ViewportHeight - Transcript.VerticalOffset <= 80)
            Transcript.ScrollToEnd();
    }

    private void InputBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.Control)
            return;
        if (DataContext is ChatViewModel vm && vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// MarkdownScrollViewer and read-only TextBoxes are nested scrolling controls and consume
    /// wheel input before the transcript sees it. Handle the tunneling event at page level so
    /// wheel input anywhere over the message surface always moves the transcript. Outside the
    /// transcript we still mark it handled, preventing NavigationView's hidden ScrollViewer from
    /// moving the whole page (and the composer with it).
    /// </summary>
    private void ChatPage_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Transcript.IsMouseOver && Transcript.ScrollableHeight > 0)
        {
            var offset = Transcript.VerticalOffset - e.Delta * 0.75;
            Transcript.ScrollToVerticalOffset(Math.Clamp(offset, 0, Transcript.ScrollableHeight));
        }

        e.Handled = true;
    }
}
