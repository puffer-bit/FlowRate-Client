// The log of WebRTC: to the callback of fr_set_log, and to stderr when FLOWRATE_WEBRTC_LOG asks for it.

#ifndef FLOWRATE_LOGGING_H
#define FLOWRATE_LOGGING_H

#include "flowrate_webrtc.h"

namespace flowrate {

// Takes the configuration of the log of WebRTC, once: printed to stderr only when FLOWRATE_WEBRTC_LOG
// says how much, verbose, info, warning or error. Called by SetLogCallback and Runtime::Initialize
void ConfigureLogging();

// See fr_set_log
void SetLogCallback(fr_log_callback callback, int min_severity);

}  // namespace flowrate

#endif
