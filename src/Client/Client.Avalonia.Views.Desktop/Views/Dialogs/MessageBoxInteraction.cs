using System;
using System.Threading.Tasks;
using Avalonia;
using ReactiveUI;
using Client.ViewModels.Dialogs;

namespace Client.Views.Dialogs;

/// <summary>
/// The message boxes of the desktop views. They are the shared ones of
/// <see cref="Client.Views.Shared.Dialogs.MessageBoxDialog"/>, shown in the window by <see cref="InWindowDialog"/>.
/// </summary>
internal static class MessageBoxInteraction
{
    /// <summary>
    /// Shows the message boxes of <paramref name="interaction"/> in the window that hosts
    /// <paramref name="owner"/>.
    /// </summary>
    /// <returns>The registration, to be disposed together with the view activation.</returns>
    public static IDisposable RegisterMessageBoxHandler(this Interaction<MessageBoxVM, Result> interaction,
        Visual owner) =>
        Client.Views.Shared.Dialogs.MessageBoxDialog.RegisterMessageBoxHandler(interaction, owner);

    public static Task<Result> ShowAsync(MessageBoxVM messageBox, Visual? owner) =>
        Client.Views.Shared.Dialogs.MessageBoxDialog.ShowAsync(messageBox, owner);
}
