using System;
using System.Threading.Tasks;
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
            context.SetOutput(await ShowAsync(context.Input, owner)));
    }

    public static async Task<bool> ShowAsync(DatabasePasswordVM password, Visual owner)
    {
        var view = new DatabasePasswordDialog { DataContext = password };

        using var answers = password.Answers.Subscribe(accepted => InWindowDialog.Close(view, accepted));

        return await InWindowDialog.ShowAsync(view, owner, false);
    }
}
