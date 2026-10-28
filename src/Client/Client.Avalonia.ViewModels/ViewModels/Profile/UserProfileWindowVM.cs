using Client.Services;
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

    public bool IsBusy => _isBusy.Value;

    /// <summary>Our own profile, or another user whose roles rank below ours and that we may manage.</summary>
    public bool CanEdit { get; }

    /// <summary>
    /// The roles the user has, and the ones the current user may give them, highest first.
    /// </summary>
    public ObservableCollection<UserRoleVM> Roles { get; } = new();

    public bool HasRoles => Roles.Count > 0;

    public bool IsChangingRole => _isChangingRole.Value;

    public string? RoleError
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<UserRoleVM, RxVoid> ToggleRoleCommand { get; }

    public int UserId { get; }
    public bool IsCurrentUser { get; }
    public string FirstConnected { get; }
    public string LastConnected { get; }
    public string NameHint => UsernameRules.Hint;

    public Interaction<RxVoid, RxVoid> CloseWindow { get; } = new();

    /// <summary>
    /// Asks the view to move the focus into the name field once it shows.
    /// </summary>
    public Interaction<RxVoid, RxVoid> FocusName { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> EditCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ResetAvatarCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelEditCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }

    public UserProfileWindowVM(UserModel user, bool isCurrentUser, IAvatarService avatars,
        ReactiveCommand<(int UserId, string Username), RequestResult> changeUsernameCommand,
        ReactiveCommand<PreparedAvatar, (RequestResult Result, string? AvatarUrl)> setAvatarCommand,
        ReactiveCommand<int, RequestResult> resetAvatarCommand,
        CoordinatorPermissions permissions,
        ReactiveCommand<(int UserId, int RoleId, bool Has), RequestResult<IReadOnlyList<int>>>
            setUserRoleCommand)
    {
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
        FirstConnected = FormatTime(user.FirstConnectionTime);
        LastConnected = FormatTime(user.LastConnectionTime);

        EditCommand = ReactiveCommand.CreateFromTask(StartEditingAsync,
            this.WatchProperty(nameof(IsEditing), vm => vm.IsEditing).Select(editing => !editing && CanEdit));
        ToggleRoleCommand = ReactiveCommand.CreateFromTask<UserRoleVM>(ToggleRoleAsync);
        _isChangingRole = ToggleRoleCommand.IsExecuting.ToProperty(this, nameof(IsChangingRole));

        var hasChanges = this.WatchProperties(nameof(Name), nameof(Username), nameof(IsEditing), nameof(IsAvatarReset))
            .CombineLatest(AvatarPicker.WatchProperty(nameof(AvatarPickerVM.Avatar), picker => picker.Avatar),
                (vm, avatar) => vm.IsEditing && UsernameRules.Validate(vm.Name) == null
                    && (vm.Name.Trim() != vm.Username || vm.IsAvatarReset || avatar != null));
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, hasChanges);

        var notBusy = SaveCommand.IsExecuting.Select(busy => !busy);
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

    private static string FormatTime(DateTime time)
    {
        if (time == default)
            return Strings.Get("common.unknown");

        var local = time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time;
        return Strings.FormatDateTime(local);
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _isBusy.Dispose();
        _isChangingRole.Dispose();
        ToggleRoleCommand.Dispose();
        AvatarPicker.Dispose();
    }
}
