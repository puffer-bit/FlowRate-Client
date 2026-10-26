using System.Threading.Channels;
using Client.Application.Ports;
using Client.Core.Settings;
using Shared.Enums.WebRTC;

namespace Client.LibWebRTC.Voice;

/// <summary>
/// Serializes a platform peer's commands and callbacks away from both the UI and the native threads.
/// Commands are accepted synchronously, but run on the worker; creation and disposal do too.
/// </summary>
public sealed class VoicePeerWorker : IVoicePeer
{
    private readonly Channel<Action<IVoicePeer>> _commands = Channel.CreateUnbounded<Action<IVoicePeer>>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly IVoicePeerListener _listener;
    private int _isClosed;

    public VoicePeerWorker(Func<IVoicePeerListener, IVoicePeer> createPeer, IVoicePeerListener listener)
        : this(peerListener => Task.FromResult(createPeer(peerListener)), listener)
    {
    }

    public VoicePeerWorker(Func<IVoicePeerListener, Task<IVoicePeer>> createPeer, IVoicePeerListener listener)
    {
        _listener = listener;
        _ = Task.Run(() => RunAsync(createPeer));
    }

    private bool Enqueue(Action<IVoicePeer> command) =>
        Volatile.Read(ref _isClosed) == 0 && _commands.Writer.TryWrite(command);

    private async Task RunAsync(Func<IVoicePeerListener, Task<IVoicePeer>> createPeer)
    {
        IVoicePeer? peer = null;

        try
        {
            if (Volatile.Read(ref _isClosed) != 0)
                return;

            peer = await createPeer(new Listener(this)).ConfigureAwait(false);

            await foreach (var command in _commands.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (Volatile.Read(ref _isClosed) != 0)
                    break;

                command(peer);
            }
        }
        catch (Exception exception)
        {
            if (Volatile.Read(ref _isClosed) == 0)
            {
                try
                {
                    _listener.OnFailed(exception.Message);
                    _listener.OnConnectionChanged(WebRTCConnectionState.Failed);
                }
                catch (Exception)
                {
                    // A subscriber must not prevent the native peer from being released.
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isClosed, 1);
            _commands.Writer.TryComplete();

            try
            {
                peer?.Dispose();
            }
            catch (Exception)
            {
                // Disposal runs off the caller's thread and must not fault an unobserved task.
            }

            // Release descriptions and callbacks left queued by a concurrent close.
            while (_commands.Reader.TryRead(out _)) { }
        }
    }

    public void Negotiate(string offer, IReadOnlyList<string> sendMids, IReadOnlyList<uint> sendSsrcs)
    {
        var mids = sendMids.ToArray();
        var ssrcs = sendSsrcs.ToArray();
        Enqueue(peer => peer.Negotiate(offer, mids, ssrcs));
    }

    public void SetMicrophoneEnabled(bool isEnabled) => Enqueue(peer => peer.SetMicrophoneEnabled(isEnabled));

    // This worker is for platforms where device selection is system-owned. A failure after a
    // command was accepted is reported through the listener, not by blocking the caller.
    public bool SetMicrophone(AudioInput input, NoiseSuppressionLevel noiseSuppression, bool isEchoCancellationEnabled) =>
        Enqueue(peer =>
        {
            if (!peer.SetMicrophone(input, noiseSuppression, isEchoCancellationEnabled))
                throw new InvalidOperationException("The microphone could not be configured.");
        });

    public void SetMicrophoneVolume(double volume) => Enqueue(peer => peer.SetMicrophoneVolume(volume));
    public void SetVoiceGate(VoiceGateOptions? voiceGate) => Enqueue(peer => peer.SetVoiceGate(voiceGate));
    public void SetVolume(string mid, double volume) => Enqueue(peer => peer.SetVolume(mid, volume));
    public void RequestStats() => Enqueue(peer => peer.RequestStats());

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isClosed, 1) == 0)
            _commands.Writer.TryComplete();
    }

    private sealed class Listener(VoicePeerWorker owner) : IVoicePeerListener
    {
        // Never enter application code from a native callback: a caller may be waiting for that
        // very native thread while holding a lock the callback needs.
        public void OnConnectionChanged(WebRTCConnectionState state) =>
            owner.Enqueue(_ => owner._listener.OnConnectionChanged(state));
        public void OnNegotiated(string localSdp) => owner.Enqueue(_ => owner._listener.OnNegotiated(localSdp));
        public void OnFailed(string error) => owner.Enqueue(_ => owner._listener.OnFailed(error));
        public void OnStats(VoiceStats stats) => owner.Enqueue(_ => owner._listener.OnStats(stats));
    }
}
