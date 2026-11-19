#include "flowrate_voices.h"

#include <algorithm>
#include <cstdint>
#include <cstdlib>

namespace flowrate {

void LevelSink::OnData(const void* audio_data, int bits_per_sample, int, size_t number_of_channels,
                       size_t number_of_frames) {
  if (bits_per_sample != 16)
    return;

  auto samples = static_cast<const int16_t*>(audio_data);
  int peak = 0;

  for (size_t i = 0; i < number_of_channels * number_of_frames; i++)
    peak = std::max(peak, std::abs(static_cast<int>(samples[i])));

  for (int previous = peak_.load(); peak > previous && !peak_.compare_exchange_weak(previous, peak);) {
  }
}

void Voices::Voice::Release() {
  if (track != nullptr && level != nullptr)
    track->RemoveSink(level.get());
}

void Voices::Attach(const std::string& mid, libwebrtc::scoped_refptr<libwebrtc::RTCAudioTrack> track) {
  std::lock_guard<std::mutex> lock(mutex_);
  auto& voice = voices_[mid];
  voice.Release();
  voice.track = track;
  voice.level = std::make_unique<LevelSink>();
  track->AddSink(voice.level.get());

  // A volume set before the track came
  if (auto volume = volumes_.find(mid); volume != volumes_.end())
    track->SetVolume(volume->second);
}

void Voices::SetVolume(const std::string& mid, double volume) {
  std::lock_guard<std::mutex> lock(mutex_);
  volumes_[mid] = volume;

  if (auto voice = voices_.find(mid); voice != voices_.end())
    voice->second.track->SetVolume(volume);
}

void Voices::AppendLevels(std::string& json) {
  std::lock_guard<std::mutex> lock(mutex_);

  // Whole numbers: a fraction would be written in the number format of the locale of the process
  for (auto& [mid, voice] : voices_) {
    if (json.size() > 1)
      json += ",";

    json += "{\"type\":\"flowrate-voice-level\",\"mid\":\"" + mid + "\",\"peak\":" +
            std::to_string(voice.level->Take()) + "}";
  }
}

void Voices::Clear() {
  std::lock_guard<std::mutex> lock(mutex_);

  for (auto& [mid, voice] : voices_)
    voice.Release();

  voices_.clear();
}

}  // namespace flowrate
