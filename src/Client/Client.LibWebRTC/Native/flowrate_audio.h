// The audio device boundary: GStreamer owns the hardware and pulls decoded audio on its clock.
#ifndef FLOWRATE_AUDIO_H_
#define FLOWRATE_AUDIO_H_

#include "api/audio/audio_device.h"

namespace flowrate {
webrtc::scoped_refptr<webrtc::AudioDeviceModule> CreateAudioDevice();
}

#endif
