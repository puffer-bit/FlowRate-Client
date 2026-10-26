using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Client.Avalonia.Controls.Indicators;

/// <summary>
/// The quality of a media connection as the arcs of an antenna. Fewer lit arcs and a warmer color
/// mean a worse connection; the levels are the :good, :fair and :poor pseudo-classes, and with
/// none of them every arc stays unlit.
/// </summary>
public class SignalIndicator : TemplatedControl
{
    /// <summary>
    /// 3 for a good connection, 2 for a fair one, 1 for a poor one, 0 while it is unknown.
    /// </summary>
    public static readonly StyledProperty<int> LevelProperty =
        AvaloniaProperty.Register<SignalIndicator, int>(nameof(Level));

    public int Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public SignalIndicator()
    {
        UpdateLevel(0);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LevelProperty)
            UpdateLevel(change.GetNewValue<int>());
    }

    private void UpdateLevel(int level)
    {
        PseudoClasses.Set(":good", level >= 3);
        PseudoClasses.Set(":fair", level == 2);
        PseudoClasses.Set(":poor", level == 1);
    }
}
