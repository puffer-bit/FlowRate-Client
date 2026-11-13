using Client.Application.Ports;
using Client.Application.Push;
using Client.Application.Tests.Fakes;
using Client.Core.Push;
using Xunit;

namespace Client.Application.Tests;

public class PushRepliesTests
{
    private const string Coordinator = "coordinator";

    private readonly MemoryPushKeys _keys = new();
    private readonly RecordingTransport _transport = new();
    private readonly PushReplies _replies;
    private readonly MemoryReads _reads = new();

    public PushRepliesTests()
    {
        _replies = new PushReplies(_keys, new NoTrustedCertificates(), _transport, _reads);
        _keys.Keys[Coordinator] = new PushKey("00", Coordinator, new byte[32], 5);
    }

    [Fact]
    public async Task Send_GoesToTheKeptAddressWithTheNormalizedText()
    {
        var target = new PushReplyTarget("server", 1, "reply-token");
        await _keys.SetReplyTargetAsync(Coordinator, target);

        var result = await _replies.SendAsync(Coordinator, 7, "  hi  ", TestContext.Current.CancellationToken);

        Assert.Equal(PushReplyResult.Sent, result);
        Assert.Equal((target, 7, "hi"), Assert.Single(_transport.Sent));
    }

    [Fact]
    public async Task DeclineCall_GoesToTheKeptAddress()
    {
        var target = new PushReplyTarget("server", 1, "reply-token");
        await _keys.SetReplyTargetAsync(Coordinator, target);
        var callId = Guid.NewGuid();

        var result = await _replies.DeclineCallAsync(Coordinator, callId, TestContext.Current.CancellationToken);

        Assert.Equal(PushReplyResult.Sent, result);
        Assert.Equal((target, callId), Assert.Single(_transport.Declined));
        Assert.Equal(PushReplyResult.NotRegistered,
            await _replies.DeclineCallAsync("other", callId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Send_WithoutAReplyToken_IsNotRegistered()
    {
        var result = await _replies.SendAsync(Coordinator, 7, "hi", TestContext.Current.CancellationToken);

        Assert.Equal(PushReplyResult.NotRegistered, result);
        Assert.Empty(_transport.Sent);
    }

    [Fact]
    public async Task Send_OfBlankText_IsRejectedHere()
    {
        await _keys.SetReplyTargetAsync(Coordinator, new PushReplyTarget("server", 1, "reply-token"));

        var result = await _replies.SendAsync(Coordinator, 7, "   ", TestContext.Current.CancellationToken);

        Assert.Equal(PushReplyResult.Rejected, result);
        Assert.Empty(_transport.Sent);
    }

    [Fact]
    public async Task MarkRead_IsSavedLocallyWithoutAReplyToken()
    {
        var result = await _replies.MarkReadAsync(Coordinator, 7, 42, TestContext.Current.CancellationToken);

        Assert.Equal(PushReplyResult.Sent, result);
        Assert.Equal((Coordinator, 5, new Client.Core.Chat.ChatReadState(7, 42, 0, 0)), Assert.Single(_reads.Saved));
        Assert.Empty(_transport.Read);
    }

    [Fact]
    public async Task MarkRead_WithoutALocalAccount_IsNotRegistered()
    {
        _keys.Keys[Coordinator] = _keys.Keys[Coordinator] with { UserId = null };
        var result = await _replies.MarkReadAsync(Coordinator, 7, 42, TestContext.Current.CancellationToken);

        Assert.Equal(PushReplyResult.NotRegistered, result);
        Assert.Empty(_transport.Read);
    }

    private sealed class MemoryReads : Client.Application.Ports.Repositories.IChatReadStateRepository
    {
        public List<(string, int, Client.Core.Chat.ChatReadState)> Saved { get; } = [];
        public Task<IReadOnlyList<Client.Core.Chat.ChatReadState>> GetAsync(string coordinatorInstanceId, int userId) =>
            Task.FromResult<IReadOnlyList<Client.Core.Chat.ChatReadState>>([]);
        public Task SetAsync(string coordinatorInstanceId, int userId, Client.Core.Chat.ChatReadState state)
        {
            Saved.Add((coordinatorInstanceId, userId, state));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTransport : IPushReplyTransport
    {
        public List<(PushReplyTarget Target, int ChatId, string Text)> Sent { get; } = [];
        public List<(PushReplyTarget Target, int ChatId, int MessageId)> Read { get; } = [];
        public List<(PushReplyTarget Target, Guid CallId)> Declined { get; } = [];

        public Task<PushReplyResult> DeclineCallAsync(PushReplyTarget target, string? trustedFingerprint, Guid callId,
            CancellationToken cancellationToken = default)
        {
            Declined.Add((target, callId));
            return Task.FromResult(PushReplyResult.Sent);
        }

        public Task<PushReplyResult> MarkReadAsync(PushReplyTarget target, string? trustedFingerprint, int chatId,
            int messageId, CancellationToken cancellationToken = default)
        {
            Read.Add((target, chatId, messageId));
            return Task.FromResult(PushReplyResult.Sent);
        }

        public Task<PushReplyResult> SendAsync(PushReplyTarget target, string? trustedFingerprint, int chatId,
            string text, CancellationToken cancellationToken = default)
        {
            Sent.Add((target, chatId, text));
            return Task.FromResult(PushReplyResult.Sent);
        }
    }
}
