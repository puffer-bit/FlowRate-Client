using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Client.Mobile.Views.Common;

/// <summary>What a child of a <see cref="MessageMenu"/> is.</summary>
public enum MessageMenuPart
{
    /// <summary>Covers the screen; a tap on it dismisses the menu.</summary>
    Backdrop,
    /// <summary>The row of reactions, over the message.</summary>
    Reactions,
    /// <summary>The message the menu is about, where it was in the chat.</summary>
    Message,
    /// <summary>What can be done with the message, under it.</summary>
    Actions
}

/// <summary>
/// The menu of a message, as a messenger shows it: the screen dims, the message stays where it was,
/// lifted over the dimmed screen, with the reactions over it and the actions under it. When all of it
/// does not fit, the group moves up or down, and a long message is cut short before the actions are.
/// </summary>
/// <remarks>
/// The children say what they are with <see cref="PartProperty"/>. The menu stays in the tree while
/// closed, so that it can be animated both ways, and takes no pointer input then.
/// </remarks>
public class MessageMenu : Panel
{
    private const double ScreenMargin = 8;
    private const double Gap = 8;
    // Never cut a message shorter than this
    private const double MinMessageHeight = 72;
    private const double ActionsWidth = 260;

    public static readonly AttachedProperty<MessageMenuPart> PartProperty =
        AvaloniaProperty.RegisterAttached<MessageMenu, Control, MessageMenuPart>("Part");

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<MessageMenu, bool>(nameof(IsOpen));

    /// <summary>Where the message is in the chat, in the coordinates of the menu.</summary>
    public static readonly StyledProperty<Rect> MessageBoundsProperty =
        AvaloniaProperty.Register<MessageMenu, Rect>(nameof(MessageBounds));

    static MessageMenu()
    {
        AffectsMeasure<MessageMenu>(MessageBoundsProperty);
        IsOpenProperty.Changed.AddClassHandler<MessageMenu>((menu, _) =>
        {
            menu.PseudoClasses.Set(":open", menu.IsOpen);
            MenuPresence.Follow(menu, menu.IsOpen, ref menu._hiding);
        });
    }

    private IDisposable? _hiding;

    public MessageMenu()
    {
        // Out of the layout until it is first opened
        IsVisible = false;
        AddHandler(TappedEvent, OnTapped);
    }

    public static MessageMenuPart GetPart(Control control) => control.GetValue(PartProperty);
    public static void SetPart(Control control, MessageMenuPart value) => control.SetValue(PartProperty, value);

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public Rect MessageBounds
    {
        get => GetValue(MessageBoundsProperty);
        set => SetValue(MessageBoundsProperty, value);
    }

    /// <summary>
    /// Raised by a tap beside the reactions and the actions. The owner closes the menu.
    /// </summary>
    public event Action? Dismissed;

    private Control? Find(MessageMenuPart part) =>
        Children.FirstOrDefault(child => child.IsVisible && GetPart(child) == part);

    // A tap on the backdrop or on the message, which only shows what the menu is about
    private void OnTapped(object? sender, TappedEventArgs e)
    {
        var part = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
            .FirstOrDefault(control => ReferenceEquals(control.GetVisualParent(), this));
        if (part == null || GetPart(part) is MessageMenuPart.Backdrop or MessageMenuPart.Message)
        {
            e.Handled = true;
            Dismissed?.Invoke();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MessageBounds.Width : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        var room = new Size(Math.Max(0, width - 2 * ScreenMargin), Math.Max(0, height - 2 * ScreenMargin));

        Find(MessageMenuPart.Backdrop)?.Measure(new Size(width, height));
        var reactions = Find(MessageMenuPart.Reactions);
        reactions?.Measure(room);
        var actions = Find(MessageMenuPart.Actions);
        actions?.Measure(new Size(Math.Min(ActionsWidth, room.Width), double.PositiveInfinity));
        var message = Find(MessageMenuPart.Message);
        message?.Measure(new Size(room.Width, double.PositiveInfinity));

        // A long message gives way first, then the actions scroll
        var reactionsHeight = DesiredHeight(reactions);
        var free = room.Height - reactionsHeight - 2 * Gap;
        var messageHeight = Math.Min(DesiredHeight(message), Math.Max(MinMessageHeight, free - DesiredHeight(actions)));
        var actionsHeight = Math.Max(0, Math.Min(DesiredHeight(actions), free - messageHeight));
        message?.Measure(new Size(room.Width, messageHeight));
        actions?.Measure(new Size(Math.Min(ActionsWidth, room.Width), actionsHeight));

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var backdrop = Find(MessageMenuPart.Backdrop);
        backdrop?.Arrange(new Rect(finalSize));

        var reactions = Find(MessageMenuPart.Reactions);
        var message = Find(MessageMenuPart.Message);
        var actions = Find(MessageMenuPart.Actions);

        var reactionsHeight = DesiredHeight(reactions);
        var messageHeight = DesiredHeight(message);
        var actionsHeight = DesiredHeight(actions);
        var total = (reactions != null ? reactionsHeight + Gap : 0) + messageHeight
                    + (actions != null ? Gap + actionsHeight : 0);

        // The message stays where it was when everything fits around it
        var top = MessageBounds.Y - (reactions != null ? reactionsHeight + Gap : 0);
        top = Math.Clamp(top, ScreenMargin, Math.Max(ScreenMargin, finalSize.Height - ScreenMargin - total));

        var left = ScreenMargin;
        var y = top;
        if (reactions != null)
        {
            reactions.RenderTransformOrigin = new RelativePoint(0, 1, RelativeUnit.Relative);
            reactions.Arrange(Valid(left, y, Math.Min(reactions.DesiredSize.Width, finalSize.Width - 2 * ScreenMargin), reactionsHeight));
            y += reactionsHeight + Gap;
        }

        if (message != null)
        {
            message.RenderTransformOrigin = RelativePoint.Center;
            message.Arrange(Valid(left, y, Math.Max(0, finalSize.Width - 2 * ScreenMargin), messageHeight));
            y += messageHeight + Gap;
        }

        if (actions != null)
        {
            actions.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
            actions.Arrange(Valid(left, y, actions.DesiredSize.Width, actionsHeight));
        }

        return finalSize;
    }

    private static double DesiredHeight(Control? control) => control?.DesiredSize.Height ?? 0;

    // While the menu has no room yet, such as before the chat is on the screen, a size may come out
    // negative; a control cannot be arranged in that
    private static Rect Valid(double x, double y, double width, double height) => new(
        double.IsFinite(x) ? x : 0, double.IsFinite(y) ? y : 0,
        double.IsFinite(width) ? Math.Max(0, width) : 0, double.IsFinite(height) ? Math.Max(0, height) : 0);
}
