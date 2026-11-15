using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Client.Avalonia.Controls.Settings;

namespace Client.Views.Settings;

/// <summary>Stacks wide editors under their labels in narrow Desktop settings pages.</summary>
public sealed class SettingsRowLayout : AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsAdaptiveProperty =
        AvaloniaProperty.RegisterAttached<SettingsRowLayout, SettingsRow, bool>("IsAdaptive");

    static SettingsRowLayout()
    {
        IsAdaptiveProperty.Changed.AddClassHandler<SettingsRow>((row, change) =>
        {
            if (change.GetNewValue<bool>())
                row.PropertyChanged += OnRowChanged;
            else
                row.PropertyChanged -= OnRowChanged;
            Update(row);
        });
    }

    public static bool GetIsAdaptive(SettingsRow row) => row.GetValue(IsAdaptiveProperty);
    public static void SetIsAdaptive(SettingsRow row, bool value) => row.SetValue(IsAdaptiveProperty, value);

    private static void OnRowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is SettingsRow row &&
            (e.Property == Visual.BoundsProperty || e.Property == ContentControl.ContentProperty ||
             e.Property == SettingsRow.ContentPlacementProperty))
            Update(row);
    }

    private static void Update(SettingsRow row) => row.Classes.Set("stackedEditor",
        GetIsAdaptive(row) && row.ContentPlacement == SettingsRowContentPlacement.Right &&
        row.Content is Control and not (ToggleSwitch or ToggleButton) &&
        row.Bounds.Width is > 0 and < 600);
}
