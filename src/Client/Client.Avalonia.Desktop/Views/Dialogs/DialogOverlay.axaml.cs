using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Client.Avalonia.Styles.Animation;

namespace Client.Views.Dialogs;

/// <summary>
/// Hosts a dialog inside a window instead of in a window of its own. Created and removed by
/// <see cref="InWindowDialog"/>.
/// </summary>
internal partial class DialogOverlay : UserControl
{
    /// <summary>
    /// Raised when the dialog content closes it with a result, see <see cref="InWindowDialog.Close"/>.
    /// </summary>
    public event Action<object?>? Closed;
    
    /// <summary>
    /// Raised when the user dismisses the dialog with Escape.
    /// </summary>
    public event Action? Dismissed;
    
    public DialogOverlay()
    {
        InitializeComponent();
    }
    
    /// <param name="content">The dialog content.</param>
    /// <param name="fillWindow">
    /// Whether the dialog grows with the window up to the MaxWidth and MaxHeight of the content,
    /// for large dialogs such as the settings. Otherwise it keeps the size of its content.
    /// </param>
    public DialogOverlay(Control content, bool fillWindow = false) : this()
    {
        DialogContent.Content = content;

        if (!fillWindow)
            return;

        // A stretched element limited by its maximum size is centered, which keeps the dialog in
        // the middle of large windows
        var border = Outline.BorderThickness;
        Card.HorizontalAlignment = HorizontalAlignment.Stretch;
        Card.VerticalAlignment = VerticalAlignment.Stretch;
        Card.MaxWidth = content.MaxWidth + border.Left + border.Right;
        Card.MaxHeight = content.MaxHeight + border.Top + border.Bottom;
    }

    public void Close(object? result) => Closed?.Invoke(result);

    /// <summary>
    /// Plays the hide animation. The caller removes the overlay once it has finished.
    /// </summary>
    public async Task HideAsync()
    {
        Presence.SetIsLeaving(this, true);
        Presence.SetIsLeaving(Card, true);

        await Task.Delay(Presence.GetLeaveTime(this));
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Keyboard input must land in the dialog, not in the window content underneath. Items of
        // an ItemsControl only exist after layout, hence the deferred call.
        Dispatcher.UIThread.Post(FocusFirstInput, DispatcherPriority.Loaded);
    }

    private void FocusFirstInput()
    {
        var inputs = this.GetVisualDescendants()
            .OfType<InputElement>()
            .Where(i => i.Focusable && i.IsEffectivelyVisible && i.IsEffectivelyEnabled)
            .ToList();
        
        // A text box first, so forms can be typed into right away: Enter in a single-line box still
        // reaches the default button. Otherwise the default button itself, so Enter confirms.
        // Check boxes and other toggles derive from Button but would swallow Enter as a toggle.
        var buttons = inputs.OfType<Button>().Where(b => b is not ToggleButton).ToList();
        var target = inputs.OfType<TextBox>().FirstOrDefault()
                     ?? (InputElement?)buttons.FirstOrDefault(b => b.IsDefault)
                     ?? buttons.FirstOrDefault();
        target?.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key != Key.Escape) 
            return;
        
        Dismissed?.Invoke();
        e.Handled = true;
    }
}
