// The audio device boundary: GStreamer owns the hardware and pulls decoded audio on its clock.
#ifndef FLOWRATE_AUDIO_H_
#define FLOWRATE_AUDIO_H_

#include <cstdint>

#include "api/audio/audio_device.h"

namespace flowrate {

// The audio device of the factory, put in place of the platform one by Build/patches/flowrate_target.patch
webrtc::scoped_refptr<webrtc::AudioDeviceModule> CreateAudioDevice();

// See fr_pull_playout
void PullPlayout(int16_t* samples);

}  // namespace flowrate

#endif
