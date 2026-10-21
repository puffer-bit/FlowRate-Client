using System;
using Avalonia;
using Client.ViewModels.Dialogs;
using ReactiveUI;

namespace Client.Views.Dialogs;

internal static class DatabasePasswordInteraction
{
    /// <summary>
    /// Shows the password dialogs of <paramref name="interaction"/> in the window that hosts
    /// <paramref name="owner"/>; Escape gives up as the cancel button does.
    /// </summary>
    /// <returns>The registration, to be disposed together with the view activation.</returns>
    public static IDisposable RegisterDatabasePasswordHandler(this Interaction<DatabasePasswordVM, bool> interaction,
        Visual owner)
    {
        return interaction.RegisterHandler(async context =>
        {
            var view = new DatabasePasswordDialog { DataContext = context.Input };

            using var answers = context.Input.Answers.Subscribe(accepted => InWindowDialog.Close(view, accepted));

            context.SetOutput(await InWindowDialog.ShowAsync(view, owner, false));
        });
    }
}
