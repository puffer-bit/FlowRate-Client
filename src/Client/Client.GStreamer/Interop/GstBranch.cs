using Gst;

namespace Client.GStreamer.Interop;

/// <summary>
/// What every part of a pipeline that is added and taken out while it runs needs.
/// </summary>
public static class GstBranch
{
    /// <summary>
    /// Stops a branch that is still inside the pipeline. Its state is locked first: the owner of the
    /// pipeline may set it to playing at any time (a transport does after every negotiation round),
    /// which would start the branch again in the moment between this and its removal.
    /// </summary>
    public static void Shutdown(Element branch)
    {
        branch.SetLockedState(true);
        branch.SetState(State.Null);
    }
}
