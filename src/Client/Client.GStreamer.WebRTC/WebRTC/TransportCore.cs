using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// What the parts of a <see cref="WebRTCTransport"/> share: the pipeline, webrtcbin and whether
/// they are still there. The transport sets and clears them; the parts only read.
/// </summary>
internal sealed partial class TransportCore
{
    private readonly ILogger _logger;

    public TransportCore(int sessionId, ILogger logger)
    {
        SessionId = sessionId;
        _logger = logger;
    }

    public int SessionId { get; }

    /// <summary>
    /// Guards the pipeline and webrtcbin being created and taken away, and the state of the negotiation.
    /// </summary>
    public Lock StateLock { get; } = new();

    /// <summary>
    /// Completes once the peer connection is connected, or with false once it cannot be any more.
    /// </summary>
    public TaskCompletionSource<bool> Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Pipeline? Pipeline { get; set; }

    public Element? WebRTC { get; set; }

    public IncomingStreamPlayer? Player { get; set; }

    public volatile bool IsDisposed;

    /// <summary>
    /// Runs a property notification handler. The notifications arrive on GStreamer threads through
    /// a native callback, where an exception would tear the process down, and they keep coming
    /// while the pipeline shuts down, when nobody should hear about them any more.
    /// </summary>
    public void Notify(Action handler)
    {
        if (IsDisposed)
            return;

        try
        {
            handler();
        }
        catch (Exception exception)
        {
            LogNotificationFailed(exception, SessionId);
        }
    }
}
