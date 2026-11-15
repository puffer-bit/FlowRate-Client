// The SSRCs of the answer: the SFU knows the streams we send by the SSRCs it gave their producers alone.

#ifndef FLOWRATE_SDP_H
#define FLOWRATE_SDP_H

#include <cstdint>
#include <map>
#include <string>

namespace flowrate {

// The SSRCs the SFU expects in a section we send in: of the stream, and of the RTX stream its lost
// packets are resent on, 0 where it gave none
struct SendSsrcs {
  uint32_t ssrc;
  uint32_t rtx;
};

// Replaces the SSRCs libwebrtc picked in each section we send in, by mid, with the ones of the
// producer of the SFU: the first of the section, or of its group FID, is the stream, the second of the
// group the RTX stream. libwebrtc sends with what the local description says.
std::string RewriteSsrcs(const std::string& sdp, const std::map<std::string, SendSsrcs>& sends);

}  // namespace flowrate

#endif
