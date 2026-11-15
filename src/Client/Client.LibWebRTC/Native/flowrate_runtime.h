// The libwebrtc of the process: initialized once, with the one factory every peer and source is made
// by. The factory of the wrapper for the peers, the factory of WebRTC beneath it for the video tracks.

#ifndef FLOWRATE_RUNTIME_H
#define FLOWRATE_RUNTIME_H

#include "api/peer_connection_interface.h"
#include "api/rtp_sender_interface.h"
#include "api/scoped_refptr.h"
#include "rtc_peerconnection_factory.h"
#include "rtc_rtp_sender.h"

namespace flowrate {

class Runtime {
 public:
  // See fr_initialize; true once it succeeded, also on later calls
  static bool Initialize();

  // Null before Initialize. Never destroyed: the threads of libwebrtc run until the process is gone
  static libwebrtc::scoped_refptr<libwebrtc::RTCPeerConnectionFactory> Factory();

  // The factory of WebRTC beneath Factory(); null before Initialize
  static webrtc::scoped_refptr<webrtc::PeerConnectionFactoryInterface> NativeFactory();
};

// The sender of WebRTC beneath one of the wrapper, as long as that one lives
webrtc::RtpSenderInterface* NativeSender(libwebrtc::RTCRtpSender* sender);

}  // namespace flowrate

#endif
