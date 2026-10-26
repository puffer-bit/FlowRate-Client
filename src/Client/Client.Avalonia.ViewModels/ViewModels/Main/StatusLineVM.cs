using System;
using System.Threading.Tasks;
using Client.Avalonia.Localization;
using Client.Avalonia.Styles.Animation;
using Client.Services;
using ReactiveUI;

namespace Client.ViewModels.Main;

/// <summary>
/// The line of the status bar telling what the application does: a new text fades the old one out.
/// </summary>
public sealed class StatusLineVM : ReactiveObject
{
    public string Text
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // Builds the text in the current language, to build it again when the language changes
    private Func<string> _text = () => Strings.Get("main.status.notConnected");

    /// <summary>
    /// The action goes on: the spinner is shown instead of the icon.
    /// </summary>
    public bool IsActive
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsTextVisible
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // The same duration as the opacity transition of the status text in the markup
    private static TimeSpan FadeDuration => Motion.Get(MotionDuration.Short);

    // The text the running fade swaps in; a newer change only replaces it instead of fading again
    private (Func<string> Text, bool IsActive)? _pending;

    public StatusLineVM()
    {
        Text = _text();

        // The line lives as long as the main window, so the subscription is never dropped
        Strings.LanguageChanged += () =>
        {
            if (_pending == null)
                Text = _text();
        };
    }

    /// <summary>
    /// Shows a new text; may be called from any thread.
    /// </summary>
    /// <param name="text">Builds the text, again on every change of the language.</param>
    public Task SetAsync(Func<string> text, bool isActive) => UiThread.RunAsync(() => SetOnUiThreadAsync(text, isActive));

    /// <summary>
    /// Shows a new text without waiting for the fade.
    /// </summary>
    public void Set(Func<string> text, bool isActive) => _ = SetAsync(text, isActive);

    private async Task SetOnUiThreadAsync(Func<string> text, bool isActive)
    {
        if (_pending != null)
        {
            _pending = (text, isActive);
            return;
        }

        // The same text again would only blink
        if (text() == Text)
        {
            _text = text;
            IsActive = isActive;
            return;
        }

        _pending = (text, isActive);
        IsTextVisible = false;
        await Task.Delay(FadeDuration);

        (_text, IsActive) = _pending.Value;
        Text = _text();
        _pending = null;
        IsTextVisible = true;
    }
}
