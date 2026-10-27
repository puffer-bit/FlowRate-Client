using Gst;

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

    /// <summary>Name of the payloader of a capture branch.</summary>
    public const string PayloaderName = "payloader";

    /// <summary>
    /// The sequence number the next branch of the same track has to start with.
    /// </summary>
    /// <remarks>
    /// A screen that is stopped and shown again keeps its producers on the SFU, so to the SFU and to
    /// SRTP each of its tracks is one RTP stream. A payloader of its own picks a random sequence
    /// number. The SFU drops the first packets after such a jump as out of order: for the picture
    /// those are the keyframe every viewer waits for. And SRTP refuses a number lower than one it
    /// already protected, which stops the branch for good; so went the sound of a screen shown again.
    /// The timestamps need no such care: a jump in them is only a moment of silence or a late frame.
    /// </remarks>
    /// <returns>Null when the branch has sent nothing yet.</returns>
    public static int? ReadNextSequenceNumber(Bin branch)
    {
        using var payloader = branch.GetByName(PayloaderName);

        if (payloader == null || payloader.GetProperty<uint>("timestamp") == 0)
            return null;

        return (int)((payloader.GetProperty<uint>("seqnum") + 1) & 0xFFFF);
    }
}
