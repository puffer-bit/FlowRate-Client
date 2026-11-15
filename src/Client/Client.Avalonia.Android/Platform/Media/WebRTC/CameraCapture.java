package com.flowrate.rtc;

import android.content.Context;

import java.nio.ByteBuffer;
import java.util.ArrayList;

import org.webrtc.Camera2Capturer;
import org.webrtc.Camera2Enumerator;
import org.webrtc.CameraVideoCapturer;
import org.webrtc.EglBase;
import org.webrtc.JavaI420Buffer;
import org.webrtc.JniCommon;
import org.webrtc.PeerConnectionFactory;
import org.webrtc.SurfaceTextureHelper;
import org.webrtc.VideoFrame;
import org.webrtc.VideoSink;
import org.webrtc.VideoSource;
import org.webrtc.VideoTrack;
import org.webrtc.YuvHelper;

/**
 * The camera we send, captured by Camera2 and encoded by libwebrtc itself: the track goes into its
 * section through VoicePeer.negotiate. What is captured is also handed out, upright, as the preview.
 */
public final class CameraCapture {

    public interface Listener {
        /** A picture of the preview, on the thread of the camera. The listener releases it. */
        void onPicture(VoicePeer.Picture picture);

        /** The camera was lost or could not open; nothing is captured any more. */
        void onFailed(String error);
    }

    /**
     * The cameras to choose from: the first one facing the user, then the first one facing away, by
     * their names in Camera2. A phone often lists more of each, which are the same lens to the user.
     */
    public static String[] getDevices(Context context) {
        Camera2Enumerator enumerator = new Camera2Enumerator(context);
        ArrayList<String> devices = new ArrayList<>();
        String back = null;

        for (String name : enumerator.getDeviceNames()) {
            if (enumerator.isFrontFacing(name)) {
                if (devices.isEmpty())
                    devices.add(name);
            } else if (back == null && enumerator.isBackFacing(name)) {
                back = name;
            }
        }

        if (back != null)
            devices.add(back);

        return devices.toArray(new String[0]);
    }

    public static boolean isFrontFacing(Context context, String device) {
        return new Camera2Enumerator(context).isFrontFacing(device);
    }

    private final Listener listener;
    private final SurfaceTextureHelper textures;
    private final Camera2Capturer capturer;
    private final VideoSource source;
    private final VideoTrack track;
    private final int width;
    private final int height;
    private final int framerate;
    private volatile boolean isPreviewShown = true;
    private boolean isClosed;

    private final VideoSink preview = new VideoSink() {
        @Override
        public void onFrame(VideoFrame frame) {
            if (!isPreviewShown)
                return;

            VideoFrame.I420Buffer buffer = frame.getBuffer().toI420();

            if (buffer == null)
                return;

            int rotation = frame.getRotation();

            if (rotation != 0) {
                VideoFrame.I420Buffer upright = rotate(buffer, rotation);
                buffer.release();
                buffer = upright;
            }

            // The listener releases it, whatever happens to the picture there
            listener.onPicture(new VoicePeer.Picture(buffer, frame.getTimestampNs()));
        }
    };

    CameraCapture(Context context, PeerConnectionFactory factory, EglBase.Context eglContext, String device,
                  int width, int height, int framerate, Listener listener) {
        this.listener = listener;
        this.width = width;
        this.height = height;
        this.framerate = framerate;

        textures = SurfaceTextureHelper.create("FlowRateCamera", eglContext);
        capturer = new Camera2Capturer(context, device, new CameraVideoCapturer.CameraEventsHandler() {
            @Override public void onCameraError(String error) { fail(error); }
            @Override public void onCameraDisconnected() { fail("camera disconnected"); }
            @Override public void onCameraFreezed(String error) { }
            @Override public void onCameraOpening(String name) { }
            @Override public void onFirstFrameAvailable() { }
            @Override public void onCameraClosed() { }
        });
        source = factory.createVideoSource(false);
        capturer.initialize(textures, context, source.getCapturerObserver());
        track = factory.createVideoTrack("camera", source);
        track.addSink(preview);
    }

    VideoTrack track() {
        return track;
    }

    /** Opens the camera; the pictures go out once the track is in a section. */
    public void start() {
        capturer.startCapture(width, height, framerate);
    }

    /** Captures from another camera, without touching the track or its section. */
    public void switchTo(String device) {
        capturer.switchCamera(null, device);
    }

    /** Starts or stops handing out the preview; sending goes on either way. */
    public void setPreviewShown(boolean isShown) {
        isPreviewShown = isShown;
    }

    public synchronized void close() {
        if (isClosed)
            return;

        isClosed = true;
        isPreviewShown = false;
        track.removeSink(preview);

        // Waits for the camera to close
        capturer.stopCapture();

        capturer.dispose();
        textures.dispose();
        // A sender that still holds the track keeps it alive until its section is emptied
        track.dispose();
        source.dispose();
    }

    private void fail(String error) {
        synchronized (this) {
            if (isClosed)
                return;
        }

        listener.onFailed(error);
    }

    // The camera gives its pictures as the sensor lies; the preview shows them as the user holds the phone
    private static VideoFrame.I420Buffer rotate(VideoFrame.I420Buffer buffer, int rotation) {
        int width = rotation % 180 == 0 ? buffer.getWidth() : buffer.getHeight();
        int height = rotation % 180 == 0 ? buffer.getHeight() : buffer.getWidth();
        int chromaWidth = (width + 1) / 2;
        int chromaHeight = (height + 1) / 2;
        int sizeY = width * height;
        int sizeChroma = chromaWidth * chromaHeight;
        final ByteBuffer memory = JniCommon.nativeAllocateByteBuffer(sizeY + 2 * sizeChroma);

        YuvHelper.I420Rotate(buffer.getDataY(), buffer.getStrideY(), buffer.getDataU(), buffer.getStrideU(),
                buffer.getDataV(), buffer.getStrideV(), memory, buffer.getWidth(), buffer.getHeight(), rotation);

        return JavaI420Buffer.wrap(width, height,
                slice(memory, 0, sizeY), width,
                slice(memory, sizeY, sizeChroma), chromaWidth,
                slice(memory, sizeY + sizeChroma, sizeChroma), chromaWidth,
                () -> JniCommon.nativeFreeByteBuffer(memory));
    }

    private static ByteBuffer slice(ByteBuffer memory, int offset, int length) {
        ByteBuffer view = memory.duplicate();
        view.position(offset);
        view.limit(offset + length);
        return view.slice();
    }
}
