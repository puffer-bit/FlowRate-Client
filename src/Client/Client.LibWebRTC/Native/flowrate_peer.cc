#include "flowrate_peer.h"

#include <map>
#include <vector>

#include "rtc_audio_track.h"
#include "rtc_mediaconstraints.h"
#include "rtc_peerconnection_factory.h"
#include "rtc_rtp_receiver.h"
#include "rtc_rtp_sender.h"

#include "flowrate_runtime.h"
#include "flowrate_sdp.h"
#include "flowrate_video_source.h"

namespace flowrate {

// One offer/answer round, kept alive by the callbacks of its steps
struct Peer::Round {
  Peer* peer;
  std::shared_ptr<CallbackGate> gate;
  std::map<std::string, SendSsrcs> sends;
  // The video sources of the sections that send one, by mid
  std::map<std::string, webrtc::scoped_refptr<VideoSource>> videos;
  // The sounds of the audio sections that send one, by mid; the others send the microphone
  std::map<std::string, libwebrtc::scoped_refptr<libwebrtc::RTCAudioTrack>> audios;
};

Peer* Peer::Create(const fr_peer_callbacks& callbacks, void* context) {
  auto factory = Runtime::Factory();

  if (factory == nullptr)
    return nullptr;

  auto microphone = AudioSource::Create(AudioSource::Kind::kMicrophone);

  if (microphone == nullptr)
    return nullptr;

  libwebrtc::RTCConfiguration config;
  // Every section on one transport, which is what keeps all the clients of the SFU on one port
  config.bundle_policy = libwebrtc::kBundlePolicyMaxBundle;
  config.rtcp_mux_policy = libwebrtc::RtcpMuxPolicy::kRtcpMuxPolicyRequire;
  config.sdp_semantics = libwebrtc::SdpSemantics::kUnifiedPlan;

  auto peer = new Peer(callbacks, context, std::move(microphone));
  peer->connection_ = factory->Create(config, libwebrtc::RTCMediaConstraints::Create());

  if (peer->connection_ == nullptr) {
    delete peer;
    return nullptr;
  }

  peer->connection_->RegisterRTCPeerConnectionObserver(peer);
  return peer;
}

void Peer::Negotiate(const char* offer, const fr_send_section* sends, int send_count) {
  auto round = std::make_shared<Round>();
  round->peer = this;
  round->gate = gate_;

  for (int i = 0; i < send_count; i++) {
    const auto& send = sends[i];
    round->sends[send.mid] = {send.ssrc, send.rtx_ssrc};

    if (send.video != nullptr)
      round->videos[send.mid] = webrtc::scoped_refptr<VideoSource>(reinterpret_cast<VideoSource*>(send.video));

    if (send.audio != nullptr)
      round->audios[send.mid] = reinterpret_cast<AudioSource*>(send.audio)->track();
  }

  connection_->SetRemoteDescription(
      offer, "offer", [round] { round->gate->Run([&] { round->peer->Answer(round); }); },
      [round](const char* error) {
        round->gate->Run([&] { round->peer->Fail("set-remote-description", error); });
      });
}

void Peer::Answer(const std::shared_ptr<Round>& round) {
  for (auto& transceiver : connection_->transceivers().std_vector())
    AttachTransceiver(transceiver, *round);

  connection_->CreateAnswer(
      [round](const libwebrtc::string sdp, const libwebrtc::string) {
        round->gate->Run([&] {
          auto local = std::make_shared<std::string>(RewriteSsrcs(sdp.std_string(), round->sends));

          round->peer->connection_->SetLocalDescription(
              local->c_str(), "answer",
              [round, local] {
                round->gate->Run([&] {
                  round->peer->ConfigureVideoSenders(*round);
                  round->peer->callbacks_.negotiated(round->peer->context_, local->c_str());
                });
              },
              [round](const char* error) {
                round->gate->Run([&] { round->peer->Fail("set-local-description", error); });
              });
        });
      },
      [round](const char* error) { round->gate->Run([&] { round->peer->Fail("create-answer", error); }); },
      libwebrtc::RTCMediaConstraints::Create());
}

void Peer::AttachTransceiver(const libwebrtc::scoped_refptr<libwebrtc::RTCRtpTransceiver>& transceiver,
                             const Round& round) {
  auto mid = transceiver->mid().std_string();
  auto sender = transceiver->sender();

  if (!mid.empty() && round.sends.count(mid) > 0) {
    transceiver->SetDirectionWithError(libwebrtc::RTCRtpTransceiverDirection::kSendOnly);

    if (auto video = round.videos.find(mid); video != round.videos.end())
      NativeSender(sender.get())->SetTrack(video->second->Track().get());
    else if (transceiver->media_type() == libwebrtc::RTCMediaType::VIDEO)
      // A screen that stopped: its stream stays, silent, and goes on where it was when it is back
      sender->set_track(nullptr);
    else if (auto audio = round.audios.find(mid); audio != round.audios.end())
      sender->set_track(audio->second);
    else
      sender->set_track(microphone_->track());
  } else if (sender->track() != nullptr) {
    // A track that stopped: its section stays, emptied
    sender->set_track(nullptr);
    transceiver->SetDirectionWithError(libwebrtc::RTCRtpTransceiverDirection::kRecvOnly);
  }
}

void Peer::ConfigureVideoSenders(const Round& round) {
  for (auto& transceiver : connection_->transceivers().std_vector()) {
    if (auto video = round.videos.find(transceiver->mid().std_string()); video != round.videos.end())
      video->second->ConfigureSender(NativeSender(transceiver->sender().get()));
  }
}

void Peer::Fail(const std::string& step, const char* error) {
  std::string message = step + ": " + (error ? error : "unknown");
  gate_->Run([&] { callbacks_.failed(context_, message.c_str()); });
}

void Peer::OnPeerConnectionState(libwebrtc::RTCPeerConnectionState state) {
  gate_->Run([&] { callbacks_.state(context_, static_cast<int>(state)); });
}

void Peer::OnTrack(libwebrtc::scoped_refptr<libwebrtc::RTCRtpTransceiver> transceiver) {
  auto mid = transceiver->mid().std_string();
  auto track = transceiver->receiver()->track();

  if (mid.empty() || track == nullptr || track->kind().std_string() != "audio")
    return;

  auto voice = libwebrtc::scoped_refptr<libwebrtc::RTCAudioTrack>(static_cast<libwebrtc::RTCAudioTrack*>(track.get()));
  gate_->Run([&] { voices_.Attach(mid, voice); });
}

void Peer::RequestStats() {
  auto gate = gate_;

  connection_->GetStats(
      [this, gate](const libwebrtc::vector<libwebrtc::scoped_refptr<libwebrtc::MediaRTCStats>> reports) {
        std::string json = "[";

        for (size_t i = 0; i < reports.size(); i++) {
          if (i > 0)
            json += ",";

          json += reports[i]->ToJson().std_string();
        }

        gate->Run([&] {
          // The levels of the voices as entries of their own, see LevelSink
          voices_.AppendLevels(json);
          json += "]";
          callbacks_.stats(context_, json.c_str());
        });
      },
      [](const char*) {});
}

void Peer::Destroy() {
  gate_->Close();
  connection_->DeRegisterRTCPeerConnectionObserver();
  voices_.Clear();
  connection_->Close();

  if (auto factory = Runtime::Factory())
    factory->Delete(connection_);

  // A callback still queued finds the gate closed and leaves the peer alone
  delete this;
}

}  // namespace flowrate
