using Avalonia;
using Avalonia.Controls.Primitives;

namespace Client.Avalonia.Controls.Banners;

/// <summary>
/// A blue plate with an "i" in front of a header and a message: something worth knowing, not a warning.
/// </summary>
public class InfoBanner : TemplatedControl
{
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<InfoBanner, string>(nameof(Header));

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<InfoBanner, string>(nameof(Message));

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}
