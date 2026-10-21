using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Client.Avalonia.Controls;

/// <summary>
/// The title of a section of a settings page. A header with a <see cref="SectionKey"/> folds its
/// section away when it is clicked: the page it stands on hides what follows it up to the next header.
/// </summary>
public class SettingsHeader : TemplatedControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SettingsHeader, string>(nameof(Text));

    public static readonly StyledProperty<string?> SectionKeyProperty =
        AvaloniaProperty.Register<SettingsHeader, string?>(nameof(SectionKey));

    public static readonly StyledProperty<bool> IsCollapsedProperty =
        AvaloniaProperty.Register<SettingsHeader, bool>(nameof(IsCollapsed));

    static SettingsHeader()
    {
        SectionKeyProperty.Changed.AddClassHandler<SettingsHeader>((header, change) =>
            header.PseudoClasses.Set(":collapsible", change.NewValue is string));
        IsCollapsedProperty.Changed.AddClassHandler<SettingsHeader>((header, change) =>
            header.PseudoClasses.Set(":collapsed", change.GetNewValue<bool>()));
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// The name the collapsed state of the section is remembered under, "tab.section".
    /// A header without one cannot be collapsed.
    /// </summary>
    public string? SectionKey
    {
        get => GetValue(SectionKeyProperty);
        set => SetValue(SectionKeyProperty, value);
    }

    public bool IsCollapsed
    {
        get => GetValue(IsCollapsedProperty);
        set => SetValue(IsCollapsedProperty, value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (SectionKey == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        SetCurrentValue(IsCollapsedProperty, !IsCollapsed);
        e.Handled = true;
    }
}
