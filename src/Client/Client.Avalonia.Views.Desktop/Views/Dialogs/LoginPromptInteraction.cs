using System;
using Avalonia;
using Client.Application.Coordinator;
using Client.ViewModels.Connections;
using ReactiveUI;

namespace Client.Views.Dialogs;

internal static class LoginPromptInteraction
{
    /// <summary>
    /// Shows the login questions of <paramref name="interaction"/> in the window that hosts <paramref name="owner"/>;
    /// Escape gives up as the cancel button does.
    /// </summary>
    /// <returns>The registration, to be disposed together with the view activation.</returns>
    public static IDisposable RegisterLoginPromptHandler(this Interaction<LoginPromptVM, LoginAnswer?> interaction,
        Visual owner)
    {
        return interaction.RegisterHandler(async context =>
        {
            var view = new LoginPromptDialog { DataContext = context.Input };

            LoginAnswer? answer = null;
            using var answers = context.Input.Answers.Subscribe(given =>
            {
                answer = given;
                InWindowDialog.Close(view);
            });

            await InWindowDialog.ShowAsync<object?>(view, owner, null);
            context.SetOutput(answer);
        });
    }
}
