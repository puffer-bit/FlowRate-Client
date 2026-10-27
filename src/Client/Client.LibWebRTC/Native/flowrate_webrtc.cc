// See flowrate_webrtc.h. Built over the C++ interface of webrtc-sdk/libwebrtc, inside the WebRTC tree
// and linked into the same library (../Build/linux/build.sh, ../Build/windows/build.ps1). The video
// goes past that interface, to the classes of the wrapper and of WebRTC beneath it (flowrate_video.h).

#include "flowrate_webrtc.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

#if defined(_WIN32)
#include <windows.h>
#include <mmdeviceapi.h>
#endif

#include "libwebrtc.h"
#include "rtc_audio_device.h"
#include "rtc_audio_processing.h"
#include "rtc_audio_source.h"
#include "rtc_audio_track.h"
#include "rtc_mediaconstraints.h"
#include "rtc_peerconnection.h"
#include "rtc_peerconnection_factory.h"
#include "rtc_rtp_receiver.h"
#include "rtc_rtp_sender.h"
#include "rtc_rtp_transceiver.h"

#include "flowrate_video.h"

using namespace libwebrtc;

namespace {

// What the microphone goes through after the processing of libwebrtc: the input volume, then the
// voice gate. The samples are floats in the range of 16-bit ones, 10 ms at a time.
class CaptureProcessing : public RTCAudioProcessing::CustomProcessing {
 public:
  void SetVolume(double volume) { volume_.store(static_cast<float>(volume)); }

  void SetGate(fr_level_callback on_level, void* context) {
    std::lock_guard<std::mutex> lock(mutex_);
    on_level_ = on_level;
    context_ = context;
  }

  void Initialize(int sample_rate_hz, int) override { Reset(sample_rate_hz); }

  void Reset(int new_rate) override {
    std::lock_guard<std::mutex> lock(mutex_);
    sample_rate_ = new_rate > 0 ? new_rate : 48000;
    delay_.clear();
  }

  void Release() override {}

  void Process(int, int num_frames, int, float* buffer) override {
    const float volume = volume_.load();

    if (volume != 1.0f) {
      for (int i = 0; i < num_frames; i++)
        buffer[i] = std::clamp(buffer[i] * volume, -32768.0f, 32767.0f);
    }

    std::lock_guard<std::mutex> lock(mutex_);

    if (on_level_ == nullptr) {
      position_ = 1.0;
      delay_.clear();
      return;
    }

    float peak = 0;

    for (int i = 0; i < num_frames; i++)
      peak = std::max(peak, std::fabs(buffer[i]));

    const double peak_db = peak > 0 ? 20.0 * std::log10(peak / 32768.0) : -100.0;
    const bool is_open = on_level_(context_, peak_db) != 0;

    // The pre-roll: what goes out is the audio of a moment ago, so the gate that opens on a loud
    // reading does not cut the start of the syllable that made it loud
    const size_t delay_frames = static_cast<size_t>(sample_rate_ * kPreRollMs / 1000);
    delay_.insert(delay_.end(), buffer, buffer + num_frames);

    if (delay_.size() < delay_frames + num_frames) {
      std::fill(buffer, buffer + num_frames, 0.0f);
      return;
    }

    std::copy(delay_.begin(), delay_.begin() + num_frames, buffer);
    delay_.erase(delay_.begin(), delay_.begin() + num_frames);

    // A smoothstep ramp, so the voice starts without a click and its tail fades out
    const double step = 1000.0 / (sample_rate_ * (is_open ? kAttackMs : kReleaseMs));

    for (int i = 0; i < num_frames; i++) {
      position_ = is_open ? std::min(1.0, position_ + step) : std::max(0.0, position_ - step);
      const double gain = position_ * position_ * (3 - 2 * position_);
      buffer[i] = static_cast<float>(buffer[i] * gain);
    }
  }

 private:
  // The same as the ones of the GStreamer gate, VoiceGate.PreRoll and VoiceGateFade
  static constexpr int kPreRollMs = 20;
  static constexpr double kAttackMs = 8;
  static constexpr double kReleaseMs = 200;

  std::atomic<float> volume_{1.0f};
  std::mutex mutex_;
  fr_level_callback on_level_ = nullptr;
  void* context_ = nullptr;
  int sample_rate_ = 48000;
  std::vector<float> delay_;
  double position_ = 0;
};

// Never destroyed: the threads of libwebrtc run until the process is gone, and would find them
// destroyed by the exit handlers
std::mutex& g_mutex = *new std::mutex();
scoped_refptr<RTCPeerConnectionFactory>& g_factory = *new scoped_refptr<RTCPeerConnectionFactory>();
CaptureProcessing& g_capture = *new CaptureProcessing();

// How loud a voice we receive is, from its sound before the playback volume scales it: the level of
// the statistics is taken after, so a voice turned down or muted would never read as speaking
class LevelSink : public AudioTrackSink {
 public:
  void OnData(const void* audio_data, int bits_per_sample, int, size_t number_of_channels,
              size_t number_of_frames) override {
    if (bits_per_sample != 16)
      return;

    auto samples = static_cast<const int16_t*>(audio_data);
    int peak = 0;

    for (size_t i = 0; i < number_of_channels * number_of_frames; i++)
      peak = std::max(peak, std::abs(static_cast<int>(samples[i])));

    for (int previous = peak_.load(); peak > previous && !peak_.compare_exchange_weak(previous, peak);) {
    }
  }

  // The peak since the previous call, of 16-bit samples
  int Take() { return peak_.exchange(0); }

 private:
  std::atomic<int> peak_{0};
};

// The voice a section plays, with what measures it
struct Voice {
  scoped_refptr<RTCAudioTrack> track;
  std::unique_ptr<LevelSink> level;

  void Release() {
    if (track != nullptr && level != nullptr)
      track->RemoveSink(level.get());
  }
};

// Lets callbacks through until the peer is destroyed, and holds destroying back while one runs
struct Gate {
  std::recursive_mutex mutex;
  bool is_closed = false;
};

// The SSRCs the SFU expects in a section we send in: of the stream, and of the RTX stream its lost
// packets are resent on, 0 where it gave none
struct SendSsrcs {
  uint32_t ssrc;
  uint32_t rtx;
};

std::string RewriteSsrcs(const std::string& sdp, const std::map<std::string, SendSsrcs>& sends);

}  // namespace

struct fr_peer : public RTCPeerConnectionObserver {
  fr_peer_callbacks callbacks{};
  void* context = nullptr;
  std::shared_ptr<Gate> gate = std::make_shared<Gate>();
  scoped_refptr<RTCPeerConnection> connection;
  // The track we send and where its sound comes from; the source is read by fr_peer_capture on a
  // thread of its own while another one may replace it
  std::mutex source_mutex;
  scoped_refptr<RTCAudioSource> microphone_source;
  scoped_refptr<RTCAudioTrack> microphone;

  // The voice each section plays, and the volume asked for it, by mid
  std::mutex voices_mutex;
  std::map<std::string, Voice> voices;
  std::map<std::string, double> volumes;

  scoped_refptr<RTCAudioTrack> Microphone() {
    std::lock_guard<std::mutex> lock(source_mutex);
    return microphone;
  }

  template <typename Call>
  void Report(Call call) {
    std::lock_guard<std::recursive_mutex> lock(gate->mutex);

    if (!gate->is_closed)
      call();
  }

  void Fail(const std::string& step, const char* error) {
    std::string message = step + ": " + (error ? error : "unknown");
    Report([&] { callbacks.failed(context, message.c_str()); });
  }

  void OnPeerConnectionState(RTCPeerConnectionState state) override {
    Report([&] { callbacks.state(context, static_cast<int>(state)); });
  }

  void OnTrack(scoped_refptr<RTCRtpTransceiver> transceiver) override {
    auto mid = transceiver->mid().std_string();
    auto track = transceiver->receiver()->track();

    if (mid.empty() || track == nullptr || track->kind().std_string() != "audio")
      return;

    auto voice = scoped_refptr<RTCAudioTrack>(static_cast<RTCAudioTrack*>(track.get()));
    std::lock_guard<std::recursive_mutex> open(gate->mutex);

    if (gate->is_closed)
      return;

    std::lock_guard<std::mutex> lock(voices_mutex);
    auto& entry = voices[mid];
    entry.Release();
    entry.track = voice;
    entry.level = std::make_unique<LevelSink>();
    voice->AddSink(entry.level.get());

    // A volume set before the track came
    if (auto volume = volumes.find(mid); volume != volumes.end())
      voice->SetVolume(volume->second);
  }

  void OnSignalingState(RTCSignalingState) override {}
  void OnIceGatheringState(RTCIceGatheringState) override {}
  void OnIceConnectionState(RTCIceConnectionState) override {}
  // mediasoup is ICE lite and learns our address from the checks, so nothing is trickled
  void OnIceCandidate(scoped_refptr<RTCIceCandidate>) override {}
  void OnAddStream(scoped_refptr<RTCMediaStream>) override {}
  void OnRemoveStream(scoped_refptr<RTCMediaStream>) override {}
  void OnDataChannel(scoped_refptr<RTCDataChannel>) override {}
  void OnRenegotiationNeeded() override {}
  void OnAddTrack(vector<scoped_refptr<RTCMediaStream>>, scoped_refptr<RTCRtpReceiver>) override {}
  void OnRemoveTrack(scoped_refptr<RTCRtpReceiver>) override {}
};

namespace {

// One offer/answer round, kept alive by the callbacks of its steps
struct Round {
  fr_peer* peer;
  std::shared_ptr<Gate> gate;
  std::map<std::string, SendSsrcs> sends;
  // The video sources of the sections that send one, by mid
  std::map<std::string, webrtc::scoped_refptr<flowrate::VideoSource>> videos;
  // The sounds of the audio sections that send one, by mid; the others send the microphone
  std::map<std::string, scoped_refptr<RTCAudioTrack>> audios;
};


// Runs a callback of libwebrtc unless the peer is gone; destroying it waits for one that runs
template <typename Call>
void WhileOpen(const std::shared_ptr<Gate>& gate, Call call) {
  std::lock_guard<std::recursive_mutex> lock(gate->mutex);

  if (!gate->is_closed)
    call();
}

void ConfigureVideoSenders(const std::shared_ptr<Round>& round);

void Answer(std::shared_ptr<Round> round) {
  fr_peer* peer = round->peer;
  auto microphone = peer->Microphone();

  for (auto& transceiver : peer->connection->transceivers().std_vector()) {
    auto mid = transceiver->mid().std_string();
    auto sender = transceiver->sender();

    if (!mid.empty() && round->sends.count(mid) > 0) {
      transceiver->SetDirectionWithError(RTCRtpTransceiverDirection::kSendOnly);

      if (auto video = round->videos.find(mid); video != round->videos.end())
        flowrate::NativeSender(sender.get())->SetTrack(video->second->Track().get());
      else if (transceiver->media_type() == RTCMediaType::VIDEO)
        // A screen that stopped: its stream stays, silent, and goes on where it was when it is back
        sender->set_track(nullptr);
      else if (auto audio = round->audios.find(mid); audio != round->audios.end())
        sender->set_track(audio->second);
      else
        sender->set_track(microphone);
    } else if (sender->track() != nullptr) {
      // A track that stopped: its section stays, emptied
      sender->set_track(nullptr);
      transceiver->SetDirectionWithError(RTCRtpTransceiverDirection::kRecvOnly);
    }
  }

  peer->connection->CreateAnswer(
      [round](const string sdp, const string) {
        WhileOpen(round->gate, [&] {
          auto local = std::make_shared<std::string>(RewriteSsrcs(sdp.std_string(), round->sends));

          round->peer->connection->SetLocalDescription(
              local->c_str(), "answer",
              [round, local] {
                // The encodings of a sender are there once it sends
                ConfigureVideoSenders(round);
                round->peer->Report([&] { round->peer->callbacks.negotiated(round->peer->context, local->c_str()); });
              },
              [round](const char* error) {
                WhileOpen(round->gate, [&] { round->peer->Fail("set-local-description", error); });
              });
        });
      },
      [round](const char* error) { WhileOpen(round->gate, [&] { round->peer->Fail("create-answer", error); }); },
      RTCMediaConstraints::Create());
}

// Sets up the senders of the video sources of a round
void ConfigureVideoSenders(const std::shared_ptr<Round>& round) {
  WhileOpen(round->gate, [&] {
    for (auto& transceiver : round->peer->connection->transceivers().std_vector()) {
      if (auto video = round->videos.find(transceiver->mid().std_string()); video != round->videos.end())
        video->second->ConfigureSender(flowrate::NativeSender(transceiver->sender().get()));
    }
  });
}

// Gives the SSRCs of the lines a=ssrc and a=ssrc-group of a section the names of renames
std::string RenameSsrcs(const std::string& section, const std::map<std::string, std::string>& renames) {
  std::string result;
  size_t start = 0;

  while (start <= section.size()) {
    size_t end = section.find("\r\n", start);
    std::string line = section.substr(start, end == std::string::npos ? std::string::npos : end - start);

    if (line.rfind("a=ssrc:", 0) == 0 || line.rfind("a=ssrc-group:", 0) == 0) {
      // a=ssrc:<ssrc> <attribute>, a=ssrc-group:<semantics> <ssrc>...
      bool is_group = line[6] == '-';
      std::string renamed = line.substr(0, line.find(is_group ? ' ' : ':') + 1);
      size_t at = renamed.size();

      while (at <= line.size()) {
        size_t space = line.find(' ', at);
        std::string token = line.substr(at, space == std::string::npos ? std::string::npos : space - at);
        auto rename = renames.find(token);
        renamed += rename != renames.end() ? rename->second : token;

        // The SSRC of a=ssrc is followed by its attribute, which is kept as it is
        if (space == std::string::npos || !is_group) {
          if (space != std::string::npos)
            renamed += line.substr(space);
          break;
        }

        renamed += ' ';
        at = space + 1;
      }

      line = renamed;
    }

    result += line;

    if (end == std::string::npos)
      break;

    result += "\r\n";
    start = end + 2;
  }

  return result;
}

// Replaces the SSRCs libwebrtc picked in each section we send in with the ones of the producer of the
// SFU, which knows our streams by them alone: the first of the section, or of its group FID, is the
// stream, the second of the group the RTX stream. libwebrtc sends with what the local description says.
std::string RewriteSsrcs(const std::string& sdp, const std::map<std::string, SendSsrcs>& sends) {
  std::vector<std::string> sections;
  size_t start = 0;

  for (size_t next; (next = sdp.find("\r\nm=", start + 1)) != std::string::npos; start = next)
    sections.push_back(sdp.substr(start, next - start));

  sections.push_back(sdp.substr(start));
  std::string result;

  for (auto& section : sections) {
    for (const auto& [mid, ssrcs] : sends) {
      if (section.find("\r\na=mid:" + mid + "\r\n") == std::string::npos)
        continue;

      std::map<std::string, std::string> renames;
      const std::string group = "\r\na=ssrc-group:FID ";
      const std::string marker = "\r\na=ssrc:";

      if (size_t group_at = section.find(group); group_at != std::string::npos) {
        size_t from = group_at + group.size();
        size_t space = section.find(' ', from);
        size_t end = section.find("\r\n", from);

        if (space != std::string::npos && space < end) {
          renames[section.substr(from, space - from)] = std::to_string(ssrcs.ssrc);

          if (ssrcs.rtx != 0)
            renames[section.substr(space + 1, end - space - 1)] = std::to_string(ssrcs.rtx);
        }
      } else if (size_t ssrc_at = section.find(marker); ssrc_at != std::string::npos) {
        size_t from = ssrc_at + marker.size();
        renames[section.substr(from, section.find(' ', from) - from)] = std::to_string(ssrcs.ssrc);
      }

      // A later round finds the SSRCs of the SFU there already
      section = RenameSsrcs(section, renames);
      break;
    }

    result += section;
  }

  return result;
}

int CopyDeviceName(int32_t status, const char* name, const char* guid, char* name_out, int name_size,
                   char* guid_out, int guid_size) {
  if (status != 0)
    return 0;

  snprintf(name_out, name_size, "%s", name);
  snprintf(guid_out, guid_size, "%s", guid);
  return 1;
}

void CreateMicrophone(scoped_refptr<RTCPeerConnectionFactory> factory, fr_peer* peer, bool is_custom,
                      int echo_cancellation, int noise_suppression, int auto_gain_control, bool is_enabled) {
  RTCAudioOptions options;
  options.echo_cancellation = echo_cancellation != 0;
  options.noise_suppression = noise_suppression != 0;
  options.auto_gain_control = auto_gain_control != 0;
  options.highpass_filter = true;

  auto source = factory->CreateAudioSource(
      "microphone", is_custom ? RTCAudioSource::SourceType::kCustom : RTCAudioSource::SourceType::kMicrophone,
      options);
  auto track = factory->CreateAudioTrack(source, "microphone");
  track->set_enabled(is_enabled);

  std::lock_guard<std::mutex> lock(peer->source_mutex);
  peer->microphone_source = source;
  peer->microphone = track;
}

scoped_refptr<RTCPeerConnectionFactory> Factory() {
  std::lock_guard<std::mutex> lock(g_mutex);
  return g_factory;
}

scoped_refptr<RTCAudioDevice> AudioDevice() {
  std::lock_guard<std::mutex> lock(g_mutex);
  return g_factory != nullptr ? g_factory->GetAudioDevice() : nullptr;
}

}  // namespace

struct fr_audio_source {
  scoped_refptr<RTCAudioSource> source;
  scoped_refptr<RTCAudioTrack> track;
};

extern "C" {

int fr_initialize(void) {
  std::lock_guard<std::mutex> lock(g_mutex);

  if (g_factory != nullptr)
    return 1;

  flowrate::ConfigureLogging();

  if (!LibWebRTC::Initialize())
    return 0;

  auto factory = LibWebRTC::CreateRTCPeerConnectionFactory();

  if (factory == nullptr || !factory->Initialize())
    return 0;

  factory->GetAudioProcessing()->SetCapturePostProcessing(&g_capture);
  flowrate::SetPeerConnectionFactory(factory.get());
  g_factory = factory;
  return 1;
}

int fr_recording_device_count(void) {
  auto device = AudioDevice();
  return device != nullptr ? device->RecordingDevices() : 0;
}

int fr_playout_device_count(void) {
  auto device = AudioDevice();
  return device != nullptr ? device->PlayoutDevices() : 0;
}

int fr_recording_device(int index, char* name, int name_size, char* guid, int guid_size) {
  auto device = AudioDevice();
  char device_name[RTCAudioDevice::kAdmMaxDeviceNameSize] = {};
  char device_guid[RTCAudioDevice::kAdmMaxGuidSize] = {};

  return device != nullptr
             ? CopyDeviceName(device->RecordingDeviceName(index, device_name, device_guid), device_name,
                              device_guid, name, name_size, guid, guid_size)
             : 0;
}

int fr_playout_device(int index, char* name, int name_size, char* guid, int guid_size) {
  auto device = AudioDevice();
  char device_name[RTCAudioDevice::kAdmMaxDeviceNameSize] = {};
  char device_guid[RTCAudioDevice::kAdmMaxGuidSize] = {};

  return device != nullptr
             ? CopyDeviceName(device->PlayoutDeviceName(index, device_name, device_guid), device_name,
                              device_guid, name, name_size, guid, guid_size)
             : 0;
}

int fr_set_recording_device(int index) {
  auto device = AudioDevice();
  return device != nullptr && device->SetRecordingDevice(index) == 0;
}

int fr_set_playout_device(int index) {
  auto device = AudioDevice();
  return device != nullptr && device->SetPlayoutDevice(index) == 0;
}

int fr_default_device(int is_recording, char* guid, int guid_size) {
#if defined(_WIN32)
  // Without an index libwebrtc opens the default device for communications, which may be another
  // one than where the rest of the sound of the system goes. COM may be on already, in another mode.
  const HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
  IMMDeviceEnumerator* enumerator = nullptr;
  IMMDevice* device = nullptr;
  LPWSTR id = nullptr;
  int result = 0;

  if (SUCCEEDED(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&enumerator))) &&
      SUCCEEDED(enumerator->GetDefaultAudioEndpoint(is_recording ? eCapture : eRender, eConsole, &device)) &&
      SUCCEEDED(device->GetId(&id)))
    result = WideCharToMultiByte(CP_UTF8, 0, id, -1, guid, guid_size, nullptr, nullptr) > 0;

  CoTaskMemFree(id);

  if (device != nullptr)
    device->Release();

  if (enumerator != nullptr)
    enumerator->Release();

  if (SUCCEEDED(com))
    CoUninitialize();

  return result;
#else
  (void)is_recording;
  (void)guid;
  (void)guid_size;
  return 0;
#endif
}

void fr_set_capture_volume(double volume) { g_capture.SetVolume(volume); }

void fr_set_voice_gate(fr_level_callback on_level, void* context) { g_capture.SetGate(on_level, context); }

fr_peer* fr_peer_create(const fr_peer_callbacks* callbacks, void* context, int echo_cancellation,
                        int noise_suppression, int auto_gain_control) {
  auto factory = Factory();

  if (factory == nullptr || callbacks == nullptr)
    return nullptr;

  RTCConfiguration config;
  // Every section on one transport, which is what keeps all the clients of the SFU on one port
  config.bundle_policy = kBundlePolicyMaxBundle;
  config.rtcp_mux_policy = RtcpMuxPolicy::kRtcpMuxPolicyRequire;
  config.sdp_semantics = SdpSemantics::kUnifiedPlan;

  auto peer = new fr_peer();
  peer->callbacks = *callbacks;
  peer->context = context;
  peer->connection = factory->Create(config, RTCMediaConstraints::Create());

  if (peer->connection == nullptr) {
    delete peer;
    return nullptr;
  }

  peer->connection->RegisterRTCPeerConnectionObserver(peer);

  CreateMicrophone(factory, peer, false, echo_cancellation, noise_suppression, auto_gain_control, false);

  return peer;
}

void fr_peer_negotiate(fr_peer* peer, const char* offer, const fr_send_section* sends, int send_count) {
  auto round = std::make_shared<Round>();
  round->peer = peer;
  round->gate = peer->gate;

  for (int i = 0; i < send_count; i++) {
    const auto& send = sends[i];
    round->sends[send.mid] = {send.ssrc, send.rtx_ssrc};

    if (send.video != nullptr)
      round->videos[send.mid] = flowrate::SourceOf(send.video);

    if (send.audio != nullptr)
      round->audios[send.mid] = send.audio->track;
  }

  peer->connection->SetRemoteDescription(
      offer, "offer",
      [round] { WhileOpen(round->gate, [&] { Answer(round); }); },
      [round](const char* error) {
        WhileOpen(round->gate, [&] { round->peer->Fail("set-remote-description", error); });
      });
}

void fr_peer_set_microphone_enabled(fr_peer* peer, int is_enabled) { peer->Microphone()->set_enabled(is_enabled != 0); }

void fr_peer_set_audio_options(fr_peer* peer, int is_custom, int echo_cancellation, int noise_suppression,
                               int auto_gain_control) {
  auto factory = Factory();

  if (factory == nullptr)
    return;

  auto previous = peer->Microphone();
  CreateMicrophone(factory, peer, is_custom != 0, echo_cancellation, noise_suppression, auto_gain_control,
                   previous->enabled());

  for (auto& sender : peer->connection->senders().std_vector()) {
    if (sender->track() != nullptr && sender->track()->id().std_string() == previous->id().std_string())
      sender->set_track(peer->Microphone());
  }
}

void fr_peer_capture(fr_peer* peer, const int16_t* samples, int frames, int sample_rate, int channels) {
  scoped_refptr<RTCAudioSource> source;

  {
    std::lock_guard<std::mutex> lock(peer->source_mutex);
    source = peer->microphone_source;
  }

  if (source != nullptr && source->GetSourceType() == RTCAudioSource::SourceType::kCustom)
    source->CaptureFrame(samples, 16, sample_rate, channels, frames);
}

void fr_peer_set_volume(fr_peer* peer, const char* mid, double volume) {
  std::lock_guard<std::mutex> lock(peer->voices_mutex);
  peer->volumes[mid] = volume;

  if (auto voice = peer->voices.find(mid); voice != peer->voices.end())
    voice->second.track->SetVolume(volume);
}

void fr_peer_request_stats(fr_peer* peer) {
  auto gate = peer->gate;

  peer->connection->GetStats(
      [peer, gate](const vector<scoped_refptr<MediaRTCStats>> reports) {
        std::string json = "[";

        for (size_t i = 0; i < reports.size(); i++) {
          if (i > 0)
            json += ",";

          json += reports[i]->ToJson().std_string();
        }

        WhileOpen(gate, [&] {
          // The levels of the voices as entries of their own, see LevelSink. Whole numbers: a
          // fraction would be written in the number format of the locale of the process
          std::lock_guard<std::mutex> lock(peer->voices_mutex);

          for (auto& [mid, voice] : peer->voices) {
            if (json.size() > 1)
              json += ",";

            json += "{\"type\":\"flowrate-voice-level\",\"mid\":\"" + mid + "\",\"peak\":" +
                    std::to_string(voice.level->Take()) + "}";
          }

          json += "]";
          peer->callbacks.stats(peer->context, json.c_str());
        });
      },
      [](const char*) {});
}

void fr_peer_destroy(fr_peer* peer) {
  {
    std::lock_guard<std::recursive_mutex> lock(peer->gate->mutex);
    peer->gate->is_closed = true;
  }

  peer->connection->DeRegisterRTCPeerConnectionObserver();

  {
    std::lock_guard<std::mutex> lock(peer->voices_mutex);

    for (auto& [mid, voice] : peer->voices)
      voice.Release();

    peer->voices.clear();
  }

  peer->connection->Close();

  {
    std::lock_guard<std::mutex> lock(g_mutex);

    if (g_factory != nullptr)
      g_factory->Delete(peer->connection);
  }

  // A callback still queued finds the gate closed and leaves the peer alone
  delete peer;
}

fr_audio_source* fr_audio_source_create(void) {
  static std::atomic<int> count{0};
  auto factory = Factory();

  if (factory == nullptr)
    return nullptr;

  // A custom source takes no part in the processing of the microphone, whatever these say
  RTCAudioOptions options;
  options.echo_cancellation = false;
  options.noise_suppression = false;
  options.auto_gain_control = false;
  options.highpass_filter = false;

  std::string id = "sound-" + std::to_string(++count);
  auto source = factory->CreateAudioSource(id.c_str(), RTCAudioSource::SourceType::kCustom, options);
  auto track = factory->CreateAudioTrack(source, id.c_str());

  if (source == nullptr || track == nullptr)
    return nullptr;

  return new fr_audio_source{source, track};
}

void fr_audio_source_push(fr_audio_source* source, const int16_t* samples, int frames, int sample_rate,
                          int channels) {
  source->source->CaptureFrame(samples, 16, sample_rate, channels, frames);
}

void fr_audio_source_destroy(fr_audio_source* source) { delete source; }

}  // extern "C"
