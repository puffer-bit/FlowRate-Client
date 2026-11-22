using System.Globalization;
using Client.Application.Ports;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.Core.Settings;
using Client.WebRTC.Sdp;
using Microsoft.Extensions.Logging;
using Shared.Enums.WebRTC;
using Shared.Models.Media;

namespace Client.LibWebRTC.Transport;

public sealed partial class LibWebRTCTransport
{
    public bool StartOutbound(OutboundTrack outbound, AudioInput input, double volume, VoiceGateOptions? voiceGate,
        NoiseSuppressionLevel noiseSuppression, bool isEchoCancellationEnabled, bool isAutoGainControlEnabled)
    {
        if (outbound.MediaType != MediaType.Audio)
            return false;

        // A screen is heard as it plays: nothing of the microphone, its gate or its volume applies
        if (outbound.SourceType == MediaSourceType.Screen)
            return StartSound(outbound, input, volume);

        if (outbound.SourceType != MediaSourceType.Microphone)
            return false;

        IMediaPeer? peer;

        lock (_lock)
        {
            peer = _peer;

            if (peer == null || _outbounds.ContainsKey(outbound.Id))
                return false;
        }

        // Never inside the lock: the peer waits for the threads of libwebrtc, and its callbacks on
        // them wait for the lock
        if (!peer.SetMicrophone(input, noiseSuppression, isEchoCancellationEnabled, isAutoGainControlEnabled))
            return false;

        peer.SetMicrophoneVolume(volume);
        peer.SetVoiceGate(voiceGate);
        peer.SetMicrophoneEnabled(true);

        lock (_lock)
        {
            if (_peer != peer || !_outbounds.TryAdd(outbound.Id, outbound))
                return false;

            ClaimSendingSlot(outbound, AudioKind);
            _isMicrophoneMuted = false;
        }

        LogOutboundStarted(_sessionId, outbound.SSRC);

        return Negotiate();
    }

    /// <summary>
    /// Sends the sound of a shown screen: <see cref="IAudioMedia"/> captures it, and libwebrtc encodes
    /// it into a section of its own, untouched by the processing of the microphone.
    /// </summary>
    private bool StartSound(OutboundTrack outbound, AudioInput input, double volume)
    {
        IMediaPeer? peer;

        lock (_lock)
        {
            peer = _peer;

            if (peer == null || _audio == null)
                return false;

            if (_outbounds.ContainsKey(outbound.Id))
                return true;
        }

        var capture = _audio.CreateCapture(input, _sessionId.ToString(CultureInfo.InvariantCulture));

        if (capture == null)
        {
            LogSoundCaptureFailed(_sessionId, input);
            return false;
        }

        if (peer.CreateAudioSource() is not { } source)
        {
            capture.Dispose();
            return false;
        }

        var sound = new OutgoingSound(capture, source) { Volume = volume };

        lock (_lock)
        {
            if (_peer != peer || !_outbounds.TryAdd(outbound.Id, outbound))
            {
                sound.Dispose();
                return false;
            }

            _outgoingSounds[outbound.Id] = sound;
            ClaimSendingSlot(outbound, AudioKind);
        }

        if (!sound.Start())
        {
            StopOutbound(outbound.Id);
            return false;
        }

        LogScreenSoundStarted(_sessionId, outbound.SSRC);

        return Negotiate();
    }

    /// <summary>
    /// Shows a screen or a camera: <see cref="IVideoMedia"/> captures and encodes it, or the peer does
    /// both itself (<see cref="IMediaPeer.CreateVideoCapture"/>); libwebrtc sends the pictures.
    /// What is sent also comes back through <see cref="VideoFrameReady"/>, as our own screen is not
    /// received from the SFU.
    /// </summary>
    public bool StartOutbound(OutboundTrack outbound, VideoInput input, ScreenShareSettings settings)
    {
        if (outbound.MediaType != MediaType.Video || RtpCodec.VideoCodecOf(outbound.MimeType) is not { } codec)
            return false;

        IMediaPeer? peer;

        lock (_lock)
        {
            peer = _peer;

            if (peer == null)
                return false;

            if (_outbounds.ContainsKey(outbound.Id))
                return true;
        }

        // A peer that captures the input itself, as the camera of a phone, also encodes it: never inside
        // the lock, like the microphone
        var ownCapture = peer.CreateVideoCapture(input, settings);
        var capture = ownCapture ?? _video?.CreateCapture(input, settings, codec, _sessionId.ToString(CultureInfo.InvariantCulture));

        if (capture == null)
        {
            LogCaptureFailed(_sessionId, input, codec, "Failed to create the capture; see the media backend logs");
            return false;
        }

        var video = new OutgoingVideo(capture, settings, kbps => LogScreenBitrate(_sessionId, kbps));

        // The size is what libwebrtc takes the stream for: the one of the setting, as wide as a usual screen
        var height = settings.MaxHeight;
        var width = (height * 16 / 9 + 1) & ~1;

        if (((IVideoSendSource?)ownCapture ?? peer.CreateVideoSource(width, height, settings.Framerate, video)) is not { } source)
        {
            capture.Dispose();
            LogCaptureFailed(_sessionId, input, codec,
                $"Failed to create the libwebrtc video source ({width}x{height} at {settings.Framerate} fps)");
            return false;
        }

        video.Source = source;
        capture.Failed += _ => VideoCaptureFailed?.Invoke(outbound.Id);
        capture.EncodedFrameReady += source.Push;
        capture.PreviewFrameReady += frame => VideoFrameReady?.Invoke(outbound.UserId, outbound.SourceType, frame);

        lock (_lock)
        {
            if (_peer != peer || !_outbounds.TryAdd(outbound.Id, outbound))
            {
                video.Dispose();
                return false;
            }

            _outgoingVideos[outbound.Id] = video;
            ClaimSendingSlot(outbound, VideoKind);
        }

        // The pictures before the round that puts the source into its section go nowhere; libwebrtc
        // asks for a keyframe with its first one
        if (!capture.Start())
        {
            StopOutbound(outbound.Id);
            LogCaptureFailed(_sessionId, input, codec, "Failed to start the capture; see the media backend logs");
            return false;
        }

        LogScreenStarted(_sessionId, outbound.SSRC, capture.Description);

        return Negotiate();
    }

    /// <summary>Gives a track a section we send in, an emptied one of its kind and codec where there is one. Only inside the lock.</summary>
    private void ClaimSendingSlot(OutboundTrack outbound, string kind)
    {
        var slot = _slots.FirstOrDefault(item => item.IsSend && item.OutboundId == null && item.Kind == kind &&
                                                 item.Carries(outbound.MimeType, RtpCodec.FromOutbound(outbound).IsNackEnabled));

        // A new producer replaces the paused screen rather than continuing its RTP sequence.
        foreach (var previous in _slots.Where(item => item.IsSend && item.Kind == kind &&
                                                     item.PausedSsrc != null && item.PausedSsrc != outbound.SSRC))
        {
            previous.PausedSsrc = null;
            previous.PausedRtxSsrc = 0;
        }

        if (slot == null)
            _slots.Add(slot = new Slot { IsSend = true, Kind = kind });

        slot.OutboundId = outbound.Id;

        // The same for every track of the server; an extension once negotiated keeps its id
        if (_transportCcExtensionId <= 0)
            _transportCcExtensionId = outbound.TransportCcExtensionId;
        slot.PausedSsrc = null;
        slot.PausedRtxSsrc = 0;
    }

    public void StopOutbound(string outboundId)
    {
        IMediaPeer? peer;
        OutgoingVideo? video;
        OutgoingSound? sound;
        bool isMicrophone;
        bool isLastMicrophone;

        lock (_lock)
        {
            if (!_outbounds.Remove(outboundId, out var outbound))
                return;

            _outgoingVideos.Remove(outboundId, out video);
            _outgoingSounds.Remove(outboundId, out sound);

            foreach (var slot in _slots.Where(slot => slot.OutboundId == outboundId))
            {
                slot.OutboundId = null;

                // The SFU only pauses a screen that stops, see Slot.PausedSsrc
                if (video != null)
                {
                    slot.PausedSsrc = outbound.SSRC;
                    slot.PausedRtxSsrc = SendSections.RtxSsrcOf(outbound);
                }
            }

            peer = _peer;
            isMicrophone = IsMicrophone(outbound);
            isLastMicrophone = !_outbounds.Values.Any(IsMicrophone);
        }

        // Outside the lock: stopping the capture waits for its threads
        video?.Dispose();
        sound?.Dispose();

        if (isMicrophone && isLastMicrophone)
            peer?.StopMicrophone();

        Negotiate();
    }

    public bool SwitchOutboundInput(string outboundId, VideoInput input)
    {
        OutgoingVideo? video;

        lock (_lock)
            _outgoingVideos.TryGetValue(outboundId, out video);

        return video?.SwitchInput(input) == true;
    }

    public void SetOutboundPreviewShown(string outboundId, bool isShown)
    {
        OutgoingVideo? video;

        lock (_lock)
            _outgoingVideos.TryGetValue(outboundId, out video);

        video?.SetPreviewShown(isShown);
    }

    public void SetOutboundPreviewHeight(string outboundId, int height)
    {
        OutgoingVideo? video;

        lock (_lock)
            _outgoingVideos.TryGetValue(outboundId, out video);

        video?.SetPreviewHeight(height);
    }

    public bool SetOutboundMuted(string outboundId, bool isMuted)
    {
        lock (_lock)
        {
            if (_outgoingSounds.TryGetValue(outboundId, out var sound))
            {
                sound.IsMuted = isMuted;
                return true;
            }
        }

        if (FindOutboundPeer(outboundId) is not { } peer)
            return false;

        lock (_lock)
            _isMicrophoneMuted = isMuted;

        peer.SetMicrophoneEnabled(!isMuted);
        return true;
    }

    public void SetOutboundVolume(string outboundId, double volume)
    {
        lock (_lock)
        {
            if (_outgoingSounds.TryGetValue(outboundId, out var sound))
            {
                sound.Volume = volume;
                return;
            }
        }

        FindOutboundPeer(outboundId)?.SetMicrophoneVolume(volume);
    }

    public void SetOutboundInput(string outboundId, AudioInput input, NoiseSuppressionLevel noiseSuppression,
        bool isEchoCancellationEnabled, bool isAutoGainControlEnabled)
    {
        OutgoingSound? sound;

        lock (_lock)
            sound = _outgoingSounds.GetValueOrDefault(outboundId);

        if (sound == null)
        {
            FindOutboundPeer(outboundId)?.SetMicrophone(input, noiseSuppression, isEchoCancellationEnabled,
                isAutoGainControlEnabled);
            return;
        }

        // Outside the lock: stopping the old capture waits for its thread
        if (_audio?.CreateCapture(input, _sessionId.ToString(CultureInfo.InvariantCulture)) is { } capture)
            sound.Replace(capture);
        else
            LogSoundCaptureFailed(_sessionId, input);
    }

    public void SetVoiceGate(string outboundId, VoiceGateOptions? voiceGate) =>
        FindOutboundPeer(outboundId)?.SetVoiceGate(voiceGate);

    /// <summary>The peer, while it sends the track of the microphone.</summary>
    private IMediaPeer? FindOutboundPeer(string outboundId)
    {
        lock (_lock)
            return _outbounds.TryGetValue(outboundId, out var outbound) && IsMicrophone(outbound) ? _peer : null;
    }

    private static bool IsMicrophone(OutboundTrack outbound) =>
        outbound.MediaType == MediaType.Audio && outbound.SourceType == MediaSourceType.Microphone;
}
