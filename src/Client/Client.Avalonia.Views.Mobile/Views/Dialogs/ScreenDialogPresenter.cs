using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Client.Views.Shared.Dialogs;

namespace Client.Mobile.Views.Dialogs;

/// <summary>
/// Shows the shared dialogs over the whole screen, on the dialog layer of the root view. The system back
/// button dismisses the topmost one, see <see cref="TryDismissTopmost"/>.
/// </summary>
public sealed class ScreenDialogPresenter : IDialogPresenter
{
    private readonly Panel _layer;

    // How each dialog on screen is dismissed, the topmost one last
    private readonly List<Action> _dismissals = [];

    public ScreenDialogPresenter(Panel layer)
    {
        _layer = layer;
    }

    /// <summary>The root view is the whole screen, so there is always somewhere to show a dialog.</summary>
    public bool CanShow(Visual? owner) => true;

    public async Task<TResult> ShowAsync<TResult>(Control content, Visual? owner, TResult dismissedResult,
        bool fillWindow = false)
    {
        var frame = new DialogFrame(content, fillWindow);
        var result = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        frame.Closed += value => result.TrySetResult(value is TResult typed ? typed : dismissedResult);
        Action dismiss = () => result.TrySetResult(dismissedResult);

        _dismissals.Add(dismiss);
        _layer.Children.Add(frame);

        try
        {
            return await result.Task;
        }
        finally
        {
            _layer.Children.Remove(frame);
            _dismissals.Remove(dismiss);
        }
    }

    public void Close(Visual content, object? result = null)
    {
        content.FindAncestorOfType<DialogFrame>()?.Close(result);
    }

    /// <summary>Dismisses the topmost dialog, for the system back button.</summary>
    /// <returns>Whether there was a dialog to dismiss.</returns>
    public bool TryDismissTopmost()
    {
        if (_dismissals.Count == 0)
            return false;

        _dismissals[^1]();
        return true;
    }
}
