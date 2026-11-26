using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Ports.Repositories;
using Microsoft.Extensions.Logging;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// The texts typed in chats and not sent: the input of a chat gets its text back when the chat is opened
/// again, and the list shows which chats have one. Kept in memory for the coordinators read, and saved a
/// moment after the typing stops. Used on the UI thread; a failed read gives nothing, a failed write is
/// only logged.
/// </summary>
public sealed partial class ChatDrafts(IChatDraftsRepository repository, ILogger<ChatDrafts> logger)
{
    // Typing goes on for a while; the draft is written once it pauses
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private readonly ILogger _logger = logger;
    private readonly Dictionary<string, Dictionary<int, string>> _drafts = new();
    private readonly Dictionary<(string CoordinatorInstanceId, int ChatId), DispatcherTimer> _pendingSaves = new();
    private readonly Dictionary<string, Task<Dictionary<int, string>>> _loads = new();

    /// <summary>A draft was typed, changed or cleared: the coordinator, the chat and the text, null once cleared.</summary>
    public event Action<string, int, string?>? Changed;

    /// <summary>Every draft of the coordinator, read once.</summary>
    public async Task<IReadOnlyDictionary<int, string>> LoadAsync(string coordinatorInstanceId)
    {
        if (_drafts.TryGetValue(coordinatorInstanceId, out var loaded))
            return loaded;

        if (!_loads.TryGetValue(coordinatorInstanceId, out var load))
            _loads[coordinatorInstanceId] = load = ReadAsync(coordinatorInstanceId);

        var drafts = await load;
        // A draft typed while the stored ones were read is newer than them
        if (_drafts.TryGetValue(coordinatorInstanceId, out var typed))
            return typed;
        _drafts[coordinatorInstanceId] = drafts;
        return drafts;
    }

    private async Task<Dictionary<int, string>> ReadAsync(string coordinatorInstanceId)
    {
        try
        {
            return new Dictionary<int, string>(await repository.GetAllAsync(coordinatorInstanceId));
        }
        catch (Exception exception)
        {
            LogLoadFailed(exception);
            return new Dictionary<int, string>();
        }
        finally
        {
            _loads.Remove(coordinatorInstanceId);
        }
    }

    /// <summary>The draft of a chat, null when there is none or the drafts of the coordinator are not read yet.</summary>
    public string? Get(string coordinatorInstanceId, int chatId) =>
        _drafts.TryGetValue(coordinatorInstanceId, out var drafts) ? drafts.GetValueOrDefault(chatId) : null;

    /// <summary>Replaces the draft of a chat; an empty or whitespace-only text clears it.</summary>
    public void Set(string coordinatorInstanceId, int chatId, string? text)
    {
        var draft = string.IsNullOrWhiteSpace(text) ? null : text;
        if (!_drafts.TryGetValue(coordinatorInstanceId, out var drafts))
            _drafts[coordinatorInstanceId] = drafts = new Dictionary<int, string>();

        if (drafts.GetValueOrDefault(chatId) == draft)
            return;

        if (draft == null)
            drafts.Remove(chatId);
        else
            drafts[chatId] = draft;
        Changed?.Invoke(coordinatorInstanceId, chatId, draft);
        ScheduleSave(coordinatorInstanceId, chatId);
    }

    /// <summary>Writes the draft of a chat now instead of after the pause, when its input goes away.</summary>
    public void Flush(string coordinatorInstanceId, int chatId)
    {
        if (!_pendingSaves.Remove((coordinatorInstanceId, chatId), out var timer))
            return;

        timer.Stop();
        _ = SaveAsync(coordinatorInstanceId, chatId);
    }

    /// <summary>Forgets the draft of a chat that was deleted or left.</summary>
    public void Remove(string coordinatorInstanceId, int chatId) => Set(coordinatorInstanceId, chatId, null);

    private void ScheduleSave(string coordinatorInstanceId, int chatId)
    {
        var key = (coordinatorInstanceId, chatId);
        if (_pendingSaves.TryGetValue(key, out var timer))
        {
            // Restarted: the delay counts from the last change
            timer.Stop();
            timer.Start();
            return;
        }

        timer = new DispatcherTimer { Interval = SaveDelay };
        timer.Tick += (_, _) => Flush(coordinatorInstanceId, chatId);
        _pendingSaves[key] = timer;
        timer.Start();
    }

    private async Task SaveAsync(string coordinatorInstanceId, int chatId)
    {
        try
        {
            await repository.SetAsync(coordinatorInstanceId, chatId, Get(coordinatorInstanceId, chatId));
        }
        catch (Exception exception)
        {
            LogSaveFailed(exception, chatId);
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to load the chat drafts")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to save the draft of chat {ChatId}")]
    private partial void LogSaveFailed(Exception exception, int chatId);
}
