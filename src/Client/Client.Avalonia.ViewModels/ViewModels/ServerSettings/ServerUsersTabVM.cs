using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using Shared.Enums;
using Shared.Enums.User;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using Client.Core.Users;
using Client.ViewModels.Dialogs;
using Result = Client.ViewModels.Dialogs.Result;

namespace Client.ViewModels.ServerSettings;

/// <summary>
/// How many users the server takes, and every user it has. The users are loaded with the settings and follow
/// the changes to them and to the roles while the dialog is open. Each user other than the current one has a menu
/// with what the roles of the current user allow to do to them.
/// </summary>
public sealed class ServerUsersTabVM : ReactiveObject, IDisposable
{
    private readonly ICoordinatorSession _coordinatorSession;
    private readonly Interaction<MessageBoxVM, Result> _showMessageBox;
    private readonly ReactiveCommand<int, RxVoid> _viewProfileCommand;
    private readonly ObservableAsPropertyHelper<bool> _isLoading;
    private readonly ObservableAsPropertyHelper<bool> _isBusy;

    // By id, as the server sent them and as they changed since
    private readonly Dictionary<int, UserModel> _users = new();

    public CoordinatorSettingsFormVM Form { get; }

    /// <summary>The users matching <see cref="Filter"/>, by name.</summary>
    public ObservableCollection<ServerUserRowVM> Users { get; } = new();

    /// <summary>A part of the name of the users to show; all of them when empty.</summary>
    public string? Filter
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            Rebuild();
        }
    }

    public int TotalCount => _users.Count;

    public bool IsLoading => _isLoading.Value;

    /// <summary>A user is being banned, frozen, reinstated or deleted; the list waits meanwhile.</summary>
    public bool IsBusy => _isBusy.Value;

    /// <summary>The limits are the settings of the server; the users can be managed without changing them.</summary>
    public bool CanEditLimits
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Why the users did not come.</summary>
    public string? Error
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<ServerUserRowVM, RxVoid> EditProfileCommand { get; }
    public ReactiveCommand<ServerUserRowVM, RxVoid> ToggleFreezeCommand { get; }
    public ReactiveCommand<ServerUserRowVM, RxVoid> ToggleBanCommand { get; }
    public ReactiveCommand<ServerUserRowVM, RxVoid> DeleteCommand { get; }

    /// <param name="showMessageBox">Asks before a user is banned or deleted, and tells why it failed.</param>
    /// <param name="viewProfileCommand">Opens the profile of a user, where its name and avatar are changed.</param>
    public ServerUsersTabVM(CoordinatorSettingsFormVM form, ICoordinatorSession coordinatorSession,
        Interaction<MessageBoxVM, Result> showMessageBox, ReactiveCommand<int, RxVoid> viewProfileCommand)
    {
        Form = form;
        _coordinatorSession = coordinatorSession;
        _showMessageBox = showMessageBox;
        _viewProfileCommand = viewProfileCommand;
        CanEditLimits = coordinatorSession.Permissions.Has(Permission.ManageCoordinatorSettings);

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync);
        _isLoading = LoadCommand.IsExecuting.ToProperty(this, nameof(IsLoading));

        EditProfileCommand = ReactiveCommand.CreateFromObservable<ServerUserRowVM, RxVoid>(
            row => _viewProfileCommand.Execute(row.User.Id));
        ToggleFreezeCommand = ReactiveCommand.CreateFromTask<ServerUserRowVM>(ToggleFreezeAsync);
        ToggleBanCommand = ReactiveCommand.CreateFromTask<ServerUserRowVM>(ToggleBanAsync);
        DeleteCommand = ReactiveCommand.CreateFromTask<ServerUserRowVM>(DeleteAsync);
        _isBusy = ToggleFreezeCommand.IsExecuting
            .CombineLatest(ToggleBanCommand.IsExecuting, DeleteCommand.IsExecuting,
                (freezing, banning, deleting) => freezing || banning || deleting)
            .ToProperty(this, nameof(IsBusy));

        _coordinatorSession.UserUpdated += OnUserUpdated;
        _coordinatorSession.UserDeleted += OnUserDeleted;
        _coordinatorSession.PermissionsChanged += OnPermissionsChanged;
    }

    private async Task LoadAsync()
    {
        Error = null;

        var (result, users) = await _coordinatorSession.GetAllUsers();
        if (users == null)
        {
            // Rejected is told by the main window
            if (!result.IsRejected)
                Error = result.Error == ErrorCode.TimedOut
                    ? Strings.Get("serverSettings.error.timedOut")
                    : Strings.Format("serverSettings.users.error.notLoaded", ("reason", result));
            return;
        }

        _users.Clear();
        foreach (var user in users)
            _users[user.Id] = user;

        Rebuild();
    }

    private Task ToggleFreezeAsync(ServerUserRowVM row) =>
        SetStateAsync(row, row.IsFrozen ? UserState.Normal : UserState.Suspended,
            row.IsFrozen ? "serverSettings.users.action.unfreeze" : "serverSettings.users.action.freeze");

    private async Task ToggleBanAsync(ServerUserRowVM row)
    {
        if (row.IsBanned)
        {
            await SetStateAsync(row, UserState.Normal, "serverSettings.users.action.unban");
            return;
        }

        if (await _showMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.YesNo,
                Strings.Format("serverSettings.users.ban.question", ("name", row.Username)),
                Strings.Get("serverSettings.users.ban.title"))) != Result.Yes)
            return;

        await SetStateAsync(row, UserState.Banned, "serverSettings.users.action.ban");
    }

    // The new state comes back as an update of the user
    private async Task SetStateAsync(ServerUserRowVM row, UserState state, string actionKey) =>
        await TellFailureAsync(await _coordinatorSession.SetUserState(row.User.Id, state), actionKey);

    private async Task DeleteAsync(ServerUserRowVM row)
    {
        if (await _showMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.YesNo,
                Strings.Format("serverSettings.users.delete.question", ("name", row.Username)),
                Strings.Get("serverSettings.users.delete.title"))) != Result.Yes)
            return;

        // The user leaves the list with the notice of the deletion
        await TellFailureAsync(await _coordinatorSession.DeleteUser(row.User.Id), "serverSettings.users.action.delete");
    }

    private async Task TellFailureAsync(RequestResult result, string actionKey)
    {
        // Rejected is told by the main window
        var message = result switch
        {
            { IsSuccess: true } or { IsRejected: true } => null,
            { Error: ErrorCode.TimedOut } => Strings.Get("serverSettings.error.timedOut"),
            { Error: ErrorCode.UserNotFound } => Strings.Get("serverSettings.users.error.userNotFound"),
            { Error: ErrorCode.CannotTargetSelf } => Strings.Get("serverSettings.users.error.cannotTargetSelf"),
            _ => Strings.Format("serverSettings.users.error.failed", ("reason", result))
        };
        if (message != null)
            await _showMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.Ok, message, Strings.Get(actionKey)));
    }

    // Raised on the transport thread
    private void OnUserUpdated(UserModel user) => Dispatcher.UIThread.Post(() =>
    {
        // Only the users of the list; one registered meanwhile comes with the next load
        if (!_users.ContainsKey(user.Id))
            return;

        _users[user.Id] = user;
        Rebuild();
    });

    // Raised on the transport thread
    private void OnUserDeleted(int userId) => Dispatcher.UIThread.Post(() =>
    {
        if (_users.Remove(userId))
            Rebuild();
    });

    // The names and colors of the roles may have changed, and what the current user may do
    private void OnPermissionsChanged(CoordinatorPermissions permissions) => Dispatcher.UIThread.Post(() =>
    {
        CanEditLimits = permissions.Has(Permission.ManageCoordinatorSettings);
        Rebuild();
    });

    private void Rebuild()
    {
        var filter = Filter?.Trim() ?? string.Empty;
        var permissions = _coordinatorSession.Permissions;
        var roles = _coordinatorSession.Roles.ToDictionary(role => role.Id);
        var currentUserId = _coordinatorSession.GetCurrentUser().Id;

        Users.Clear();
        foreach (var user in _users.Values
                     .Where(user => filter.Length == 0 || Matches(user, filter))
                     .OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase))
        {
            // Highest first, as in the roles tab
            var userRoles = user.RoleIds
                .Select(id => roles.GetValueOrDefault(id))
                .OfType<RoleModel>()
                .OrderByDescending(role => role.Position)
                .ToList();

            // As the server decides: the current user acts only on users below its highest role, never on itself
            var isSelf = user.Id == currentUserId;
            var outranks = !isSelf && permissions.Outranks(user.RoleIds);
            Users.Add(new ServerUserRowVM(user, userRoles, isSelf,
                canEditProfile: outranks && permissions.Has(Permission.ManageUsers),
                canModerate: outranks && permissions.Has(Permission.BanMembers),
                this));
        }

        this.RaisePropertyChanged(nameof(TotalCount));
    }

    private static bool Matches(UserModel user, string filter) =>
        user.Username.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || user.DisplayName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;

    public void Dispose()
    {
        _coordinatorSession.UserUpdated -= OnUserUpdated;
        _coordinatorSession.UserDeleted -= OnUserDeleted;
        _coordinatorSession.PermissionsChanged -= OnPermissionsChanged;
        _isLoading.Dispose();
        _isBusy.Dispose();
        LoadCommand.Dispose();
        EditProfileCommand.Dispose();
        ToggleFreezeCommand.Dispose();
        ToggleBanCommand.Dispose();
        DeleteCommand.Dispose();
    }
}

/// <summary>A user in the list of the users tab, with the commands of the tab for its menu.</summary>
public sealed class ServerUserRowVM(UserModel user, IReadOnlyList<RoleModel> roles, bool isSelf,
    bool canEditProfile, bool canModerate, ServerUsersTabVM tab)
{
    // The menu opens in a popup, out of reach of the tab; each command takes the row
    public ReactiveCommand<ServerUserRowVM, RxVoid> EditProfileCommand => tab.EditProfileCommand;
    public ReactiveCommand<ServerUserRowVM, RxVoid> ToggleFreezeCommand => tab.ToggleFreezeCommand;
    public ReactiveCommand<ServerUserRowVM, RxVoid> ToggleBanCommand => tab.ToggleBanCommand;
    public ReactiveCommand<ServerUserRowVM, RxVoid> DeleteCommand => tab.DeleteCommand;

    public UserModel User { get; } = user;
    public string Username => User.Username;
    public string? AvatarUrl => User.AvatarUrl;
    public UserStatus Status => User.Status;
    public bool IsBanned => User.State == UserState.Banned;
    public bool IsFrozen => User.State == UserState.Suspended;

    /// <summary>Highest first, without the everyone role.</summary>
    public IReadOnlyList<RoleModel> Roles { get; } = roles;

    /// <summary>The current user; nothing can be done to them here.</summary>
    public bool IsSelf { get; } = isSelf;

    public bool CanEditProfile { get; } = canEditProfile;

    /// <summary>The user can be frozen, banned and deleted, and brought back.</summary>
    public bool CanModerate { get; } = canModerate;

    public bool CanFreeze => CanModerate && !IsFrozen;
    public bool CanUnfreeze => CanModerate && IsFrozen;
    public bool CanBan => CanModerate && !IsBanned;
    public bool CanUnban => CanModerate && IsBanned;

    /// <summary>The menu has something in it.</summary>
    public bool HasActions => CanEditProfile || CanModerate;
}
