// A video we send whose pictures come encoded from outside, behind an fr_video_source.

#ifndef FLOWRATE_VIDEO_SOURCE_H
#define FLOWRATE_VIDEO_SOURCE_H

#include <cstdint>
#include <memory>
#include <mutex>

#include "api/media_stream_interface.h"
#include "api/peer_connection_interface.h"
#include "api/ref_count.h"
#include "api/rtp_sender_interface.h"
#include "api/scoped_refptr.h"

#include "flowrate_webrtc.h"

namespace flowrate {

class EncodedTrackSource;
class SourceLink;

// Kept alive by every round that puts it into a section, so destroying it during one is safe
class VideoSource : public webrtc::RefCountInterface {
 public:
  // Null before the runtime is initialized or for an empty picture
  static webrtc::scoped_refptr<VideoSource> Create(const fr_video_source_callbacks& callbacks, void* context, int width,
                                                   int height, int max_framerate);

  // The track of the source, which the sections that send it are given
  webrtc::scoped_refptr<webrtc::VideoTrackInterface> Track() const { return track_; }

  // Sets a sender up for the pictures of the source: nothing scaled or dropped, the frame rate let through
  void ConfigureSender(webrtc::RtpSenderInterface* sender) const;

  // See fr_video_source_push
  void Push(const uint8_t* data, int size, bool is_keyframe);

  // No callback comes and no picture goes on once it returns
  void Close();

  void TakeStats(fr_video_send_stats* stats);

 protected:
  VideoSource(const fr_video_source_callbacks& callbacks, void* context, int width, int height, int max_framerate,
              webrtc::PeerConnectionFactoryInterface* factory);
  ~VideoSource() override;

 private:
  const std::shared_ptr<SourceLink> link_;
  const int width_;
  const int height_;
  const int max_framerate_;
  webrtc::scoped_refptr<EncodedTrackSource> track_source_;
  webrtc::scoped_refptr<webrtc::VideoTrackInterface> track_;
  std::mutex mutex_;
  bool is_closed_ = false;
  int64_t last_ms_ = 0;
  uint64_t next_id_ = 0;
};

}  // namespace flowrate

#endif
