using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Client.Views.Shared.Dialogs;

namespace Client.Views.Dialogs;

/// <summary>Shows the shared dialogs in the window of the requesting view, see <see cref="InWindowDialog"/>.</summary>
public sealed class WindowDialogPresenter : IDialogPresenter
{
    public bool CanShow(Visual? owner) => InWindowDialog.CanShow(owner);

    public Task<TResult> ShowAsync<TResult>(Control content, Visual? owner, TResult dismissedResult,
        bool fillWindow = false) =>
        InWindowDialog.ShowAsync(content, owner, dismissedResult, fillWindow);

    public void Close(Visual content, object? result = null) => InWindowDialog.Close(content, result);
}
