// ScreenCaptureKit behind the C interface of flowrate_sck.h. Compiled with ARC.
#import <AppKit/AppKit.h>
#import <CoreGraphics/CoreGraphics.h>
#import <CoreMedia/CoreMedia.h>
#import <CoreVideo/CoreVideo.h>
#import <Foundation/Foundation.h>
#import <ScreenCaptureKit/ScreenCaptureKit.h>
#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#include "flowrate_sck.h"

// How long the system may take to list the content or to start and stop a capture
static const int64_t WaitNanoseconds = 5 * NSEC_PER_SEC;

// A still screen produces no pictures, and an encoder without input cannot answer the keyframe
// request of a viewer who has just joined, so the last picture is repeated after this long
static const uint64_t KeepaliveNanoseconds = 100 * NSEC_PER_MSEC;

// The size of the picture of a capture that only takes the sound
static const int32_t AudioOnlySize = 2;

static const int32_t AudioSampleRate = 48000;
static const int32_t AudioChannels = 2;

static uint64_t Now(void) { return clock_gettime_nsec_np(CLOCK_UPTIME_RAW); }

static void WriteError(char *error, int32_t size, NSString *message)
{
    if (error == NULL || size <= 0)
        return;

    const char *text = message.UTF8String ?: "";
    strlcpy(error, text, (size_t)size);
}

static int32_t ErrorCode(NSError *error)
{
    if ([error.domain isEqualToString:SCStreamErrorDomain] && error.code == SCStreamErrorUserDeclined)
        return FR_SCK_ERROR_ACCESS_DENIED;

    return FR_SCK_ERROR_FAILED;
}

static SCShareableContent *LoadContent(int32_t *code, NSString **message)
{
    __block SCShareableContent *result = nil;
    __block NSError *failure = nil;
    dispatch_semaphore_t done = dispatch_semaphore_create(0);

    [SCShareableContent getShareableContentExcludingDesktopWindows:YES
                                               onScreenWindowsOnly:YES
                                                 completionHandler:^(SCShareableContent *content, NSError *error) {
        result = content;
        failure = error;
        dispatch_semaphore_signal(done);
    }];

    if (dispatch_semaphore_wait(done, dispatch_time(DISPATCH_TIME_NOW, WaitNanoseconds)) != 0)
    {
        *code = FR_SCK_ERROR_TIMEOUT;
        *message = @"The system did not list the shareable content in time";
        return nil;
    }

    if (result == nil)
    {
        *code = failure != nil ? ErrorCode(failure) : FR_SCK_ERROR_FAILED;
        *message = failure.localizedDescription ?: @"The system listed no shareable content";
    }

    return result;
}

static NSString *DisplayName(CGDirectDisplayID displayId)
{
    for (NSScreen *screen in NSScreen.screens)
    {
        NSNumber *number = screen.deviceDescription[@"NSScreenNumber"];

        if (number != nil && number.unsignedIntValue == displayId)
            return screen.localizedName;
    }

    return @"";
}

static void PixelSize(CGDirectDisplayID displayId, int32_t *width, int32_t *height)
{
    CGDisplayModeRef mode = CGDisplayCopyDisplayMode(displayId);

    if (mode != NULL)
    {
        *width = (int32_t)CGDisplayModeGetPixelWidth(mode);
        *height = (int32_t)CGDisplayModeGetPixelHeight(mode);
        CGDisplayModeRelease(mode);
    }
    else
    {
        *width = (int32_t)CGDisplayPixelsWide(displayId);
        *height = (int32_t)CGDisplayPixelsHigh(displayId);
    }
}

int32_t fr_sck_version(void) { return FR_SCK_VERSION; }

int32_t fr_sck_has_access(void) { return CGPreflightScreenCaptureAccess() ? 1 : 0; }

int32_t fr_sck_request_access(void) { return CGRequestScreenCaptureAccess() ? 1 : 0; }

int32_t fr_sck_list(int32_t windows, fr_sck_source_cb callback, void *ctx)
{
    @autoreleasepool
    {
        int32_t code = 0;
        NSString *message = nil;
        SCShareableContent *content = LoadContent(&code, &message);

        if (content == nil)
            return code;

        int32_t count = 0;

        if (!windows)
        {
            for (SCDisplay *display in content.displays)
            {
                int32_t width = 0;
                int32_t height = 0;
                PixelSize(display.displayID, &width, &height);
                callback(ctx, display.displayID, 0, width, height, DisplayName(display.displayID).UTF8String ?: "");
                count++;
            }

            return count;
        }

        pid_t own = getpid();

        for (SCWindow *window in content.windows)
        {
            SCRunningApplication *application = window.owningApplication;

            // Only the windows of the other applications at the level of documents: menus, the Dock
            // and the status items live on other layers
            if (application == nil || application.processID == own || window.windowLayer != 0 ||
                !window.isOnScreen || window.frame.size.width < 64 || window.frame.size.height < 64)
                continue;

            NSString *name = application.applicationName ?: @"";
            NSString *title = window.title.length > 0 && ![window.title isEqualToString:name]
                ? [NSString stringWithFormat:@"%@ — %@", name, window.title]
                : name;

            if (title.length == 0)
                continue;

            callback(ctx, window.windowID, (uint32_t)application.processID, (int32_t)window.frame.size.width,
                     (int32_t)window.frame.size.height, title.UTF8String ?: "");
            count++;
        }

        return count;
    }
}

@interface FRCapture : NSObject <SCStreamOutput, SCStreamDelegate>
@end

@implementation FRCapture
{
@public
    fr_sck_callbacks _callbacks;
    void *_ctx;
    int32_t _kind;
    SCStream *_stream;
    dispatch_queue_t _queue;
    dispatch_source_t _keepalive;
    atomic_bool _isStopping;

    // Touched on _queue only
    CVPixelBufferRef _last;
    uint64_t _lastAt;
    uint8_t *_rows;
    size_t _rowsSize;
    float *_samples;
    size_t _samplesSize;
    AudioBufferList *_list;
    size_t _listSize;
}

- (void)dealloc
{
    if (_last != NULL)
        CVPixelBufferRelease(_last);

    free(_rows);
    free(_samples);
    free(_list);
}

- (void)deliver:(CVPixelBufferRef)pixels
{
    if (CVPixelBufferLockBaseAddress(pixels, kCVPixelBufferLock_ReadOnly) != kCVReturnSuccess)
        return;

    const uint8_t *base = CVPixelBufferGetBaseAddress(pixels);
    size_t width = CVPixelBufferGetWidth(pixels);
    size_t height = CVPixelBufferGetHeight(pixels);
    size_t stride = CVPixelBufferGetBytesPerRow(pixels);
    size_t row = width * 4;

    // GStreamer expects the rows of BGRA packed, while the surfaces of the system are padded
    if (base != NULL && stride != row)
    {
        size_t size = row * height;

        if (_rowsSize < size)
        {
            free(_rows);
            _rows = malloc(size);
            _rowsSize = _rows != NULL ? size : 0;
        }

        if (_rows != NULL)
        {
            for (size_t y = 0; y < height; y++)
                memcpy(_rows + y * row, base + y * stride, row);
        }

        base = _rows;
    }

    if (base != NULL)
        _callbacks.video(_ctx, base, (int32_t)width, (int32_t)height);

    CVPixelBufferUnlockBaseAddress(pixels, kCVPixelBufferLock_ReadOnly);
    _lastAt = Now();
}

- (void)keepAlive
{
    if (atomic_load(&_isStopping) || _last == NULL || Now() - _lastAt < KeepaliveNanoseconds)
        return;

    [self deliver:_last];
}

- (void)onScreen:(CMSampleBufferRef)sampleBuffer
{
    if (_kind == FR_SCK_KIND_AUDIO)
        return;

    CFArrayRef attachments = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, false);

    if (attachments == NULL || CFArrayGetCount(attachments) == 0)
        return;

    NSDictionary *info = (__bridge NSDictionary *)CFArrayGetValueAtIndex(attachments, 0);
    NSNumber *status = info[SCStreamFrameInfoStatus];

    // Idle and blank frames carry no picture: the keepalive repeats the last one
    if (status == nil || status.integerValue != SCFrameStatusComplete)
        return;

    CVPixelBufferRef pixels = CMSampleBufferGetImageBuffer(sampleBuffer);

    if (pixels == NULL)
        return;

    CVPixelBufferRetain(pixels);

    if (_last != NULL)
        CVPixelBufferRelease(_last);

    _last = pixels;
    [self deliver:pixels];
}

- (void)onAudio:(CMSampleBufferRef)sampleBuffer
{
    CMFormatDescriptionRef format = CMSampleBufferGetFormatDescription(sampleBuffer);
    const AudioStreamBasicDescription *description =
        format != NULL ? CMAudioFormatDescriptionGetStreamBasicDescription(format) : NULL;

    if (description == NULL || description->mFormatID != kAudioFormatLinearPCM ||
        !(description->mFormatFlags & kAudioFormatFlagIsFloat) || description->mBitsPerChannel != 32)
        return;

    size_t needed = 0;

    if (CMSampleBufferGetAudioBufferListWithRetainedBlockBuffer(sampleBuffer, &needed, NULL, 0, NULL, NULL, 0, NULL) !=
            noErr ||
        needed == 0)
        return;

    if (_listSize < needed)
    {
        free(_list);
        _list = malloc(needed);
        _listSize = _list != NULL ? needed : 0;

        if (_list == NULL)
            return;
    }

    CMBlockBufferRef block = NULL;

    if (CMSampleBufferGetAudioBufferListWithRetainedBlockBuffer(sampleBuffer, NULL, _list, _listSize, NULL, NULL,
            kCMSampleBufferFlag_AudioBufferList_Assure16ByteAlignment, &block) != noErr)
        return;

    int32_t channels = (int32_t)description->mChannelsPerFrame;
    int32_t frames = (int32_t)CMSampleBufferGetNumSamples(sampleBuffer);

    if (description->mFormatFlags & kAudioFormatFlagIsNonInterleaved)
    {
        // One buffer per channel, interleaved here for the caps of the appsrc
        size_t size = (size_t)frames * (size_t)channels * sizeof(float);

        if (_samplesSize < size)
        {
            free(_samples);
            _samples = malloc(size);
            _samplesSize = _samples != NULL ? size : 0;
        }

        if (_samples != NULL && (int32_t)_list->mNumberBuffers == channels)
        {
            for (int32_t channel = 0; channel < channels; channel++)
            {
                const float *source = _list->mBuffers[channel].mData;
                int32_t available = (int32_t)(_list->mBuffers[channel].mDataByteSize / sizeof(float));
                int32_t count = available < frames ? available : frames;

                for (int32_t frame = 0; frame < count; frame++)
                    _samples[frame * channels + channel] = source[frame];
            }

            _callbacks.audio(_ctx, _samples, frames, channels);
        }
    }
    else if (_list->mNumberBuffers > 0)
    {
        _callbacks.audio(_ctx, _list->mBuffers[0].mData, frames, channels);
    }

    if (block != NULL)
        CFRelease(block);
}

- (void)stream:(SCStream *)stream didOutputSampleBuffer:(CMSampleBufferRef)sampleBuffer ofType:(SCStreamOutputType)type
{
    if (atomic_load(&_isStopping) || !CMSampleBufferIsValid(sampleBuffer))
        return;

    if (type == SCStreamOutputTypeScreen)
        [self onScreen:sampleBuffer];
    else if (type == SCStreamOutputTypeAudio && _kind == FR_SCK_KIND_AUDIO)
        [self onAudio:sampleBuffer];
}

- (void)stream:(SCStream *)stream didStopWithError:(NSError *)error
{
    if (atomic_exchange(&_isStopping, true))
        return;

    // Reported from the queue of the pictures, so it never overlaps a callback
    NSString *message = error.localizedDescription ?: @"The capture stopped";
    int32_t code = ErrorCode(error);

    dispatch_async(_queue, ^{
        self->_callbacks.stopped(self->_ctx, code, message.UTF8String ?: "");
    });
}

@end

static int32_t Even(double value)
{
    int32_t rounded = (int32_t)value;
    return rounded < 2 ? 2 : rounded & ~1;
}

void *fr_sck_start(const fr_sck_config *config, const fr_sck_callbacks *callbacks, void *ctx, char *error,
                   int32_t error_size)
{
    @autoreleasepool
    {
        int32_t code = 0;
        NSString *message = nil;
        SCShareableContent *content = LoadContent(&code, &message);

        if (content == nil)
        {
            WriteError(error, error_size, message);
            return NULL;
        }

        SCContentFilter *filter = nil;
        SCStreamConfiguration *configuration = [SCStreamConfiguration new];

        if (config->kind == FR_SCK_KIND_DISPLAY)
        {
            for (SCDisplay *display in content.displays)
            {
                if (display.displayID == (CGDirectDisplayID)config->id)
                    filter = [[SCContentFilter alloc] initWithDisplay:display excludingWindows:@[]];
            }
        }
        else if (config->kind == FR_SCK_KIND_WINDOW)
        {
            for (SCWindow *window in content.windows)
            {
                if (window.windowID == (CGWindowID)config->id)
                    filter = [[SCContentFilter alloc] initWithDesktopIndependentWindow:window];
            }
        }
        else if (config->kind == FR_SCK_KIND_AUDIO && content.displays.count > 0)
        {
            // The sound does not depend on the display, only on the applications the filter takes
            SCDisplay *display = content.displays.firstObject;
            SCRunningApplication *application = nil;

            for (SCRunningApplication *candidate in content.applications)
            {
                if (candidate.processID == (pid_t)config->audio_pid)
                    application = candidate;
            }

            if (config->audio_exclude)
            {
                NSArray *excluded = application != nil && application.processID != getpid() ? @[application] : @[];
                filter = [[SCContentFilter alloc] initWithDisplay:display
                                            excludingApplications:excluded
                                                 exceptingWindows:@[]];
                configuration.excludesCurrentProcessAudio = config->audio_pid == (uint32_t)getpid();
            }
            else if (application != nil)
            {
                filter = [[SCContentFilter alloc] initWithDisplay:display
                                            includingApplications:@[application]
                                                 exceptingWindows:@[]];
                configuration.excludesCurrentProcessAudio = YES;
            }
        }

        if (filter == nil)
        {
            WriteError(error, error_size, @"The source to capture was not found");
            return NULL;
        }

        if (config->kind == FR_SCK_KIND_AUDIO)
        {
            configuration.width = AudioOnlySize;
            configuration.height = AudioOnlySize;
            configuration.minimumFrameInterval = CMTimeMake(1, 1);
            configuration.capturesAudio = YES;
            configuration.sampleRate = AudioSampleRate;
            configuration.channelCount = AudioChannels;
        }
        else
        {
            double width = filter.contentRect.size.width * filter.pointPixelScale;
            double height = filter.contentRect.size.height * filter.pointPixelScale;

            if (config->max_height > 0 && height > config->max_height)
            {
                width = width * config->max_height / height;
                height = config->max_height;
            }

            configuration.width = (size_t)Even(width);
            configuration.height = (size_t)Even(height);
            configuration.minimumFrameInterval = CMTimeMake(1, config->framerate > 0 ? config->framerate : 30);
            configuration.pixelFormat = kCVPixelFormatType_32BGRA;
            configuration.colorSpaceName = kCGColorSpaceSRGB;
            configuration.showsCursor = config->show_cursor != 0;
            configuration.preservesAspectRatio = YES;
            configuration.scalesToFit = YES;
            // One surface stays with the keepalive
            configuration.queueDepth = 6;
        }

        FRCapture *capture = [FRCapture new];
        capture->_callbacks = *callbacks;
        capture->_ctx = ctx;
        capture->_kind = config->kind;
        capture->_queue = dispatch_queue_create("flowrate.sck",
            dispatch_queue_attr_make_with_qos_class(DISPATCH_QUEUE_SERIAL, QOS_CLASS_USER_INTERACTIVE, 0));
        capture->_stream = [[SCStream alloc] initWithFilter:filter configuration:configuration delegate:capture];

        NSError *failure = nil;

        // The pictures of a sound capture are taken too: without an output the system complains about
        // every one it drops
        if (![capture->_stream addStreamOutput:capture
                                          type:SCStreamOutputTypeScreen
                            sampleHandlerQueue:capture->_queue
                                         error:&failure] ||
            (config->kind == FR_SCK_KIND_AUDIO &&
             ![capture->_stream addStreamOutput:capture
                                           type:SCStreamOutputTypeAudio
                             sampleHandlerQueue:capture->_queue
                                          error:&failure]))
        {
            WriteError(error, error_size, failure.localizedDescription ?: @"The capture could not be set up");
            return NULL;
        }

        __block NSError *startFailure = nil;
        dispatch_semaphore_t started = dispatch_semaphore_create(0);

        [capture->_stream startCaptureWithCompletionHandler:^(NSError *startError) {
            startFailure = startError;
            dispatch_semaphore_signal(started);
        }];

        if (dispatch_semaphore_wait(started, dispatch_time(DISPATCH_TIME_NOW, WaitNanoseconds)) != 0)
        {
            atomic_store(&capture->_isStopping, true);
            [capture->_stream stopCaptureWithCompletionHandler:^(NSError *ignored) {}];
            WriteError(error, error_size, @"The capture did not start in time");
            return NULL;
        }

        if (startFailure != nil)
        {
            WriteError(error, error_size, startFailure.localizedDescription ?: @"The capture did not start");
            return NULL;
        }

        if (config->kind != FR_SCK_KIND_AUDIO)
        {
            capture->_keepalive = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, 0, capture->_queue);
            __weak FRCapture *weak = capture;
            dispatch_source_set_event_handler(capture->_keepalive, ^{
                [weak keepAlive];
            });
            dispatch_source_set_timer(capture->_keepalive, dispatch_time(DISPATCH_TIME_NOW, KeepaliveNanoseconds),
                                      KeepaliveNanoseconds, KeepaliveNanoseconds / 10);
            dispatch_resume(capture->_keepalive);
        }

        return (__bridge_retained void *)capture;
    }
}

void fr_sck_stop(void *handle)
{
    if (handle == NULL)
        return;

    @autoreleasepool
    {
        FRCapture *capture = (__bridge_transfer FRCapture *)handle;
        bool wasStopped = atomic_exchange(&capture->_isStopping, true);

        if (!wasStopped)
        {
            dispatch_semaphore_t stopped = dispatch_semaphore_create(0);

            [capture->_stream stopCaptureWithCompletionHandler:^(NSError *ignored) {
                dispatch_semaphore_signal(stopped);
            }];

            dispatch_semaphore_wait(stopped, dispatch_time(DISPATCH_TIME_NOW, WaitNanoseconds));
        }

        if (capture->_keepalive != NULL)
            dispatch_source_cancel(capture->_keepalive);

        // Every callback runs on the queue and checks the flag first, so once the queue is drained none
        // runs any more, the stopped one of a capture the system ended included
        dispatch_sync(capture->_queue, ^{});
    }
}
