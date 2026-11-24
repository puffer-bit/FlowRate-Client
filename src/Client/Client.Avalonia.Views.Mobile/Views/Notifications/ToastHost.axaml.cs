using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Client.Mobile.Views.Common;
using Client.ViewModels.Notifications;

namespace Client.Mobile.Views.Notifications;

public partial class ToastHost : UserControl
{
    // A card swiped this part of its width away goes, whatever the speed of the finger
    private const double DismissShare = 0.35;
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(180);

    public ToastHost()
    {
        InitializeComponent();
    }

    // Each card follows the finger to either side; the gesture lives as long as the card
    private void OnToastAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Border card || card.Tag is DragGesture)
            return;

        // The rise of the card may have put a shift there already, and moves the same one
        if (card.RenderTransform is not TranslateTransform shift)
        {
            shift = new TranslateTransform();
            card.RenderTransform = shift;
        }

        card.Tag = new DragGesture(card, Orientation.Horizontal)
        {
            CanStart = (_, _) => card.DataContext is ToastVM { IsLeaving: false },
            Moved = offset =>
            {
                // The notification is being read, and its time waits until the finger is lifted
                if (card.DataContext is ToastVM toast)
                    toast.IsPointerOver = true;

                shift.X = offset;
                card.Opacity = 1 - Math.Min(1, Math.Abs(offset) / Math.Max(1, card.Bounds.Width));
            },
            Ended = (offset, velocity) => _ = SettleAsync(card, shift, offset, velocity),
        };
    }

    private static async Task SettleAsync(Border card, TranslateTransform shift,
        double offset, double velocity)
    {
        var toast = card.DataContext as ToastVM;
        var width = Math.Max(1, card.Bounds.Width);
        var flung = Math.Abs(velocity) >= DragGesture.FlingVelocity && Math.Sign(velocity) == Math.Sign(offset);
        var goes = offset != 0 && (flung || Math.Abs(offset) >= width * DismissShare);

        var from = shift.X;
        var to = goes ? Math.Sign(offset) * width : 0;
        var fromOpacity = card.Opacity;
        var toOpacity = goes ? 0 : 1;

        await DragGesture.Animate(card, SettleTime, progress =>
        {
            shift.X = from + (to - from) * progress;
            card.Opacity = fromOpacity + (toOpacity - fromOpacity) * progress;
        });

        if (toast == null)
            return;

        if (goes)
            await toast.CloseAsync();
        else
            toast.IsPointerOver = false;
    }

    // A tap puts a card away, unless it waits for one of its actions
    private void OnToastTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ToastVM { HasActions: false } toast)
            return;

        e.Handled = true;
        _ = toast.CloseAsync();
    }
}
