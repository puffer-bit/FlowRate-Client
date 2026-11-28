using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Application.Coordinator;
using Client.Application.Links;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using Client.Core.Users;
using Client.Services;
using Client.ViewModels.Connections;
using ReactiveUI;
using Shared.Enums;
using Shared.Validation;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Profile;

/// <summary>
/// What the current user logs in with: the password, one-time codes and passkeys, the recovery phrase, and the devices
/// of the account. Every change of how the account logs in asks for the current password and code the user has, so an
/// open session alone cannot take over the account; removing other devices takes nothing from it, and asks for nothing.
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
        ResetPhrase,
        LinkDevice,
        Logout
    }

    /// <summary>Keeps a new recovery phrase on this device until the user confirms saving it.</summary>
    public Func<string, Task>? KeepRecoveryPhrase { get; init; }

    /// <summary>The user saved the phrase shown; true keeps it on this device anyway.</summary>
    public Func<bool, Task>? ConfirmRecoveryPhraseSaved { get; init; }

    /// <summary>The phrase while it is kept on this device; null otherwise.</summary>
    public Func<Task<string?>>? LoadRecoveryPhrase { get; init; }

    /// <summary>This device left the account: the saved account forgets it.</summary>
    public Func<Task>? LoggedOut { get; init; }

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
        ResetPhraseCommand = ReactiveCommand.Create(() => Begin(Change.ResetPhrase), inOverview);
        LinkDeviceCommand = ReactiveCommand.Create(() => Begin(Change.LinkDevice), inOverview);
        DeclineLinkCommand = ReactiveCommand.CreateFromTask(DeclineLinkAsync, notBusy);
        LogoutCommand = ReactiveCommand.Create(() => Begin(Change.Logout), inOverview);
        ShowPhraseCommand = ReactiveCommand.CreateFromTask(ShowKeptPhraseAsync, notBusy);
        ConfirmPhraseSavedCommand = ReactiveCommand.CreateFromTask(ConfirmPhraseSavedAsync, notBusy);
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

    /// <summary>The devices of the account, this one first.</summary>
    public ObservableCollection<AccountSessionVM> Sessions { get; } = [];

    /// <summary>A recovery phrase to show: a new one, or the one kept on this device.</summary>
    public string? RecoveryPhrase
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsRecoveryPhraseShown));
            this.RaisePropertyChanged(nameof(RecoveryPhraseText));
        }
    }

    public bool IsRecoveryPhraseShown => RecoveryPhrase != null;

    /// <summary>Desktop supplies a single show/hide action for the saved phrase.</summary>
    public bool TogglePhraseVisibility { get; set; }

    /// <summary>The words numbered, four a line.</summary>
    public string RecoveryPhraseText => RecoveryPhrase == null ? "" : NumberWords(RecoveryPhrase);

    /// <summary>Keep the phrase on this device after the user saved it.</summary>
    public bool KeepPhraseOnDevice
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The code a new device shows, as typed or pasted (flowrate-link:… too).</summary>
    public string LinkCode
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            LinkInfo = null;
            Error = null;
        }
    } = "";

    /// <summary>What the new device told about itself, once the code was looked up.</summary>
    public DeviceLinkInfo? LinkInfo
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(LinkInfoText));
            this.RaisePropertyChanged(nameof(IsLinkInfoShown));
            this.RaisePropertyChanged(nameof(ConfirmText));
        }
    }

    public bool IsLinkInfoShown => LinkInfo != null;

    // A link pasted or scanned whole gives the code it carries
    private string LinkCodeToSend => FlowRateLinks.DeviceLinkCode(LinkCode);

    /// <summary>
    /// Reads the QR code a new device shows with the scanner of the phone, and looks the device up.
    /// </summary>
    public async Task ScanLinkCodeAsync(Func<Task<string?>> scan)
    {
        string? text;
        try
        {
            text = await scan();
        }
        catch (Exception exception)
        {
            Error = Strings.Format("link.scan.unavailable", ("error", exception.Message));
            return;
        }

        if (text == null)
            return;
        if (FlowRateLinks.Parse(text) is not DeviceLinkApproval approval)
        {
            Error = Strings.Get("link.scan.notDeviceLink");
            return;
        }

        LinkCode = approval.Code;
        ConfirmCommand.Execute().Subscribe();
    }

    /// <summary>
    /// Loads the dialog on the approval of a new device with its code, from a link or a scan, and looks the device up;
    /// the user approves it once its details are shown. Called in place of <see cref="LoadCommand"/>.
    /// </summary>
    public void StartLink(string code)
    {
        Begin(Change.LinkDevice);
        LinkCode = code;
        LoadCommand.Execute().Subscribe(_ =>
        {
            if (Current == Change.LinkDevice && LinkInfo == null)
                ConfirmCommand.Execute().Subscribe();
        });
    }

    public string LinkInfoText => LinkInfo is not { } info ? "" : Strings.Format("accountSecurity.link.info",
        ("device", info.DeviceName ?? "?"), ("platform", info.Platform ?? "?"), ("address", info.Address ?? "?"));

    public bool IsLinkCodeAsked => Current == Change.LinkDevice;

    public static string NumberWords(string phrase) => string.Join("\n", Client.Application.Security.RecoveryPhrase
        .Split(phrase).Select((word, index) => $"{index + 1,2}. {word}").Chunk(4).Select(line => string.Join("   ", line)));

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
            this.RaisePropertyChanged(nameof(IsLinkCodeAsked));
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
    public bool IsProofAsked => Current is not (Change.None or Change.EndSession or Change.EndOtherSessions
                                    or Change.LinkDevice or Change.Logout)
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
        Change.ResetPhrase => Strings.Get("accountSecurity.phrase.reset"),
        Change.LinkDevice => Strings.Get("accountSecurity.link.title"),
        Change.Logout => Strings.Get("accountSecurity.logout.title"),
        _ => ""
    };

    /// <summary>What the change does, for the changes that need it told.</summary>
    public string? ChangeHint => Current switch
    {
        Change.EndSession or Change.EndOtherSessions => Strings.Get("accountSecurity.sessions.endHint"),
        Change.ResetPhrase => Strings.Get("accountSecurity.phrase.resetHint"),
        Change.LinkDevice => Strings.Get("accountSecurity.link.hint"),
        Change.Logout => Strings.Get("accountSecurity.logout.hint"),
        _ => null
    };

    public string ConfirmText => Strings.Get(Current switch
    {
        Change.RemovePassword or Change.DeletePasskey => "common.delete",
        Change.EndSession or Change.EndOtherSessions => "accountSecurity.sessions.endConfirm",
        Change.ResetPhrase => "accountSecurity.phrase.resetConfirm",
        Change.LinkDevice when LinkInfo == null => "accountSecurity.next",
        Change.LinkDevice => "accountSecurity.link.approve",
        Change.Logout => "accountSecurity.logout.confirm",
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
    public ReactiveCommand<RxVoid, RxVoid> ResetPhraseCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> LinkDeviceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeclineLinkCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> LogoutCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ShowPhraseCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ConfirmPhraseSavedCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ConfirmCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> BackCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }

    private Task LoadAsync() => RunAsync(LoadOverviewAsync);

    private async Task<RequestResult> LoadOverviewAsync()
    {
        var (result, security) = await _session.GetAccountSecurity();
        Apply(result, security);
        if (!result.IsSuccess)
            return result;

        var (sessionsResult, sessions) = await _session.GetAccountDevices();
        ApplySessions(sessionsResult, sessions);
        return sessionsResult;
    }

    private async Task ShowKeptPhraseAsync()
    {
        if (TogglePhraseVisibility && IsRecoveryPhraseShown)
        {
            RecoveryPhrase = null;
            return;
        }
        RecoveryPhrase = LoadRecoveryPhrase == null ? null : await LoadRecoveryPhrase();
        if (RecoveryPhrase == null)
            Error = Strings.Get("accountSecurity.phrase.notKept");
    }

    private async Task ConfirmPhraseSavedAsync()
    {
        if (ConfirmRecoveryPhraseSaved != null)
            await ConfirmRecoveryPhraseSaved(KeepPhraseOnDevice);
        RecoveryPhrase = null;
    }

    private async Task DeclineLinkAsync()
    {
        if (string.IsNullOrWhiteSpace(LinkCode))
        {
            Back();
            return;
        }

        await RunAsync(async () =>
        {
            var result = await _session.ApproveDeviceLink(LinkCodeToSend, false);
            if (result.IsSuccess)
                Back();
            return result;
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
        LinkCode = "";
        LinkInfo = null;
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
                    return FinishSessions(await _session.RevokeAccountDevice(session.Id));
                case Change.EndOtherSessions:
                    return FinishSessions(await _session.RevokeAccountDevice(null));
                case Change.ResetPhrase:
                {
                    var (result, phrase) = await _session.ResetRecoveryPhrase(proof);
                    if (!result.IsSuccess || phrase == null)
                        return result;

                    // Kept until the user saves it; the devices stay, they have keys of their own
                    if (KeepRecoveryPhrase != null)
                        await KeepRecoveryPhrase(phrase);
                    RecoveryPhrase = phrase;
                    Back();
                    return RequestResult.Success;
                }
                case Change.LinkDevice when LinkInfo == null:
                {
                    var (result, info) = await _session.GetDeviceLinkInfo(LinkCodeToSend);
                    LinkInfo = info;
                    return result;
                }
                case Change.LinkDevice:
                {
                    var result = await _session.ApproveDeviceLink(LinkCodeToSend, true);
                    if (!result.IsSuccess)
                        return result;

                    var (devicesResult, devices) = await _session.GetAccountDevices();
                    ApplySessions(devicesResult, devices);
                    Back();
                    return RequestResult.Success;
                }
                case Change.Logout:
                {
                    var result = await _session.Logout();
                    if (!result.IsSuccess)
                        return result;

                    if (LoggedOut != null)
                        await LoggedOut();
                    await CloseWindow.Handle(RxVoid.Default);
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

    private RequestResult FinishSessions(RequestResult<IReadOnlyList<AccountDevice>> response)
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
        IReadOnlyList<AccountDevice>? sessions)
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
        if (Current == Change.LinkDevice)
            return Require(!string.IsNullOrWhiteSpace(LinkCode), "accountSecurity.link.codeEmpty");

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
        { Error: ErrorCode.DeviceNotFound } => Strings.Get("accountSecurity.error.sessionNotFound"),
        { Error: ErrorCode.DeviceLinkNotFound } => Strings.Get("accountSecurity.link.notFound"),
        _ => Strings.Format("accountSecurity.error.failed", ("code", (int?)result.Error))
    };
}
