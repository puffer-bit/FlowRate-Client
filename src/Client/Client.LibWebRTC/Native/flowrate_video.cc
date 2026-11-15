#include "flowrate_video.h"

#include <vector>

#include "api/environment/environment.h"
#include "api/video_codecs/sdp_video_format.h"

#include "flowrate_video_decoder.h"
#include "flowrate_video_encoder.h"

namespace flowrate {
namespace {

// What the clients encode: H.264 of any common profile (the decoders take them all, and the profile
// the SFU describes a stream with is not the one it was encoded in), VP8 and single-layer AV1.
std::vector<webrtc::SdpVideoFormat> SupportedFormats() {
  std::vector<webrtc::SdpVideoFormat> formats;

  for (const char* profile : {"42e01f", "42001f", "4d001f", "640c1f", "64001f"}) {
    formats.emplace_back("H264", webrtc::CodecParameterMap{{"profile-level-id", profile},
                                                           {"level-asymmetry-allowed", "1"},
                                                           {"packetization-mode", "1"}});
  }

  formats.emplace_back("VP8");
  formats.emplace_back("AV1");
  return formats;
}

class PassthroughDecoderFactory : public webrtc::VideoDecoderFactory {
 public:
  std::vector<webrtc::SdpVideoFormat> GetSupportedFormats() const override { return SupportedFormats(); }

  std::unique_ptr<webrtc::VideoDecoder> Create(const webrtc::Environment&,
                                               const webrtc::SdpVideoFormat&) override {
    return CreatePassthroughDecoder();
  }
};

class PassthroughEncoderFactory : public webrtc::VideoEncoderFactory {
 public:
  std::vector<webrtc::SdpVideoFormat> GetSupportedFormats() const override { return SupportedFormats(); }

  std::unique_ptr<webrtc::VideoEncoder> Create(const webrtc::Environment&,
                                               const webrtc::SdpVideoFormat&) override {
    return CreatePassthroughEncoder();
  }
};

}  // namespace

std::unique_ptr<webrtc::VideoDecoderFactory> CreateVideoDecoderFactory() {
  return std::make_unique<PassthroughDecoderFactory>();
}

std::unique_ptr<webrtc::VideoEncoderFactory> CreateVideoEncoderFactory() {
  return std::make_unique<PassthroughEncoderFactory>();
}

}  // namespace flowrate
