// The C interface of libflowrate_sck.dylib: screens, windows and the sound of applications captured
// through ScreenCaptureKit on macOS. Built by ../../Build/mac/build-sck.sh, bound in
// ../../Video/Mac/ScreenCaptureKitNative.cs.
#ifndef FLOWRATE_SCK_H
#define FLOWRATE_SCK_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define FR_SCK_VERSION 1

#define FR_SCK_API __attribute__((visibility("default")))

// What a capture shows
#define FR_SCK_KIND_DISPLAY 0
#define FR_SCK_KIND_WINDOW 1
// Only the sound: a display is captured at a minimal size and its pictures are dropped
#define FR_SCK_KIND_AUDIO 2

// The errors of fr_sck_list and of the stopped callback
#define FR_SCK_ERROR_ACCESS_DENIED (-1)
#define FR_SCK_ERROR_TIMEOUT (-2)
#define FR_SCK_ERROR_NOT_FOUND (-3)
#define FR_SCK_ERROR_FAILED (-4)

// A display or a window. The strings live until the callback returns
typedef void (*fr_sck_source_cb)(void *ctx, uint64_t id, uint32_t pid, int32_t width, int32_t height,
                                 const char *title);

typedef struct {
    // A picture in BGRA, rows of width * 4 bytes. Called on a queue of the capture; the data lives
    // until the callback returns
    void (*video)(void *ctx, const uint8_t *data, int32_t width, int32_t height);
    // Interleaved 32-bit float samples at 48 kHz, frames * channels of them
    void (*audio)(void *ctx, const float *data, int32_t frames, int32_t channels);
    // The system ended the capture by itself, for example from its sharing indicator. Not called for
    // fr_sck_stop
    void (*stopped)(void *ctx, int32_t error, const char *message);
} fr_sck_callbacks;

typedef struct {
    int32_t kind;
    // CGDirectDisplayID or CGWindowID; ignored for the sound
    uint64_t id;
    int32_t framerate;
    // Pictures higher than this are scaled down by the system; 0 keeps the size of the source
    int32_t max_height;
    int32_t show_cursor;
    // FR_SCK_KIND_AUDIO: the process whose sound is taken, or the one left out of everything
    uint32_t audio_pid;
    int32_t audio_exclude;
} fr_sck_config;

FR_SCK_API int32_t fr_sck_version(void);

// Whether Screen Recording is allowed; fr_sck_request_access shows the prompt of the system the
// first time. The answer takes effect after a restart
FR_SCK_API int32_t fr_sck_has_access(void);
FR_SCK_API int32_t fr_sck_request_access(void);

// The displays (windows == 0) or the windows of the other applications. Blocks until the system
// answers. Returns the count, or one of FR_SCK_ERROR_*
FR_SCK_API int32_t fr_sck_list(int32_t windows, fr_sck_source_cb callback, void *ctx);

// Starts a capture and blocks until it runs. Returns null and writes a message into error on failure
FR_SCK_API void *fr_sck_start(const fr_sck_config *config, const fr_sck_callbacks *callbacks, void *ctx,
                   char *error, int32_t error_size);

// Stops the capture and waits until no callback runs any more
FR_SCK_API void fr_sck_stop(void *capture);

#ifdef __cplusplus
}
#endif

#endif
