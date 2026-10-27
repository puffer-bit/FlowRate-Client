// The video codecs of the peer connection factory, which the patch of the wrapper
// (Build/patches/flowrate_target.patch) puts in place of the built-in ones, and what the peer of
// flowrate_webrtc.cc needs of the sources of flowrate_video.cc. Not a part of the C interface.

#ifndef FLOWRATE_VIDEO_H
#define FLOWRATE_VIDEO_H

#include <memory>

#include "api/peer_connection_interface.h"
#include "api/rtp_sender_interface.h"
#include "api/scoped_refptr.h"
#include "api/video_codecs/video_decoder_factory.h"
#include "api/video_codecs/video_encoder_factory.h"

#include "flowrate_webrtc.h"

namespace libwebrtc {
class RTCPeerConnectionFactory;
class RTCRtpSender;
}  // namespace libwebrtc

namespace flowrate {

// Decoders that decode nothing: each hands the encoded pictures of its stream to the receiver of
// fr_set_video_receiver, and gives libwebrtc a blank picture in return
std::unique_ptr<webrtc::VideoDecoderFactory> CreateVideoDecoderFactory();

// Encoders that encode nothing: each sends the pictures a source of fr_video_source_create was given
std::unique_ptr<webrtc::VideoEncoderFactory> CreateVideoEncoderFactory();

// A video we send, behind an fr_video_source. Kept alive by every round that puts it into a section,
// so destroying it during one is safe
class VideoSource : public webrtc::RefCountInterface {
 public:
  // The track of the source, created with the factory the peers are made by
  virtual webrtc::scoped_refptr<webrtc::VideoTrackInterface> Track() = 0;

  // Sets a sender up for the pictures of the source: nothing scaled or dropped, the frame rate let through
  virtual void ConfigureSender(webrtc::RtpSenderInterface* sender) = 0;
};

webrtc::scoped_refptr<VideoSource> SourceOf(fr_video_source* source);

// Set by fr_initialize: the factory of the wrapper, whose factory of WebRTC the tracks of the sources
// are made by
void SetPeerConnectionFactory(libwebrtc::RTCPeerConnectionFactory* factory);

// The sender of WebRTC beneath one of the wrapper, as long as that one lives. Here and not in
// flowrate_webrtc.cc: the classes of the wrapper bring every name of WebRTC into its namespace
webrtc::RtpSenderInterface* NativeSender(libwebrtc::RTCRtpSender* sender);

}  // namespace flowrate

#endif
