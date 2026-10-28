using Shared.Enums;
using Client.Core.Coordinator;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Core.Users;
using Client.Services;
using Client.ViewModels.Connections;
using ReactiveUI;
using Shared.Validation;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Profile;

/// <summary>
/// What the current user logs in with: the password, one-time codes and passkeys, and the devices logged in. Every
/// change asks for the current password and code the user has, so an open session alone cannot take over the
/// account; ending the sessions of other devices takes nothing from it, and asks for nothing.
/// </summary>
public sealed class AccountSecurityVM : ReactiveObject
{
    // The server takes no shorter one
    public const int PasswordMinLength = 8;

    public enum Change
    {
        None,
        SetPassword,
        RemovePassword,
        EnableCodes,
        DisableCodes,
        DeletePasskey,
        EndSession,
        EndOtherSessions,
        ResetIdentity
    }

    private readonly ICoordinatorSession _session;

    public AccountSecurityVM(ICoordinatorSession session)
    {
        _session = session;

        var notBusy = this.WatchProperty(nameof(IsBusy), vm => vm.IsBusy).Select(busy => !busy);
        var inOverview = this.WatchProperties(nameof(IsBusy), nameof(Security), nameof(Current))
            .Select(_ => !IsBusy && Security != null && Current == Change.None);

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync, notBusy);
        SetPasswordCommand = ReactiveCommand.Create(() => Begin(Change.SetPassword), inOverview);
        RemovePasswordCommand = ReactiveCommand.Create(() => Begin(Change.RemovePassword), inOverview);
        EnableCodesCommand = ReactiveCommand.CreateFromTask(BeginEnableCodesAsync, inOverview);
        DisableCodesCommand = ReactiveCommand.Create(() => Begin(Change.DisableCodes), inOverview);
        DeletePasskeyCommand = ReactiveCommand.Create<AccountPasskeyVM>(passkey =>
        {
            PasskeyToDelete = passkey;
            Begin(Change.DeletePasskey);
        }, inOverview);
        EndSessionCommand = ReactiveCommand.Create<AccountSessionVM>(session =>
        {
            SessionToEnd = session;
            Begin(Change.EndSession);
        }, inOverview);
        EndOtherSessionsCommand = ReactiveCommand.Create(() => Begin(Change.EndOtherSessions), inOverview);
        ResetIdentityCommand = ReactiveCommand.Create(() => Begin(Change.ResetIdentity), inOverview);
        ConfirmCommand = ReactiveCommand.CreateFromTask(ConfirmAsync,
            this.WatchProperties(nameof(IsBusy), nameof(Current)).Select(_ => !IsBusy && Current != Change.None));
        BackCommand = ReactiveCommand.Create(Back, notBusy);
        CloseCommand = ReactiveCommand.CreateFromTask(async () => { await CloseWindow.Handle(RxVoid.Default); },
            notBusy);
    }

    public AccountSecurity? Security
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            Passkeys.Clear();
            foreach (var passkey in value?.Passkeys ?? [])
                Passkeys.Add(new AccountPasskeyVM(passkey));
            this.RaisePropertyChanged(nameof(HasPassword));
            this.RaisePropertyChanged(nameof(IsTwoFactorEnabled));
            this.RaisePropertyChanged(nameof(HasPasskeys));
            this.RaisePropertyChanged(nameof(PasswordState));
            this.RaisePropertyChanged(nameof(CodesState));
            this.RaisePropertyChanged(nameof(PasskeysState));
            this.RaisePropertyChanged(nameof(IsPasswordProofAsked));
            this.RaisePropertyChanged(nameof(IsCodeProofAsked));
            this.RaisePropertyChanged(nameof(IsProofAsked));
        }
    }

    public bool HasPassword => Security?.HasPassword == true;
    public bool IsTwoFactorEnabled => Security?.IsTwoFactorEnabled == true;
    public bool HasPasskeys => Passkeys.Count > 0;

    public string PasswordState => Strings.Get(HasPassword ? "accountSecurity.password.set" : "accountSecurity.password.notSet");

    public string CodesState => Strings.Get(IsTwoFactorEnabled ? "accountSecurity.codes.on" : "accountSecurity.codes.off");

    public string PasskeysState => Strings.Get(Security?.PasskeysAvailable == false
        ? "accountSecurity.passkeys.unavailable"
        : "accountSecurity.passkeys.hint");

    public ObservableCollection<AccountPasskeyVM> Passkeys { get; } = [];

    /// <summary>The devices the user is logged in from, this one first.</summary>
    public ObservableCollection<AccountSessionVM> Sessions { get; } = [];

    public bool HasOtherSessions => Sessions.Any(session => !session.IsCurrent);

    /// <summary>The change being made; <see cref="Change.None"/> shows what the user has.</summary>
    public Change Current
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsOverview));
            this.RaisePropertyChanged(nameof(IsChanging));
            this.RaisePropertyChanged(nameof(IsNewPasswordAsked));
            this.RaisePropertyChanged(nameof(IsProofAsked));
            this.RaisePropertyChanged(nameof(ChangeTitle));
            this.RaisePropertyChanged(nameof(ChangeHint));
            this.RaisePropertyChanged(nameof(ConfirmText));
        }
    }

    public bool IsOverview => Current == Change.None;
    public bool IsChanging => Current != Change.None;
    public bool IsNewPasswordAsked => Current == Change.SetPassword;

    /// <summary>Turning the codes on asks for the proof once, before the secret is offered.</summary>
    public bool IsProofAsked => Current is not (Change.None or Change.EndSession or Change.EndOtherSessions)
                                && (Current != Change.EnableCodes || Setup == null)
                                && (IsPasswordProofAsked || IsCodeProofAsked);

    public bool IsPasswordProofAsked => HasPassword;
    public bool IsCodeProofAsked => IsTwoFactorEnabled;

    public string ChangeTitle => Current switch
    {
        Change.SetPassword => Strings.Get(HasPassword ? "accountSecurity.password.change" : "accountSecurity.password.add"),
        Change.RemovePassword => Strings.Get("accountSecurity.password.remove"),
        Change.EnableCodes => Strings.Get("accountSecurity.codes.enable"),
        Change.DisableCodes => Strings.Get("accountSecurity.codes.disable"),
        Change.DeletePasskey => Strings.Format("accountSecurity.passkeys.delete", ("name", PasskeyToDelete?.Label ?? "")),
        Change.EndSession => Strings.Format("accountSecurity.sessions.end", ("name", SessionToEnd?.Name ?? "")),
        Change.EndOtherSessions => Strings.Get("accountSecurity.sessions.endOthers"),
        Change.ResetIdentity => Strings.Get("accountSecurity.identity.reset"),
        _ => ""
    };

    /// <summary>What the change does, for the changes that need it told.</summary>
    public string? ChangeHint => Current switch
    {
        Change.EndSession or Change.EndOtherSessions => Strings.Get("accountSecurity.sessions.endHint"),
        Change.ResetIdentity => Strings.Get("accountSecurity.identity.resetHint"),
        _ => null
    };

    public string ConfirmText => Strings.Get(Current switch
    {
        Change.RemovePassword or Change.DeletePasskey => "common.delete",
        Change.EndSession or Change.EndOtherSessions => "accountSecurity.sessions.endConfirm",
        Change.ResetIdentity => "accountSecurity.identity.resetConfirm",
        Change.DisableCodes => "accountSecurity.codes.disableConfirm",
        Change.EnableCodes when Setup == null => "accountSecurity.next",
        Change.EnableCodes => "accountSecurity.codes.enableConfirm",
        _ => "common.save"
    });

    public string ProofPassword
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            Error = null;
        }
    } = "";

    public string ProofCode
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            Error = null;
        }
    } = "";

    public string NewPassword
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            Error = null;
        }
    } = "";

    public string RepeatPassword
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

    /// <summary>The secret offered for the authenticator app, while the codes are being turned on.</summary>
    public TwoFactorSetup? Setup
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsSetupShown));
            this.RaisePropertyChanged(nameof(IsProofAsked));
            this.RaisePropertyChanged(nameof(ConfirmText));
        }
    }

    public bool IsSetupShown => Setup != null;

    /// <summary>The first code the app makes from the offered secret.</summary>
    public string SetupCode
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            Error = null;
        }
    } = "";

    public int CodeLength => Limits.TwoFactorMaxLength;
    public int PasswordMaxLength => Limits.PasswordMaxLength;
    public string NewPasswordHint => Strings.Format("accountSecurity.password.hint",
        ("min", PasswordMinLength), ("max", Limits.PasswordMaxLength));

    public AccountPasskeyVM? PasskeyToDelete { get; private set; }

    public AccountSessionVM? SessionToEnd { get; private set; }

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

    public Interaction<RxVoid, RxVoid> CloseWindow { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SetPasswordCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemovePasswordCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> EnableCodesCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DisableCodesCommand { get; }
    public ReactiveCommand<AccountPasskeyVM, RxVoid> DeletePasskeyCommand { get; }
    public ReactiveCommand<AccountSessionVM, RxVoid> EndSessionCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> EndOtherSessionsCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ResetIdentityCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ConfirmCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> BackCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }

    private async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var (result, security) = await _session.GetAccountSecurity();
            Apply(result, security);
            if (!result.IsSuccess)
                return result;

            var (sessionsResult, sessions) = await _session.GetAccountSessions();
            ApplySessions(sessionsResult, sessions);
            return sessionsResult;
        });
    }

    private void Begin(Change change)
    {
        ProofPassword = "";
        ProofCode = "";
        NewPassword = "";
        RepeatPassword = "";
        SetupCode = "";
        Setup = null;
        Current = change;
        Error = null;
    }

    // Without a password or codes there is nothing to prove: the secret is offered at once
    private async Task BeginEnableCodesAsync()
    {
        Begin(Change.EnableCodes);
        if (!IsProofAsked)
            await ConfirmAsync();
    }

    private void Back()
    {
        Current = Change.None;
        Setup = null;
        PasskeyToDelete = null;
        SessionToEnd = null;
        Error = null;
    }

    private async Task ConfirmAsync()
    {
        if (!Validate())
            return;

        var proof = new AccountProof(HasPassword ? ProofPassword : null,
            IsTwoFactorEnabled ? LoginPromptVM.NormalizeCode(ProofCode) : null);

        await RunAsync(async () =>
        {
            switch (Current)
            {
                case Change.SetPassword:
                    return Finish(await _session.SetAccountPassword(proof, NewPassword));
                case Change.RemovePassword:
                    return Finish(await _session.SetAccountPassword(proof, null));
                case Change.EnableCodes when Setup == null:
                {
                    var (result, setup) = await _session.SetupTwoFactor(proof);
                    Setup = setup;
                    return result;
                }
                case Change.EnableCodes:
                    return Finish(await _session.EnableTwoFactor(LoginPromptVM.NormalizeCode(SetupCode)));
                case Change.DisableCodes:
                    return Finish(await _session.DisableTwoFactor(proof));
                case Change.DeletePasskey when PasskeyToDelete is { } passkey:
                    return Finish(await _session.DeletePasskey(proof, passkey.Id));
                case Change.EndSession when SessionToEnd is { } session:
                    return FinishSessions(await _session.EndAccountSession(session.Id));
                case Change.EndOtherSessions:
                    return FinishSessions(await _session.EndAccountSession(null));
                case Change.ResetIdentity:
                {
                    var result = await _session.ResetUserIdentity(proof);
                    if (!result.IsSuccess)
                        return result;

                    // The other devices were logged out with the old identity
                    var (sessionsResult, sessions) = await _session.GetAccountSessions();
                    ApplySessions(sessionsResult, sessions);
                    Back();
                    return RequestResult.Success;
                }
                default:
                    return RequestResult.Success;
            }
        });
    }

    private RequestResult Finish(RequestResult<AccountSecurity> response)
    {
        Apply(response.Result, response.Value);
        if (response.IsSuccess)
            Back();
        return response.Result;
    }

    private RequestResult FinishSessions(RequestResult<IReadOnlyList<AccountSession>> response)
    {
        ApplySessions(response.Result, response.Value);
        if (response.IsSuccess)
            Back();
        return response.Result;
    }

    private void Apply(RequestResult result, AccountSecurity? security)
    {
        if (result.IsSuccess && security != null)
            Security = security;
    }

    private void ApplySessions(RequestResult result,
        IReadOnlyList<AccountSession>? sessions)
    {
        if (!result.IsSuccess || sessions == null)
            return;

        Sessions.Clear();
        foreach (var session in sessions.OrderByDescending(session => session.IsCurrent))
            Sessions.Add(new AccountSessionVM(session));
        this.RaisePropertyChanged(nameof(HasOtherSessions));
    }

    /// <summary>Checks here what the server would refuse anyway, so the user learns it without a round trip.</summary>
    private bool Validate()
    {
        if (Current == Change.EnableCodes && Setup != null)
            return Require(IsCodeValid(SetupCode), "accountSecurity.error.codeFormat");

        if (!IsProofAsked)
            return true;

        if (IsPasswordProofAsked && !Require(ProofPassword.Length > 0, "accountSecurity.error.proofPassword"))
            return false;

        if (IsCodeProofAsked && !Require(IsCodeValid(ProofCode), "accountSecurity.error.codeFormat"))
            return false;

        if (Current != Change.SetPassword)
            return true;

        return Require(NewPassword.Length is >= PasswordMinLength and <= Limits.PasswordMaxLength, null)
               && Require(NewPassword == RepeatPassword, "accountSecurity.error.mismatch");
    }

    private bool Require(bool condition, string? errorKey)
    {
        if (!condition)
            Error = errorKey == null ? NewPasswordHint : Strings.Get(errorKey);
        return condition;
    }

    private static bool IsCodeValid(string code)
    {
        var normalized = LoginPromptVM.NormalizeCode(code);
        return normalized.Length == Limits.TwoFactorMaxLength && normalized.All(char.IsAsciiDigit);
    }

    private async Task RunAsync(Func<Task<RequestResult>> request)
    {
        IsBusy = true;
        Error = null;

        try
        {
            Error = Describe(await request());
        }
        catch (Exception exception)
        {
            Error = Strings.Format("accountSecurity.error.unexpected", ("error", exception.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string? Describe(RequestResult result) => result switch
    {
        { IsSuccess: true } => null,
        { Error: ErrorCode.ProofRequired } => Strings.Get("accountSecurity.error.proof"),
        { Error: ErrorCode.VerificationFailed } => Strings.Get("accountSecurity.error.codeRefused"),
        { Error: ErrorCode.InvalidValue } when Current == Change.SetPassword => NewPasswordHint,
        { Error: ErrorCode.InvalidValue } => Strings.Get("accountSecurity.error.invalid"),
        { Error: ErrorCode.FeatureDisabled } => Strings.Get("accountSecurity.passkeys.unavailable"),
        { Error: ErrorCode.PasskeyNotFound } => Strings.Get("accountSecurity.error.passkeyNotFound"),
        { Error: ErrorCode.SessionNotFound } => Strings.Get("accountSecurity.error.sessionNotFound"),
        _ => Strings.Format("accountSecurity.error.failed", ("code", (int?)result.Error))
    };
}

public sealed class AccountPasskeyVM(AccountPasskey passkey)
{
    public int Id => passkey.Id;

    public string Label => string.IsNullOrWhiteSpace(passkey.Label)
        ? Strings.Get("accountSecurity.passkeys.unnamed")
        : passkey.Label;

    public string Details => passkey.LastUsedAtUtc is { } lastUsed
        ? Strings.Format("accountSecurity.passkeys.usedAt", ("created", Format(passkey.CreatedAtUtc)),
            ("used", Format(lastUsed)))
        : Strings.Format("accountSecurity.passkeys.createdAt", ("created", Format(passkey.CreatedAtUtc)));

    private static string Format(DateTime time) =>
        Strings.FormatDateTime(time.Kind == DateTimeKind.Local ? time : DateTime.SpecifyKind(time, DateTimeKind.Utc).ToLocalTime());
}

public sealed class AccountSessionVM(AccountSession session)
{
    public Guid Id => session.Id;
    public bool IsCurrent => session.IsCurrent;
    public bool IsOther => !session.IsCurrent;

    public string Name => string.IsNullOrWhiteSpace(session.DeviceName)
        ? Strings.Get("accountSecurity.sessions.unnamed")
        : session.DeviceName;

    /// <summary>"Linux · 0.8.0 · this device", "Android · 0.8.0 · online", or when it was last used, and from where.</summary>
    public string Details => string.Join(" · ", new[]
    {
        session.Platform,
        session.AppVersion,
        session.IsCurrent ? Strings.Get("accountSecurity.sessions.current")
        : session.IsOnline ? Strings.Get("accountSecurity.sessions.online")
        : Strings.Format("accountSecurity.sessions.lastUsed", ("time", Format(session.LastUsedAtUtc))),
        session.LastAddress
    }.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string Format(DateTime time) =>
        Strings.FormatDateTime(DateTime.SpecifyKind(time, DateTimeKind.Utc).ToLocalTime());
}
