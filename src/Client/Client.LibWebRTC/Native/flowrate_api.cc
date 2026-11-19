// The C interface of flowrate_webrtc.h over the classes of the other flowrate_*.h, which are built over
// the C++ interface of webrtc-sdk/libwebrtc and, for the video, over the classes of WebRTC beneath it.
// Built inside the WebRTC tree and linked into the same library (../Build/linux/build.sh,
// ../Build/windows/build.ps1). Each handle of the interface is the object behind it.

#include "flowrate_webrtc.h"

#include "flowrate_audio.h"
#include "flowrate_audio_source.h"
#include "flowrate_logging.h"
#include "flowrate_peer.h"
#include "flowrate_runtime.h"
#include "flowrate_video_decoder.h"
#include "flowrate_video_source.h"

namespace {

flowrate::Peer* PeerOf(fr_peer* peer) { return reinterpret_cast<flowrate::Peer*>(peer); }

flowrate::VideoSource* VideoSourceOf(fr_video_source* source) {
  return reinterpret_cast<flowrate::VideoSource*>(source);
}

flowrate::AudioSource* AudioSourceOf(fr_audio_source* source) {
  return reinterpret_cast<flowrate::AudioSource*>(source);
}

}  // namespace

extern "C" {

void fr_set_log(fr_log_callback callback, int min_severity) { flowrate::SetLogCallback(callback, min_severity); }

int fr_initialize(void) { return flowrate::Runtime::Initialize() ? 1 : 0; }

int fr_audio_bridge_version(void) { return 1; }

void fr_pull_playout(int16_t* samples) { flowrate::PullPlayout(samples); }

fr_peer* fr_peer_create(const fr_peer_callbacks* callbacks, void* context) {
  return callbacks != nullptr ? reinterpret_cast<fr_peer*>(flowrate::Peer::Create(*callbacks, context)) : nullptr;
}

void fr_peer_negotiate(fr_peer* peer, const char* offer, const fr_send_section* sends, int send_count) {
  PeerOf(peer)->Negotiate(offer, sends, send_count);
}

void fr_peer_set_microphone_enabled(fr_peer* peer, int is_enabled) {
  PeerOf(peer)->SetMicrophoneEnabled(is_enabled != 0);
}

void fr_peer_capture(fr_peer* peer, const int16_t* samples, int frames, int sample_rate, int channels) {
  PeerOf(peer)->Capture(samples, frames, sample_rate, channels);
}

void fr_peer_set_volume(fr_peer* peer, const char* mid, double volume) { PeerOf(peer)->SetVolume(mid, volume); }

void fr_peer_request_stats(fr_peer* peer) { PeerOf(peer)->RequestStats(); }

void fr_peer_destroy(fr_peer* peer) { PeerOf(peer)->Destroy(); }

void fr_set_video_receiver(fr_video_frame_callback on_frame, void* context) {
  flowrate::SetVideoReceiver(on_frame, context);
}

void fr_video_request_keyframe(uint32_t ssrc) { flowrate::RequestKeyFrame(ssrc); }

fr_video_source* fr_video_source_create(const fr_video_source_callbacks* callbacks, void* context, int width,
                                        int height, int max_framerate) {
  if (callbacks == nullptr)
    return nullptr;

  // The handle holds a reference of its own until it is destroyed
  auto source = flowrate::VideoSource::Create(*callbacks, context, width, height, max_framerate);
  return reinterpret_cast<fr_video_source*>(source.release());
}

void fr_video_source_push(fr_video_source* source, const uint8_t* data, int size, int is_keyframe) {
  VideoSourceOf(source)->Push(data, size, is_keyframe != 0);
}

void fr_video_source_take_stats(fr_video_source* source, fr_video_send_stats* stats) {
  *stats = {};
  VideoSourceOf(source)->TakeStats(stats);
}

int fr_video_take_receive_stats(uint32_t ssrc, fr_video_receive_stats* stats) {
  return flowrate::TakeReceiveStats(ssrc, stats) ? 1 : 0;
}

void fr_video_source_destroy(fr_video_source* source) {
  auto* video = VideoSourceOf(source);
  video->Close();
  video->Release();
}

fr_audio_source* fr_audio_source_create(void) {
  auto source = flowrate::AudioSource::Create(flowrate::AudioSource::Kind::kScreen);
  return reinterpret_cast<fr_audio_source*>(source.release());
}

void fr_audio_source_push(fr_audio_source* source, const int16_t* samples, int frames, int sample_rate,
                          int channels) {
  AudioSourceOf(source)->Push(samples, frames, sample_rate, channels);
}

void fr_audio_source_destroy(fr_audio_source* source) { delete AudioSourceOf(source); }

}  // extern "C"
