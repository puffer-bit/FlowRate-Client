// Durations of a window of the statistics of the media.

#ifndef FLOWRATE_TIMING_H
#define FLOWRATE_TIMING_H

#include <algorithm>
#include <cstdint>

namespace flowrate {

// Their count, sum and longest, in microseconds
struct Timing {
  int64_t count = 0;
  int64_t sum_us = 0;
  int64_t max_us = 0;

  void Add(int64_t us) {
    if (us < 0)
      return;

    count++;
    sum_us += us;
    max_us = std::max(max_us, us);
  }
};

}  // namespace flowrate

#endif
