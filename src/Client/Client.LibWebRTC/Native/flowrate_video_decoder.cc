#include "flowrate_video_decoder.h"

#include <algorithm>
#include <atomic>
#include <map>
#include <mutex>

#include "api/video/encoded_image.h"
#include "api/video/i420_buffer.h"
#include "api/video/video_frame.h"
#include "modules/video_coding/include/video_error_codes.h"
#include "rtc_base/numerics/sequence_number_unwrapper.h"
#include "rtc_base/time_utils.h"

#include "flowrate_timing.h"

namespace flowrate {
namespace {

// The receiver of fr_set_video_receiver, set once before any stream
std::atomic<fr_video_frame_callback> g_on_frame{nullptr};
std::atomic<void*> g_context{nullptr};

class PassthroughDecoder;

// The decoders by the SSRC of their stream, for the keyframe requests and the statistics. Never
// destroyed, as the decoders may outlive the exit handlers on the threads of libwebrtc
std::mutex& g_decoders_mutex = *new std::mutex();
std::map<uint32_t, PassthroughDecoder*>& g_decoders = *new std::map<uint32_t, PassthroughDecoder*>();

class PassthroughDecoder : public webrtc::VideoDecoder {
 public:
  ~PassthroughDecoder() override { SetSsrc(0); }

  bool Configure(const Settings&) override { return true; }

  int32_t RegisterDecodeCompleteCallback(webrtc::DecodedImageCallback* callback) override {
    callback_ = callback;
    return WEBRTC_VIDEO_CODEC_OK;
  }

  int32_t Release() override { return WEBRTC_VIDEO_CODEC_OK; }

  const char* ImplementationName() const override { return "FlowRatePassthrough"; }

  void RequestKeyFrame() { is_keyframe_requested_ = true; }

  // Under the mutex of the decoders, which keeps the decoder alive
  void TakeStats(fr_video_receive_stats* stats) {
    std::lock_guard<std::mutex> lock(stats_mutex_);
    stats->frames = wait_.count;
    stats->wait_sum_us = wait_.sum_us;
    stats->wait_max_us = wait_.max_us;
    stats->handoff_sum_us = handoff_.sum_us;
    stats->handoff_max_us = handoff_.max_us;
    wait_ = {};
    handoff_ = {};
  }

  // On the decoding thread of the stream
  int32_t Decode(const webrtc::EncodedImage& image, int64_t) override {
    const int64_t now_us = webrtc::TimeMicros();

    // The stream of a decoder does not change, but it is only known from its packets
    if (!image.PacketInfos().empty())
      SetSsrc(image.PacketInfos().begin()->ssrc());

    // From the first packet of the picture on: what the jitter buffer and the assembly held it
    int64_t first_us = now_us;
    for (const auto& packet : image.PacketInfos())
      first_us = std::min(first_us, packet.receive_time().us());

    // 90 kHz, the clock of every video codec
    const int64_t timestamp_ns = unwrapper_.Unwrap(image.RtpTimestamp()) * 100000 / 9;

    if (auto on_frame = g_on_frame.load(); on_frame != nullptr && ssrc_ != 0) {
      on_frame(g_context.load(), ssrc_, image.data(), static_cast<int>(image.size()), timestamp_ns, image.IsKey());

      std::lock_guard<std::mutex> lock(stats_mutex_);
      wait_.Add(now_us - first_us);
      handoff_.Add(webrtc::TimeMicros() - now_us);
    }

    // libwebrtc keeps its timing by the pictures it gets back: a blank one of the least size
    if (callback_ != nullptr) {
      if (blank_ == nullptr) {
        blank_ = webrtc::I420Buffer::Create(2, 2);
        webrtc::I420Buffer::SetBlack(blank_.get());
      }

      auto frame = webrtc::VideoFrame::Builder()
                       .set_video_frame_buffer(blank_)
                       .set_rtp_timestamp(image.RtpTimestamp())
                       .build();
      callback_->Decoded(frame, std::nullopt, std::nullopt);
    }

    // libwebrtc asks the sender with a PLI
    return is_keyframe_requested_.exchange(false) ? WEBRTC_VIDEO_CODEC_OK_REQUEST_KEYFRAME : WEBRTC_VIDEO_CODEC_OK;
  }

 private:
  void SetSsrc(uint32_t ssrc) {
    if (ssrc == ssrc_)
      return;

    std::lock_guard<std::mutex> lock(g_decoders_mutex);

    if (auto entry = g_decoders.find(ssrc_); entry != g_decoders.end() && entry->second == this)
      g_decoders.erase(entry);

    if (ssrc != 0)
      g_decoders[ssrc] = this;

    ssrc_ = ssrc;
  }

  webrtc::DecodedImageCallback* callback_ = nullptr;
  webrtc::RtpTimestampUnwrapper unwrapper_;
  webrtc::scoped_refptr<webrtc::I420Buffer> blank_;
  std::atomic<bool> is_keyframe_requested_{false};
  uint32_t ssrc_ = 0;
  std::mutex stats_mutex_;
  Timing wait_;
  Timing handoff_;
};

}  // namespace

std::unique_ptr<webrtc::VideoDecoder> CreatePassthroughDecoder() { return std::make_unique<PassthroughDecoder>(); }

void SetVideoReceiver(fr_video_frame_callback on_frame, void* context) {
  g_context = context;
  g_on_frame = on_frame;
}

void RequestKeyFrame(uint32_t ssrc) {
  std::lock_guard<std::mutex> lock(g_decoders_mutex);

  if (auto entry = g_decoders.find(ssrc); entry != g_decoders.end())
    entry->second->RequestKeyFrame();
}

bool TakeReceiveStats(uint32_t ssrc, fr_video_receive_stats* stats) {
  *stats = {};
  std::lock_guard<std::mutex> lock(g_decoders_mutex);

  auto entry = g_decoders.find(ssrc);

  if (entry == g_decoders.end())
    return false;

  entry->second->TakeStats(stats);
  return true;
}

}  // namespace flowrate
