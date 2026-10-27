using System.Globalization;
using Gst;
using Client.Application.Ports;
using Client.GStreamer.Interop;

namespace Client.GStreamer.Audio;

/// <summary>
/// Captures the sound of one application through PipeWire: every playback stream it has gets a
/// capture of its own, linked to that stream alone, and the captures are mixed. Streams are added
/// and removed as the application opens and closes them, which a browser does with every tab that
/// starts or stops playing.
/// </summary>
/// <remarks>
/// A capture follows nothing else: it is neither moved to another node when its stream goes away,
/// nor connected to the default one when its stream is not found, which would send the microphone.
/// The capture of a stream that went away fails with "target not found"; the bus of the transport
/// drops errors, and the mixer goes on with the other inputs. A silent input keeps the mixer and
/// its timestamps running while the application plays nothing.
/// </remarks>
public sealed class PipeWireAppAudioSource : IDisposable
{
    private const string MixerName = "mix";
    private const string Caps = "audio/x-raw,rate=48000,channels=2,format=F32LE";

    private const string Description =
        "audiotestsrc is-live=true wave=silence ! capsfilter caps=\"" + Caps + "\" ! " +
        "audiomixer name=" + MixerName + " latency=20000000";

    // A stall of a single stream must not hold the mixer up, so its queue drops the oldest audio
    private const string StreamDescription =
        "pipewiresrc target-object={0} stream-properties=\"props,node.dont-reconnect=true,node.dont-fallback=true\" ! " +
        "audioconvert ! audioresample ! capsfilter caps=\"" + Caps + "\" ! " +
        "queue max-size-buffers=0 max-size-bytes=0 max-size-time=100000000 leaky=downstream";

    public static bool IsAvailable =>
        ElementFactory.Find("pipewiresrc") != null && ElementFactory.Find("audiomixer") != null;

    // The captures and the mixer; the owner holds the bin around it, whose wrapper it may dispose
    private readonly Bin _bin;
    private readonly Element _mixer;
    private readonly PipeWireAppAudioInput _input;
    private readonly IDisposable _watch;
    private readonly Action<Exception> _onError;
    private readonly Lock _lock = new();
    private readonly Dictionary<uint, (Bin Branch, Pad MixerPad)> _streams = new();
    private bool _isDisposed;

    /// <summary>
    /// The element that delivers the mixed sound. The owner of the source owns it as well.
    /// </summary>
    public Element Element { get; }

    private PipeWireAppAudioSource(Bin outer, Bin bin, Element mixer, PipeWireAppAudioInput input,
        Action<Exception> onError)
    {
        Element = outer;
        _bin = bin;
        _mixer = mixer;
        _input = input;
        _onError = onError;

        _input.Apps.Changed += Update;
        _watch = _input.Apps.Watch();
        Update();
    }

    /// <param name="onError">Receives the failures to follow the streams, on the thread that reported the change.</param>
    /// <returns>Null when the runtime lacks an element.</returns>
    public static PipeWireAppAudioSource? Create(PipeWireAppAudioInput input, Action<Exception> onError)
    {
        if (!IsAvailable)
            return null;

        var bin = Global.ParseBinFromDescription(Description, true);

        if (bin?.GetByName(MixerName) is not { } mixer)
        {
            bin?.Dispose();
            return null;
        }

        var outer = Bin.New(null);
        outer.Add(bin);

        using (var output = bin.GetStaticPad("src"))
        using (var ghost = output == null ? null : GhostPad.New("src", output))
        {
            if (ghost == null || !outer.AddPad(ghost))
            {
                mixer.Dispose();
                bin.Dispose();
                outer.Dispose();
                return null;
            }
        }

        return new PipeWireAppAudioSource(outer, bin, mixer, input, onError);
    }

    private void Update()
    {
        try
        {
            var app = _input.Apps.Apps.FirstOrDefault(item => item.Key == _input.AppKey);
            var wanted = app?.StreamSerials.ToHashSet() ?? [];

            lock (_lock)
            {
                if (_isDisposed)
                    return;

                foreach (var serial in _streams.Keys.Where(serial => !wanted.Contains(serial)).ToList())
                    RemoveStream(serial);

                foreach (var serial in wanted.Where(serial => !_streams.ContainsKey(serial)))
                    AddStream(serial);
            }
        }
        catch (Exception exception)
        {
            _onError(exception);
        }
    }

    // Called under the lock
    private void AddStream(uint serial)
    {
        var branch = Global.ParseBinFromDescription(string.Format(CultureInfo.InvariantCulture, StreamDescription,
            serial), true);

        if (branch == null)
            return;

        var mixerPad = _mixer.RequestPadSimple("sink_%u");

        if (mixerPad == null)
        {
            branch.Dispose();
            return;
        }

        _bin.Add(branch);

        using (var output = branch.GetStaticPad("src"))
        {
            if (output == null || output.Link(mixerPad) != PadLinkReturn.Ok)
            {
                _bin.Remove(branch);
                _mixer.ReleaseRequestPad(mixerPad);
                mixerPad.Dispose();
                branch.Dispose();
                return;
            }
        }

        branch.SyncStateWithParent();
        _streams[serial] = (branch, mixerPad);
    }

    // Called under the lock
    private void RemoveStream(uint serial)
    {
        if (!_streams.Remove(serial, out var stream))
            return;

        GstBranch.Shutdown(stream.Branch);
        _bin.Remove(stream.Branch);
        _mixer.ReleaseRequestPad(stream.MixerPad);
        stream.MixerPad.Dispose();
        stream.Branch.Dispose();
    }

    /// <summary>
    /// Stops following the streams. Call before the element is shut down; the captures go with it.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            // Only the wrappers: the captures stay in the bin, which its owner shuts down
            foreach (var (branch, mixerPad) in _streams.Values)
            {
                mixerPad.Dispose();
                branch.Dispose();
            }

            _streams.Clear();
        }

        _input.Apps.Changed -= Update;
        _watch.Dispose();
        _mixer.Dispose();
        _bin.Dispose();
    }
}
