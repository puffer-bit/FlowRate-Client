using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.TextFormatting;
using Client.Avalonia.Localization;

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
            Button button;
            if (index < Children.Count)
                button = (Button)Children[index];
            else
            {
                var icon = new Image { Width = 14, Height = 14 };
                icon.Bind(Image.SourceProperty, new DynamicResourceExtension("CopyIcon"));
                button = new Button
                {
                    Content = icon,
                    Width = ButtonSize,
                    Height = ButtonSize,
                    Padding = new Thickness(4),
                    CornerRadius = new CornerRadius(4),
                    Classes = { "ghost" }
                };
                button.Bind(ToolTip.TipProperty, Strings.Observe("chat.input.copy"));
                button.Bind(AutomationProperties.NameProperty, Strings.Observe("chat.input.copy"));
                int feedbackVersion = 0;
                button.Click += async (_, e) =>
                {
                    e.Handled = true;
                    if (Target?.DataContext is ChatMessageVM message && button.Tag is string code)
                    {
                        int version = ++feedbackVersion;
                        var body = ChatMessageText.GetBody(Target);
                        icon.Bind(Image.SourceProperty, new DynamicResourceExtension("CopyIcon"));
                        await message.CopyMessageInteraction.Handle(code);
                        if (version != feedbackVersion || !Children.Contains(button)
                            || button.Tag is not string currentCode || currentCode != code
                            || Target is not { } currentTarget || !ReferenceEquals(body, ChatMessageText.GetBody(currentTarget)))
                            return;

                        icon.Bind(Image.SourceProperty, new DynamicResourceExtension("MarkIcon"));
                        await Task.Delay(TimeSpan.FromSeconds(1.5));
                        if (version == feedbackVersion)
                            icon.Bind(Image.SourceProperty, new DynamicResourceExtension("CopyIcon"));
                    }
                };
                Children.Add(button);
            }
            if (!Equals(button.Tag, text) && button.Content is Image image)
                image.Bind(Image.SourceProperty, new DynamicResourceExtension("CopyIcon"));
            button.Tag = text;
            SetLeft(button, Math.Max(0, bounds.Right - ButtonSize - ButtonInset));
            SetTop(button, Math.Max(0, bounds.Top + ButtonInset));
            index++;
        }
        while (Children.Count > index)
            Children.RemoveAt(Children.Count - 1);
    }

}
