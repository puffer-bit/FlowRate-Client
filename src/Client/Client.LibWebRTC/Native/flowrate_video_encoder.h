// The encoder of a video stream we send, which encodes nothing: it sends the pictures a VideoSource
// was given, already encoded, and passes what libwebrtc wants of an encoder back to that source.

#ifndef FLOWRATE_VIDEO_ENCODER_H
#define FLOWRATE_VIDEO_ENCODER_H

#include <memory>

#include "api/video_codecs/video_encoder.h"

namespace flowrate {

std::unique_ptr<webrtc::VideoEncoder> CreatePassthroughEncoder();

}  // namespace flowrate

#endif
