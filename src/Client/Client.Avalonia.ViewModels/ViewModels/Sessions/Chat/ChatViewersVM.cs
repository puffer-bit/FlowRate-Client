using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using Client.Core.Users;
using Client.Services;
using ReactiveUI;
using Shared.Enums.User;
using Shared.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

public sealed class ChatViewerVM : ReactiveObject, IDisposable
{
    private UserModel _user;
    private readonly ChatViewersVM _owner;
    public int Id => _user.Id;
    public string Username => _user.Username;
    public string? AvatarUrl => _user.AvatarUrl;
    public UserStatus Status => _user.Status;
    public bool IsOnline => Status is UserStatus.Online or UserStatus.Away or UserStatus.DoNotDisturb;
    public bool ShowOnlineHeader { get; internal set => this.RaiseAndSetIfChanged(ref field, value); }
    public bool ShowOfflineHeader { get; internal set => this.RaiseAndSetIfChanged(ref field, value); }
    public bool IsBanned => _user.State == UserState.Banned;
    public bool IsFrozen => _user.State == UserState.Suspended;
    public int? RoleColor => _owner.GetRoleColor(_user);
    public ReactiveCommand<RxVoid, RxVoid> ViewProfile { get; }

    public bool CanMention => _owner.CanMention;
    public bool CanBan => _owner.CanBan(_user);
    public bool CanUnban => _owner.CanUnban(_user);
    public bool CanModerate => CanBan || CanUnban;
    public ReactiveCommand<RxVoid, RxVoid> Mention { get; }
    public ReactiveCommand<RxVoid, RxVoid> Ban { get; }
    public ReactiveCommand<RxVoid, RxVoid> Unban { get; }

    public ChatViewerVM(UserModel user, ReactiveCommand<int, RxVoid> viewProfile, ChatViewersVM owner)
    {
        (_user, _owner) = (user, owner);
        // Launch the shared profile action without binding the whole list to its dialog lifetime.
        ViewProfile = ReactiveCommand.Create(() => { viewProfile.Execute(Id).Subscribe(); });
        Mention = ReactiveCommand.Create(() => { if (CanMention) _owner.MentionUser(_user); });
        Ban = ReactiveCommand.Create(() => { if (CanBan) _owner.BanUser(_user); });
        Unban = ReactiveCommand.Create(() => { if (CanUnban) _owner.UnbanUser(_user); });
    }

    public void RefreshAccess()
    {
        this.RaisePropertyChanged(nameof(CanMention));
        this.RaisePropertyChanged(nameof(CanBan));
        this.RaisePropertyChanged(nameof(CanUnban));
        this.RaisePropertyChanged(nameof(CanModerate));
        this.RaisePropertyChanged(nameof(RoleColor));
    }

    public void Dispose()
    {
        ViewProfile.Dispose();
        Mention.Dispose();
        Ban.Dispose();
        Unban.Dispose();
    }

    public void Update(UserModel user)
    {
        _user = user;
        this.RaisePropertyChanged(nameof(Username));
        this.RaisePropertyChanged(nameof(AvatarUrl));
        this.RaisePropertyChanged(nameof(Status));
        this.RaisePropertyChanged(nameof(IsBanned));
        this.RaisePropertyChanged(nameof(IsFrozen));
        RefreshAccess();
    }
}

/// <summary>An initial audience snapshot followed exclusively by server deltas while the panel is open.</summary>
public sealed class ChatViewersVM : ReactiveObject, IDisposable
{
    private readonly ICoordinatorSession _session;
    private readonly int _chatId;
    private readonly ChatComposerVM? _composer;
    private readonly ReactiveCommand<(int UserId, string Name), RxVoid>? _banUser;
    private readonly ReactiveCommand<int, RxVoid>? _unbanUser;
    private readonly int _currentUserId;
    private readonly ReactiveCommand<int, RxVoid> _viewProfile;
    private readonly List<ChatViewersChangedEvent> _pending = [];
    private Guid _subscriptionId;
    private bool _open;
    private bool _disposed;

    public bool IsOpen
    {
        get => _open;
        set { if (value) Open(); else Close(); }
    }

    public ObservableCollection<ChatViewerVM> Users { get; } = [];
    public bool GroupOnlineUsers
    {
        get;
        set
        {
            if (field == value) return;
            this.RaiseAndSetIfChanged(ref field, value);
            SortUsers();
        }
    }
    public ReactiveCommand<RxVoid, RxVoid> ToggleGroupOnlineUsers { get; }
    public bool IsLoading { get; private set => this.RaiseAndSetIfChanged(ref field, value); }
    public bool HasError { get; private set => this.RaiseAndSetIfChanged(ref field, value); }
    public string Caption => Strings.Format("chat.viewers.count", ("count", Users.Count.ToString()));

    public ChatViewersVM(int chatId, ICoordinatorSession session, ReactiveCommand<int, RxVoid> viewProfile,
        ChatComposerVM? composer = null, ReactiveCommand<(int UserId, string Name), RxVoid>? banUser = null,
        int currentUserId = 0, ReactiveCommand<int, RxVoid>? unbanUser = null)
    {
        (_chatId, _session, _viewProfile) = (chatId, session, viewProfile);
        ToggleGroupOnlineUsers = ReactiveCommand.Create(() => { GroupOnlineUsers = !GroupOnlineUsers; });
        _unbanUser = unbanUser;
        (_composer, _banUser, _currentUserId) = (composer, banUser, currentUserId);
        session.PermissionsChanged += OnPermissionsChanged;
        if (composer != null) composer.PropertyChanged += OnComposerChanged;
        session.ChatViewersChanged += OnChanged;
        session.StateChanged += OnStateChanged;
    }

    internal bool CanMention => _composer is { CanWrite: true, IsConnected: true };

    internal int? GetRoleColor(UserModel user) => _session.Permissions.Roles
        .LastOrDefault(role => role.Color != null && user.RoleIds.Contains(role.Id))?.Color;

    internal bool CanBan(UserModel user) => _banUser != null && user.State != UserState.Banned && CanModerate(user);

    internal bool CanUnban(UserModel user) => _unbanUser != null && user.State == UserState.Banned && CanModerate(user);

    private bool CanModerate(UserModel user) => user.Id != _currentUserId
        && _session.ConnectionStatus == CoordinatorState.Connected
        && _session.Permissions.Has(Permission.BanMembers) && _session.Permissions.Outranks(user.RoleIds);

    internal void UnbanUser(UserModel user) => _unbanUser?.Execute(user.Id).Subscribe();

    internal void MentionUser(UserModel user) => _composer?.MentionUser(user.Id, user.Username);
    internal void BanUser(UserModel user) => _banUser?.Execute((user.Id, user.Username)).Subscribe();

    private void RefreshAccess()
    {
        foreach (var row in Users) row.RefreshAccess();
    }

    private void OnPermissionsChanged(CoordinatorPermissions _) => Dispatcher.UIThread.Post(RefreshAccess);
    private void OnComposerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ChatComposerVM.CanWrite) or nameof(ChatComposerVM.IsConnected))
            Dispatcher.UIThread.Post(RefreshAccess);
    }

    private void ClearRows()
    {
        foreach (var row in Users) row.Dispose();
        Users.Clear();
    }

    public void Open()
    {
        if (_disposed || _open) return;
        _open = true;
        this.RaisePropertyChanged(nameof(IsOpen));
        _ = SubscribeAsync();
    }

    private async Task SubscribeAsync()
    {
        var subscriptionId = _subscriptionId = Guid.NewGuid();
        ClearRows();
        _pending.Clear();
        IsLoading = true;
        HasError = false;
        UpdateTexts();
        var result = await _session.SetChatViewersSubscription(_chatId, subscriptionId, true);
        await UiThread.RunAsync(() =>
        {
            if (!_open || _disposed || _subscriptionId != subscriptionId) return;
            IsLoading = false;
            HasError = !result.IsSuccess;
            if (!result.IsSuccess) return;
            Apply(result.Value, []);
            foreach (var delta in _pending) Apply(delta.Upserted, delta.Removed);
            _pending.Clear();
        });
    }

    private void OnChanged(ChatViewersChangedEvent delta) => Dispatcher.UIThread.Post(() =>
    {
        if (!_open || _disposed || delta.ChatId != _chatId || delta.SubscriptionId != _subscriptionId) return;
        // A delta can arrive before the snapshot continuation gets its turn on the UI thread.
        if (IsLoading) _pending.Add(delta);
        else Apply(delta.Upserted, delta.Removed);
    });

    private void Apply(IReadOnlyList<UserModel> upserted, IReadOnlyList<int> removed)
    {
        foreach (var id in removed)
            if (Users.FirstOrDefault(row => row.Id == id) is { } row)
            {
                Users.Remove(row);
                row.Dispose();
            }
        foreach (var user in upserted)
        {
            var row = Users.FirstOrDefault(item => item.Id == user.Id);
            if (row == null)
            {
                row = new ChatViewerVM(user, _viewProfile, this);
                Users.Add(row);
            }
            else row.Update(user);
        }
        SortUsers();
        UpdateTexts();
    }

    private void SortUsers()
    {
        var ordered = Users.OrderBy(row => GroupOnlineUsers && !row.IsOnline)
            .ThenBy(row => row.Username, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Id).ToArray();
        for (int index = 0; index < ordered.Length; index++)
        {
            int oldIndex = Users.IndexOf(ordered[index]);
            if (oldIndex != index) Users.Move(oldIndex, index);
            var startsGroup = index == 0 || ordered[index - 1].IsOnline != ordered[index].IsOnline;
            ordered[index].ShowOnlineHeader = GroupOnlineUsers && startsGroup && ordered[index].IsOnline;
            ordered[index].ShowOfflineHeader = GroupOnlineUsers && startsGroup && !ordered[index].IsOnline;
        }
    }

    private void OnStateChanged(CoordinatorState state) => Dispatcher.UIThread.Post(() =>
    {
        if (!_open || _disposed) return;
        RefreshAccess();
        if (state == CoordinatorState.Connected) _ = SubscribeAsync();
        else
        {
            _subscriptionId = Guid.Empty;
            ClearRows();
            _pending.Clear();
            IsLoading = false;
            HasError = true;
            UpdateTexts();
        }
    });

    public void UpdateTexts() => this.RaisePropertyChanged(nameof(Caption));

    public void Close()
    {
        if (!_open) return;
        _open = false;
        this.RaisePropertyChanged(nameof(IsOpen));
        var id = _subscriptionId;
        _subscriptionId = Guid.Empty;
        _pending.Clear();
        ClearRows();
        if (id != Guid.Empty && _session.ConnectionStatus == CoordinatorState.Connected)
            _ = _session.SetChatViewersSubscription(_chatId, id, false);
    }

    public void Dispose()
    {
        Close();
        _disposed = true;
        ToggleGroupOnlineUsers.Dispose();
        _session.PermissionsChanged -= OnPermissionsChanged;
        if (_composer != null) _composer.PropertyChanged -= OnComposerChanged;
        _session.ChatViewersChanged -= OnChanged;
        _session.StateChanged -= OnStateChanged;
    }
}
