using System.Runtime.InteropServices;

namespace Client.LibWebRTC.Native;

/// <summary>
/// The functions of flowrate_webrtc.h, the C interface over libwebrtc next to the executable.
/// </summary>
internal static unsafe partial class NativeMethods
{
    private const string Library = "flowrate_webrtc";

    [StructLayout(LayoutKind.Sequential)]
    public struct PeerCallbacks
    {
        public delegate* unmanaged<nint, int, void> State;
        public delegate* unmanaged<nint, byte*, void> Negotiated;
        public delegate* unmanaged<nint, byte*, void> Failed;
        public delegate* unmanaged<nint, byte*, void> Stats;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VideoSourceCallbacks
    {
        public delegate* unmanaged<nint, int, void> Bitrate;
        public delegate* unmanaged<nint, void> KeyFrame;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SendSection
    {
        public byte* Mid;
        public uint Ssrc;
        public uint RtxSsrc;
        public nint Video;
        public nint Audio;
    }

    [LibraryImport(Library, EntryPoint = "fr_initialize")]
    public static partial int Initialize();

    [LibraryImport(Library, EntryPoint = "fr_recording_device_count")]
    public static partial int RecordingDeviceCount();

    [LibraryImport(Library, EntryPoint = "fr_recording_device")]
    public static partial int RecordingDevice(int index, byte* name, int nameSize, byte* guid, int guidSize);

    [LibraryImport(Library, EntryPoint = "fr_set_recording_device")]
    public static partial int SetRecordingDevice(int index);

    [LibraryImport(Library, EntryPoint = "fr_playout_device_count")]
    public static partial int PlayoutDeviceCount();

    [LibraryImport(Library, EntryPoint = "fr_playout_device")]
    public static partial int PlayoutDevice(int index, byte* name, int nameSize, byte* guid, int guidSize);

    [LibraryImport(Library, EntryPoint = "fr_set_playout_device")]
    public static partial int SetPlayoutDevice(int index);

    [LibraryImport(Library, EntryPoint = "fr_default_device")]
    public static partial int DefaultDevice(int isRecording, byte* guid, int guidSize);

    [LibraryImport(Library, EntryPoint = "fr_set_capture_volume")]
    public static partial void SetCaptureVolume(double volume);

    [LibraryImport(Library, EntryPoint = "fr_set_voice_gate")]
    public static partial void SetVoiceGate(delegate* unmanaged<nint, double, int> onLevel, nint context);

    [LibraryImport(Library, EntryPoint = "fr_peer_create")]
    public static partial nint PeerCreate(PeerCallbacks* callbacks, nint context, int echoCancellation,
        int noiseSuppression, int autoGainControl);

    [LibraryImport(Library, EntryPoint = "fr_peer_negotiate", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void PeerNegotiate(nint peer, string offer, SendSection* sends, int sendCount);

    [LibraryImport(Library, EntryPoint = "fr_peer_set_microphone_enabled")]
    public static partial void PeerSetMicrophoneEnabled(nint peer, int isEnabled);

    [LibraryImport(Library, EntryPoint = "fr_peer_set_audio_options")]
    public static partial void PeerSetAudioOptions(nint peer, int isCustom, int echoCancellation, int noiseSuppression,
        int autoGainControl);

    [LibraryImport(Library, EntryPoint = "fr_peer_capture")]
    public static partial void PeerCapture(nint peer, short* samples, int frames, int sampleRate, int channels);

    [LibraryImport(Library, EntryPoint = "fr_peer_set_volume", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void PeerSetVolume(nint peer, string mid, double volume);

    [LibraryImport(Library, EntryPoint = "fr_peer_request_stats")]
    public static partial void PeerRequestStats(nint peer);

    [LibraryImport(Library, EntryPoint = "fr_peer_destroy")]
    public static partial void PeerDestroy(nint peer);

    [LibraryImport(Library, EntryPoint = "fr_set_video_receiver")]
    public static partial void SetVideoReceiver(delegate* unmanaged<nint, uint, byte*, int, long, int, void> onFrame,
        nint context);

    [LibraryImport(Library, EntryPoint = "fr_video_request_keyframe")]
    public static partial void VideoRequestKeyFrame(uint ssrc);

    [LibraryImport(Library, EntryPoint = "fr_video_source_create")]
    public static partial nint VideoSourceCreate(VideoSourceCallbacks* callbacks, nint context, int width, int height,
        int maxFramerate);

    [LibraryImport(Library, EntryPoint = "fr_video_source_push")]
    public static partial void VideoSourcePush(nint source, byte* data, int size, int isKeyFrame);

    [LibraryImport(Library, EntryPoint = "fr_video_source_destroy")]
    public static partial void VideoSourceDestroy(nint source);

    [LibraryImport(Library, EntryPoint = "fr_audio_source_create")]
    public static partial nint AudioSourceCreate();

    [LibraryImport(Library, EntryPoint = "fr_audio_source_push")]
    public static partial void AudioSourcePush(nint source, short* samples, int frames, int sampleRate, int channels);

    [LibraryImport(Library, EntryPoint = "fr_audio_source_destroy")]
    public static partial void AudioSourceDestroy(nint source);
}
