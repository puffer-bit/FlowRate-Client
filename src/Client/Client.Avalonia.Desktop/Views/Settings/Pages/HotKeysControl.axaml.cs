using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Client.Services.HotKeys;
using Client.ViewModels.Settings.Pages;
using Client.Views.Common;

namespace Client.Views.Settings.Pages;

public partial class HotKeysControl : ViewControl<HotKeysControlVM>
{
    private TopLevel? _topLevel;

    public HotKeysControl()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Tunnelling, so the keys being assigned reach no button and do not close the window
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        if (_topLevel is WindowBase window)
            window.Deactivated += OnDeactivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _topLevel?.RemoveHandler(KeyDownEvent, OnKeyDown);
        if (_topLevel is WindowBase window)
            window.Deactivated -= OnDeactivated;
        _topLevel = null;

        Recording?.CancelRecording();
        base.OnDetachedFromVisualTree(e);
    }

    private HotKeyItemVM? Recording => ViewModel?.Items.FirstOrDefault(item => item.IsRecording);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Recording is not { } item)
            return;

        e.Handled = true;

        if (e.PhysicalKey == PhysicalKey.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            item.CancelRecording();
            return;
        }

        // A modifier alone is not a hot key: the key that comes with it is waited for
        if (e.PhysicalKey == PhysicalKey.None || HotKeyGesture.IsModifier(e.PhysicalKey))
            return;

        var modifiers = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta);
        item.Assign(new HotKeyGesture(modifiers, e.PhysicalKey));
    }

    // The keys pressed in another window are not meant for this one
    private void OnDeactivated(object? sender, System.EventArgs e) => Recording?.CancelRecording();
}
