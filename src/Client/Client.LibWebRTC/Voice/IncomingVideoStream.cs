using Client.Application.Ports;
using Client.Core.Media;

namespace Client.LibWebRTC.Voice;

/// <summary>
/// One video stream we receive: its pictures, as libwebrtc hands them out encoded, go to a decoder,
/// and the keyframes the decoder asks for back to the sender. The counterpart of IncomingVideo of
/// the GStreamer transport.
/// </summary>
/// <remarks>
/// The decoder is made with the first picture, on the thread that delivers them: nothing is decoded
/// before libwebrtc has a keyframe of the stream. Until then a keyframe is asked for every second:
/// libwebrtc does not ask by itself, and a viewer who joined would wait for the encoder to make one.
/// </remarks>
internal sealed class IncomingVideoStream : IDisposable
{
    private readonly IEncodedVideoReceiver _receiver;
    private readonly uint _ssrc;
    private readonly Func<IVideoDecoder?> _createDecoder;
    private readonly Action<RawVideoFrame> _onFrame;
    private readonly Action<VideoDecoding> _onDecoding;
    private readonly Action<Exception> _onError;
    private readonly IDisposable _subscription;
    private readonly Lock _lock = new();
    private IVideoDecoder? _decoder;
    private bool _isDecoderMissing;
    private bool _isDisposed;

    private static readonly TimeSpan KeyFrameInterval = TimeSpan.FromSeconds(1);
    private readonly Timer _keyFrameTimer;
    private bool _hasPicture;

    /// <param name="createDecoder">Makes the decoder, or gives null when there is none for the stream.</param>
    /// <param name="onFrame">Receives the decoded pictures, on a thread of the decoder.</param>
    /// <param name="onDecoding">Receives how the decoder decodes, on a thread of the decoder.</param>
    /// <param name="onError">Receives a failure to hand a picture over, on the thread that delivers them.</param>
    public IncomingVideoStream(IEncodedVideoReceiver receiver, uint ssrc, Func<IVideoDecoder?> createDecoder,
        Action<RawVideoFrame> onFrame, Action<VideoDecoding> onDecoding, Action<Exception> onError)
    {
        _receiver = receiver;
        _ssrc = ssrc;
        _createDecoder = createDecoder;
        _onFrame = onFrame;
        _onDecoding = onDecoding;
        _onError = onError;
        _keyFrameTimer = new Timer(_ => RequestKeyFrame(), null, TimeSpan.Zero, KeyFrameInterval);
        _subscription = receiver.Receive(ssrc, OnPicture);
    }

    // On the thread of libwebrtc that delivers the pictures of the stream
    private void OnPicture(EncodedVideoFrame frame)
    {
        try
        {
            if (!_hasPicture)
            {
                _hasPicture = true;
                _keyFrameTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            EnsureDecoder()?.Push(frame);
        }
        catch (Exception exception)
        {
            _onError(exception);
        }
    }

    private IVideoDecoder? EnsureDecoder()
    {
        if (_decoder != null || _isDecoderMissing)
            return _decoder;

        var decoder = _createDecoder();

        if (decoder == null)
        {
            // The pictures are dropped from now on
            _isDecoderMissing = true;
            return null;
        }

        decoder.FrameReady += _onFrame;
        decoder.DecodingChanged += _onDecoding;
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

    // On a thread of the decoder; libwebrtc asks the sender with a PLI
    private void RequestKeyFrame()
    {
        lock (_lock)
        {
            if (!_isDisposed)
                _receiver.RequestKeyFrame(_ssrc);
        }
    }

    /// <summary>Stops taking pictures, then the decoder.</summary>
    public void Dispose()
    {
        IVideoDecoder? decoder;

        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            decoder = _decoder;
        }

        _keyFrameTimer.Dispose();
        _subscription.Dispose();

        // A picture still being pushed finds the decoder stopped, which drops it
        if (decoder != null)
        {
            decoder.FrameReady -= _onFrame;
            decoder.DecodingChanged -= _onDecoding;
            decoder.KeyFrameNeeded -= RequestKeyFrame;
            decoder.Dispose();
        }
    }
}
