using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using Client.Core.Users;
using Client.ViewModels.Dialogs;
using ReactiveUI;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models.DataTransferObjects;

using Result = Client.ViewModels.Dialogs.Result;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.ServerSettings;

/// <summary>A bot in the list of the bots section.</summary>
public sealed class ServerBotRowVM(UserModel bot, string? ownerName, ServerBotsVM section)
{
    public UserModel Bot { get; } = bot;
    public string Username => Bot.Username;
    public string? AvatarUrl => Bot.AvatarUrl;
    public UserStatus Status => Bot.Status;
    public UserKind Kind => Bot.Kind;

    /// <summary>Who made the bot, when the maker is still there.</summary>
    public string? OwnerText => ownerName == null ? null : Strings.Format("serverSettings.bots.owner", ("name", ownerName));

    public ReactiveCommand<ServerBotRowVM, RxVoid> NewCodeCommand => section.NewCodeCommand;
    public ReactiveCommand<ServerBotRowVM, RxVoid> DeleteCommand => section.DeleteCommand;
}

/// <summary>
/// The bots of the server, for users who may manage them: making one with its first login code, a new code for one,
/// and deleting one. A code is shown once, in the section, until another one or the dialog closes.
/// </summary>
public sealed class ServerBotsVM : ReactiveObject, IDisposable
{
    private readonly ICoordinatorSession _coordinatorSession;
    private readonly Interaction<MessageBoxVM, Result> _showMessageBox;
    private readonly ObservableAsPropertyHelper<bool> _isBusy;

    // The names of the makers of the bots, looked up with the list
    private readonly Dictionary<int, string> _ownerNames = new();

    public ObservableCollection<ServerBotRowVM> Bots { get; } = new();

    public bool CanManage
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsEmpty => Bots.Count == 0;

    public string NewBotName
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    /// <summary>The new bot is a debug bot: marked apart and not counted in the limit.</summary>
    public bool NewBotIsDebug
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The login code given last; shown once.</summary>
    public string? Code
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasCode));
        }
    }

    public bool HasCode => Code != null;

    /// <summary>For which bot the code is, and until when it works.</summary>
    public string? CodeText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsBusy => _isBusy.Value;

    public ReactiveCommand<RxVoid, RxVoid> CreateCommand { get; }
    public ReactiveCommand<ServerBotRowVM, RxVoid> NewCodeCommand { get; }
    public ReactiveCommand<ServerBotRowVM, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> HideCodeCommand { get; }

    public ServerBotsVM(ICoordinatorSession coordinatorSession, Interaction<MessageBoxVM, Result> showMessageBox)
    {
        _coordinatorSession = coordinatorSession;
        _showMessageBox = showMessageBox;
        CanManage = coordinatorSession.Permissions.Has(Permission.ManageBots);

        var canCreate = this.WhenAnyValue(vm => vm.NewBotName, name => !string.IsNullOrWhiteSpace(name));
        CreateCommand = ReactiveCommand.CreateFromTask(CreateAsync, canCreate);
        NewCodeCommand = ReactiveCommand.CreateFromTask<ServerBotRowVM>(NewCodeAsync);
        DeleteCommand = ReactiveCommand.CreateFromTask<ServerBotRowVM>(DeleteAsync);
        HideCodeCommand = ReactiveCommand.Create(() =>
        {
            Code = null;
            CodeText = null;
        });
        _isBusy = CreateCommand.IsExecuting
            .CombineLatest(NewCodeCommand.IsExecuting, DeleteCommand.IsExecuting,
                (creating, renewing, deleting) => creating || renewing || deleting)
            .ToProperty(this, nameof(IsBusy));

        _coordinatorSession.UserUpdated += OnUserUpdated;
        _coordinatorSession.UserDeleted += OnUserDeleted;
        _coordinatorSession.PermissionsChanged += OnPermissionsChanged;
    }

    public async Task LoadAsync()
    {
        if (!CanManage)
        {
            Bots.Clear();
            this.RaisePropertyChanged(nameof(IsEmpty));
            return;
        }

        var bots = await _coordinatorSession.GetBots();
        if (bots.Value is not { } list)
        {
            await TellFailureAsync(bots.Result, "serverSettings.bots.title");
            return;
        }

        var ownerIds = list.Where(bot => bot.OwnerUserId != null).Select(bot => bot.OwnerUserId!.Value).Distinct()
            .Where(id => !_ownerNames.ContainsKey(id)).ToList();
        if (ownerIds.Count > 0)
            foreach (var (id, owner) in await _coordinatorSession.GetUsers(ownerIds))
                _ownerNames[id] = owner.Username;

        Bots.Clear();
        foreach (var bot in list.OrderBy(bot => bot.Username, StringComparer.OrdinalIgnoreCase))
            Bots.Add(Row(bot));
        this.RaisePropertyChanged(nameof(IsEmpty));
    }

    private async Task CreateAsync()
    {
        var created = await _coordinatorSession.CreateBot(NewBotName, NewBotIsDebug);
        if (!created.IsSuccess)
        {
            await TellFailureAsync(created.Result, "serverSettings.bots.create");
            return;
        }

        var (bot, code) = created.Value;
        NewBotName = string.Empty;
        NewBotIsDebug = false;
        ShowCode(bot.Username, code);
        await LoadAsync();
    }

    private async Task NewCodeAsync(ServerBotRowVM row)
    {
        if (await _showMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.YesNo,
                Strings.Format("serverSettings.bots.newCode.question", ("name", row.Username)),
                Strings.Get("serverSettings.bots.newCode"))) != Result.Yes)
            return;

        var code = await _coordinatorSession.NewBotLoginCode(row.Bot.Id, endDevices: true);
        if (code.Value is not { } value)
        {
            await TellFailureAsync(code.Result, "serverSettings.bots.newCode");
            return;
        }

        ShowCode(row.Username, value);
    }

    private async Task DeleteAsync(ServerBotRowVM row)
    {
        if (await _showMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.YesNo,
                Strings.Format("serverSettings.bots.delete.question", ("name", row.Username)),
                Strings.Get("serverSettings.bots.delete"))) != Result.Yes)
            return;

        // The bot leaves the list with the notice of the deletion, or here when no notice comes
        var result = await _coordinatorSession.DeleteBot(row.Bot.Id);
        if (result.IsSuccess)
            Remove(row.Bot.Id);
        else
            await TellFailureAsync(result, "serverSettings.bots.delete");
    }

    private void ShowCode(string botName, BotLoginCodeDTO code)
    {
        Code = code.Code;
        CodeText = Strings.Format("serverSettings.bots.code.text",
            ("name", botName), ("time", Strings.FormatDateTime(code.ExpiresAt.ToLocalTime())));
    }

    private ServerBotRowVM Row(UserModel bot)
    {
        var owner = bot.OwnerUserId is { } ownerId ? _ownerNames.GetValueOrDefault(ownerId) : null;
        return new ServerBotRowVM(bot, owner, this);
    }

    private async Task TellFailureAsync(RequestResult result, string actionKey)
    {
        // Rejected is told by the main window
        var message = result switch
        {
            { IsSuccess: true } or { IsRejected: true } => null,
            { Error: ErrorCode.TimedOut } => Strings.Get("serverSettings.error.timedOut"),
            { Error: ErrorCode.NameTaken } => Strings.Get("serverSettings.bots.error.nameTaken"),
            { Error: ErrorCode.BotLimitReached } => Strings.Get("serverSettings.bots.error.limit"),
            { Error: ErrorCode.UserNotFound } => Strings.Get("serverSettings.users.error.userNotFound"),
            _ => Strings.Format("serverSettings.users.error.failed", ("reason", result))
        };
        if (message != null)
            await _showMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.Ok, message, Strings.Get(actionKey)));
    }

    private void Remove(int botId)
    {
        if (Bots.FirstOrDefault(row => row.Bot.Id == botId) is not { } row)
            return;
        Bots.Remove(row);
        this.RaisePropertyChanged(nameof(IsEmpty));
    }

    // Raised on the transport thread
    private void OnUserUpdated(UserModel user) => Dispatcher.UIThread.Post(() =>
    {
        var index = Bots.ToList().FindIndex(row => row.Bot.Id == user.Id);
        if (index >= 0)
            Bots[index] = Row(user);
    });

    // Raised on the transport thread
    private void OnUserDeleted(int userId) => Dispatcher.UIThread.Post(() => Remove(userId));

    private void OnPermissionsChanged(CoordinatorPermissions permissions) => Dispatcher.UIThread.Post(() =>
    {
        var could = CanManage;
        CanManage = permissions.Has(Permission.ManageBots);
        if (could != CanManage)
            _ = LoadAsync();
    });

    public void Dispose()
    {
        _coordinatorSession.UserUpdated -= OnUserUpdated;
        _coordinatorSession.UserDeleted -= OnUserDeleted;
        _coordinatorSession.PermissionsChanged -= OnPermissionsChanged;
        _isBusy.Dispose();
        CreateCommand.Dispose();
        NewCodeCommand.Dispose();
        DeleteCommand.Dispose();
        HideCodeCommand.Dispose();
    }
}
