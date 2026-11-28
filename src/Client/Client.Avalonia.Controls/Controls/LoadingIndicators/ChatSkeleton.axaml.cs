using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Client.Avalonia.Styles.Animation;

namespace Client.Avalonia.Controls.LoadingIndicators;

public partial class ChatSkeleton : UserControl
{
    // In bubbles two of every five rows are the user's own, on the right, as a chat usually goes back and forth
    private static readonly ChatSkeletonRow[] InitialRows = Enumerable.Range(0, 30)
        .Select(index => new ChatSkeletonRow(
            68 + index % 4 * 12,
            new GridLength(2 + index % 3, GridUnitType.Star),
            index % 5 is 1 or 2,
            120 + index * 37 % 140,
            index % 3 == 0 ? 52 : 34))
        .ToArray();

    private static readonly ChatSkeletonRow[] OlderRows = InitialRows.Take(2).ToArray();

    /// <summary>Drawn as bubbles, as the chat is when its messages are; set as the class "bubbles".</summary>
    public static readonly StyledProperty<bool> IsBubblesProperty =
        AvaloniaProperty.Register<ChatSkeleton, bool>(nameof(IsBubbles));

    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<ChatSkeleton, bool>(nameof(IsCompact));

    static ChatSkeleton()
    {
        IsCompactProperty.Changed.AddClassHandler<ChatSkeleton>((control, _) => control.UpdateRows());
        IsBubblesProperty.Changed.AddClassHandler<ChatSkeleton>((control, _) =>
            control.Classes.Set("bubbles", control.IsBubbles));
    }

    public bool IsBubbles
    {
        get => GetValue(IsBubblesProperty);
        set => SetValue(IsBubblesProperty, value);
    }

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public ChatSkeleton()
    {
        InitializeComponent();
        UpdateRows();
    }

    private void UpdateRows()
    {
        if (Rows is { } rows)
            rows.ItemsSource = IsCompact ? OlderRows : InitialRows;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Motion.Changed += OnMotionChanged;
        UpdateMotion();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Motion.Changed -= OnMotionChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnMotionChanged() => Dispatcher.UIThread.Post(UpdateMotion);

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!IsCompact)
            SkeletonScroll.ScrollToEnd();
    }

    private void UpdateMotion() => Pulse.Classes.Set("animated", Motion.IsEnabled);
}

public sealed record ChatSkeletonRow(
    double NameWidth,
    GridLength MessageWidth,
    bool IsMine,
    double BubbleWidth,
    double BubbleHeight);
