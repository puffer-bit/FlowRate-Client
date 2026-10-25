using System;
using System.Reactive.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Core.Users;
using Client.Services;
using Client.ViewModels.Dialogs;
using Client.ViewModels.Profile;
using ReactiveUI;
using ReactiveUI.Primitives;
using Shared.Enums;
using Shared.Enums.Coordinator;
using Shared.Enums.User;
using Result = Client.ViewModels.Dialogs.Result;

namespace Client.ViewModels.Main;

/// <summary>
/// What can be done to users of the connected coordinator: the profiles, the status, poking a peer of
/// a hybrid session or kicking it out, and banning a user.
/// </summary>
public sealed class UserActions
{
    public ReactiveCommand<RxVoid, RxVoid> EditProfileCommand { get; }
    public ReactiveCommand<int, RxVoid> ViewProfileCommand { get; }
    public ReactiveCommand<UserStatus, RxVoid> SetStatusCommand { get; }
    public ReactiveCommand<(int SessionId, int UserId), RxVoid> KickPeerCommand { get; }

    /// <summary>Bans a user from the coordinator, once the user agrees to it.</summary>
    public ReactiveCommand<(int UserId, string Name), RxVoid> BanUserCommand { get; }

    /// <summary>Lets a peer waiting for the host into a hybrid session, or turns it away, which takes it out.</summary>
    public ReactiveCommand<(int SessionId, int UserId, bool IsApproved), RxVoid> AdmitPeerCommand { get; }

    /// <summary>Pokes a peer of the hybrid session we are in, with a message if there is one.</summary>
    public ReactiveCommand<(int SessionId, int UserId, string? Message), RxVoid> PokePeerCommand { get; }

    private readonly ReactiveCommand<string, ChangeUsernameResult> _changeUsernameCommand;
    private readonly ReactiveCommand<(int UserId, string Username), ChangeUsernameResult> _changeUserUsernameCommand;
    private readonly ReactiveCommand<PreparedAvatar, (SetAvatarResult Result, string? AvatarUrl)> _setAvatarCommand;
    private readonly ReactiveCommand<int, SetAvatarResult> _resetAvatarCommand;
    private readonly ReactiveCommand<(int UserId, int RoleId, bool Has), (CoordinatorRoleResult Result, IReadOnlyList<int>? RoleIds)>
        _setUserRoleCommand;

    private readonly Func<ICoordinatorSession?> _session;
    private readonly Func<UserModel?> _currentUser;
    private readonly IAvatarService _avatars;
    private readonly INotificationService _notifications;
    private readonly AccessVM _access;
    private readonly Interaction<MessageBoxVM, Result> _showMessageBox;
    private readonly Interaction<EditProfileWindowVM, RxVoid> _showEditProfile;
    private readonly Interaction<UserProfileWindowVM, RxVoid> _showUserProfile;

    /// <param name="session">The connected coordinator session; the commands run only while there is one.</param>
    /// <param name="isConnected">Whether the commands that open something can run.</param>
    public UserActions(Func<ICoordinatorSession?> session, Func<UserModel?> currentUser, IObservable<bool> isConnected,
        IAvatarService avatars, INotificationService notifications, AccessVM access,
        Interaction<MessageBoxVM, Result> showMessageBox,
        Interaction<EditProfileWindowVM, RxVoid> showEditProfile,
        Interaction<UserProfileWindowVM, RxVoid> showUserProfile)
    {
        _session = session;
        _currentUser = currentUser;
        _avatars = avatars;
        _notifications = notifications;
        _access = access;
        _showMessageBox = showMessageBox;
        _showEditProfile = showEditProfile;
        _showUserProfile = showUserProfile;

        EditProfileCommand = ReactiveCommand.CreateFromTask(EditProfile, isConnected);
        ViewProfileCommand = ReactiveCommand.CreateFromTask<int>(ViewProfile, isConnected);
        SetStatusCommand = ReactiveCommand.CreateFromTask<UserStatus>(SetStatus, isConnected);
        KickPeerCommand = ReactiveCommand.CreateFromTask<(int SessionId, int UserId)>(KickPeer, isConnected);
        BanUserCommand = ReactiveCommand.CreateFromTask<(int UserId, string Name)>(BanUser, isConnected);
        AdmitPeerCommand = ReactiveCommand.CreateFromTask<(int SessionId, int UserId, bool IsApproved)>(AdmitPeer,
            isConnected);
        PokePeerCommand = ReactiveCommand.CreateFromTask<(int SessionId, int UserId, string? Message)>(PokePeer,
            isConnected);

        _changeUsernameCommand = ReactiveCommand.CreateFromTask<string, ChangeUsernameResult>(
            name => Session.ChangeUsername(name));
        _changeUserUsernameCommand = ReactiveCommand.CreateFromTask<(int UserId, string Username), ChangeUsernameResult>(
            change => Session.ChangeUsername(change.UserId, change.Username));
        _setAvatarCommand = ReactiveCommand.CreateFromTask<PreparedAvatar, (SetAvatarResult Result, string? AvatarUrl)>(
            async avatar =>
            {
                var (result, user) = await Session.SetAvatar(avatar.Encoded, avatar.MimeType);
                return (result, user?.AvatarUrl);
            });
        _resetAvatarCommand = ReactiveCommand.CreateFromTask<int, SetAvatarResult>(
            userId => Session.ResetAvatar(userId));
        _setUserRoleCommand = ReactiveCommand
            .CreateFromTask<(int UserId, int RoleId, bool Has), (CoordinatorRoleResult Result, IReadOnlyList<int>? RoleIds)>(
                change => Session.SetUserRole(change.UserId, change.RoleId, change.Has));
    }

    private ICoordinatorSession Session => _session() ?? throw new InvalidOperationException("Not connected");

    private async Task EditProfile()
    {
        var user = _currentUser();
        using var vm = new EditProfileWindowVM(user?.Username ?? string.Empty, user?.Id, user?.AvatarUrl, _avatars,
            _changeUsernameCommand, _setAvatarCommand, _resetAvatarCommand);
        await _showEditProfile.Handle(vm);
    }

    private async Task ViewProfile(int userId)
    {
        var user = await Session.GetUser(userId);

        if (user == null)
        {
            await _showMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.Ok,
                Strings.Get("main.profile.userNotReturned"), Strings.Get("main.profile.title")));
            return;
        }

        using var vm = new UserProfileWindowVM(user, user.Id == _currentUser()?.Id, _avatars, _changeUserUsernameCommand,
            _setAvatarCommand, _resetAvatarCommand, _access.Permissions, _setUserRoleCommand);
        await _showUserProfile.Handle(vm);
    }

    // The card follows the current user, which the server sends back once the status is set
    private async Task SetStatus(UserStatus status)
    {
        var result = await Session.SetStatus(status);

        // A rejection is reported on its own
        if (result is SetUserStatusResult.NoError or SetUserStatusResult.Rejected)
            return;

        _notifications.Show(NotificationType.Error, Strings.Get("profile.status.failed"),
            Strings.Format("profile.status.failed.text", ("code", (int)result)));
    }

    private async Task KickPeer((int SessionId, int UserId) target)
    {
        var result = await Session.KickFromHybridSession(target.SessionId, target.UserId);

        // A rejection is reported on its own; a peer that is already gone needs no word
        if (result is LeaveSessionResult.NoError or LeaveSessionResult.Rejected or LeaveSessionResult.PeerNotExists)
            return;

        _notifications.Show(NotificationType.Error, Strings.Get("peer.kick.failed.title"),
            Strings.Format("peer.kick.failed.text", ("code", (int)result)));
    }

    private async Task PokePeer((int SessionId, int UserId, string? Message) target)
    {
        var result = await Session.PokeInHybridSession(target.SessionId, target.UserId, target.Message);

        // A rejection is reported on its own; a peer that is already gone needs no word
        if (result is PokeResult.NoError or PokeResult.Rejected or PokeResult.PeerNotExists)
            return;

        _notifications.Show(NotificationType.Error, Strings.Get("peer.poke.failed.title"),
            Strings.Format("peer.poke.failed.text", ("code", (int)result)));
    }

    // The user leaves its sessions and the coordinator with the ban, as from the server settings
    private async Task BanUser((int UserId, string Name) target)
    {
        if (await _showMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.YesNo,
                Strings.Format("serverSettings.users.ban.question", ("name", target.Name)),
                Strings.Get("serverSettings.users.ban.title"))) != Result.Yes)
            return;

        var result = await Session.SetUserState(target.UserId, UserState.Banned);

        // A rejection is reported on its own
        if (result is ModerateUserResult.NoError or ModerateUserResult.Rejected)
            return;

        _notifications.Show(NotificationType.Error, Strings.Get("peer.ban.failed.title"),
            Strings.Format("peer.kick.failed.text", ("code", (int)result)));
    }

    private async Task AdmitPeer((int SessionId, int UserId, bool IsApproved) target)
    {
        // A peer that is gone, or was decided on by someone else meanwhile, needs no word
        var code = target.IsApproved
            ? await Session.ApproveInHybridSession(target.SessionId, target.UserId) switch
            {
                ApproveUserSessionResult.NoError or ApproveUserSessionResult.PeerNotExists => (int?)null,
                var result => (int)result
            }
            : await Session.RejectInHybridSession(target.SessionId, target.UserId) switch
            {
                RejectUserSessionResult.NoError or RejectUserSessionResult.PeerNotExists
                    or RejectUserSessionResult.AlreadyApproved => (int?)null,
                var result => (int)result
            };
        if (code is not { } failure)
            return;

        _notifications.Show(NotificationType.Error, Strings.Get("peer.approve.failed"),
            Strings.Format("peer.kick.failed.text", ("code", failure)));
    }
}
