using Avalonia;
using Avalonia.Data;

namespace Client.Avalonia.Localization;

/// <summary>
/// Markup extension: <c>Text="{l:Text settings.audio.section.microphone}"</c> binds the property to
/// the text of the key, so it follows the language while the view is open.
/// </summary>
public class TextExtension
{
    public TextExtension()
    {
    }

    public TextExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    public BindingBase ProvideValue(IServiceProvider serviceProvider) => Strings.Observe(Key).ToBinding();
}
