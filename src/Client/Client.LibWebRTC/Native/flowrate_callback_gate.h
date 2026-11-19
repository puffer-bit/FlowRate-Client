// Lets the callbacks of an object through until it is closed, and holds closing back while one runs:
// what the peer and the video sources hand to the threads of libwebrtc, which may call back after the
// object behind a handle of the C interface is gone.

#ifndef FLOWRATE_CALLBACK_GATE_H
#define FLOWRATE_CALLBACK_GATE_H

#include <mutex>

namespace flowrate {

class CallbackGate {
 public:
  // Runs call unless the gate is closed. Recursive: a call may run another one through the gate
  template <typename Call>
  void Run(Call&& call) {
    std::lock_guard<std::recursive_mutex> lock(mutex_);

    if (!is_closed_)
      call();
  }

  // Waits for a call that runs; none runs once it returns
  void Close() {
    std::lock_guard<std::recursive_mutex> lock(mutex_);
    is_closed_ = true;
  }

 private:
  std::recursive_mutex mutex_;
  bool is_closed_ = false;
};

}  // namespace flowrate

#endif
