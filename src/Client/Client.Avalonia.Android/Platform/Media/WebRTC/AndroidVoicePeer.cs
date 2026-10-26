using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Client.Application.Ports;
using Client.Core.Settings;
using Client.LibWebRTC.Voice;
using Com.Flowrate.Rtc;
using Shared.Enums.WebRTC;

namespace Client.Droid.Platform.Media.WebRTC;

/// <summary>
/// The voice peer of Android, over VoicePeer.java. The system chooses the microphone and processes
/// it, with the echo canceller and the noise suppressor of the device, so the settings of the
/// microphone are kept as they are. The sound is routed as for a call while the peer lives.
/// </summary>
internal sealed class AndroidVoicePeer : IVoicePeer
{
    private readonly VoicePeer _peer;

    public AndroidVoicePeer(IVoicePeerListener listener)
    {
        _peer = new VoicePeer(new PeerListener(listener));
        VoicePeer.SetCommunicationMode(global::Android.App.Application.Context, true);
    }

    public void Negotiate(string offer, IReadOnlyList<string> sendMids, IReadOnlyList<uint> sendSsrcs) =>
        _peer.Negotiate(offer, sendMids.ToArray(), sendSsrcs.Select(ssrc => (long)ssrc).ToArray());

    public void SetMicrophoneEnabled(bool isEnabled) => _peer.SetMicrophoneEnabled(isEnabled);

    public bool SetMicrophone(AudioInput input, NoiseSuppressionLevel noiseSuppression, bool isEchoCancellationEnabled) =>
        true;

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
        _peer.Close();
        _peer.Dispose();
        VoicePeer.SetCommunicationMode(global::Android.App.Application.Context, false);
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
    }
}
