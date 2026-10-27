using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Client.Application.Ports;
using Client.Core.Media;
using Client.Core.Settings;
using Client.LibWebRTC.Voice;
using Com.Flowrate.Rtc;
using Shared.Enums.WebRTC;

namespace Client.Droid.Platform.Media.WebRTC;

/// <summary>
/// The voice peer of Android, over VoicePeer.java. The system chooses the microphone and processes
/// it, with the echo canceller and the noise suppressor of the device, so the settings of the
/// microphone are kept as they are. The sound is routed as for a call while the peer lives.
/// Screens are decoded here, by MediaCodec where the device can, and handed out as I420 pictures.
/// </summary>
internal sealed class AndroidVoicePeer : IVoicePeer
{
    private readonly VoicePeer _peer;

    public AndroidVoicePeer(IVoicePeerListener listener)
    {
        VoicePeer.Initialize(global::Android.App.Application.Context);
        _peer = new VoicePeer(new PeerListener(listener));
        VoicePeer.SetCommunicationMode(global::Android.App.Application.Context, true);
    }

    // Only the microphone: no screen is sent from here
    public void Negotiate(string offer, IReadOnlyList<SendSection> sends) =>
        _peer.Negotiate(offer, sends.Select(send => send.Mid).ToArray(), sends.Select(send => (long)send.Ssrc).ToArray());

    public void SetMicrophoneEnabled(bool isEnabled) => _peer.SetMicrophoneEnabled(isEnabled);

    public bool SetMicrophone(AudioInput input, NoiseSuppressionLevel noiseSuppression, bool isEchoCancellationEnabled,
        bool isAutoGainControlEnabled) => true;

    public void SetMicrophoneVolume(double volume)
    {
    }

    public void SetVoiceGate(VoiceGateOptions? voiceGate)
    {
    }

    public void SetVolume(string mid, double volume) => _peer.SetVolume(mid, volume);

    public void RequestStats() => _peer.RequestLevels();

    public void Dispose()
    {
        try
        {
            _peer.Close();
        }
        finally
        {
            _peer.Dispose();
            VoicePeer.SetCommunicationMode(global::Android.App.Application.Context, false);
        }
    }

    private sealed class PeerListener(IVoicePeerListener listener) : Java.Lang.Object, VoicePeer.IListener
    {
        public void OnConnectionChanged(string? state) => listener.OnConnectionChanged(state switch
        {
            "NEW" => WebRTCConnectionState.New,
            "CONNECTING" => WebRTCConnectionState.Connecting,
            "CONNECTED" => WebRTCConnectionState.Connected,
            "DISCONNECTED" => WebRTCConnectionState.Disconnected,
            "FAILED" => WebRTCConnectionState.Failed,
            _ => WebRTCConnectionState.Closed
        });

        public void OnNegotiated(string? localSdp) => listener.OnNegotiated(localSdp ?? string.Empty);

        public void OnFailed(string? error) => listener.OnFailed(error ?? "unknown");

        /// <summary>"mid=level;...;local=level", see VoicePeer.Listener.onLevels.</summary>
        public void OnLevels(string? levels)
        {
            var parsed = new List<VoiceLevel>();

            foreach (var item in (levels ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = item.Split('=', 2);

                if (parts.Length == 2 && double.TryParse(parts[1], CultureInfo.InvariantCulture, out var level))
                    parsed.Add(new VoiceLevel(parts[0] == "local" ? null : parts[0], level));
            }

            listener.OnStats(new VoiceStats(parsed));
        }

        // On the decoding thread of the stream, where an exception would reach libwebrtc
        public void OnPicture(string? mid, VoicePeer.Picture? picture)
        {
            if (picture == null)
                return;

            AndroidPictureFrame frame;

            try
            {
                frame = AndroidPictureFrame.Wrap(picture);
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                if (mid != null)
                    listener.OnVideoFrame(mid, new RawVideoFrame(IntPtr.Zero, frame.Width, frame.Height, 0)
                    {
                        Timestamp = picture.TimestampNs,
                        Gpu = frame
                    });
            }
            catch (Exception)
            {
                // The picture is dropped
            }
            finally
            {
                // A view that keeps the picture took a reference of its own
                frame.Dispose();
            }
        }
    }
}
