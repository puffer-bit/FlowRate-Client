using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Client.Avalonia.Localization;
using Shared.Enums.User;

namespace Client.Avalonia.Controls.Indicators;

/// <summary>
/// A small robot next to the name of a bot: in the color of the text for a bot, yellow for a debug bot, with what it
/// is in the tooltip. Hidden for people, so it can sit next to every name.
/// </summary>
public class UserKindBadge : Image
{
    public static readonly StyledProperty<UserKind> KindProperty =
        AvaloniaProperty.Register<UserKindBadge, UserKind>(nameof(Kind));

    /// <summary>The side of the icon; about the height of the name next to it.</summary>
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<UserKindBadge, double>(nameof(IconSize), 18);

    private IDisposable? _iconBinding;
    private IDisposable? _tipBinding;

    public UserKindBadge()
    {
        VerticalAlignment = VerticalAlignment.Center;
        ApplySize();
        Apply();
    }

    public UserKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty)
            Apply();
        else if (change.Property == IconSizeProperty)
            ApplySize();
    }

    private void ApplySize()
    {
        Width = IconSize;
        Height = IconSize;
    }

    private void Apply()
    {
        IsVisible = Kind.IsBot();
        var isDebug = Kind == UserKind.DebugBot;
        _iconBinding?.Dispose();
        _iconBinding = Bind(SourceProperty, this.GetResourceObservable(isDebug ? "DebugBotIcon" : "BotIcon"));
        _tipBinding?.Dispose();
        _tipBinding = Bind(ToolTip.TipProperty, Strings.Observe(isDebug ? "user.kind.debugBot" : "user.kind.bot"));
    }
}
