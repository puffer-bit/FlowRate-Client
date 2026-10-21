using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Client.Avalonia.Controls.Indicators;

/// <summary>
/// Round trip to a server as signal bars and milliseconds. Fewer bars and a warmer color mean a
/// slower connection; the levels are the :good, :fair, :poor and :bad pseudo-classes.
/// </summary>
public class PingIndicator : TemplatedControl
{
    private static readonly TimeSpan GoodLimit = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan FairLimit = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan PoorLimit = TimeSpan.FromMilliseconds(400);

    public static readonly StyledProperty<TimeSpan?> RoundTripProperty =
        AvaloniaProperty.Register<PingIndicator, TimeSpan?>(nameof(RoundTrip));

    public static readonly DirectProperty<PingIndicator, string> TextProperty =
        AvaloniaProperty.RegisterDirect<PingIndicator, string>(nameof(Text), indicator => indicator.Text);

    private string _text = "— ms";

    /// <summary>
    /// The measured round trip, or null when there is none yet.
    /// </summary>
    public TimeSpan? RoundTrip
    {
        get => GetValue(RoundTripProperty);
        set => SetValue(RoundTripProperty, value);
    }

    public string Text
    {
        get => _text;
        private set => SetAndRaise(TextProperty, ref _text, value);
    }

    public PingIndicator()
    {
        UpdateLevel(null);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RoundTripProperty)
            UpdateLevel(change.GetNewValue<TimeSpan?>());
    }

    private void UpdateLevel(TimeSpan? roundTrip)
    {
        Text = roundTrip is { } value ? $"{Math.Ceiling(value.TotalMilliseconds):0} ms" : "— ms";

        PseudoClasses.Set(":good", roundTrip < GoodLimit);
        PseudoClasses.Set(":fair", roundTrip >= GoodLimit && roundTrip < FairLimit);
        PseudoClasses.Set(":poor", roundTrip >= FairLimit && roundTrip < PoorLimit);
        PseudoClasses.Set(":bad", roundTrip >= PoorLimit);
    }
}
