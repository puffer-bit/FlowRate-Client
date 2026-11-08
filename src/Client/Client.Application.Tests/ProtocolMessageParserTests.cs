using System.Text;
using System.Text.Json;
using Client.Api.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;
using Shared.Models.Requests.Heartbeat;
using Shared.Serialization;
using Xunit;

namespace Client.Application.Tests;

public class ProtocolMessageParserTests
{
    private readonly MessageParser _parser = new(NullLogger<MessageParser>.Instance);

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
