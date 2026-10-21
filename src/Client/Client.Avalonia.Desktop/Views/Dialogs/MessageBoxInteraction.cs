using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using ReactiveUI;
using Client.ViewModels.Dialogs;

namespace Client.Views.Dialogs;

/// <summary>
/// The single place that decides how a message box requested by a view model is shown. Views
/// only register it, so changing the presentation happens here.
/// </summary>
internal static class MessageBoxInteraction
{
    /// <summary>
    /// Shows the message boxes of <paramref name="interaction"/> in the window that hosts
    /// <paramref name="owner"/>.
    /// </summary>
    /// <returns>The registration, to be disposed together with the view activation.</returns>
    public static IDisposable RegisterMessageBoxHandler(this Interaction<MessageBoxVM, Result> interaction,
        Visual owner)
    {
        return interaction.RegisterHandler(async context =>
            context.SetOutput(await ShowAsync(context.Input, owner)));
    }

    public static async Task<Result> ShowAsync(MessageBoxVM messageBox, Visual? owner)
    {
        if (!InWindowDialog.CanShow(owner))
        {
            // Nothing to show it in, e.g. the view was already detached while its window closed
            return await ShowInWindowAsync(messageBox);
        }

        var view = new MessageBoxView
        {
            DataContext = messageBox
        };

        using var buttons = messageBox.Answers.Subscribe(result => InWindowDialog.Close(view, result));

        return await InWindowDialog.ShowAsync(view, owner, Result.Cancel);
    }

    private static async Task<Result> ShowInWindowAsync(MessageBoxVM messageBox)
    {
        var dialog = new MessageBoxWindow
        {
            DataContext = messageBox
        };

        // A modeless window does not return its close result, so it is taken from the buttons
        var result = new TaskCompletionSource<Result>();

        using var buttons = messageBox.Answers.Subscribe(r => result.TrySetResult(r));
        dialog.Closed += (_, _) => result.TrySetResult(Result.Cancel);

        dialog.Show();
        return await result.Task;
    }
}
