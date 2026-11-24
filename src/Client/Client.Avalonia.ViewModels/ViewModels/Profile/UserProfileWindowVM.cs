using Client.Services;
using Client.Application.Ports.Repositories;
using Client.Avalonia.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using Client.Core.Coordinator;
using Client.Core.Users;
using Client.ViewModels.Roles;
using Client.ViewModels.Sessions.List;
using Shared.Enums;
using Shared.Enums.User;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Profile;

/// <summary>
/// The dialog with the profile of a user, which can also rename them. In our own profile
/// an avatar can be picked as well; other users can only get theirs reset, and only by roles that manage users.
/// It also lists the roles of the user and gives or takes those the current user may manage.
/// </summary>
public sealed class UserProfileWindowVM : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly ObservableAsPropertyHelper<bool> _isBusy;
    private readonly ReactiveCommand<(int UserId, string Username), RequestResult> _changeUsernameCommand;
    private readonly ReactiveCommand<PreparedAvatar, (RequestResult Result, string? AvatarUrl)> _setAvatarCommand;
    private readonly ReactiveCommand<int, RequestResult> _resetAvatarCommand;
    private readonly ReactiveCommand<(int UserId, int RoleId, bool Has),
        RequestResult<IReadOnlyList<int>>> _setUserRoleCommand;
    private readonly ObservableAsPropertyHelper<bool> _isChangingRole;

    /// <summary>
    /// The name the server knows, shown while not editing.
    /// </summary>
    public string Username
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The name typed while editing.
    /// </summary>
    public string Name
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? NameError
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsEditing
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Set while editing when the avatar goes back to the default one on save.
    /// </summary>
    public bool IsAvatarReset
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(ShownAvatarUrl));
        }
    }

    /// <summary>
    /// The avatar the server knows.
    /// </summary>
    /// <summary>The status of the user when the profile was opened.</summary>
    public UserStatus Status { get; }
    public string StatusText => UserStatusText.Of(Status);

    public string? AvatarUrl
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(ShownAvatarUrl));
        }
    }

    /// <summary>
    /// The avatar to show: none while it waits to be reset on save.
    /// </summary>
    public string? ShownAvatarUrl => IsAvatarReset ? null : AvatarUrl;

    public AvatarPickerVM AvatarPicker { get; }
    public UserNoteVM Note { get; }

    public bool IsBusy => _isBusy.Value;

    /// <summary>Our own profile, or another user whose roles rank below ours and that we may manage.</summary>
    public bool CanEdit { get; }

    /// <summary>
    /// The roles the user has, and the ones the current user may give them, highest first.
    /// </summary>
    public ObservableCollection<UserRoleVM> Roles { get; } = new();

    public bool HasRoles => Roles.Count > 0;
    public IEnumerable<UserRoleVM> AssignedRoles => Roles.Where(role => role.IsAssigned);
    public IEnumerable<UserRoleVM> ManageableRoles => Roles.Where(role => role.CanChange);
    public bool CanManageRoles => Roles.Any(role => role.CanChange);

    public bool IsChangingRole => _isChangingRole.Value;

    public string? RoleError
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<UserRoleVM, RxVoid> ToggleRoleCommand { get; }

    public int UserId { get; }
    public bool IsCurrentUser { get; }
    public string LastConnected { get; }

    /// <summary>Online from a phone: the status badge is a phone.</summary>
    public bool IsMobile { get; }

    /// <summary>The server tells the device: the user is online and not invisible, or it is our own profile.</summary>
    public bool HasDevice { get; }

    /// <summary>The system of the device and its architecture, "Windows 11 (26100) · x64".</summary>
    public string? DeviceSystem { get; }

    /// <summary>The icon of the system of the device; null for a system without one.</summary>
    public string? DeviceIconKey { get; }

    public string? DeviceAppVersion { get; }
    public string NameHint => UsernameRules.Hint;

    public Interaction<RxVoid, RxVoid> CloseWindow { get; } = new();

    /// <summary>
    /// Asks the view to move the focus into the name field once it shows.
    /// </summary>
    public Interaction<RxVoid, RxVoid> FocusName { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> EditCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ChangeAvatarCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ResetAvatarCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelEditCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> WriteMessageCommand { get; }
    public bool WantsDirectChat { get; private set; }
    public bool CanWriteMessage => !IsCurrentUser;

    /// <summary>Asked to call the user once the profile is closed.</summary>
    public bool WantsCall { get; private set; }
    public bool CanCall => !IsCurrentUser && Client.Core.Media.MediaRuntime.IsEnabled;
    public ReactiveCommand<RxVoid, RxVoid> CallCommand { get; }

    /// <summary>
    /// The direct chat with the user, set before the profile shows: its notifications are muted from here.
    /// Without one there is nothing to mute yet.
    /// </summary>
    public ChatSessionVM? DirectChat
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(CanMuteChat));
            this.RaisePropertyChanged(nameof(IsChatMuted));
            this.RaisePropertyChanged(nameof(HasActions));
        }
    }

    public bool CanMuteChat => DirectChat != null && !IsCurrentUser;
    public bool IsChatMuted => DirectChat?.IsMuted == true;
    public ReactiveCommand<RxVoid, RxVoid> ToggleChatMuteCommand { get; }

    /// <summary>The row of buttons under the name has something to show.</summary>
    public bool HasActions => CanWriteMessage || CanCall || CanMuteChat || CanEdit;

    public UserProfileWindowVM(UserModel user, bool isCurrentUser, IAvatarService avatars,
        ReactiveCommand<(int UserId, string Username), RequestResult> changeUsernameCommand,
        ReactiveCommand<PreparedAvatar, (RequestResult Result, string? AvatarUrl)> setAvatarCommand,
        ReactiveCommand<int, RequestResult> resetAvatarCommand,
        CoordinatorPermissions permissions,
        ReactiveCommand<(int UserId, int RoleId, bool Has), RequestResult<IReadOnlyList<int>>>
            setUserRoleCommand, IUserNotesRepository userNotes, string coordinatorInstanceId)
    {
        WriteMessageCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            WantsDirectChat = true;
            await CloseWindow.Handle(RxVoid.Default);
        });
        CallCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            WantsCall = true;
            await CloseWindow.Handle(RxVoid.Default);
        });
        ToggleChatMuteCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (DirectChat is not { } chat) return;
            await chat.ToggleMuteCommand.Execute(chat.Model.Id);
            this.RaisePropertyChanged(nameof(IsChatMuted));
        });
        Note = new UserNoteVM(userNotes, coordinatorInstanceId, user.Id);
        _changeUsernameCommand = changeUsernameCommand;
        _setUserRoleCommand = setUserRoleCommand;
        CanEdit = isCurrentUser || (permissions.Has(Permission.ManageUsers) && permissions.Outranks(user.RoleIds));
        foreach (var role in permissions.Roles.Where(role => !role.IsEveryone).Reverse())
        {
            var isAssigned = user.RoleIds.Contains(role.Id);
            var canChange = permissions.CanAssign(role, isCurrentUser, user.RoleIds);
            if (isAssigned || canChange)
                Roles.Add(new UserRoleVM(role, isAssigned, canChange));
        }
        _setAvatarCommand = setAvatarCommand;
        _resetAvatarCommand = resetAvatarCommand;
        AvatarPicker = new AvatarPickerVM(avatars);
        AvatarUrl = user.AvatarUrl;
        Status = user.Status;
        UserId = user.Id;
        IsCurrentUser = isCurrentUser;
        Username = user.Username;
        Name = user.Username;
        LastConnected = FormatTime(user.LastConnectionTime);
        IsMobile = user.IsMobile;
        if (user.Device is { } device)
        {
            HasDevice = true;
            DeviceSystem = string.Join(" · ", new[] { device.OsVersion ?? device.Platform, device.Architecture }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
            DeviceIconKey = PlatformIconKey(device.Platform);
            DeviceAppVersion = device.AppVersion;
        }

        EditCommand = ReactiveCommand.CreateFromTask(StartEditingAsync,
            this.WatchProperty(nameof(IsEditing), vm => vm.IsEditing).Select(editing => !editing && CanEdit));
        ChangeAvatarCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await AvatarPicker.ChooseAvatarCommand.Execute();
            if (!IsEditing && (AvatarPicker.Picked != null || AvatarPicker.AvatarError != null))
            {
                Name = Username;
                NameError = null;
                IsEditing = true;
            }
        }, Observable.Return(IsCurrentUser));
        ToggleRoleCommand = ReactiveCommand.CreateFromTask<UserRoleVM>(ToggleRoleAsync);
        _isChangingRole = ToggleRoleCommand.IsExecuting.ToProperty(this, nameof(IsChangingRole));

        var hasChanges = this.WatchProperties(nameof(Name), nameof(Username), nameof(IsEditing), nameof(IsAvatarReset))
            .CombineLatest(AvatarPicker.WatchProperty(nameof(AvatarPickerVM.Avatar), picker => picker.Avatar),
                (vm, avatar) => vm.IsEditing && UsernameRules.Validate(vm.Name) == null
                    && (vm.Name.Trim() != vm.Username || vm.IsAvatarReset || avatar != null));
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, hasChanges);

        var notBusy = SaveCommand.IsExecuting.CombineLatest(
            Note.WatchProperty(nameof(UserNoteVM.IsBusy), note => note.IsBusy),
            (profileBusy, noteBusy) => !profileBusy && !noteBusy);
        ResetAvatarCommand = ReactiveCommand.Create(ResetAvatar,
            this.WatchProperty(nameof(IsAvatarReset), vm => vm.IsAvatarReset)
                .CombineLatest(AvatarPicker.WatchProperty(nameof(AvatarPickerVM.Avatar), picker => picker.Avatar),
                    this.WatchProperty(nameof(AvatarUrl), vm => vm.AvatarUrl),
                    (reset, avatar, current) => avatar != null || (!reset && current != null)));
        CancelEditCommand = ReactiveCommand.Create(StopEditing, notBusy);
        CloseCommand = ReactiveCommand.CreateFromTask(async () => { await CloseWindow.Handle(RxVoid.Default); },
            notBusy);

        _isBusy = SaveCommand.IsExecuting.ToProperty(this, nameof(IsBusy));

        // A picked image replaces the reset
        _disposables.Add(AvatarPicker.WatchProperty(nameof(AvatarPickerVM.Avatar), picker => picker.Avatar)
            .Where(avatar => avatar != null)
            .Subscribe(_ => IsAvatarReset = false));

        _disposables.Add(this.WatchProperty(nameof(Name), vm => vm.Name).Skip(1)
            .Where(_ => IsEditing)
            .Subscribe(name => NameError = UsernameRules.Validate(name)));
    }

    private async Task ToggleRoleAsync(UserRoleVM role)
    {
        RoleError = null;
        if (!role.CanChange)
            return;

        var (result, roleIds) = await _setUserRoleCommand.Execute((UserId, role.Role.Id, !role.IsAssigned));
        RoleError = RoleResultMessages.Describe(result);

        if (roleIds == null)
        {
            // Shows the box as it was
            role.IsAssigned = role.IsAssigned;
            return;
        }

        foreach (var each in Roles)
            each.IsAssigned = roleIds.Contains(each.Role.Id);

        this.RaisePropertyChanged(nameof(AssignedRoles));
    }

    private async Task StartEditingAsync()
    {
        Name = Username;
        NameError = null;
        IsEditing = true;

        await FocusName.Handle(RxVoid.Default);
    }

    private void ResetAvatar()
    {
        AvatarPicker.Clear();
        IsAvatarReset = AvatarUrl != null;
    }

    private void StopEditing()
    {
        IsEditing = false;
        IsAvatarReset = false;
        AvatarPicker.Clear();
        NameError = null;
        Name = Username;
    }

    private async Task SaveAsync()
    {
        NameError = UsernameRules.Validate(Name);

        if (NameError != null)
            return;

        var name = Name.Trim();

        if (name != Username)
        {
            var result = await _changeUsernameCommand.Execute((UserId, name));

            NameError = UsernameRules.DescribeResult(result);

            if (!result.IsSuccess)
                return;

            Username = name;
        }

        if (AvatarPicker.Picked is { } picked && IsCurrentUser)
        {
            var (result, avatarUrl) = await _setAvatarCommand.Execute(picked);

            AvatarPicker.ShowResult(result);

            if (!result.IsSuccess)
                return;

            AvatarUrl = avatarUrl;
        }
        else if (IsAvatarReset)
        {
            var result = await _resetAvatarCommand.Execute(UserId);

            AvatarPicker.ShowResult(result);

            if (!result.IsSuccess)
                return;

            AvatarUrl = null;
        }

        StopEditing();
    }

    /// <summary>The icon of a system the server names, null for one without an icon.</summary>
    internal static string? PlatformIconKey(string platform) => platform switch
    {
        "Browser" => "BrowserIcon",
        "Windows" => "WindowsIcon",
        "Linux" => "LinuxIcon",
        "macOS" => "MacOSIcon",
        "Android" => "AndroidIcon",
        "iOS" => "IPhoneIcon",
        _ => null
    };

    private static string FormatTime(DateTimeOffset time)
    {
        if (time == default)
            return Strings.Get("common.unknown");

        return Strings.FormatDateTime(time.ToLocalTime());
    }

    public void Dispose()
    {
        WriteMessageCommand.Dispose();
        _disposables.Dispose();
        _isBusy.Dispose();
        _isChangingRole.Dispose();
        ToggleRoleCommand.Dispose();
        ChangeAvatarCommand.Dispose();
        AvatarPicker.Dispose();
        Note.Dispose();
    }
}
