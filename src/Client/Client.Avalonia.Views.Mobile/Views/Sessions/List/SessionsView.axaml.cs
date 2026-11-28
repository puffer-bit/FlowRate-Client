using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Client.ViewModels.Sessions.List;

namespace Client.Mobile.Views.Sessions.List;

/// <summary>
/// The sessions of the server, as in the side panel of the desktop: the categories, the chats and the
/// voice sessions with who is in them. The home screen shows it in place of the personal chats.
/// </summary>
public partial class SessionsView : UserControl
{
    public SessionsView()
    {
        InitializeComponent();

        // A long press asks for the context menu, as the right button does with a mouse
        SessionsListBox.AddHandler(ContextRequestedEvent, OnSessionContextRequested);

        // The finger that held a chat for its menu is lifted over it, which must not open the chat
        SessionsListBox.AddHandler(PointerPressedEvent, (_, _) => _isMenuPress = false, RoutingStrategies.Tunnel);
        SessionsListBox.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (_isMenuPress)
                e.Handled = true;

            _isMenuPress = false;
        }, RoutingStrategies.Tunnel);
    }

    private bool _isMenuPress;

    /// <summary>
    /// Raised by a long press on a chat.
    /// </summary>
    public event Action<ChatSessionVM>? ChatMenuRequested;

    /// <summary>
    /// Raised by a long press on a voice session.
    /// </summary>
    public event Action<HybridSessionVM>? HybridMenuRequested;

    /// <summary>
    /// Raised by a tap on a peer of a voice session.
    /// </summary>
    public event Action<PeerVM>? PeerMenuRequested;

    /// <summary>
    /// Raised by a long press on the header of a category.
    /// </summary>
    public event Action<SessionCategoryVM>? CategoryMenuRequested;

    private void OnSessionContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not StyledElement { DataContext: { } row })
            return;

        switch (row)
        {
            case ChatSessionVM chat:
                ChatMenuRequested?.Invoke(chat);
                break;
            case HybridSessionVM hybrid:
                HybridMenuRequested?.Invoke(hybrid);
                break;
            case SessionCategoryVM { IsEditing: false } category:
                CategoryMenuRequested?.Invoke(category);
                break;
            default:
                return;
        }

        e.Handled = true;
        _isMenuPress = true;
    }

    private void OnPeerTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not StyledElement { DataContext: PeerVM peer })
            return;

        PeerMenuRequested?.Invoke(peer);
        e.Handled = true;
    }

    private void OnCategoryTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not StyledElement { DataContext: SessionCategoryVM { IsEditing: false } category })
            return;

        category.ToggleCollapseCommand.Execute().Subscribe();
        e.Handled = true;
    }

    // The name opens for typing with the keyboard up and all of it selected
    private void OnCategoryEditorLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox editor)
            return;

        editor.PropertyChanged += (_, change) =>
        {
            if (change.Property != IsVisibleProperty || !editor.IsVisible)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                editor.Focus();
                editor.SelectAll();
            }, DispatcherPriority.Input);
        };
    }

    private void OnCategoryEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not StyledElement { DataContext: SessionCategoryVM category })
            return;

        if (e.Key == Key.Enter)
            category.CommitRenameCommand.Execute().Subscribe();
        else if (e.Key == Key.Escape)
            category.CancelRenameCommand.Execute().Subscribe();
        else
            return;

        e.Handled = true;
    }

    private void OnCategoryEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is StyledElement { DataContext: SessionCategoryVM category })
            category.CommitRenameCommand.Execute().Subscribe();
    }

    private void OnHybridSessionTapped(object? sender, TappedEventArgs e)
    {
        if (_isMenuPress || sender is not StyledElement { DataContext: HybridSessionVM hybrid })
            return;

        // The fold of the peers is a button of its own
        if ((e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().Any() == true)
            return;

        hybrid.Connect();
    }
}
