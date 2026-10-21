using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Client.Droid.Views;

/// <summary>
/// A menu at the bottom of the screen, where the thumb is: it slides up over a backdrop that covers
/// everything else and slides back down when it is closed. It stays in the tree while closed, so that
/// it can be animated both ways, and takes no pointer input then.
/// </summary>
public class BottomSheet : ContentControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<BottomSheet, bool>(nameof(IsOpen));

    private Border? _backdrop;

    static BottomSheet()
    {
        IsOpenProperty.Changed.AddClassHandler<BottomSheet>((sheet, _) =>
            sheet.PseudoClasses.Set(":open", sheet.IsOpen));
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Raised by a tap on the backdrop. The owner closes the sheet: it may have more to undo than that.
    /// </summary>
    public event Action? Dismissed;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_backdrop != null)
            _backdrop.Tapped -= OnBackdropTapped;

        _backdrop = e.NameScope.Find<Border>("PART_Backdrop");
        if (_backdrop != null)
            _backdrop.Tapped += OnBackdropTapped;
    }

    private void OnBackdropTapped(object? sender, TappedEventArgs e) => Dismissed?.Invoke();
}
