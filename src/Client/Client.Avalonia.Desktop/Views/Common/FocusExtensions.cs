using Avalonia.Controls;
using Avalonia.Threading;

namespace Client.Views.Common;

internal static class FocusExtensions
{
    /// <summary>
    /// Moves the keyboard focus to <paramref name="input"/> after the layout pass that is pending: an input
    /// that has only just been created or shown cannot take the focus before it.
    /// </summary>
    /// <param name="selectAll">Whether the text is selected too, so that typing replaces it.</param>
    public static void FocusWhenLoaded(this TextBox input, bool selectAll = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            input.Focus();
            if (selectAll)
                input.SelectAll();
        }, DispatcherPriority.Loaded);
    }
}
