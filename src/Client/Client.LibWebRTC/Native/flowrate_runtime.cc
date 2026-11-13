#include "flowrate_runtime.h"

#include <cstdlib>
#include <mutex>
#include <vector>

#include "libwebrtc.h"
#include "rtc_audio_processing.h"
#include "src/rtc_audio_processing_impl.h"
#include "src/rtc_peerconnection_factory_impl.h"
#include "src/rtc_rtp_sender_impl.h"

#include "flowrate_logging.h"

namespace flowrate {
namespace {

// Never destroyed: the threads of libwebrtc run until the process is gone, and would find them
// destroyed by the exit handlers
std::mutex& g_mutex = *new std::mutex();
libwebrtc::scoped_refptr<libwebrtc::RTCPeerConnectionFactory>& g_factory =
    *new libwebrtc::scoped_refptr<libwebrtc::RTCPeerConnectionFactory>();

// The processing of the microphone, one for the process. The microphone comes processed by the media
// engine into a custom source, so only its configuration of Initialize is left here
webrtc::AudioProcessing* Processing(const libwebrtc::scoped_refptr<libwebrtc::RTCPeerConnectionFactory>& factory) {
  auto processing = factory->GetAudioProcessing();
  return processing != nullptr
             ? static_cast<libwebrtc::RTCAudioProcessingImpl*>(processing.get())->GetAudioProcessing().get()
             : nullptr;
}

}  // namespace

bool Runtime::Initialize() {
  std::lock_guard<std::mutex> lock(g_mutex);

  if (g_factory != nullptr)
    return true;

  ConfigureLogging();

  // Read once, by everything created afterwards. FLOWRATE_WEBRTC_FIELD_TRIALS ("Name/Value/...")
  // replaces them all, to try something out. Measured with the stamped test picture of TestBot:
  // - abort_further: the first probe of a screen shared right after joining was capped at twice
  //   the bitrate of the voice, and its result, about 150 kbit/s, replaced the start bitrate; the
  //   screen then went below the network for 15 s, with 2 s of pictures queued in the pacer
  // - ForcePlayoutDelay 0: a picture goes to the decoder as soon as it is whole, as in the GStreamer
  //   transport. The timing of libwebrtc otherwise added 10-20 ms on a clean network, and on one
  //   that reorders packets it ran away to 1-2 s
  std::vector<libwebrtc::string> trials;
  const char* custom = std::getenv("FLOWRATE_WEBRTC_FIELD_TRIALS");
  trials.emplace_back(custom != nullptr ? custom
                                        : "WebRTC-Bwe-ProbingConfiguration/abort_further:true,"
                                          "skip_if_est_larger_than_fraction_of_max:1.0/"
                                          "WebRTC-ForcePlayoutDelay/min_ms:0,max_ms:0/");

  libwebrtc::LibWebRTC::InitializeWithFieldTrials(libwebrtc::vector<libwebrtc::string>(trials));

  if (!libwebrtc::LibWebRTC::Initialize())
    return false;

  auto factory = libwebrtc::LibWebRTC::CreateRTCPeerConnectionFactory();

  if (factory == nullptr || !factory->Initialize())
    return false;

  // The whole band of the voice, up to 24 kHz, as the processing of GStreamer keeps it: by default
  // the processing runs at 32 kHz at most, and what is above 16 kHz is lost
  if (auto processing = Processing(factory)) {
    auto config = processing->GetConfig();
    config.pipeline.maximum_internal_processing_rate = 48000;
    processing->ApplyConfig(config);
  }

  g_factory = factory;
  return true;
}

libwebrtc::scoped_refptr<libwebrtc::RTCPeerConnectionFactory> Runtime::Factory() {
  std::lock_guard<std::mutex> lock(g_mutex);
  return g_factory;
}

webrtc::scoped_refptr<webrtc::PeerConnectionFactoryInterface> Runtime::NativeFactory() {
  auto factory = Factory();

  if (factory == nullptr)
    return nullptr;

  return static_cast<libwebrtc::RTCPeerConnectionFactoryImpl*>(factory.get())->peer_connection_factory();
}

webrtc::RtpSenderInterface* NativeSender(libwebrtc::RTCRtpSender* sender) {
  return static_cast<libwebrtc::RTCRtpSenderImpl*>(sender)->rtc_rtp_sender().get();
}

}  // namespace flowrate
