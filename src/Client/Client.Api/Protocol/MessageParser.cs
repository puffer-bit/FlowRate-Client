using System.Text.Json;
using Shared.Models;
using Shared.Serialization;
using Microsoft.Extensions.Logging;

namespace Client.Api.Protocol;

internal partial class MessageParser : IMessageParser
{
    private readonly ILogger<MessageParser> _logger;

    public MessageParser(ILogger<MessageParser> logger)
    {
        _logger = logger;
    }

    public byte[] EncodeBaseMessage(BaseMessage message)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJsonContext.Default.BaseMessage);
        }
        catch (Exception ex)
        {
            LogEncodeFailed(ex, message.GetType().Name);
            throw;
        }
    }

    public BaseMessage DecodeBaseMessage(ReadOnlySpan<byte> rawMessage)
    {
        try
        {
            return JsonSerializer.Deserialize(rawMessage, ProtocolJsonContext.Default.BaseMessage)
                   ?? throw new JsonException("The message deserialized to null");
        }
        catch (Exception ex)
        {
            LogDecodeFailed(ex);
            throw;
        }
    }
}
