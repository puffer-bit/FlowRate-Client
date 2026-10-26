using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Client.ViewModels.Main;
using Client.ViewModels.Sessions.List;

namespace Client.Droid.Views;

/// <summary>
/// What the menu holds: the side panel of the desktop, with the server on top, its sessions under it
/// and the signed-in user at the bottom.
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
    /// Raised by a tap on the server we are connected to.
    /// </summary>
    public event Action? ServerMenuRequested;

    /// <summary>
    /// Raised by a tap on the list, whether or not it changed what is selected.
    /// </summary>
    public event Action? SessionTapped;

    /// <summary>
    /// Raised by a long press on a chat.
    /// </summary>
    public event Action<ChatSessionVM>? ChatMenuRequested;

    /// <summary>
    /// Raised by a long press on a voice session: its menu only moves it for now.
    /// </summary>
    public event Action<ISessionVM>? SessionMoveMenuRequested;

    /// <summary>
    /// Raised by a long press on the header of a category.
    /// </summary>
    public event Action<SessionCategoryVM>? CategoryMenuRequested;

    /// <summary>
    /// Raised by a long press on the user.
    /// </summary>
    public event Action? StatusMenuRequested;

    /// <summary>
    /// Raised by the gear next to the user.
    /// </summary>
    public event Action? SettingsRequested;

    private void OnSettingsClick(object? sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OnServerTitleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowVM viewModel)
            return;

        if (viewModel.ServerTitle.IsReady)
            ServerMenuRequested?.Invoke();
        else if (!viewModel.IsConnected)
            viewModel.ConnectCommand.Execute().Subscribe();
    }

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
                SessionMoveMenuRequested?.Invoke(hybrid);
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

    private void OnSessionsTapped(object? sender, TappedEventArgs e) => SessionTapped?.Invoke();

    private void OnHybridSessionTapped(object? sender, TappedEventArgs e)
    {
        if (_isMenuPress || sender is not StyledElement { DataContext: HybridSessionVM hybrid })
            return;

        hybrid.Connect();
    }

    private void OnUserCardContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is MainWindowVM { IsConnected: true })
            StatusMenuRequested?.Invoke();
    }

    private void OnUserCardClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowVM { IsConnected: true, UserCard.UserId: { } userId } viewModel)
            viewModel.UserCard.ViewProfileCommand.Execute(userId).Subscribe();
    }
}
