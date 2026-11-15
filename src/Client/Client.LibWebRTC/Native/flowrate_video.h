// The video codecs of the peer connection factory, which the patch of the wrapper
// (Build/patches/flowrate_target.patch) puts in place of the built-in ones. libwebrtc carries the video:
// the transport, the jitter buffer, the pacing, the retransmissions and the keyframe requests. It
// neither decodes nor encodes it: what it would decode goes out to the decoders of the client, which
// draw on the GPU, and what it would encode comes in already encoded by the capture of the client.

#ifndef FLOWRATE_VIDEO_H
#define FLOWRATE_VIDEO_H

#include <memory>

#include "api/video_codecs/video_decoder_factory.h"
#include "api/video_codecs/video_encoder_factory.h"

namespace flowrate {

// Decoders that decode nothing, see flowrate_video_decoder.h
std::unique_ptr<webrtc::VideoDecoderFactory> CreateVideoDecoderFactory();

// Encoders that encode nothing, see flowrate_video_encoder.h
std::unique_ptr<webrtc::VideoEncoderFactory> CreateVideoEncoderFactory();

}  // namespace flowrate

#endif
