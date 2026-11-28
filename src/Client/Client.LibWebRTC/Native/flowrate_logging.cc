#include "flowrate_logging.h"

#include <algorithm>
#include <cstdlib>
#include <memory>
#include <mutex>
#include <string>

#include "rtc_base/logging.h"

namespace flowrate {
namespace {

// Hands every message of WebRTC to the callback of fr_set_log, as "file.cc:123: message"
class CallbackLogSink : public webrtc::LogSink {
 public:
  explicit CallbackLogSink(fr_log_callback callback) : callback_(callback) {}

  using webrtc::LogSink::OnLogMessage;

  // What LogMessage calls; the overloads below only for messages that come without a line
  void OnLogMessage(const webrtc::LogLineRef& line) override {
    auto file = line.filename();
    auto slash = file.find_last_of("/\\");
    if (slash != absl::string_view::npos)
      file.remove_prefix(slash + 1);

    std::string text;
    if (!file.empty())
      text.append(file).append(":").append(std::to_string(line.line())).append(": ");
    text.append(line.message());

    Pass(line.severity(), text);
  }

  void OnLogMessage(const std::string& message, webrtc::LoggingSeverity severity) override {
    Pass(severity, message);
  }

  void OnLogMessage(const std::string& message) override { Pass(webrtc::LS_INFO, message); }

 private:
  void Pass(webrtc::LoggingSeverity severity, std::string text) {
    while (!text.empty() && (text.back() == '\n' || text.back() == '\r'))
      text.pop_back();

    callback_(static_cast<int>(severity), text.c_str());
  }

  fr_log_callback callback_;
};

std::mutex g_log_mutex;
std::unique_ptr<CallbackLogSink> g_log_sink;

}  // namespace

void ConfigureLogging() {
  // The configuration of WebRTC is taken once, by the first message or by this; it says what goes to
  // stderr. Nothing does unless FLOWRATE_WEBRTC_LOG asks for it: the journal gets the log through
  // fr_set_log, and prints it to the console itself
  static std::once_flag once;

  std::call_once(once, [] {
    const char* level = std::getenv("FLOWRATE_WEBRTC_LOG");

    webrtc::LoggingConfig config;
    // Only what a sink or stderr wants is formatted at all
    config.set_min_severity(webrtc::LS_NONE);
    config.set_debug_severity(webrtc::LS_NONE);

    if (level != nullptr) {
      std::string name = level;
      config.set_debug_severity(name == "verbose" ? webrtc::LS_VERBOSE
                                : name == "info"  ? webrtc::LS_INFO
                                : name == "error" ? webrtc::LS_ERROR
                                                  : webrtc::LS_WARNING);
      config.set_log_timestamp(true);
      config.set_log_thread(true);
    }

    webrtc::InitializeLogging(std::move(config));
  });
}

void SetLogCallback(fr_log_callback callback, int min_severity) {
  // Before the sink: it lowers the level WebRTC formats from, which would let its printing to stderr through
  ConfigureLogging();

  std::lock_guard<std::mutex> lock(g_log_mutex);

  // RemoveLogToStream waits for the messages being written, so the old sink is not used after this
  if (g_log_sink != nullptr) {
    webrtc::LogMessage::RemoveLogToStream(g_log_sink.get());
    g_log_sink.reset();
  }

  if (callback == nullptr || min_severity >= webrtc::LS_NONE)
    return;

  g_log_sink = std::make_unique<CallbackLogSink>(callback);
  webrtc::LogMessage::AddLogToStream(g_log_sink.get(), static_cast<webrtc::LoggingSeverity>(std::max(min_severity, 0)));
}

}  // namespace flowrate
