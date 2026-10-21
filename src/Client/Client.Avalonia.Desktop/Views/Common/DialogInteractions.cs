using System;
using Avalonia;
using Avalonia.Controls;
using Client.Views.Dialogs;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace Client.Views.Common;

/// <summary>
/// The answers to the dialog interactions of the view models that are the same in every view.
/// </summary>
internal static class DialogInteractions
{
    /// <summary>
    /// Closes the dialog that contains <paramref name="view"/> when its view model asks for it. What the
    /// view model passes becomes the result of the dialog.
    /// </summary>
    /// <returns>The registration, to be disposed together with the view activation.</returns>
    public static IDisposable RegisterCloseHandler<TResult>(this Interaction<TResult, RxVoid> interaction, Visual view)
    {
        return interaction.RegisterHandler(context =>
        {
            InWindowDialog.Close(view, context.Input is RxVoid ? null : context.Input);
            context.SetOutput(RxVoid.Default);
        });
    }

    /// <summary>
    /// Shows the view made by <paramref name="createView"/> as a dialog in the window that hosts
    /// <paramref name="owner"/> and answers the interaction once the dialog is closed. The input of the
    /// interaction becomes the data context of the view; a view shown without an input keeps its own.
    /// </summary>
    /// <param name="fillWindow">See <see cref="DialogOverlay(Control,bool)"/>.</param>
    /// <returns>The registration, to be disposed together with the view activation.</returns>
    public static IDisposable RegisterDialogHandler<TInput>(this Interaction<TInput, RxVoid> interaction, Visual owner,
        Func<Control> createView, bool fillWindow = false)
    {
        return interaction.RegisterHandler(async context =>
        {
            var view = createView();
            if (context.Input is not RxVoid)
                view.DataContext = context.Input;

            await InWindowDialog.ShowAsync<object?>(view, owner, null, fillWindow);
            context.SetOutput(RxVoid.Default);
        });
    }
}
