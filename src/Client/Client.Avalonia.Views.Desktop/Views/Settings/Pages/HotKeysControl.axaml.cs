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

    // A modifier pressed last while recording, with the modifiers held before it: assigned alone
    // when released before any other key
    private (PhysicalKey Key, KeyModifiers Modifiers)? _pendingModifier;

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
        _topLevel?.AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        if (_topLevel is WindowBase window)
            window.Deactivated += OnDeactivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _topLevel?.RemoveHandler(KeyDownEvent, OnKeyDown);
        _topLevel?.RemoveHandler(KeyUpEvent, OnKeyUp);
        if (_topLevel is WindowBase window)
            window.Deactivated -= OnDeactivated;
        _topLevel = null;

        _pendingModifier = null;
        Recording?.CancelRecording();
        base.OnDetachedFromVisualTree(e);
    }

    private HotKeyItemVM? Recording => ViewModel?.Items.FirstOrDefault(item => item.IsRecording);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Recording is not { } item)
        {
            _pendingModifier = null;
            return;
        }

        e.Handled = true;

        if (e.PhysicalKey == PhysicalKey.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            _pendingModifier = null;
            item.CancelRecording();
            return;
        }

        if (e.PhysicalKey == PhysicalKey.None)
            return;

        // A modifier may come with a key, which is waited for, or be the hot key itself
        if (HotKeyGesture.IsModifier(e.PhysicalKey))
        {
            if (_pendingModifier?.Key != e.PhysicalKey)
                _pendingModifier = (e.PhysicalKey, e.KeyModifiers);
            return;
        }

        _pendingModifier = null;
        item.Assign(HotKeyGesture.Of(e.KeyModifiers, e.PhysicalKey));
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (Recording is not { } item)
        {
            _pendingModifier = null;
            return;
        }

        e.Handled = true;

        if (_pendingModifier is not { } pending || pending.Key != e.PhysicalKey)
            return;

        _pendingModifier = null;
        item.Assign(HotKeyGesture.Of(pending.Modifiers, pending.Key));
    }

    // The keys pressed in another window are not meant for this one
    private void OnDeactivated(object? sender, System.EventArgs e)
    {
        _pendingModifier = null;
        Recording?.CancelRecording();
    }
}
