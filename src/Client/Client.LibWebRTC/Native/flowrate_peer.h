// A peer connection to the SFU, behind an fr_peer: it answers the offers of the SFU, sends the
// microphone and the sources of the rounds, and plays the voices it receives.

#ifndef FLOWRATE_PEER_H
#define FLOWRATE_PEER_H

#include <cstdint>
#include <memory>
#include <string>

#include "rtc_peerconnection.h"
#include "rtc_rtp_transceiver.h"

#include "flowrate_audio_source.h"
#include "flowrate_callback_gate.h"
#include "flowrate_voices.h"
#include "flowrate_webrtc.h"

namespace flowrate {

class Peer : public libwebrtc::RTCPeerConnectionObserver {
 public:
  // Null before the runtime is initialized or when libwebrtc creates no connection
  static Peer* Create(const fr_peer_callbacks& callbacks, void* context);

  // See fr_peer_negotiate
  void Negotiate(const char* offer, const fr_send_section* sends, int send_count);

  void SetMicrophoneEnabled(bool is_enabled) { microphone_->track()->set_enabled(is_enabled); }

  // The next 10 ms of the processed microphone
  void Capture(const int16_t* samples, int frames, int sample_rate, int channels) {
    microphone_->Push(samples, frames, sample_rate, channels);
  }

  void SetVolume(const std::string& mid, double volume) { voices_.SetVolume(mid, volume); }

  // Reads the statistics once; they come in callbacks.stats
  void RequestStats();

  // Closes the connection and deletes the peer; no callback comes once it returns
  void Destroy();

  void OnPeerConnectionState(libwebrtc::RTCPeerConnectionState state) override;
  void OnTrack(libwebrtc::scoped_refptr<libwebrtc::RTCRtpTransceiver> transceiver) override;

  void OnSignalingState(libwebrtc::RTCSignalingState) override {}
  void OnIceGatheringState(libwebrtc::RTCIceGatheringState) override {}
  void OnIceConnectionState(libwebrtc::RTCIceConnectionState) override {}
  // mediasoup is ICE lite and learns our address from the checks, so nothing is trickled
  void OnIceCandidate(libwebrtc::scoped_refptr<libwebrtc::RTCIceCandidate>) override {}
  void OnAddStream(libwebrtc::scoped_refptr<libwebrtc::RTCMediaStream>) override {}
  void OnRemoveStream(libwebrtc::scoped_refptr<libwebrtc::RTCMediaStream>) override {}
  void OnDataChannel(libwebrtc::scoped_refptr<libwebrtc::RTCDataChannel>) override {}
  void OnRenegotiationNeeded() override {}
  void OnAddTrack(libwebrtc::vector<libwebrtc::scoped_refptr<libwebrtc::RTCMediaStream>>,
                  libwebrtc::scoped_refptr<libwebrtc::RTCRtpReceiver>) override {}
  void OnRemoveTrack(libwebrtc::scoped_refptr<libwebrtc::RTCRtpReceiver>) override {}

 private:
  struct Round;

  Peer(const fr_peer_callbacks& callbacks, void* context, std::unique_ptr<AudioSource> microphone)
      : callbacks_(callbacks), context_(context), microphone_(std::move(microphone)) {}

  // Puts what a round sends into the sections, answers and applies the answer
  void Answer(const std::shared_ptr<Round>& round);

  // What one section sends in a round, or that it sends nothing any more
  void AttachTransceiver(const libwebrtc::scoped_refptr<libwebrtc::RTCRtpTransceiver>& transceiver,
                         const Round& round);

  // The encodings of a sender are there once it sends: after the answer is applied
  void ConfigureVideoSenders(const Round& round);

  void Fail(const std::string& step, const char* error);

  const fr_peer_callbacks callbacks_;
  void* const context_;
  // Shared with the callbacks of libwebrtc, which may come after the peer is deleted
  const std::shared_ptr<CallbackGate> gate_ = std::make_shared<CallbackGate>();
  const std::unique_ptr<AudioSource> microphone_;
  libwebrtc::scoped_refptr<libwebrtc::RTCPeerConnection> connection_;
  Voices voices_;
};

}  // namespace flowrate

#endif
