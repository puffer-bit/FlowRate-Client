namespace Client.GStreamer.WebRTC;

/// <summary>
/// What the capture and the playback branches of a transport have in common.
/// </summary>
internal static class PipelineBranch
{
    /// <summary>
    /// Name of the last queue of a capture branch, in front of webrtcbin. The socket takes the
    /// packets on its streaming thread, so a network that does not take what is sent fills it.
    /// </summary>
    public const string SendQueueName = "send_queue";
}
