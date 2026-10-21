using Avalonia.Controls;
using System.Reactive.Disposables;
using Client.Services;
using Client.Views.Common;
using Avalonia.Input;
using Avalonia.Interactivity;
using Client.Avalonia.Localization;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.Views.Dialogs;
using Client.ViewModels.Sessions.List;

namespace Client.Views.Sessions.List;

internal partial class SessionsList : ViewControl<SessionsListVM>
{
    // Stands for the icon in the text of the hint: the object replacement character
    private const string IconPlace = "\uFFFC";

    private bool _pointerPressed;

    private readonly SessionListDrag _drag;

    public SessionsList()
    {
        InitializeComponent();
        
        SessionsListBox.AddHandler(PointerPressedEvent, SuppressRightButtonSelection, RoutingStrategies.Tunnel);
        SessionsListBox.AddHandler(PointerPressedEvent, (_, _) => _pointerPressed = true, RoutingStrategies.Tunnel, true);
        SessionsListBox.AddHandler(PointerReleasedEvent, (_, _) => _pointerPressed = false, RoutingStrategies.Bubble, true);
        SessionsListBox.AddHandler(PointerCaptureLostEvent, (_, _) => _pointerPressed = false, RoutingStrategies.Bubble, true);
        SessionsListBox.AddHandler(RequestBringIntoViewEvent, SuppressPointerBringIntoView, RoutingStrategies.Bubble);
        _drag = new SessionListDrag(SessionsListBox, DragLayer, () => ViewModel);
        
        this.WhenActivated(disposables =>
        {
            UpdateEmptyHint();
            Strings.LanguageChanged += UpdateEmptyHint;
            Disposable.Create(() => Strings.LanguageChanged -= UpdateEmptyHint).DisposeWith(disposables);

            ViewModel!.ShowMessageBox.RegisterMessageBoxHandler(this).DisposeWith(disposables);

            // Nothing to create into while there is no list from a server
            ViewModel.WatchProperty(nameof(SessionsListVM.CanCreate), vm => vm.CanCreate)
                .Subscribe(canCreate => ListArea.ContextFlyout = canCreate ? (Flyout)Resources["CreateMenu"]! : null)
                .DisposeWith(disposables);
        });
    }

    /// <summary>
    /// The hint of an empty list has the icon of the create button inside the sentence. The text is one
    /// message, so that a translation puts the icon where its word order needs it.
    /// </summary>
    private void UpdateEmptyHint()
    {
        var parts = Strings.Format("sessions.empty", ("icon", IconPlace)).Split(IconPlace, 2);

        EmptyHintBeforeIcon.Text = parts[0];
        EmptyHintAfterIcon.Text = parts.Length > 1 ? parts[1] : string.Empty;
    }

    /// <summary>
    /// A right click opens the context menu of a session. The list would also select the item on
    /// it, and selecting a chat opens that chat, so the right button never reaches the selection.
    /// The context menu is raised by a separate event and still opens.
    /// </summary>
    private static void SuppressRightButtonSelection(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(null).Properties.IsRightButtonPressed)
            e.Handled = true;
    }

    /// <summary>
    /// A session with many peers is taller than the viewport. Clicking it focuses and selects its
    /// container, which asks the scroll viewer to bring the whole item into view, and the list
    /// jumps to the item's top. What the pointer clicks is already visible, so a click never scrolls;
    /// keyboard navigation still does.
    /// </summary>
    private void SuppressPointerBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        if (_pointerPressed || _drag.IsDragging)
            e.Handled = true;
    }
}
