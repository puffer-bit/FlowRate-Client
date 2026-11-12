package com.flowrate.rtc;

import android.media.AudioAttributes;
import android.media.MediaPlayer;
import android.view.Surface;
import java.util.concurrent.atomic.AtomicBoolean;
import org.webrtc.SurfaceTextureHelper;
import org.webrtc.ThreadUtils;
import org.webrtc.VideoFrame;

/** Decodes into memory-backed pictures, without creating an Android view or popup window. */
public final class ChatVideoEngine {
    public interface Listener {
        /** Takes ownership of this picture, including releasing it. */
        void onPicture(VoicePeer.Picture picture);
    }

    private final SurfaceTextureHelper helper;
    private final AtomicBoolean closed = new AtomicBoolean();
    private MediaPlayer player;
    private Surface surface;
    private volatile boolean ready, playing = true, ended;
    private volatile int position, duration;
    private volatile String error;
    private float volume;

    public ChatVideoEngine(String path, float volume, Listener listener) {
        System.loadLibrary("jingle_peerconnection_so");
        this.volume = volume;
        helper = SurfaceTextureHelper.create("ChatVideoDecoder", null);
        if (helper == null)
            throw new IllegalStateException("Could not create the video decoder surface.");
        helper.startListening(frame -> {
            if (closed.get()) return;
            VideoFrame.I420Buffer buffer;
            try { buffer = frame.getBuffer().toI420(); }
            catch (Exception exception) { fail(exception); return; }
            if (buffer != null)
                listener.onPicture(new VoicePeer.Picture(buffer, frame.getTimestampNs()));
        });
        helper.getHandler().post(() -> {
            if (closed.get()) return;
            try {
                player = new MediaPlayer();
                player.setAudioAttributes(new AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_MEDIA)
                    .setContentType(AudioAttributes.CONTENT_TYPE_MOVIE).build());
                helper.setTextureSize(640, 360);
                surface = new Surface(helper.getSurfaceTexture());
                player.setSurface(surface);
                player.setOnVideoSizeChangedListener((p, width, height) -> {
                    if (!closed.get() && width > 0 && height > 0) {
                        double scale = Math.min(1.0, 640.0 / Math.max(width, height));
                        helper.setTextureSize(Math.max(2, (int)(width * scale)),
                            Math.max(2, (int)(height * scale)));
                    }
                });
                player.setOnPreparedListener(p -> {
                    if (closed.get() || p != player) return;
                    ready = true;
                    duration = p.getDuration();
                    p.setVolume(this.volume, this.volume);
                    if (playing) p.start();
                    poll();
                });
                player.setOnCompletionListener(p -> { ended = true; playing = false; position = duration; });
                player.setOnErrorListener((p, what, extra) -> {
                    error = "Android video playback failed: " + what + "/" + extra;
                    playing = false;
                    return true;
                });
                player.setDataSource(path);
                player.prepareAsync();
            } catch (Exception exception) { fail(exception); }
        });
    }

    private void poll() {
        if (closed.get() || error != null || !ready) return;
        try {
            if (!ended) position = player.getCurrentPosition();
            helper.getHandler().postDelayed(this::poll, 200);
        } catch (Exception exception) { fail(exception); }
    }

    private void fail(Exception exception) {
        error = exception.toString();
        playing = false;
    }

    public boolean isPlaying() { return playing && !ended && error == null; }
    public int getPosition() { return position; }
    public int getDuration() { return duration; }
    public String getError() { return error; }

    public void setPlaying(boolean value) {
        helper.getHandler().post(() -> {
            if (closed.get() || error != null) return;
            try {
                playing = value;
                if (!ready) return;
                if (value) {
                    if (ended) { player.seekTo(0); ended = false; }
                    player.start();
                } else player.pause();
            } catch (Exception exception) { fail(exception); }
        });
    }

    public void seek(int milliseconds) {
        helper.getHandler().post(() -> {
            if (closed.get() || !ready || error != null) return;
            try {
                position = Math.max(0, Math.min(milliseconds, duration));
                ended = false;
                player.seekTo(position);
            } catch (Exception exception) { fail(exception); }
        });
    }

    public void setVolume(float value) {
        helper.getHandler().post(() -> {
            if (closed.get() || error != null) return;
            try {
                volume = value;
                if (ready) player.setVolume(value, value);
            } catch (Exception exception) { fail(exception); }
        });
    }

    /** Drains callbacks before their managed listener can be disposed. Safe before preparation. */
    public void close() {
        if (!closed.compareAndSet(false, true)) return;
        helper.stopListening();
        ThreadUtils.invokeAtFrontUninterruptibly(helper.getHandler(), () -> {
            if (player != null) {
                player.setOnPreparedListener(null);
                player.setOnCompletionListener(null);
                player.setOnErrorListener(null);
                player.setOnVideoSizeChangedListener(null);
                player.release();
                player = null;
            }
            if (surface != null) { surface.release(); surface = null; }
        });
        helper.dispose();
    }
}
