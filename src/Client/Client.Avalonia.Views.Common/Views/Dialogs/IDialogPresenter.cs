using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;

namespace Client.Views.Shared.Dialogs;

/// <summary>
/// How a platform shows a dialog: in the window of the requesting view on a desktop, over the whole
/// screen on a phone. Views ask through <see cref="DialogPresenter.Current"/> and stay the same everywhere.
/// </summary>
public interface IDialogPresenter
{
    /// <summary>Whether there is somewhere to show a dialog for <paramref name="owner"/>.</summary>
    bool CanShow(Visual? owner);

    /// <summary>
    /// Shows <paramref name="content"/> until it calls <see cref="Close"/> or the user dismisses it.
    /// </summary>
    /// <param name="dismissedResult">
    /// The answer when the user dismisses the dialog: Escape on a desktop, the back button on a phone.
    /// </param>
    /// <param name="fillWindow">
    /// Whether a large dialog, such as the settings, grows with the window up to the maximum size of its content.
    /// </param>
    /// <returns>The result passed to <see cref="Close"/>, or <paramref name="dismissedResult"/>.</returns>
    Task<TResult> ShowAsync<TResult>(Control content, Visual? owner, TResult dismissedResult, bool fillWindow = false);

    /// <summary>Closes the dialog that contains <paramref name="content"/> with <paramref name="result"/>.</summary>
    void Close(Visual content, object? result = null);
}

/// <summary>The presenter of the platform, set by the host before the first view is shown.</summary>
public static class DialogPresenter
{
    public static IDialogPresenter? Current { get; set; }
}
