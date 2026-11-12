using Shared.Models;

namespace Client.Api.Protocol;

internal interface IMessageParser
{
    byte[] EncodeBaseMessage(BaseMessage message);
    BaseMessage DecodeBaseMessage(ReadOnlySpan<byte> rawMessage);
}
