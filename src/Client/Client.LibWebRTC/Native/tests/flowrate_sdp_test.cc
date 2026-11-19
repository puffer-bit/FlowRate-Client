// Checks RewriteSsrcs of flowrate_sdp.cc, which needs nothing of WebRTC: run.sh builds it with the
// compiler of the system.

#include <cstdio>
#include <string>

#include "../flowrate_sdp.h"

namespace {

int failures = 0;

void Expect(const char* name, const std::string& actual, const std::string& expected) {
  if (actual == expected) {
    std::printf("ok   %s\n", name);
    return;
  }

  failures++;
  std::printf("FAIL %s\n--- expected\n%s\n--- actual\n%s\n", name, expected.c_str(), actual.c_str());
}

const std::string kSession = "v=0\r\no=- 1 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=group:BUNDLE 0 1 2 10\r\n";

// An audio section as libwebrtc answers it, without RTX
std::string Audio(const std::string& mid, const std::string& ssrc) {
  return "m=audio 9 UDP/TLS/RTP/SAVPF 100\r\na=mid:" + mid + "\r\na=sendonly\r\na=ssrc:" + ssrc +
         " cname:abc\r\na=ssrc:" + ssrc + " msid:- track\r\n";
}

// A video section with its RTX stream in a group FID
std::string Video(const std::string& mid, const std::string& ssrc, const std::string& rtx) {
  return "m=video 9 UDP/TLS/RTP/SAVPF 102 103\r\na=mid:" + mid + "\r\na=sendonly\r\na=ssrc-group:FID " + ssrc +
         " " + rtx + "\r\na=ssrc:" + ssrc + " cname:abc\r\na=ssrc:" + rtx + " cname:abc\r\n";
}

std::string Receiving(const std::string& mid) {
  return "m=audio 9 UDP/TLS/RTP/SAVPF 100\r\na=mid:" + mid + "\r\na=recvonly\r\n";
}

}  // namespace

int main() {
  using flowrate::RewriteSsrcs;

  Expect("audio: the stream gets the SSRC of the SFU",
         RewriteSsrcs(kSession + Audio("0", "111"), {{"0", {5000, 0}}}), kSession + Audio("0", "5000"));

  Expect("video: the stream and its RTX stream, in the group and in their lines",
         RewriteSsrcs(kSession + Video("1", "111", "222"), {{"1", {5000, 6000}}}),
         kSession + Video("1", "5000", "6000"));

  Expect("video without an RTX SSRC keeps the one of libwebrtc",
         RewriteSsrcs(kSession + Video("1", "111", "222"), {{"1", {5000, 0}}}), kSession + Video("1", "5000", "222"));

  const std::string mixed = kSession + Audio("0", "111") + Video("1", "222", "333") + Receiving("2") + Audio("10", "444");

  Expect("only the sections sent in change, each with its own SSRCs",
         RewriteSsrcs(mixed, {{"0", {7000, 0}}, {"1", {8000, 8001}}}),
         kSession + Audio("0", "7000") + Video("1", "8000", "8001") + Receiving("2") + Audio("10", "444"));

  Expect("mid 1 is not mid 10", RewriteSsrcs(kSession + Audio("10", "444"), {{"1", {9000, 0}}}),
         kSession + Audio("10", "444"));

  const std::string once = RewriteSsrcs(mixed, {{"0", {7000, 0}}, {"1", {8000, 8001}}});
  Expect("a later round finds the SSRCs of the SFU there already",
         RewriteSsrcs(once, {{"0", {7000, 0}}, {"1", {8000, 8001}}}), once);

  Expect("a section without SSRCs stays as it is", RewriteSsrcs(kSession + Receiving("0"), {{"0", {5000, 0}}}),
         kSession + Receiving("0"));

  Expect("an SSRC that is a prefix of another is not renamed inside it",
         RewriteSsrcs(kSession + Video("1", "11", "111"), {{"1", {5000, 6000}}}),
         kSession + Video("1", "5000", "6000"));

  std::printf(failures == 0 ? "all passed\n" : "%d failed\n", failures);
  return failures == 0 ? 0 : 1;
}
