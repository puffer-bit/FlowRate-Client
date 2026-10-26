using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Users;
using Client.Services;
using Shared.Enums.User;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// The users a chat shows: messages carry only the author id, so names, avatars and statuses are
/// looked up and kept here, with the roles that color the names, together with the users mentioned in the texts.
/// Touched only on the UI thread.
/// </summary>
public sealed class ChatSenderDirectory : IDisposable
{
    private static string UnknownName => Strings.Get("chat.unknownUser");
    private static string DeletedName => Strings.Get("chat.deletedUser");
    // Shown for a mentioned user whose name is still being looked up
    private const string PendingMentionName = "…";

    private readonly Dictionary<int, string> _names = new();
    private readonly Dictionary<int, string?> _avatars = new();
    private readonly Dictionary<int, UserStatus> _statuses = new();
    private readonly Dictionary<int, IReadOnlyList<int>> _roleIds = new();
    private readonly HashSet<int> _resolving = new();
    private readonly HashSet<int> _deleted = new();
    private readonly ICoordinatorSession _session;
    private readonly int _currentUserId;
    private readonly EventSubscriptions _subscriptions = new();

    /// <summary>
    /// Something shown of the users has changed; raised on the UI thread.
    /// </summary>
    public event Action? Changed;

    public ChatSenderDirectory(ICoordinatorSession session, int currentUserId)
    {
        _session = session;
        _currentUserId = currentUserId;
        _subscriptions
            .Add(() => session.UserUpdated += OnUserUpdated, () => session.UserUpdated -= OnUserUpdated)
            .Add(() => session.UserDeleted += OnUserDeleted, () => session.UserDeleted -= OnUserDeleted)
            .Add(() => session.PermissionsChanged += OnPermissionsChanged,
                () => session.PermissionsChanged -= OnPermissionsChanged);
    }

    /// <summary>
    /// Looks up the users that are not known yet, all of them in one request.
    /// </summary>
    public async Task ResolveAsync(HashSet<int> userIds)
    {
        userIds.ExceptWith(_names.Keys);
        userIds.ExceptWith(_resolving);
        if (userIds.Count == 0)
            return;

        _resolving.UnionWith(userIds);
        try
        {
            var users = await _session.GetUsers(userIds);

            await UiThread.RunAsync(() =>
            {
                // Users missing from the result are not remembered, so the next page asks for them again
                foreach (var user in users.Values)
                    Remember(user);

                // A deleted user is remembered, so it is not asked for again
                foreach (var userId in userIds)
                {
                    if (!users.ContainsKey(userId) && _session.IsUserDeleted(userId))
                        MarkDeleted(userId);
                }

                Changed?.Invoke();
            });
        }
        finally
        {
            await UiThread.RunAsync(() => _resolving.ExceptWith(userIds));
        }
    }

    /// <summary>
    /// The users a message needs: its author, the author of what it answers, and those mentioned in both.
    /// </summary>
    public static void CollectUserIds(ChatMessageVM message, HashSet<int> userIds)
    {
        userIds.Add(message.Message.SenderUserId);
        if (message.Reply?.SenderUserId is { } replySenderId)
            userIds.Add(replySenderId);
        userIds.UnionWith(ChatMessageBody.UserIdsIn(message.Message.TextContent?.Text));
        userIds.UnionWith(ChatMessageBody.UserIdsIn(message.Reply?.SourceText));
    }

    // Raised on the socket thread. A name still being looked up comes from the updated cache anyway
    private void OnUserUpdated(UserModel user) => Dispatcher.UIThread.Post(() =>
    {
        if (!_names.TryGetValue(user.Id, out var name)
            || (name == user.Username && _avatars.GetValueOrDefault(user.Id) == user.AvatarUrl
                && _statuses.GetValueOrDefault(user.Id) == user.Status
                && _roleIds.GetValueOrDefault(user.Id, []).SequenceEqual(user.RoleIds)))
            return;

        Remember(user);
        // Any message may mention the user, not only theirs
        Changed?.Invoke();
    });

    // Raised on the socket thread
    private void OnUserDeleted(int userId) => Dispatcher.UIThread.Post(() =>
    {
        MarkDeleted(userId);
        Changed?.Invoke();
    });

    // Raised on the transport thread; the names, colors or order of the roles may have changed
    private void OnPermissionsChanged(CoordinatorPermissions permissions) =>
        Dispatcher.UIThread.Post(() => Changed?.Invoke());

    private void Remember(UserModel user)
    {
        _names[user.Id] = user.Username;
        _avatars[user.Id] = user.AvatarUrl;
        _statuses[user.Id] = user.Status;
        _roleIds[user.Id] = user.RoleIds;
    }

    private void MarkDeleted(int userId)
    {
        _deleted.Add(userId);
        _names[userId] = DeletedName;
        _avatars.Remove(userId);
        _statuses.Remove(userId);
        _roleIds.Remove(userId);
    }

    public void Apply(ChatMessageVM messageVm)
    {
        var senderId = messageVm.Message.SenderUserId;
        messageVm.SenderName = GetName(senderId);
        messageVm.IsSenderDeleted = _deleted.Contains(senderId);
        messageVm.SenderAvatarUrl = _avatars.GetValueOrDefault(senderId);
        messageVm.SenderStatus = _statuses.TryGetValue(senderId, out var status) ? status : null;
        messageVm.SenderColor = GetColor(senderId);
        messageVm.Body = messageVm.Message.TextContent is { } text ? RenderBody(text.Text) : null;

        if (messageVm.Reply != null)
            Apply(messageVm.Reply);
    }

    public void Apply(ChatReplyPreviewVM replyVm)
    {
        replyVm.SenderName = replyVm.SenderUserId is { } senderUserId ? GetName(senderUserId) : UnknownName;
        replyVm.IsSenderDeleted = replyVm.SenderUserId is { } userId && _deleted.Contains(userId);
        replyVm.RenderText(RenderText);
    }

    public ChatMessageBody RenderBody(string text) =>
        ChatMessageBody.Create(text, GetMentionName, _currentUserId, UnknownName);

    public string RenderText(string text) => RenderBody(text).Text;

    /// <summary>
    /// The name as it is known, or null.
    /// </summary>
    public string? FindName(int userId) => _names.GetValueOrDefault(userId);

    /// <summary>
    /// The color of the highest role of the user that has one, 0xRRGGBB; null when none of them has.
    /// </summary>
    private int? GetColor(int userId)
    {
        if (!_roleIds.TryGetValue(userId, out var roleIds))
            return null;

        // The roles are ordered from the lowest
        return _session.Permissions.Roles
            .LastOrDefault(role => role.Color != null && roleIds.Contains(role.Id))?.Color;
    }

    private string? GetMentionName(int userId) =>
        _names.TryGetValue(userId, out var name) ? name
        : _resolving.Contains(userId) ? PendingMentionName
        : null;

    // Null while the name is still being looked up, so nothing flashes as unknown in the meantime
    private string? GetName(int userId)
    {
        if (_names.TryGetValue(userId, out var name))
            return name;

        return _resolving.Contains(userId) ? null : UnknownName;
    }

    public void Dispose() => _subscriptions.Dispose();
}
