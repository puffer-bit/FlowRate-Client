using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>Paints code panels behind a sibling text control without interrupting selection.</summary>
public sealed class ChatCodeBlockBackground : Control
{
    private TextLayout? _lastLayout;
    private Size _lastSize;

    public static readonly StyledProperty<TextBlock?> TargetProperty =
        AvaloniaProperty.Register<ChatCodeBlockBackground, TextBlock?>(nameof(Target));

    public TextBlock? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TargetProperty)
        {
            if (change.OldValue is TextBlock oldTarget)
            {
                oldTarget.LayoutUpdated -= OnTargetLayoutUpdated;
                oldTarget.PropertyChanged -= OnTargetPropertyChanged;
            }
            if (change.NewValue is TextBlock newTarget)
            {
                newTarget.LayoutUpdated += OnTargetLayoutUpdated;
                newTarget.PropertyChanged += OnTargetPropertyChanged;
            }
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Target is { } target)
        {
            target.LayoutUpdated -= OnTargetLayoutUpdated;
            target.LayoutUpdated += OnTargetLayoutUpdated;
            target.PropertyChanged -= OnTargetPropertyChanged;
            target.PropertyChanged += OnTargetPropertyChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (Target is { } target)
        {
            target.LayoutUpdated -= OnTargetLayoutUpdated;
            target.PropertyChanged -= OnTargetPropertyChanged;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTargetLayoutUpdated(object? sender, EventArgs e)
    {
        if (Target is not { IsMeasureValid: true } target)
            return;
        var layout = target.TextLayout;
        if (ReferenceEquals(layout, _lastLayout) && target.Bounds.Size == _lastSize)
            return;
        _lastLayout = layout;
        _lastSize = target.Bounds.Size;
        InvalidateVisual();
    }

    private void OnTargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Target is { } target)
            ChatMessageText.RenderCodeBlocks(target, context);
    }
}
