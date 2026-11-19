using Client.Avalonia.Icons;
using Client.Avalonia.Localization;

namespace Client.Services;

/// <summary>Keeps the application's inherited text font in sync with its selected language.</summary>
public static class ApplicationFontResources
{
    public static void Initialize(global::Avalonia.Application application)
    {
        void Apply() => application.Resources["ContentControlThemeFontFamily"] = AppFonts.ForLanguage(Strings.Current.Code);

        Apply();
        // The subscription and the resource dictionary both live for the lifetime of the application.
        Strings.LanguageChanged += Apply;
    }
}
