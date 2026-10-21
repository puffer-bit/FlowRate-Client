using Gst;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// What the capture and the playback branches of a transport have in common.
/// </summary>
internal static class PipelineBranch
{
    /// <summary>
    /// Stops a branch that is still inside the pipeline. Its state is locked first: the transport
    /// sets the pipeline to playing after every negotiation round, which would start the branch
    /// again in the moment between this and its removal.
    /// </summary>
    public static void Shutdown(Element branch)
    {
        branch.SetLockedState(true);
        branch.SetState(State.Null);
    }
}
