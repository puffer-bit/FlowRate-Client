#include "flowrate_video_source.h"

#include "api/make_ref_counted.h"
#include "api/rtp_parameters.h"
#include "api/video/encoded_image.h"
#include "api/video/video_frame.h"
#include "media/base/video_broadcaster.h"
#include "pc/video_track_source.h"
#include "rtc_base/time_utils.h"

#include "flowrate_encoded_frame.h"
#include "flowrate_runtime.h"

namespace flowrate {

// The track source of a VideoSource, which hands its frames to the senders
class EncodedTrackSource : public webrtc::VideoTrackSource {
 public:
  EncodedTrackSource() : webrtc::VideoTrackSource(false) { SetState(kLive); }

  void Deliver(const webrtc::VideoFrame& frame) { broadcaster_.OnFrame(frame); }

 protected:
  webrtc::VideoSourceInterface<webrtc::VideoFrame>* source() override { return &broadcaster_; }

 private:
  webrtc::VideoBroadcaster broadcaster_;
};

webrtc::scoped_refptr<VideoSource> VideoSource::Create(const fr_video_source_callbacks& callbacks, void* context,
                                                       int width, int height, int max_framerate) {
  auto factory = Runtime::NativeFactory();

  if (factory == nullptr || width <= 0 || height <= 0)
    return nullptr;

  return webrtc::make_ref_counted<VideoSource>(callbacks, context, width, height, max_framerate, factory.get());
}

VideoSource::VideoSource(const fr_video_source_callbacks& callbacks, void* context, int width, int height,
                         int max_framerate, webrtc::PeerConnectionFactoryInterface* factory)
    : link_(std::make_shared<SourceLink>(callbacks, context)), width_(width), height_(height),
      max_framerate_(max_framerate) {
  track_source_ = webrtc::make_ref_counted<EncodedTrackSource>();
  track_ = factory->CreateVideoTrack(track_source_, "screen");
}

VideoSource::~VideoSource() = default;

void VideoSource::ConfigureSender(webrtc::RtpSenderInterface* sender) const {
  auto parameters = sender->GetParameters();
  parameters.degradation_preference = webrtc::DegradationPreference::MAINTAIN_FRAMERATE_AND_RESOLUTION;

  for (auto& encoding : parameters.encodings)
    encoding.max_framerate = max_framerate_;

  sender->SetParameters(parameters);
}

void VideoSource::Push(const uint8_t* data, int size, bool is_keyframe) {
  std::lock_guard<std::mutex> lock(mutex_);

  if (is_closed_)
    return;

  // Stamped on arrival, like the appsrc of the GStreamer transport does. libwebrtc drops a frame
  // whose millisecond is not later than the one of the frame before
  int64_t timestamp_us = webrtc::TimeMicros();

  if (timestamp_us / 1000 <= last_ms_)
    timestamp_us = (last_ms_ + 1) * 1000;

  last_ms_ = timestamp_us / 1000;
  link_->CountPushed();

  auto buffer = webrtc::make_ref_counted<EncodedBuffer>(width_, height_, webrtc::EncodedImageBuffer::Create(data, size),
                                                        is_keyframe, ++next_id_, link_);
  track_source_->Deliver(webrtc::VideoFrame::Builder()
                             .set_video_frame_buffer(buffer)
                             .set_timestamp_us(timestamp_us)
                             .set_rotation(webrtc::kVideoRotation_0)
                             .build());
}

void VideoSource::Close() {
  {
    std::lock_guard<std::mutex> lock(mutex_);
    is_closed_ = true;
  }

  link_->Close();
}

void VideoSource::TakeStats(fr_video_send_stats* stats) { link_->TakeStats(stats); }

}  // namespace flowrate
