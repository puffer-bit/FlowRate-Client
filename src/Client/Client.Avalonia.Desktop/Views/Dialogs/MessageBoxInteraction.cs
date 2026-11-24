using System;
using System.Linq;
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
        if (messageBox.CanCopyDetail)
            foreach (var cancel in messageBox.ButtonsList.Where(button => button.IsCancel).ToArray())
                messageBox.ButtonsList.Remove(cancel);

        // Nothing to show it in, e.g. the application is shutting down
        if (!InWindowDialog.CanShow(owner))
            return Result.Cancel;

        var view = new MessageBoxView
        {
            DataContext = messageBox
        };

        using var buttons = messageBox.Answers.Subscribe(result => InWindowDialog.Close(view, result));

        return await InWindowDialog.ShowAsync(view, owner, Result.Cancel);
    }
}
