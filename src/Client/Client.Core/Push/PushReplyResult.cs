namespace Client.Core.Push;

public enum PushReplyResult
{
    Sent,

    /// <summary>The device has no reply token of the coordinator: it registered before the server gave them.</summary>
    NotRegistered,

    /// <summary>The server does not know the token any more: the device was registered again or removed.</summary>
    Unauthorized,

    /// <summary>The text is not valid, or the user may not write to the chat or it is gone.</summary>
    Rejected,

    TooManyRequests,

    /// <summary>The server could not be reached, did not answer in time or failed.</summary>
    ConnectionFailed
}
