using Avalonia.Threading;
using Client.Avalonia.Localization;
using Client.Tools.Environment;

namespace Client.Services;

public class LanguageService : ILanguageService
{
    public void Apply(string? code)
    {
        code ??= SystemLanguage.GetCode();

        // The texts are bound to controls, which are updated on the UI thread only
        if (Dispatcher.UIThread.CheckAccess())
            Strings.SetLanguage(code);
        else
            Dispatcher.UIThread.Post(() => Strings.SetLanguage(code));
    }
}
