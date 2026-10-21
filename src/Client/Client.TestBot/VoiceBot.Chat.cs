using System.Globalization;
using Client.Application.Coordinator;
using Client.Core.Chat;
using Client.Core.Sessions;
using Shared.Enums;

namespace Client.TestBot;

internal sealed partial class VoiceBot
{
    // Marks the messages of the bots; the rest of the text carries the send time for the delivery time
    private const string ChatMarker = "[load]";

    private static readonly string[] ChatPhrases =
    [
        "Hello everyone",
        "How is the sound on your side?",
        "The picture is a bit late here",
        "Testing the chat under load",
        "Can you hear me now?",
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore"
    ];

    private int _chatId;
    private int _chatSequence;

    /// <summary>
    /// Finds the chat to write into and starts counting the messages of the other bots in it.
    /// </summary>
    private async Task PrepareChatAsync(ICoordinatorSession coordinator)
    {
        var chat = await FindChatAsync(coordinator);
        _chatId = chat.Id;

        coordinator.TextMessageAdded += (chatId, message) =>
        {
            if (chatId != _chatId || message.IsMine || message.TextContent?.Text is not { } text)
                return;

            if (TryReadSendTime(text) is { } sentAt)
                _stats.ChatReceived(DateTime.UtcNow - sentAt);
        };

        Log($"writing to the chat \"{chat.Name}\"");
    }

    /// <summary>
    /// Sends a message every <paramref name="interval"/>, give or take a fifth, starting at a random
    /// moment so that the bots do not send all at once.
    /// </summary>
    private async Task ChatForeverAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        var random = new Random(_index);

        try
        {
            await Task.Delay(interval * random.NextDouble(), cancellationToken);

            while (true)
            {
                await SendChatMessageAsync(random);
                await Task.Delay(interval * (0.8 + random.NextDouble() * 0.4), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SendChatMessageAsync(Random random)
    {
        var coordinator = _coordinator!;

        if (coordinator.ConnectionStatus != Client.Core.Coordinator.CoordinatorState.Connected)
            return;

        var sequence = ++_chatSequence;
        var text = $"{ChatMarker} {DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)} " +
                   $"{Name} #{sequence}: {ChatPhrases[random.Next(ChatPhrases.Length)]}";

        AddMessageResult result;

        try
        {
            result = await coordinator.SendChatMessage(new NewChatMessage
            {
                ChatId = _chatId,
                SenderUserId = coordinator.GetCurrentUser().Id,
                SentTime = DateTime.UtcNow,
                TextContent = ChatTextContent.FromUserInput(text)
            });
        }
        catch (Exception exception)
        {
            Log($"failed to send a chat message: {exception.Message}");
            _stats.ChatSent(false);
            return;
        }

        _stats.ChatSent(result == AddMessageResult.NoError);

        if (result != AddMessageResult.NoError)
            Log($"failed to send a chat message: {result}");
    }

    private static DateTime? TryReadSendTime(string text)
    {
        if (!text.StartsWith(ChatMarker, StringComparison.Ordinal))
            return null;

        var start = ChatMarker.Length + 1;
        var end = text.IndexOf(' ', start);

        return end > start && long.TryParse(text.AsSpan(start, end - start), NumberStyles.None,
            CultureInfo.InvariantCulture, out var ticks)
            ? new DateTime(ticks, DateTimeKind.Utc)
            : null;
    }

    private async Task<SessionModel> FindChatAsync(ICoordinatorSession coordinator)
    {
        var (result, sessions, _) = await coordinator.GetAllSessions();

        if (result != SessionRequestResult.NoError || sessions == null)
            throw new InvalidOperationException($"{Name}: failed to get the session list: {result}");

        var chats = sessions.Values.OfType<ChatSessionModel>().Where(chat => !chat.IsReadOnly).ToList();

        if (chats.Count == 0)
            throw new InvalidOperationException($"{Name}: the server has no chats to write into");

        if (_options.Chat == null)
            return chats[0];

        return chats.FirstOrDefault(chat => chat.Id.ToString() == _options.Chat)
               ?? chats.FirstOrDefault(chat => string.Equals(chat.Name, _options.Chat, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException(
                   $"{Name}: no chat \"{_options.Chat}\". Available: " +
                   string.Join(", ", chats.Select(chat => $"{chat.Id} \"{chat.Name}\"")));
    }
}
