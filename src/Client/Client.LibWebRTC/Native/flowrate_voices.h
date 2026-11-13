// The voices a peer plays, one per section, with their playback volumes and their levels.

#ifndef FLOWRATE_VOICES_H
#define FLOWRATE_VOICES_H

#include <atomic>
#include <cstddef>
#include <map>
#include <memory>
#include <mutex>
#include <string>

#include "rtc_audio_track.h"

namespace flowrate {

// How loud a voice we receive is, from its sound before the playback volume scales it: the level of
// the statistics is taken after, so a voice turned down or muted would never read as speaking
class LevelSink : public libwebrtc::AudioTrackSink {
 public:
  void OnData(const void* audio_data, int bits_per_sample, int sample_rate, size_t number_of_channels,
              size_t number_of_frames) override;

  // The peak since the previous call, of 16-bit samples
  int Take() { return peak_.exchange(0); }

 private:
  std::atomic<int> peak_{0};
};

class Voices {
 public:
  ~Voices() { Clear(); }

  // The voice of a section came, or another one replaced it
  void Attach(const std::string& mid, libwebrtc::scoped_refptr<libwebrtc::RTCAudioTrack> track);

  // 0 silences it; kept for a voice that has not come yet
  void SetVolume(const std::string& mid, double volume);

  // Adds one {"type":"flowrate-voice-level","mid":...,"peak":...} per voice to a JSON array being
  // written, after json = "[" and what is in it already, and starts the next window of each level
  void AppendLevels(std::string& json);

  void Clear();

 private:
  struct Voice {
    libwebrtc::scoped_refptr<libwebrtc::RTCAudioTrack> track;
    std::unique_ptr<LevelSink> level;

    void Release();
  };

  std::mutex mutex_;
  std::map<std::string, Voice> voices_;
  std::map<std::string, double> volumes_;
};

}  // namespace flowrate

#endif
