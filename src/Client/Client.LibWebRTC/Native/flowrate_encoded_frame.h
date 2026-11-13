// What a video source of flowrate_video_source.h and the passthrough encoder of its stream share: the
// pictures, carried as the buffers of frames, and the link back to the callbacks of the source.

#ifndef FLOWRATE_ENCODED_FRAME_H
#define FLOWRATE_ENCODED_FRAME_H

#include <cstdint>
#include <memory>
#include <mutex>

#include "api/scoped_refptr.h"
#include "api/video/encoded_image.h"
#include "api/video/i420_buffer.h"
#include "api/video/video_frame_buffer.h"

#include "flowrate_callback_gate.h"
#include "flowrate_timing.h"
#include "flowrate_webrtc.h"

namespace flowrate {

// The callbacks of a source, until it is destroyed, and the statistics of its pictures
class SourceLink {
 public:
  SourceLink(const fr_video_source_callbacks& callbacks, void* context) : callbacks_(callbacks), context_(context) {}

  void ReportBitrate(int kbps) {
    gate_.Run([&] {
      if (callbacks_.bitrate != nullptr)
        callbacks_.bitrate(context_, kbps);
    });
  }

  void RequestKeyFrame() {
    gate_.Run([&] {
      if (callbacks_.keyframe != nullptr)
        callbacks_.keyframe(context_);
    });
  }

  // Waits for a callback that runs
  void Close() { gate_.Close(); }

  void CountPushed() {
    std::lock_guard<std::mutex> lock(stats_mutex_);
    pushed_++;
  }

  // The time from the push of a picture to the encoder taking it
  void CountEncoded(int64_t queue_us) {
    std::lock_guard<std::mutex> lock(stats_mutex_);
    queue_.Add(queue_us);
  }

  void TakeStats(fr_video_send_stats* stats) {
    std::lock_guard<std::mutex> lock(stats_mutex_);
    stats->pushed = pushed_;
    stats->encoded = queue_.count;
    stats->queue_sum_us = queue_.sum_us;
    stats->queue_max_us = queue_.max_us;
    pushed_ = 0;
    queue_ = {};
  }

 private:
  CallbackGate gate_;
  const fr_video_source_callbacks callbacks_;
  void* const context_;
  std::mutex stats_mutex_;
  int64_t pushed_ = 0;
  Timing queue_;
};

// An encoded picture as the buffer of a frame: libwebrtc carries it to the encoder of the stream the
// way it would carry a texture, without looking inside
class EncodedBuffer : public webrtc::VideoFrameBuffer {
 public:
  EncodedBuffer(int width, int height, webrtc::scoped_refptr<webrtc::EncodedImageBuffer> data, bool is_keyframe,
                uint64_t id, std::shared_ptr<SourceLink> link)
      : width_(width), height_(height), data_(std::move(data)), is_keyframe_(is_keyframe), id_(id),
        link_(std::move(link)) {}

  Type type() const override { return Type::kNative; }
  int width() const override { return width_; }
  int height() const override { return height_; }

  // Never asked for, as the encoder takes native frames; a blank picture if it is all the same
  webrtc::scoped_refptr<webrtc::I420BufferInterface> ToI420() override {
    auto blank = webrtc::I420Buffer::Create(width_, height_);
    webrtc::I420Buffer::SetBlack(blank.get());
    return blank;
  }

  const webrtc::scoped_refptr<webrtc::EncodedImageBuffer>& data() const { return data_; }
  bool is_keyframe() const { return is_keyframe_; }
  // Counted up by the source from 1, so a picture given again is known
  uint64_t id() const { return id_; }
  const std::shared_ptr<SourceLink>& link() const { return link_; }

 private:
  const int width_;
  const int height_;
  const webrtc::scoped_refptr<webrtc::EncodedImageBuffer> data_;
  const bool is_keyframe_;
  const uint64_t id_;
  const std::shared_ptr<SourceLink> link_;
};

}  // namespace flowrate

#endif
