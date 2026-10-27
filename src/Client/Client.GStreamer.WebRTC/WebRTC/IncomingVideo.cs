using Client.Application.Ports;
using Client.GStreamer.Interop;
using Gst;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// Carries the depayloaded frames of one video stream from the sink of its branch to its decoder, and
/// the keyframes the decoder asks for back to the sender.
/// </summary>
/// <remarks>
/// The decoder is made with the first frame, on the reader thread: a branch is built on the streaming
/// thread of webrtcbin, which holds every packet of the transport back while it waits.
/// </remarks>
internal sealed class IncomingVideo : IDisposable
{
    private readonly Func<IVideoDecoder?> _createDecoder;
    private readonly Element _sink;
    private readonly AppSinkReader _reader;
    private readonly Lock _lock = new();
    private IVideoDecoder? _decoder;
    private bool _isDecoderMissing;
    private bool _isDisposed;

    /// <param name="createDecoder">Makes the decoder, or gives null when there is none for the stream.</param>
    /// <param name="sink">The appsink of the branch; the reader takes over the wrapper.</param>
    /// <param name="name">Names the reader thread.</param>
    /// <param name="onError">Receives a failure to hand a frame over, on the reader thread.</param>
    public IncomingVideo(Func<IVideoDecoder?> createDecoder, Element sink, string name, Action<Exception> onError)
    {
        _createDecoder = createDecoder;
        _sink = sink;

        // The decoder takes the frames with the timestamps of this pipeline, which it shows them by
        _reader = new AppSinkReader(sink, name, sample =>
        {
            using var buffer = sample.GetBuffer();

            if (buffer != null && EnsureDecoder() is { } decoder)
                GstEncodedNative.Read(buffer, decoder.Push);
        }, onError);
    }

    // On the reader thread
    private IVideoDecoder? EnsureDecoder()
    {
        if (_decoder != null || _isDecoderMissing)
            return _decoder;

        var decoder = _createDecoder();

        if (decoder == null)
        {
            // The frames are dropped from now on, as a drain would
            _isDecoderMissing = true;
            return null;
        }

        decoder.KeyFrameNeeded += RequestKeyFrame;

        lock (_lock)
        {
            if (_isDisposed)
            {
                decoder.Dispose();
                return null;
            }

            _decoder = decoder;
        }

        return decoder;
    }

    // On a thread of the decoder. Sent upstream from the sink, the request reaches the RTP session,
    // which asks the sender with a PLI
    private void RequestKeyFrame()
    {
        lock (_lock)
        {
            if (!_isDisposed)
                GstEncodedNative.RequestKeyFrame(_sink);
        }
    }

    /// <summary>Stops the reader, then the decoder. Call before the branch is shut down.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
        }

        // The reader makes the decoder, so the decoder is known once it has stopped
        _reader.Dispose();

        if (_decoder != null)
        {
            _decoder.KeyFrameNeeded -= RequestKeyFrame;
            _decoder.Dispose();
        }
    }
}
