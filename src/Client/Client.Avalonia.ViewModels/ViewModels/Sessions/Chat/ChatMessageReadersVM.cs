using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Chat;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using ReactiveUI;
using Shared.Enums.User;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>Times of receipts and reactions, local, with the date unless it is today.</summary>
public static class ChatReceiptTime
{
    public static DateTime ToLocal(DateTime time) => time.Kind switch
    {
        DateTimeKind.Local => time,
        DateTimeKind.Utc => time.ToLocalTime(),
        _ => DateTime.SpecifyKind(time, DateTimeKind.Utc).ToLocalTime()
    };

    public static string Format(DateTime? time)
    {
        if (time is not { } value)
            return Strings.Get("chat.readers.unknownTime");
        var local = ToLocal(value);
        return local.Date == DateTime.Now.Date ? local.ToString("HH:mm") : Strings.FormatDateTime(local);
    }
}

/// <summary>A member who has read the message, as the readers list shows them.</summary>
public sealed class ChatMessageReaderVM(int userId, string name, string? avatarUrl, UserStatus? status, DateTime? readAt)
{
    public int UserId { get; } = userId;
    public string Name { get; } = name;
    public string? AvatarUrl { get; } = avatarUrl;
    public UserStatus? Status { get; } = status;
    public DateTime? ReadAt { get; } = readAt;
    public string TimeText => ChatReceiptTime.Format(ReadAt);
}

/// <summary>
/// Who of the other members has read an own message, and when; only its author sees it. Loaded when it is
/// first shown and kept up to date by the reads that come while it lives. Touched on the UI thread.
/// </summary>
public sealed class ChatMessageReadersVM : ReactiveObject, IDisposable
{
    private readonly int _chatId;
    private readonly int _messageId;
    private readonly ChatReceipts _receipts;
    private readonly ChatSenderDirectory _senders;
    private readonly Dictionary<int, DateTime?> _readers = new();
    private Task? _load;
    private bool _isDisposed;

    public ObservableCollection<ChatMessageReaderVM> Items { get; } = new();

    public bool IsLoading
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsEmpty));
        }
    }

    public string? Error
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasError));
            this.RaisePropertyChanged(nameof(IsEmpty));
        }
    }

    public bool HasError => Error != null;
    public bool IsEmpty => !IsLoading && !HasError && Items.Count == 0;

    /// <summary>The first time the message was read; what a direct chat shows next to its checks.</summary>
    public DateTime? FirstReadAt => _readers.Values.Where(time => time.HasValue).Min();

    public event Action? Changed;

    public ChatMessageReadersVM(int chatId, int messageId, ChatReceipts receipts, ChatSenderDirectory senders)
    {
        _chatId = chatId;
        _messageId = messageId;
        _receipts = receipts;
        _senders = senders;
        _receipts.PeerRead += OnPeerRead;
        _senders.Changed += Rebuild;
    }

    public Task LoadAsync() => _load ??= LoadCoreAsync();

    /// <summary>Asks again, e.g. after a failure.</summary>
    public Task ReloadAsync()
    {
        _load = null;
        return LoadAsync();
    }

    private async Task LoadCoreAsync()
    {
        IsLoading = true;
        Error = null;
        var result = await _receipts.RequestReaders(_chatId, _messageId);
        if (_isDisposed)
            return;
        if (!result.IsSuccess)
        {
            IsLoading = false;
            Error = Strings.Get("chat.readers.failed");
            _load = null;
            return;
        }

        foreach (var reader in result.Value)
            Remember(reader.UserId, reader.ReadAt);
        await ResolveNamesAsync(_readers.Keys.ToHashSet());
        if (_isDisposed)
            return;
        IsLoading = false;
        Rebuild();
    }

    // Raised on the socket thread
    private void OnPeerRead(ChatPeerRead read)
    {
        if (read.ChatId != _chatId || read.LastReadMessageId < _messageId)
            return;
        Dispatcher.UIThread.Post(async () =>
        {
            if (_isDisposed || _readers.ContainsKey(read.ReaderUserId))
                return;
            Remember(read.ReaderUserId, read.ReadAt);
            await ResolveNamesAsync([read.ReaderUserId]);
            if (!_isDisposed)
                Rebuild();
        });
    }

    // Names that cannot be looked up show as unknown; the list is shown anyway
    private async Task ResolveNamesAsync(HashSet<int> userIds)
    {
        try
        {
            await _senders.ResolveAsync(userIds);
        }
        catch (Exception)
        {
            // The readers are shown with the names already known
        }
    }

    private void Remember(int userId, DateTime? readAt)
    {
        if (!_readers.TryGetValue(userId, out var known) || (readAt.HasValue && (!known.HasValue || readAt < known)))
            _readers[userId] = readAt;
    }

    private void Rebuild()
    {
        if (_isDisposed)
            return;
        Items.Clear();
        foreach (var (userId, readAt) in _readers.OrderBy(pair => pair.Value ?? DateTime.MinValue)
                     .ThenBy(pair => pair.Key))
            Items.Add(new ChatMessageReaderVM(userId, _senders.FindName(userId) ?? Strings.Get("chat.unknownUser"),
                _senders.FindAvatarUrl(userId), _senders.FindStatus(userId), readAt));
        this.RaisePropertyChanged(nameof(IsEmpty));
        this.RaisePropertyChanged(nameof(FirstReadAt));
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _receipts.PeerRead -= OnPeerRead;
        _senders.Changed -= Rebuild;
    }
}
