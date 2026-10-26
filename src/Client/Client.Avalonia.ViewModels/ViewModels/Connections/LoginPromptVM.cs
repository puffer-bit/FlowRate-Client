using System;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Services;
using ReactiveUI;
using Shared.Validation;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Connections;

/// <summary>
/// Asks for what finishes a login: the password of the account, a one-time code or both.
/// </summary>
public sealed class LoginPromptVM : ReactiveObject
{
    private readonly Subject<LoginAnswer?> _dismissed = new();

    public LoginPromptVM(LoginPrompt prompt)
    {
        IsPasswordAsked = prompt.AsksPassword;
        IsCodeAsked = prompt.AsksTwoFactorCode;
        RememberPassword = prompt.IsPasswordRemembered;
        Text = Strings.Format(IsPasswordAsked
            ? IsCodeAsked ? "loginPrompt.text.both" : "loginPrompt.text.password"
            : "loginPrompt.text.code", ("server", prompt.CoordinatorAddress));
        Error = prompt.IsRetry ? Strings.Get("loginPrompt.refused") : null;

        var canConfirm = this.WatchProperties(nameof(Password), nameof(Code))
            .Select(_ => (!IsPasswordAsked || Password.Length > 0) && (!IsCodeAsked || IsCodeComplete(Code)));

        ConfirmCommand = ReactiveCommand.Create(Confirm, canConfirm);
        CancelCommand = ReactiveCommand.Create(() => (LoginAnswer?)null);

        Answers = ConfirmCommand.Where(answer => answer != null).Merge(CancelCommand).Merge(_dismissed).Take(1);
    }

    public string Text { get; }

    public bool IsPasswordAsked { get; }
    public bool IsCodeAsked { get; }

    public string Password
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

    public bool RememberPassword
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string Code
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            Error = null;
        }
    } = "";

    public int CodeLength => Limits.TwoFactorMaxLength;

    public string? Error
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, LoginAnswer?> ConfirmCommand { get; }
    public ReactiveCommand<RxVoid, LoginAnswer?> CancelCommand { get; }

    /// <summary>The answer once the user gave it, null when they gave up or the login went away.</summary>
    public IObservable<LoginAnswer?> Answers { get; }

    /// <summary>Takes the question away unanswered, as when the login was given up from elsewhere.</summary>
    public void Dismiss() => _dismissed.OnNext(null);

    /// <summary>
    /// Makes <paramref name="session"/> ask its login questions through <paramref name="ask"/>, on the UI thread.
    /// </summary>
    public static void Attach(ICoordinatorSession session, Interaction<LoginPromptVM, LoginAnswer?> ask) =>
        session.LoginPrompt = (prompt, cancellationToken) => AskAsync(ask, prompt, cancellationToken);

    private static Task<LoginAnswer?> AskAsync(Interaction<LoginPromptVM, LoginAnswer?> ask, LoginPrompt prompt,
        CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var vm = new LoginPromptVM(prompt);
            await using var dismiss = cancellationToken.Register(() => Dispatcher.UIThread.Post(vm.Dismiss));
            return await ask.Handle(vm);
        });

    /// <summary>Codes are digits; the spaces an authenticator shows inside one are dropped.</summary>
    public static string NormalizeCode(string code) => new(code.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static bool IsCodeComplete(string code)
    {
        var normalized = NormalizeCode(code);
        return normalized.Length == Limits.TwoFactorMaxLength && normalized.All(char.IsAsciiDigit);
    }

    private LoginAnswer? Confirm() => new(IsPasswordAsked ? Password : null,
        IsCodeAsked ? NormalizeCode(Code) : null, IsPasswordAsked && RememberPassword);
}
