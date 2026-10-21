namespace Client.Core.Push;

/// <summary>
/// What a reply typed into a notification of a coordinator needs to reach it without a connection:
/// the address of the coordinator and the token it gave the device on registering for push.
/// </summary>
public sealed record PushReplyTarget(string Address, int Port, string ReplyToken);
