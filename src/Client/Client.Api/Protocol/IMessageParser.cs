using Shared.Models;

namespace Client.Api.Protocol;

internal interface IMessageParser
{
    string EncodeBaseMessage(BaseMessage message);
    BaseMessage DecodeBaseMessage(string rawMessage);
}
