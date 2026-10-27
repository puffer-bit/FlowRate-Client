// See flowrate_video.h and the video part of flowrate_webrtc.h. libwebrtc carries the video: the
// transport, the jitter buffer, the pacing, the retransmissions and the keyframe requests. It neither
// decodes nor encodes it: what it would decode goes out to the decoders of the client, which draw on
// the GPU, and what it would encode comes in already encoded by the capture of the client.

#include "flowrate_video.h"

#include <atomic>
#include <cstdlib>
#include <map>
#include <mutex>
#include <string>
#include <vector>

#include "api/environment/environment.h"
#include "api/make_ref_counted.h"
#include "api/rtp_parameters.h"
#include "api/video/encoded_image.h"
#include "api/video/i420_buffer.h"
#include "api/video/video_frame.h"
#include "api/video/video_frame_buffer.h"
#include "api/video_codecs/sdp_video_format.h"
#include "api/video_codecs/video_codec.h"
#include "api/video_codecs/video_decoder.h"
#include "api/video_codecs/video_encoder.h"
#include "media/base/video_broadcaster.h"
#include "modules/video_coding/codecs/interface/common_constants.h"
#include "modules/video_coding/include/video_codec_interface.h"
#include "modules/video_coding/include/video_error_codes.h"
#include "pc/video_track_source.h"
#include "rtc_base/logging.h"
#include "rtc_base/numerics/sequence_number_unwrapper.h"
#include "rtc_base/time_utils.h"
#include "src/rtc_peerconnection_factory_impl.h"
#include "src/rtc_rtp_sender_impl.h"

#include "flowrate_webrtc.h"

namespace flowrate {
namespace {

// The receiver of fr_set_video_receiver, set once before any stream
std::atomic<fr_video_frame_callback> g_on_frame{nullptr};
std::atomic<void*> g_context{nullptr};

class PassthroughDecoder;

// The decoders by the SSRC of their stream, for the keyframe requests. Never destroyed, as the
// decoders may outlive the exit handlers on the threads of libwebrtc
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

  // On the decoding thread of the stream
  int32_t Decode(const webrtc::EncodedImage& image, int64_t) override {
    // The stream of a decoder does not change, but it is only known from its packets
    if (!image.PacketInfos().empty())
      SetSsrc(image.PacketInfos().begin()->ssrc());

    // 90 kHz, the clock of every video codec
    const int64_t timestamp_ns = unwrapper_.Unwrap(image.RtpTimestamp()) * 100000 / 9;

    if (auto on_frame = g_on_frame.load(); on_frame != nullptr && ssrc_ != 0)
      on_frame(g_context.load(), ssrc_, image.data(), static_cast<int>(image.size()), timestamp_ns, image.IsKey());

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
};

// What the clients encode: H.264 of any common profile (the decoders take them all, and the profile
// the SFU describes a stream with is not the one it was encoded in) and VP8
std::vector<webrtc::SdpVideoFormat> SupportedFormats() {
  std::vector<webrtc::SdpVideoFormat> formats;

  for (const char* profile : {"42e01f", "42001f", "4d001f", "640c1f", "64001f"}) {
    formats.emplace_back("H264", webrtc::CodecParameterMap{{"profile-level-id", profile},
                                                           {"level-asymmetry-allowed", "1"},
                                                           {"packetization-mode", "1"}});
  }

  formats.emplace_back("VP8");
  return formats;
}

class PassthroughDecoderFactory : public webrtc::VideoDecoderFactory {
 public:
  std::vector<webrtc::SdpVideoFormat> GetSupportedFormats() const override { return SupportedFormats(); }

  std::unique_ptr<webrtc::VideoDecoder> Create(const webrtc::Environment&,
                                               const webrtc::SdpVideoFormat&) override {
    return std::make_unique<PassthroughDecoder>();
  }
};

// The factory of the peers, which the tracks of the sources are made by
std::mutex& g_factory_mutex = *new std::mutex();
webrtc::scoped_refptr<webrtc::PeerConnectionFactoryInterface>& g_factory =
    *new webrtc::scoped_refptr<webrtc::PeerConnectionFactoryInterface>();

// What a source and the encoder of its stream share: the callbacks, until the source is destroyed
class SourceLink {
 public:
  SourceLink(const fr_video_source_callbacks& callbacks, void* context) : callbacks_(callbacks), context_(context) {}

  void ReportBitrate(int kbps) {
    std::lock_guard<std::mutex> lock(mutex_);

    if (!is_closed_ && callbacks_.bitrate != nullptr)
      callbacks_.bitrate(context_, kbps);
  }

  void RequestKeyFrame() {
    std::lock_guard<std::mutex> lock(mutex_);

    if (!is_closed_ && callbacks_.keyframe != nullptr)
      callbacks_.keyframe(context_);
  }

  // Waits for a callback that runs
  void Close() {
    std::lock_guard<std::mutex> lock(mutex_);
    is_closed_ = true;
  }

 private:
  std::mutex mutex_;
  const fr_video_source_callbacks callbacks_;
  void* const context_;
  bool is_closed_ = false;
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

class PassthroughEncoderFactory : public webrtc::VideoEncoderFactory {
 public:
  std::vector<webrtc::SdpVideoFormat> GetSupportedFormats() const override { return SupportedFormats(); }

  std::unique_ptr<webrtc::VideoEncoder> Create(const webrtc::Environment&,
                                               const webrtc::SdpVideoFormat&) override {
    return std::make_unique<PassthroughEncoder>();
  }
};

// The track source of an fr_video_source, which hands its frames to the senders
class EncodedTrackSource : public webrtc::VideoTrackSource {
 public:
  EncodedTrackSource() : webrtc::VideoTrackSource(false) { SetState(kLive); }

  void Deliver(const webrtc::VideoFrame& frame) { broadcaster_.OnFrame(frame); }

 protected:
  webrtc::VideoSourceInterface<webrtc::VideoFrame>* source() override { return &broadcaster_; }

 private:
  webrtc::VideoBroadcaster broadcaster_;
};

class EncodedVideoSource : public VideoSource {
 public:
  EncodedVideoSource(const fr_video_source_callbacks& callbacks, void* context, int width, int height,
                     int max_framerate, webrtc::scoped_refptr<webrtc::PeerConnectionFactoryInterface> factory)
      : link_(std::make_shared<SourceLink>(callbacks, context)), width_(width), height_(height),
        max_framerate_(max_framerate) {
    track_source_ = webrtc::make_ref_counted<EncodedTrackSource>();
    track_ = factory->CreateVideoTrack(track_source_, "screen");
  }

  webrtc::scoped_refptr<webrtc::VideoTrackInterface> Track() override { return track_; }

  void ConfigureSender(webrtc::RtpSenderInterface* sender) override {
    auto parameters = sender->GetParameters();
    parameters.degradation_preference = webrtc::DegradationPreference::MAINTAIN_FRAMERATE_AND_RESOLUTION;

    for (auto& encoding : parameters.encodings)
      encoding.max_framerate = max_framerate_;

    sender->SetParameters(parameters);
  }

  void Push(const uint8_t* data, int size, bool is_keyframe) {
    std::lock_guard<std::mutex> lock(mutex_);

    if (is_closed_)
      return;

    // Stamped on arrival, like the appsrc of the GStreamer transport does. libwebrtc drops a frame
    // whose millisecond is not later than the one of the frame before
    int64_t timestamp_us = webrtc::TimeMicros();

    if (timestamp_us / 1000 <= last_ms_)
      timestamp_us = (last_ms_ + 1) * 1000;

    last_ms_ = timestamp_us / 1000;

    auto buffer = webrtc::make_ref_counted<EncodedBuffer>(width_, height_, webrtc::EncodedImageBuffer::Create(data, size),
                                                          is_keyframe, ++next_id_, link_);
    track_source_->Deliver(webrtc::VideoFrame::Builder()
                               .set_video_frame_buffer(buffer)
                               .set_timestamp_us(timestamp_us)
                               .set_rotation(webrtc::kVideoRotation_0)
                               .build());
  }

  void Close() {
    {
      std::lock_guard<std::mutex> lock(mutex_);
      is_closed_ = true;
    }

    link_->Close();
  }

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

}  // namespace

std::unique_ptr<webrtc::VideoDecoderFactory> CreateVideoDecoderFactory() {
  return std::make_unique<PassthroughDecoderFactory>();
}

std::unique_ptr<webrtc::VideoEncoderFactory> CreateVideoEncoderFactory() {
  return std::make_unique<PassthroughEncoderFactory>();
}

webrtc::scoped_refptr<VideoSource> SourceOf(fr_video_source* source) {
  return webrtc::scoped_refptr<VideoSource>(reinterpret_cast<VideoSource*>(source));
}

void ConfigureLogging() {
  const char* level = std::getenv("FLOWRATE_WEBRTC_LOG");

  if (level == nullptr)
    return;

  std::string name = level;
  auto severity = name == "verbose" ? webrtc::LS_VERBOSE
                  : name == "info"  ? webrtc::LS_INFO
                  : name == "error" ? webrtc::LS_ERROR
                                    : webrtc::LS_WARNING;

  webrtc::LogMessage::LogToDebug(severity);
  webrtc::LogMessage::LogTimestamps();
  webrtc::LogMessage::LogThreads();
}

void SetPeerConnectionFactory(libwebrtc::RTCPeerConnectionFactory* factory) {
  std::lock_guard<std::mutex> lock(g_factory_mutex);
  g_factory = static_cast<libwebrtc::RTCPeerConnectionFactoryImpl*>(factory)->peer_connection_factory();
}

webrtc::RtpSenderInterface* NativeSender(libwebrtc::RTCRtpSender* sender) {
  return static_cast<libwebrtc::RTCRtpSenderImpl*>(sender)->rtc_rtp_sender().get();
}

}  // namespace flowrate

extern "C" {

void fr_set_video_receiver(fr_video_frame_callback on_frame, void* context) {
  flowrate::g_context = context;
  flowrate::g_on_frame = on_frame;
}

void fr_video_request_keyframe(uint32_t ssrc) {
  std::lock_guard<std::mutex> lock(flowrate::g_decoders_mutex);

  if (auto entry = flowrate::g_decoders.find(ssrc); entry != flowrate::g_decoders.end())
    entry->second->RequestKeyFrame();
}

fr_video_source* fr_video_source_create(const fr_video_source_callbacks* callbacks, void* context, int width,
                                        int height, int max_framerate) {
  webrtc::scoped_refptr<webrtc::PeerConnectionFactoryInterface> factory;

  {
    std::lock_guard<std::mutex> lock(flowrate::g_factory_mutex);
    factory = flowrate::g_factory;
  }

  if (factory == nullptr || callbacks == nullptr || width <= 0 || height <= 0)
    return nullptr;

  // The handle holds a reference of its own until it is destroyed
  auto source = webrtc::make_ref_counted<flowrate::EncodedVideoSource>(*callbacks, context, width, height,
                                                                      max_framerate, factory);
  return reinterpret_cast<fr_video_source*>(static_cast<flowrate::VideoSource*>(source.release()));
}

void fr_video_source_push(fr_video_source* source, const uint8_t* data, int size, int is_keyframe) {
  static_cast<flowrate::EncodedVideoSource*>(reinterpret_cast<flowrate::VideoSource*>(source))
      ->Push(data, size, is_keyframe != 0);
}

void fr_video_source_destroy(fr_video_source* source) {
  auto* video = reinterpret_cast<flowrate::VideoSource*>(source);
  static_cast<flowrate::EncodedVideoSource*>(video)->Close();
  video->Release();
}

}  // extern "C"
