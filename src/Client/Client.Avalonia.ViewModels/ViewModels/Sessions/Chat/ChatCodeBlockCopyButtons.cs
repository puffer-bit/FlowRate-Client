using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.TextFormatting;
using Client.Avalonia.Controls.Buttons;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>Places a copy button over the upper right corner of each fenced code panel.</summary>
public sealed class ChatCodeBlockCopyButtons : Canvas
{
    private const double ButtonSize = 24;
    private const double ButtonInset = 4;
    private TextLayout? _lastLayout;
    private Size _lastSize;

    public static readonly StyledProperty<TextBlock?> TargetProperty =
        AvaloniaProperty.Register<ChatCodeBlockCopyButtons, TextBlock?>(nameof(Target));

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
                oldTarget.LayoutUpdated -= OnTargetLayoutUpdated;
            if (change.NewValue is TextBlock newTarget)
                newTarget.LayoutUpdated += OnTargetLayoutUpdated;
            _lastLayout = null;
            Children.Clear();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Target is { } target)
        {
            target.LayoutUpdated -= OnTargetLayoutUpdated;
            target.LayoutUpdated += OnTargetLayoutUpdated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (Target is { } target)
            target.LayoutUpdated -= OnTargetLayoutUpdated;
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
        UpdateCopyButtons(target);
    }

    private void UpdateCopyButtons(TextBlock target)
    {
        var index = 0;
        foreach (var (text, bounds) in ChatMessageText.GetCodeBlocks(target))
        {
            CopyButton button;
            if (index < Children.Count)
                button = (CopyButton)Children[index];
            else
            {
                button = new CopyButton();
                Children.Add(button);
            }
            button.Text = text;
            double size = Math.Min(ButtonSize, Math.Max(12, bounds.Height - 2 * ButtonInset));
            button.Width = button.Height = size;
            button.IconSize = Math.Min(14, size - 4);
            SetLeft(button, Math.Max(0, bounds.Right - size - ButtonInset));
            SetTop(button, Math.Max(0, bounds.Top + ButtonInset));
            index++;
        }
        while (Children.Count > index)
            Children.RemoveAt(Children.Count - 1);
    }

}
