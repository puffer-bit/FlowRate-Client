#include "flowrate_video_encoder.h"

#include <cstdint>
#include <memory>
#include <vector>

#include "api/video/encoded_image.h"
#include "api/video/video_frame.h"
#include "api/video/video_frame_buffer.h"
#include "api/video_codecs/video_codec.h"
#include "modules/video_coding/codecs/interface/common_constants.h"
#include "modules/video_coding/include/video_codec_interface.h"
#include "modules/video_coding/include/video_error_codes.h"
#include "rtc_base/time_utils.h"

#include "flowrate_encoded_frame.h"

namespace flowrate {
namespace {

class PassthroughEncoder : public webrtc::VideoEncoder {
 public:
  int InitEncode(const webrtc::VideoCodec* codec, const Settings&) override {
    codec_type_ = codec->codecType;
    return WEBRTC_VIDEO_CODEC_OK;
  }

  int32_t RegisterEncodeCompleteCallback(webrtc::EncodedImageCallback* callback) override {
    callback_ = callback;
    return WEBRTC_VIDEO_CODEC_OK;
  }

  int32_t Release() override { return WEBRTC_VIDEO_CODEC_OK; }

  // The estimate of the network, passed on to the encoder of the source
  void SetRates(const RateControlParameters& parameters) override {
    kbps_ = static_cast<int>(parameters.bitrate.get_sum_kbps());

    if (link_ != nullptr)
      link_->ReportBitrate(kbps_);
  }

  // On the encoder thread of the stream
  int32_t Encode(const webrtc::VideoFrame& frame, const std::vector<webrtc::VideoFrameType>* frame_types) override {
    const auto& buffer = frame.video_frame_buffer();

    // Only the pictures of a source come here: its track is never disabled, which would send black frames
    if (buffer->type() != webrtc::VideoFrameBuffer::Type::kNative || callback_ == nullptr)
      return WEBRTC_VIDEO_CODEC_OK;

    auto* picture = static_cast<EncodedBuffer*>(buffer.get());

    if (picture->link() != link_) {
      link_ = picture->link();
      last_id_ = 0;

      if (kbps_ > 0)
        link_->ReportBitrate(kbps_);
    }

    // A picture libwebrtc gives again, as it may when the source goes quiet, would break the stream
    if (picture->id() <= last_id_)
      return WEBRTC_VIDEO_CODEC_OK;

    last_id_ = picture->id();

    // The time of a frame is its push, see EncodedVideoSource::Push
    link_->CountEncoded(webrtc::TimeMicros() - frame.timestamp_us());

    const bool is_keyframe_wanted = frame_types != nullptr && !frame_types->empty() &&
                                    (*frame_types)[0] == webrtc::VideoFrameType::kVideoFrameKey;

    // The picture goes out all the same: the keyframe follows in a moment
    if (is_keyframe_wanted && !picture->is_keyframe())
      link_->RequestKeyFrame();

    webrtc::EncodedImage image;
    image.SetEncodedData(picture->data());
    image._encodedWidth = picture->width();
    image._encodedHeight = picture->height();
    image.SetRtpTimestamp(frame.rtp_timestamp());
    image.capture_time_ms_ = frame.render_time_ms();
    image.set_frame_type(picture->is_keyframe() ? webrtc::VideoFrameType::kVideoFrameKey
                                                : webrtc::VideoFrameType::kVideoFrameDelta);

    webrtc::CodecSpecificInfo info;
    info.codecType = codec_type_;

    if (codec_type_ == webrtc::kVideoCodecH264) {
      info.codecSpecific.H264.packetization_mode = webrtc::H264PacketizationMode::NonInterleaved;
      info.codecSpecific.H264.temporal_idx = webrtc::kNoTemporalIdx;
      info.codecSpecific.H264.base_layer_sync = false;
      info.codecSpecific.H264.idr_frame = picture->is_keyframe();
    } else if (codec_type_ == webrtc::kVideoCodecVP8) {
      info.codecSpecific.VP8.nonReference = false;
      info.codecSpecific.VP8.temporalIdx = webrtc::kNoTemporalIdx;
      info.codecSpecific.VP8.layerSync = false;
      info.codecSpecific.VP8.keyIdx = webrtc::kNoKeyIdx;
    } else if (codec_type_ == webrtc::kVideoCodecAV1) {
      // Single-layer AV1 still needs the dependency descriptor: mediasoup uses it to
      // recognize keyframes. Conservatively depend on every preceding delta frame.
      auto& generic = info.generic_frame_info.emplace();
      generic.encoder_buffers.emplace_back(0, !picture->is_keyframe(), true);
      generic.decode_target_indications = {webrtc::DecodeTargetIndication::kSwitch};
      generic.part_of_chain = {true};
      if (picture->is_keyframe()) {
        auto& structure = info.template_structure.emplace();
        structure.num_decode_targets = 1;
        structure.num_chains = 1;
        structure.decode_target_protected_by_chain = {0};
        webrtc::FrameDependencyTemplate key;
        key.decode_target_indications = {webrtc::DecodeTargetIndication::kSwitch};
        key.chain_diffs = {0};
        auto delta = key;
        delta.frame_diffs = {1};
        delta.chain_diffs = {1};
        structure.templates = {key, delta};
      }
    }

    callback_->OnEncodedImage(image, &info);
    return WEBRTC_VIDEO_CODEC_OK;
  }

  EncoderInfo GetEncoderInfo() const override {
    EncoderInfo info;
    info.implementation_name = "FlowRatePassthrough";
    // The pictures stay in their buffers, and libwebrtc neither scales them nor drops the ones that
    // overshoot: the encoder of the source keeps to the bitrate itself
    info.supports_native_handle = true;
    info.has_trusted_rate_controller = true;
    info.is_hardware_accelerated = true;
    info.scaling_settings = ScalingSettings(ScalingSettings::kOff);
    return info;
  }

 private:
  webrtc::EncodedImageCallback* callback_ = nullptr;
  webrtc::VideoCodecType codec_type_ = webrtc::kVideoCodecGeneric;
  std::shared_ptr<SourceLink> link_;
  uint64_t last_id_ = 0;
  int kbps_ = 0;
};

}  // namespace

std::unique_ptr<webrtc::VideoEncoder> CreatePassthroughEncoder() { return std::make_unique<PassthroughEncoder>(); }

}  // namespace flowrate
