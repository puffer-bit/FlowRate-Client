using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Client.Avalonia.Styles.Animation;

namespace Client.Avalonia.Controls.LoadingIndicators;

public partial class ChatSkeleton : UserControl
{
    private static readonly ChatSkeletonRow[] InitialRows = Enumerable.Range(0, 30)
        .Select(index => new ChatSkeletonRow(
            68 + index % 4 * 12,
            new GridLength(2 + index % 3, GridUnitType.Star)))
        .ToArray();

    private static readonly ChatSkeletonRow[] OlderRows = InitialRows.Take(2).ToArray();

    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<ChatSkeleton, bool>(nameof(IsCompact));

    static ChatSkeleton()
    {
        IsCompactProperty.Changed.AddClassHandler<ChatSkeleton>((control, _) => control.UpdateRows());
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
    GridLength MessageWidth);
