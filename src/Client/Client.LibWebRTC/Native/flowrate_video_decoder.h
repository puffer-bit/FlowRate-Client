// The decoders of the video streams we receive, which decode nothing: each hands the encoded pictures
// of its stream to the receiver of fr_set_video_receiver, and gives libwebrtc a blank picture in return.
// They are known by the SSRC of their stream.

#ifndef FLOWRATE_VIDEO_DECODER_H
#define FLOWRATE_VIDEO_DECODER_H

#include <cstdint>
#include <memory>

#include "api/video_codecs/video_decoder.h"

#include "flowrate_webrtc.h"

namespace flowrate {

std::unique_ptr<webrtc::VideoDecoder> CreatePassthroughDecoder();

// See fr_set_video_receiver
void SetVideoReceiver(fr_video_frame_callback on_frame, void* context);

// See fr_video_request_keyframe
void RequestKeyFrame(uint32_t ssrc);

// Fills stats and starts the next window; false when no such stream is received
bool TakeReceiveStats(uint32_t ssrc, fr_video_receive_stats* stats);

}  // namespace flowrate

#endif
