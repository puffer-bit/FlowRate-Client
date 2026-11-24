using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using ReactiveUI;
using Client.ViewModels.Dialogs;

namespace Client.Views.Shared.Dialogs;

/// <summary>
/// The single place that decides how a message box requested by a view model is shown. Views only
/// register it; the platform decides where the box goes, see <see cref="IDialogPresenter"/>.
/// </summary>
public static class MessageBoxDialog
{
    /// <summary>
    /// Shows the message boxes of <paramref name="interaction"/> for <paramref name="owner"/>.
    /// </summary>
    /// <returns>The registration, to be disposed together with the view activation.</returns>
    public static IDisposable RegisterMessageBoxHandler(this Interaction<MessageBoxVM, Result> interaction,
        Visual owner)
    {
        return interaction.RegisterHandler(async context =>
            context.SetOutput(await ShowAsync(context.Input, owner)));
    }

    /// <param name="dismissedResult">The answer when the user dismisses the box instead of choosing a button.</param>
    public static async Task<Result> ShowAsync(MessageBoxVM messageBox, Visual? owner,
        Result dismissedResult = Result.Cancel)
    {
        // A box with something to copy, such as a new recovery phrase, is read and confirmed, not cancelled
        if (messageBox.CanCopyDetail)
            foreach (var cancel in messageBox.ButtonsList.Where(button => button.IsCancel).ToArray())
                messageBox.ButtonsList.Remove(cancel);

        // Nothing to show it in, e.g. the application is shutting down
        if (DialogPresenter.Current is not { } presenter || !presenter.CanShow(owner))
            return Result.Cancel;

        var view = new MessageBoxView { DataContext = messageBox };

        using var buttons = messageBox.Answers.Subscribe(result => presenter.Close(view, result));

        return await presenter.ShowAsync(view, owner, dismissedResult);
    }
}
