#include "flowrate_audio_source.h"

#include <atomic>
#include <string>

#include "rtc_peerconnection_factory.h"

#include "flowrate_runtime.h"

namespace flowrate {

std::unique_ptr<AudioSource> AudioSource::Create(Kind kind) {
  static std::atomic<int> count{0};
  auto factory = Runtime::Factory();

  if (factory == nullptr)
    return nullptr;

  libwebrtc::RTCAudioOptions options;
  options.echo_cancellation = false;
  options.noise_suppression = false;
  options.auto_gain_control = false;
  // The microphone keeps the high-pass filter of libwebrtc; screen audio bypasses capture processing,
  // and its sender must not change the shared processing of the microphone
  options.highpass_filter = kind == Kind::kMicrophone;
  options.apply_processing_options = kind == Kind::kMicrophone;

  const std::string id = kind == Kind::kMicrophone ? "microphone" : "sound-" + std::to_string(++count);
  auto source = factory->CreateAudioSource(id.c_str(), libwebrtc::RTCAudioSource::SourceType::kCustom, options);
  auto track = factory->CreateAudioTrack(source, id.c_str());

  if (source == nullptr || track == nullptr)
    return nullptr;

  if (kind == Kind::kMicrophone)
    track->set_enabled(false);

  return std::unique_ptr<AudioSource>(new AudioSource(source, track));
}

void AudioSource::Push(const int16_t* samples, int frames, int sample_rate, int channels) {
  source_->CaptureFrame(samples, 16, sample_rate, channels, frames);
}

}  // namespace flowrate
