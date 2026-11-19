using System.Text;
using System.Text.Json;
using Client.Api.Protocol;
using Client.Api.Mappers;
using Client.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;
using Shared.Models.Requests.Heartbeat;
using Shared.Enums;
using Shared.Models.Requests.SessionActions.Generic;
using Shared.Models.Requests.SessionActions.Generic.Models.SessionData;
using Shared.Validation;
using Shared.Serialization;
using Xunit;

namespace Client.Application.Tests;

public class ProtocolMessageParserTests
{
    private readonly MessageParser _parser = new(NullLogger<MessageParser>.Instance);

    [Theory]
    [InlineData(ChatKind.Direct, "Direct", 1)]
    [InlineData(ChatKind.Group, "Друзья", 1)]
    [InlineData(ChatKind.Group, "Друзья", 3)]
    [InlineData(ChatKind.Group, "3333", 3)]
    public void ConversationCreation_PreservesAudienceNameAndMembersOnTheWire(ChatKind kind, string name, int peerCount)
    {
        var members = Enumerable.Range(2, peerCount).ToArray();
        var model = new NewSessionModel(name, 1, Limits.SessionCapacityUnlimited,
            new NewChatSessionOptions(Kind: kind, MemberIds: members));
        var request = new CreateSessionRequest(model.ToDto());

        var decoded = Assert.IsType<CreateSessionRequest>(
            _parser.DecodeBaseMessage(_parser.EncodeBaseMessage(request)));

        Assert.True(MessageValidator.TryValidate(decoded, out var errors), string.Join("; ", errors));
        Assert.Equal(name, decoded.CreateSessionModel.Name);
        var data = Assert.IsType<ChatSessionCreateData>(decoded.CreateSessionModel.SessionData);
        Assert.Equal(kind, data.Kind);
        Assert.Equal(members, data.MemberIds);
    }

    [Fact]
    public void Utf8Serialization_PreservesWireFormatAndRequestId()
    {
        var request = new HeartbeatPingRequest();
        var bytes = _parser.EncodeBaseMessage(request);
        Assert.Equal(JsonSerializer.Serialize<BaseMessage>(request, ProtocolJsonContext.Default.BaseMessage),
            Encoding.UTF8.GetString(bytes));
        Assert.Equal(request.RequestId, Assert.IsType<HeartbeatPingRequest>(_parser.DecodeBaseMessage(bytes)).RequestId);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{invalid")]
    [InlineData("{\"$type\":\"UnknownMessage\"}")]
    public void MalformedMessage_IsRejected(string json)
    {
        Assert.Throws<JsonException>(() => _parser.DecodeBaseMessage(Encoding.UTF8.GetBytes(json)));
    }
}
