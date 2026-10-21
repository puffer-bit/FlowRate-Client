using System;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Avalonia.Localization;
using Client.Services;
using ReactiveUI;

namespace Client.ViewModels.Dialogs;

public enum DatabasePasswordPurpose
{
    /// <summary>At start: the encrypted database waits for its password.</summary>
    Unlock,

    /// <summary>The database is to be encrypted with a new password.</summary>
    Create,

    /// <summary>The encrypted database gets another password.</summary>
    Change
}

/// <summary>
/// Asks for the password of the database. The dialog stays open while the password is put to use,
/// so a wrong one is reported in place and a long encryption shows as busy.
/// </summary>
public class DatabasePasswordVM : ReactiveObject
{
    private readonly Func<string, Task<string?>> _accept;

    /// <param name="accept">
    /// Puts the password to use; returns the text of what went wrong, or null to close the dialog.
    /// </param>
    public DatabasePasswordVM(DatabasePasswordPurpose purpose, Func<string, Task<string?>> accept)
    {
        Purpose = purpose;
        _accept = accept;

        var prefix = purpose switch
        {
            DatabasePasswordPurpose.Unlock => "databasePassword.unlock",
            DatabasePasswordPurpose.Create => "databasePassword.create",
            _ => "databasePassword.change"
        };

        Title = Strings.Get(prefix + ".title");
        Text = Strings.Get(prefix + ".text");
        ConfirmText = Strings.Get(prefix + ".confirm");
        CancelText = Strings.Get(purpose == DatabasePasswordPurpose.Unlock ? "databasePassword.exit" : "common.cancel");

        var canConfirm = this.WatchProperties(nameof(Password), nameof(IsBusy))
            .Select(_ => Password.Length > 0 && !IsBusy);

        ConfirmCommand = ReactiveCommand.CreateFromTask(ConfirmAsync, canConfirm);
        CancelCommand = ReactiveCommand.Create(() => false,
            this.WatchProperty(nameof(IsBusy), vm => vm.IsBusy).Select(isBusy => !isBusy));

        Answers = ConfirmCommand.Where(accepted => accepted).Merge(CancelCommand);
    }

    public DatabasePasswordPurpose Purpose { get; }

    public bool IsConfirmationNeeded => Purpose != DatabasePasswordPurpose.Unlock;

    public string Title { get; }
    public string Text { get; }
    public string ConfirmText { get; }
    public string CancelText { get; }

    public string Password
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            Error = null;
        }
    } = "";

    public string Confirmation
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            Error = null;
        }
    } = "";

    public bool IsPasswordVisible
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? Error
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsBusy
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The password being put to use, for whoever has to know when it is done after the dialog is
    /// dismissed underneath it.
    /// </summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    public ReactiveCommand<RxVoid, bool> ConfirmCommand { get; }
    public ReactiveCommand<RxVoid, bool> CancelCommand { get; }

    /// <summary>True once the password was put to use, false when the user gave up.</summary>
    public IObservable<bool> Answers { get; }

    private async Task<bool> ConfirmAsync()
    {
        if (IsConfirmationNeeded && Password != Confirmation)
        {
            Error = Strings.Get("databasePassword.mismatch");
            return false;
        }

        IsBusy = true;

        try
        {
            var accepting = _accept(Password);
            Completion = accepting;

            Error = await accepting;
            return Error == null;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
