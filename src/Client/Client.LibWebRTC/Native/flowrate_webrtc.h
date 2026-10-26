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

/*
 * Runs one offer/answer round: applies the offer of the SFU, puts the microphone into the sections
 * we send in, answers, and gives each of those sections the SSRC the SFU expects there.
 */
FR_API void fr_peer_negotiate(fr_peer* peer, const char* offer, const char* const* send_mids,
                              const uint32_t* send_ssrcs, int send_count);

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

#ifdef __cplusplus
}
#endif

#endif
