#include "flowrate_audio.h"
#include "flowrate_webrtc.h"

#include <algorithm>
#include <atomic>
#include <mutex>

#include "modules/audio_device/include/audio_device_default.h"
#include "api/make_ref_counted.h"

namespace {

// Registration and pulling are serialized: the engine may unregister and destroy its transport
// as the last peer closes. No platform device or independent timer runs alongside GStreamer.
std::mutex& transport_mutex = *new std::mutex();
webrtc::AudioTransport* transport = nullptr;
std::atomic<bool> playing{false};

class AudioDevice : public webrtc::webrtc_impl::AudioDeviceModuleDefault<webrtc::AudioDeviceModule> {
 public:
  int32_t RegisterAudioCallback(webrtc::AudioTransport* callback) override {
    std::lock_guard<std::mutex> lock(transport_mutex);
    transport = callback;
    return 0;
  }
  int32_t StartPlayout() override { playing = true; return 0; }
  int32_t StopPlayout() override { playing = false; return 0; }
  bool Playing() const override { return playing; }
  int32_t Terminate() override { StopPlayout(); return RegisterAudioCallback(nullptr); }
  int32_t PlayoutIsAvailable(bool* available) override { *available = true; return 0; }
  int32_t RecordingIsAvailable(bool* available) override { *available = false; return 0; }
  int32_t ActiveAudioLayer(AudioLayer* layer) const override { *layer = kDummyAudio; return 0; }
  int32_t SpeakerVolumeIsAvailable(bool* available) override { *available = false; return 0; }
  int32_t MicrophoneVolumeIsAvailable(bool* available) override { *available = false; return 0; }
  int32_t SpeakerMuteIsAvailable(bool* available) override { *available = false; return 0; }
  int32_t MicrophoneMuteIsAvailable(bool* available) override { *available = false; return 0; }
  int32_t StereoPlayoutIsAvailable(bool* available) const override { *available = true; return 0; }
  int32_t StereoPlayout(bool* enabled) const override { *enabled = true; return 0; }
  int32_t StereoRecording(bool* enabled) const override { *enabled = false; return 0; }
};

}  // namespace

namespace flowrate {
webrtc::scoped_refptr<webrtc::AudioDeviceModule> CreateAudioDevice() {
  return webrtc::make_ref_counted<AudioDevice>();
}
}  // namespace flowrate

extern "C" {

int fr_audio_bridge_version(void) { return 1; }

void fr_pull_playout(int16_t* samples) {
  if (samples == nullptr)
    return;

  std::fill(samples, samples + 960, 0);
  std::lock_guard<std::mutex> lock(transport_mutex);
  if (transport == nullptr || !playing)
    return;

  size_t frames = 0;
  int64_t elapsed = 0, ntp = 0;
  // NeedMorePlayData keeps NetEq, PLC, mixing and per-track volume inside libwebrtc.
  if (transport->NeedMorePlayData(480, 2 * sizeof(int16_t), 2, 48000, samples, frames, &elapsed, &ntp) != 0)
    frames = 0;
  std::fill(samples + std::min<size_t>(frames, 960), samples + 960, 0);
}

}  // extern "C"
