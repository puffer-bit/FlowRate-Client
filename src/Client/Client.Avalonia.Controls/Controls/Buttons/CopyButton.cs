using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Client.Avalonia.Localization;

namespace Client.Avalonia.Controls.Buttons;

/// <summary>
/// A small button that puts <see cref="Text"/> on the clipboard of its window and shows a check mark for a moment
/// after it, then the copy icon again. A new text brings the copy icon back at once.
/// </summary>
public class CopyButton : Button
{
    private static readonly TimeSpan FeedbackDuration = TimeSpan.FromSeconds(1.5);

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<CopyButton, string?>(nameof(Text));

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<CopyButton, double>(nameof(IconSize), 14);

    private readonly Image _icon = new();

    // Counts the clicks, so the check mark of an earlier one does not take away the one of a later one
    private int _feedbackVersion;

    protected override Type StyleKeyOverride => typeof(Button);

    public CopyButton()
    {
        Padding = new Thickness(0);
        MinWidth = 0;
        MinHeight = 0;
        Width = Height = 24;
        CornerRadius = new CornerRadius(4);
        Classes.Add("ghost");
        _icon.Width = _icon.Height = IconSize;
        Content = _icon;
        ShowCopyIcon();
        // Handled here, so the message or the card under the button does not take the click as its own
        Click += (_, e) =>
        {
            e.Handled = true;
            _ = CopyAsync();
        };
        this.Bind(ToolTip.TipProperty, Strings.Observe("common.copy"));
        this.Bind(AutomationProperties.NameProperty, Strings.Observe("common.copy"));
    }

    /// <summary>What a click copies; the button does nothing without it.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            // The check mark was for the text before
            _feedbackVersion++;
            ShowCopyIcon();
        }
        else if (change.Property == IconSizeProperty)
            _icon.Width = _icon.Height = IconSize;
    }

    private async Task CopyAsync()
    {
        if (Text is not { } text || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        int version = ++_feedbackVersion;
        await clipboard.SetTextAsync(text);
        if (version != _feedbackVersion)
            return;

        _icon.Bind(Image.SourceProperty, new DynamicResourceExtension("MarkIcon"));
        await Task.Delay(FeedbackDuration);
        if (version == _feedbackVersion)
            ShowCopyIcon();
    }

    private void ShowCopyIcon() => _icon.Bind(Image.SourceProperty, new DynamicResourceExtension("CopyIcon"));
}
