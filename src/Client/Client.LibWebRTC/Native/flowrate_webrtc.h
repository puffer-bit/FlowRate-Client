/*
 * The voice of a session over libwebrtc (webrtc-sdk/libwebrtc), as a plain C interface for P/Invoke.
 * The counterpart of VoicePeer.java on Android: the description of the SFU is built on the .NET side,
 * this only answers it, captures the microphone and plays the voices.
 *
 * Callbacks come on the threads of libwebrtc: the signaling thread for the peer, the audio thread
 * for the capture level. None of them may block for long.
 */
#ifndef FLOWRATE_WEBRTC_H
#define FLOWRATE_WEBRTC_H

#include <stdint.h>

#if defined(_WIN32)
#define FR_API __declspec(dllexport)
#else
#define FR_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct fr_peer fr_peer;
typedef struct fr_video_source fr_video_source;
typedef struct fr_audio_source fr_audio_source;

/* The values of RTCPeerConnectionState: new, connecting, connected, disconnected, failed, closed. */
typedef void (*fr_state_callback)(void* context, int state);

/* The answer of a round, applied as the local description. */
typedef void (*fr_negotiated_callback)(void* context, const char* local_sdp);

typedef void (*fr_failed_callback)(void* context, const char* error);

/*
 * Every report of a reading of the statistics, as a JSON array of RTCStats objects, and one object
 * {"type":"flowrate-voice-level","mid":...,"peak":0..32767} per voice we receive: its loudest sample
 * since the previous reading, before the playback volume.
 */
typedef void (*fr_stats_callback)(void* context, const char* json);

/*
 * The peak of the last 10 ms of the microphone, in dBFS after the input volume; returns whether the
 * voice gate is open. Called on the audio thread, 100 times a second, only while the gate is on.
 */
typedef int (*fr_level_callback)(void* context, double peak_db);

typedef struct fr_peer_callbacks {
    fr_state_callback state;
    fr_negotiated_callback negotiated;
    fr_failed_callback failed;
    fr_stats_callback stats;
} fr_peer_callbacks;

/* Loads libwebrtc and opens the audio devices once per process; 0 on failure. */
FR_API int fr_initialize(void);

/* The audio devices: count, then name and id (the PulseAudio or WASAPI one) by index. */
FR_API int fr_recording_device_count(void);
FR_API int fr_playout_device_count(void);
FR_API int fr_recording_device(int index, char* name, int name_size, char* guid, int guid_size);
FR_API int fr_playout_device(int index, char* name, int name_size, char* guid, int guid_size);
FR_API int fr_set_recording_device(int index);
FR_API int fr_set_playout_device(int index);

/*
 * The id of the device the system plays to or records from by default (its WASAPI endpoint id, as
 * the device list gives it); 0 where there is none to ask, as on Linux, whose list starts with it.
 */
FR_API int fr_default_device(int is_recording, char* guid, int guid_size);

/*
 * What happens to the microphone after the processing of libwebrtc: the input volume, then the voice
 * gate, which asks on_level whether it is open while it is on. Shared by every peer, as the
 * microphone is.
 */
FR_API void fr_set_capture_volume(double volume);
FR_API void fr_set_voice_gate(fr_level_callback on_level, void* context);

/* A peer connection with a microphone track that stays muted until a section sends it. */
FR_API fr_peer* fr_peer_create(const fr_peer_callbacks* callbacks, void* context,
                               int echo_cancellation, int noise_suppression, int auto_gain_control);

/* A section we send in, and what goes into it. */
typedef struct fr_send_section {
    const char* mid;
    /* The SSRC the SFU expects there, and the one of the RTX stream (0 keeps the one of libwebrtc) */
    uint32_t ssrc;
    uint32_t rtx_ssrc;
    /* The picture of a video section; null sends nothing in one */
    fr_video_source* video;
    /* The sound of an audio section; null sends the microphone */
    fr_audio_source* audio;
} fr_send_section;

/*
 * Runs one offer/answer round: applies the offer of the SFU, puts what we send into the sections we
 * send in, answers, and gives each of those sections the SSRCs the SFU expects there.
 */
FR_API void fr_peer_negotiate(fr_peer* peer, const char* offer, const fr_send_section* sends, int send_count);

FR_API void fr_peer_set_microphone_enabled(fr_peer* peer, int is_enabled);

/*
 * Changes where the sound we send comes from and how it is processed: a new track replaces the one the
 * sections send. A custom source takes its sound from fr_peer_capture instead of the microphone, and
 * goes past the processing of libwebrtc and past the input volume and the voice gate.
 */
FR_API void fr_peer_set_audio_options(fr_peer* peer, int is_custom, int echo_cancellation, int noise_suppression,
                                      int auto_gain_control);

/* 10 ms of 16-bit interleaved sound for a custom source; nothing for the microphone. */
FR_API void fr_peer_capture(fr_peer* peer, const int16_t* samples, int frames, int sample_rate, int channels);

/* The playback volume of the voice of a section, 0 silences it. */
FR_API void fr_peer_set_volume(fr_peer* peer, const char* mid, double volume);

/* Reads the statistics once; they come in callbacks.stats. */
FR_API void fr_peer_request_stats(fr_peer* peer);

/* Closes the connection; no callback comes once it returns. */
FR_API void fr_peer_destroy(fr_peer* peer);

/*
 * One picture of a video stream we receive, as libwebrtc put it together from its packets: an H.264
 * access unit in Annex B with its parameter sets before a keyframe, or a whole VP8 frame. libwebrtc
 * only hands on what it can decode, so the stream starts with a keyframe and has no gaps. Comes on
 * the decoding thread of the stream, one per stream; the data is only valid during the call.
 * timestamp_ns is the RTP time of the picture in nanoseconds, counted from an arbitrary start.
 */
typedef void (*fr_video_frame_callback)(void* context, uint32_t ssrc, const uint8_t* data, int size,
                                        int64_t timestamp_ns, int is_keyframe);

/*
 * Where the pictures of every video stream of the process go, told apart by their SSRC. The video
 * is not decoded by libwebrtc: whoever receives them decodes them. Set before any video section.
 */
FR_API void fr_set_video_receiver(fr_video_frame_callback on_frame, void* context);

/* Asks the sender of a stream for a keyframe, for a decoder that cannot go on without one. */
FR_API void fr_video_request_keyframe(uint32_t ssrc);

/* The bitrate libwebrtc wants the pictures of a source encoded at now, in kbit/s. */
typedef void (*fr_bitrate_callback)(void* context, int kbps);

/* libwebrtc wants the next picture of a source to be a keyframe, as a viewer joined or lost one. */
typedef void (*fr_keyframe_request_callback)(void* context);

typedef struct fr_video_source_callbacks {
    fr_bitrate_callback bitrate;
    fr_keyframe_request_callback keyframe;
} fr_video_source_callbacks;

/*
 * A video we send whose pictures come encoded from outside: libwebrtc packetizes, paces and resends
 * them, and asks the source for its bitrate and its keyframes through the callbacks, which come on
 * its encoder thread. width and height are only what libwebrtc takes the stream for; max_framerate
 * is the most frames a second it lets through. Goes into a section through fr_peer_negotiate.
 */
FR_API fr_video_source* fr_video_source_create(const fr_video_source_callbacks* callbacks, void* context, int width,
                                               int height, int max_framerate);

/*
 * The next picture: an H.264 access unit in Annex B, with its parameter sets before a keyframe, or a
 * whole VP8 frame, in the codec of the section it is sent in. Copied; any thread, one at a time.
 */
FR_API void fr_video_source_push(fr_video_source* source, const uint8_t* data, int size, int is_keyframe);

/* Stops the source; no callback comes once it returns. A section that still sends it sends nothing. */
FR_API void fr_video_source_destroy(fr_video_source* source);

/*
 * A sound we send whose samples come from outside, as the sound of a shown screen: no processing of
 * the microphone touches it. Goes into a section through fr_peer_negotiate.
 */
FR_API fr_audio_source* fr_audio_source_create(void);

/*
 * The next 10 ms of the sound: 16-bit samples, interleaved when there are two channels. Any thread,
 * one at a time.
 */
FR_API void fr_audio_source_push(fr_audio_source* source, const int16_t* samples, int frames, int sample_rate,
                                 int channels);

/* Stops the source. A section that still sends it sends nothing. */
FR_API void fr_audio_source_destroy(fr_audio_source* source);

#ifdef __cplusplus
}
#endif

#endif
