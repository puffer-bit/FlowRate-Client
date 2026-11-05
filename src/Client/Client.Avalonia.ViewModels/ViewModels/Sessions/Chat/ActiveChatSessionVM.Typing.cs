using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Client.Application.Chat;

namespace Client.ViewModels.Sessions.Chat;

public partial class ActiveChatSessionVM
{
    private readonly Dictionary<int, DateTimeOffset> _typingUsers = new();
    private IDisposable? _typingExpiry;

    private void OnChatUserTyping(int chatId, int userId, bool isTyping)
    {
        if (chatId != Id || userId == _currentUserId)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_isDisposed || !IsConnected)
                return;
            RestoreTyping();
        });
    }

    private void RestoreTyping()
    {
        _typingUsers.Clear();
        foreach (var (userId, expiresAt) in _coordinatorSession.GetChatTypingUsers(Id))
            _typingUsers[userId] = expiresAt;
        UpdateTypingNames();
        if (_typingUsers.Count == 0)
            return;
        _ = _senders.ResolveAsync(new HashSet<int>(_typingUsers.Keys));
        _typingExpiry ??= DispatcherTimer.Run(ExpireTyping, TimeSpan.FromMilliseconds(250));
    }

    private bool ExpireTyping()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var userId in _typingUsers.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
            _typingUsers.Remove(userId);
        UpdateTypingNames();
        if (_typingUsers.Count > 0)
            return true;
        _typingExpiry = null;
        return false;
    }

    private void RemoveTyping(int userId)
    {
        if (_typingUsers.Remove(userId))
            UpdateTypingNames();
    }

    private void UpdateTypingNames() => StatusBar.SetTypingNames(_typingUsers.Keys
        .Select(id => _senders.FindName(id) ?? "…").ToArray());

    private void ClearTyping()
    {
        _typingExpiry?.Dispose();
        _typingExpiry = null;
        _typingUsers.Clear();
        UpdateTypingNames();
    }
}
