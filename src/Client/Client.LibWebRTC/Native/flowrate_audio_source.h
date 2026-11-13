// A sound we send whose samples come from outside: the microphone of a peer, processed by the media
// engine, or the sound of a shown screen, behind an fr_audio_source.

#ifndef FLOWRATE_AUDIO_SOURCE_H
#define FLOWRATE_AUDIO_SOURCE_H

#include <cstdint>
#include <memory>

#include "rtc_audio_source.h"
#include "rtc_audio_track.h"

namespace flowrate {

class AudioSource {
 public:
  enum class Kind {
    // Muted until a section sends it; leaves its processing options to the processing of the process
    kMicrophone,
    // Sent as it is: its sender must not change the processing the microphone shares
    kScreen,
  };

  // Null before the runtime is initialized
  static std::unique_ptr<AudioSource> Create(Kind kind);

  // The next 10 ms: 16-bit samples, interleaved when there are two channels. Any thread, one at a time
  void Push(const int16_t* samples, int frames, int sample_rate, int channels);

  const libwebrtc::scoped_refptr<libwebrtc::RTCAudioTrack>& track() const { return track_; }

 private:
  AudioSource(libwebrtc::scoped_refptr<libwebrtc::RTCAudioSource> source,
              libwebrtc::scoped_refptr<libwebrtc::RTCAudioTrack> track)
      : source_(std::move(source)), track_(std::move(track)) {}

  const libwebrtc::scoped_refptr<libwebrtc::RTCAudioSource> source_;
  const libwebrtc::scoped_refptr<libwebrtc::RTCAudioTrack> track_;
};

}  // namespace flowrate

#endif
