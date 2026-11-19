package com.flowrate.rtc;

import android.content.Context;

import java.nio.ByteBuffer;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

import org.webrtc.AudioSource;
import org.webrtc.AudioTrack;
import org.webrtc.DefaultVideoDecoderFactory;
import org.webrtc.DefaultVideoEncoderFactory;
import org.webrtc.EglBase;
import org.webrtc.Loggable;
import org.webrtc.Logging;
import org.webrtc.MediaConstraints;
import org.webrtc.MediaStreamTrack;
import org.webrtc.PeerConnection;
import org.webrtc.PeerConnectionFactory;
import org.webrtc.RTCStats;
import org.webrtc.RtpTransceiver;
import org.webrtc.SdpObserver;
import org.webrtc.SessionDescription;
import org.webrtc.VideoFrame;
import org.webrtc.VideoSink;
import org.webrtc.VideoTrack;
import org.webrtc.audio.JavaAudioDeviceModule;

/**
 * The peer connection of one voice session on top of libwebrtc, which owns ICE, DTLS-SRTP, the
 * microphone and the playback. The description of the SFU is built on the .NET side; this class only
 * answers it, so that the bindings need nothing of libwebrtc but this class.
 */
public final class VoicePeer {

    public interface Listener {
        /** The name of a PeerConnection.PeerConnectionState. */
        void onConnectionChanged(String state);

        /** The answer of a round, applied as the local description. */
        void onNegotiated(String localSdp);

        void onFailed(String error);

        /**
         * "mid=level;..." of the voices that arrive, and "local=level" of the microphone; levels 0..1.
         * report is the whole reading as a JSON array of RTCStats objects, as flowrate_webrtc gives it on
         * the desktop, when it was asked for; otherwise null.
         */
        void onStats(String levels, String report);

        /**
         * A decoded picture of the screen of a section, on the decoding thread of its stream. The
         * listener releases it, at once or once it has drawn it.
         */
        void onPicture(String mid, Picture picture);
    }

    /** The log of libwebrtc, see setLog. */
    public interface LogListener {
        /** severity: 0 verbose, 1 info, 2 warning, 3 error, the order of Logging.Severity. */
        void onLog(int severity, String tag, String message);
    }

    /**
     * A decoded picture in I420, in the memory of libwebrtc until it is released. Only the planes are
     * handed out, so that the bindings need nothing of libwebrtc.
     */
    public static final class Picture {
        private final VideoFrame.I420Buffer buffer;
        private final long timestampNs;

        Picture(VideoFrame.I420Buffer buffer, long timestampNs) {
            this.buffer = buffer;
            this.timestampNs = timestampNs;
        }

        public int getWidth() { return buffer.getWidth(); }
        public int getHeight() { return buffer.getHeight(); }
        public ByteBuffer getDataY() { return buffer.getDataY(); }
        public ByteBuffer getDataU() { return buffer.getDataU(); }
        public ByteBuffer getDataV() { return buffer.getDataV(); }
        public int getStrideY() { return buffer.getStrideY(); }
        public int getStrideU() { return buffer.getStrideU(); }
        public int getStrideV() { return buffer.getStrideV(); }
        public long getTimestampNs() { return timestampNs; }

        /** Gives the planes back to libwebrtc; once only. */
        public void release() { buffer.release(); }
    }

    private static PeerConnectionFactory factory;

    // What the camera and the encoders draw with; null where the device gave no EGL, and then the
    // camera is not offered
    private static EglBase egl;
    private static Context applicationContext;

    private final Listener listener;
    private final PeerConnection connection;
    private final AudioSource microphoneSource;
    private final AudioTrack microphone;

    // The voice each section plays, by mid. Kept from onTrack: getTransceivers disposes the wrappers
    // it handed out before, which breaks any other caller holding them
    private final Map<String, AudioTrack> voices = new ConcurrentHashMap<>();

    // The screen each section shows, by mid, with what takes its pictures. A section keeps its
    // transceiver and so its track when it is reused for another screen
    private final Map<String, VideoTrack> screens = new ConcurrentHashMap<>();
    private final Map<String, VideoSink> screenSinks = new ConcurrentHashMap<>();

    // What initialize gives libwebrtc for its log; 4 is LS_NONE
    private static LogListener logListener;
    private static int logSeverity = 4;

    /**
     * Passes the log of libwebrtc from minSeverity up (4: none) to the listener. libwebrtc takes it only
     * when it is initialized, so once it is this changes nothing until the process starts again; returns
     * whether it was in time.
     */
    public static synchronized boolean setLog(LogListener listener, int minSeverity) {
        logListener = listener;
        logSeverity = minSeverity;
        return factory == null;
    }

    /** Loads libwebrtc and opens the audio devices once per process. */
    public static synchronized void initialize(Context context) {
        if (factory != null)
            return;

        PeerConnectionFactory.InitializationOptions.Builder options =
                PeerConnectionFactory.InitializationOptions.builder(context);

        if (logListener != null && logSeverity < Logging.Severity.LS_NONE.ordinal()) {
            final LogListener listener = logListener;
            options.setInjectableLogger(new Loggable() {
                @Override
                public void onLogMessage(String message, Logging.Severity severity, String tag) {
                    listener.onLog(severity.ordinal(), tag, message);
                }
            }, Logging.Severity.values()[logSeverity]);
        }

        PeerConnectionFactory.initialize(options.createInitializationOptions());

        JavaAudioDeviceModule audioDevices = JavaAudioDeviceModule.builder(context)
                .setUseHardwareAcousticEchoCanceler(true)
                .setUseHardwareNoiseSuppressor(true)
                .createAudioDeviceModule();

        try {
            egl = EglBase.create();
        } catch (RuntimeException exception) {
            egl = null;
        }

        applicationContext = context.getApplicationContext();

        try {
            // Screens are decoded by MediaCodec where the device can, into memory: without an EGL
            // context the pictures come as planes, which the application draws itself
            factory = PeerConnectionFactory.builder()
                    .setAudioDeviceModule(audioDevices)
                    .setVideoDecoderFactory(new DefaultVideoDecoderFactory(null))
                    // The camera is encoded by MediaCodec where the device can, in software otherwise
                    .setVideoEncoderFactory(new DefaultVideoEncoderFactory(
                            egl == null ? null : egl.getEglBaseContext(), true, true))
                    .createPeerConnectionFactory();
        } finally {
            // The factory holds its own native reference.
            audioDevices.release();
        }
    }

    public VoicePeer(Listener listener) {
        this.listener = listener;

        PeerConnection.RTCConfiguration config = new PeerConnection.RTCConfiguration(new ArrayList<>());
        config.sdpSemantics = PeerConnection.SdpSemantics.UNIFIED_PLAN;
        // Every section on one transport, which is what keeps all the clients of the SFU on one port
        config.bundlePolicy = PeerConnection.BundlePolicy.MAXBUNDLE;
        config.rtcpMuxPolicy = PeerConnection.RtcpMuxPolicy.REQUIRE;
        // The phone moves between networks; the candidates of the new one are found on their own
        config.continualGatheringPolicy = PeerConnection.ContinualGatheringPolicy.GATHER_CONTINUALLY;

        connection = factory.createPeerConnection(config, new PeerConnection.Observer() {
            @Override public void onConnectionChange(PeerConnection.PeerConnectionState state) {
                VoicePeer.this.listener.onConnectionChanged(state.name());
            }
            @Override public void onSignalingChange(PeerConnection.SignalingState state) { }
            @Override public void onIceConnectionChange(PeerConnection.IceConnectionState state) { }
            @Override public void onIceConnectionReceivingChange(boolean receiving) { }
            @Override public void onIceGatheringChange(PeerConnection.IceGatheringState state) { }
            // mediasoup is ICE lite and learns our address from the checks, so nothing is trickled
            @Override public void onIceCandidate(org.webrtc.IceCandidate candidate) { }
            @Override public void onIceCandidatesRemoved(org.webrtc.IceCandidate[] candidates) { }
            @Override public void onAddStream(org.webrtc.MediaStream stream) { }
            @Override public void onRemoveStream(org.webrtc.MediaStream stream) { }
            @Override public void onDataChannel(org.webrtc.DataChannel channel) { }
            @Override public void onRenegotiationNeeded() { }

            @Override public void onTrack(RtpTransceiver transceiver) {
                MediaStreamTrack track = transceiver.getReceiver().track();

                final String mid = transceiver.getMid();

                if (mid == null)
                    return;

                if (track instanceof AudioTrack)
                    voices.put(mid, (AudioTrack) track);
                else if (track instanceof VideoTrack && !screenSinks.containsKey(mid))
                    showScreen(mid, (VideoTrack) track);
            }
        });

        microphoneSource = factory.createAudioSource(new MediaConstraints());
        microphone = factory.createAudioTrack("microphone", microphoneSource);
    }

    /**
     * Runs one offer/answer round: applies the description of the SFU, puts the microphone into the
     * sections we send in, answers, and gives each of those sections the SSRC the SFU expects there.
     *
     * @param sendMids     the sections we send in
     * @param sendSsrcs    the SSRC of each of them, as the SFU created its producer
     * @param sendRtxSsrcs the SSRC of the resent packets of each of them, 0 where there are none
     * @param sendCameras  the camera of each video section that sends one, null in the others
     */
    public void negotiate(String offer, final String[] sendMids, final long[] sendSsrcs, final long[] sendRtxSsrcs,
                          final CameraCapture[] sendCameras) {
        final Map<String, long[]> sends = new HashMap<>();
        final Map<String, CameraCapture> cameras = new HashMap<>();
        for (int i = 0; i < sendMids.length; i++) {
            sends.put(sendMids[i], new long[] { sendSsrcs[i], sendRtxSsrcs[i] });

            if (sendCameras[i] != null)
                cameras.put(sendMids[i], sendCameras[i]);
        }

        connection.setRemoteDescription(new Observer("set-remote-description") {
            @Override public void onSetSuccess() {
                for (RtpTransceiver transceiver : connection.getTransceivers()) {
                    String mid = transceiver.getMid();

                    if (mid != null && sends.containsKey(mid)) {
                        transceiver.setDirection(RtpTransceiver.RtpTransceiverDirection.SEND_ONLY);
                        CameraCapture camera = cameras.get(mid);

                        if (camera != null)
                            transceiver.getSender().setTrack(camera.track(), false);
                        else if (transceiver.getMediaType() == MediaStreamTrack.MediaType.MEDIA_TYPE_VIDEO)
                            // A camera that stopped: its stream stays, silent, until it is back
                            transceiver.getSender().setTrack(null, false);
                        else
                            transceiver.getSender().setTrack(microphone, false);
                    } else if (transceiver.getSender().track() != null) {
                        // A track that stopped: its section stays, emptied
                        transceiver.getSender().setTrack(null, false);
                        transceiver.setDirection(RtpTransceiver.RtpTransceiverDirection.RECV_ONLY);
                    }
                }

                connection.createAnswer(new Observer("create-answer") {
                    @Override public void onCreateSuccess(SessionDescription answer) {
                        final String sdp = rewriteSsrcs(answer.description, sends);

                        connection.setLocalDescription(new Observer("set-local-description") {
                            @Override public void onSetSuccess() {
                                listener.onNegotiated(sdp);
                            }
                        }, new SessionDescription(SessionDescription.Type.ANSWER, sdp));
                    }
                }, new MediaConstraints());
            }
        }, new SessionDescription(SessionDescription.Type.OFFER, offer));
    }

    /**
     * Prepares the capture of a camera at the given size and rate, by its name in Camera2; it goes into
     * a section through negotiate. Null where the device gave no EGL to capture with.
     */
    public static CameraCapture createCamera(String device, int width, int height, int framerate,
                                             CameraCapture.Listener listener) {
        synchronized (VoicePeer.class) {
            if (factory == null || egl == null)
                return null;
        }

        return new CameraCapture(applicationContext, factory, egl.getEglBaseContext(), device, width, height,
                framerate, listener);
    }

    /** Hands the pictures of the screen of a section to the listener, as I420. */
    private void showScreen(final String mid, VideoTrack track) {
        VideoSink sink = frame -> {
            // A hardware decoder gives NV12, which this converts; an I420 one is only retained
            VideoFrame.I420Buffer buffer = frame.getBuffer().toI420();

            if (buffer == null)
                return;

            // The listener releases it, whatever happens to the picture there
            listener.onPicture(mid, new Picture(buffer, frame.getTimestampNs()));
        };

        screens.put(mid, track);
        screenSinks.put(mid, sink);
        track.addSink(sink);
    }

    /** Stops or resumes sending the microphone, without touching its section. */
    public void setMicrophoneEnabled(boolean isEnabled) {
        microphone.setEnabled(isEnabled);
    }

    /** The playback volume of the voice of a section, 0 silences it. */
    public void setVolume(String mid, double volume) {
        AudioTrack voice = voices.get(mid);

        // A section whose track has not come yet takes the volume once the round completes
        if (voice != null)
            voice.setVolume(volume);
    }

    /** Reads how loud every voice is now and, when detailed, the whole reading; see Listener.onStats. */
    public void requestStats(boolean isDetailed) {
        connection.getStats(report -> {
            StringBuilder levels = new StringBuilder();
            StringBuilder json = isDetailed ? new StringBuilder("[") : null;

            for (RTCStats stats : report.getStatsMap().values()) {
                Map<String, Object> members = stats.getMembers();

                if (json != null)
                    appendJson(json, stats);

                if (!"audio".equals(members.get("kind")) || !(members.get("audioLevel") instanceof Double))
                    continue;

                if ("inbound-rtp".equals(stats.getType()) && members.get("mid") != null)
                    levels.append(members.get("mid"));
                else if ("media-source".equals(stats.getType()))
                    levels.append("local");
                else
                    continue;

                levels.append('=').append(members.get("audioLevel")).append(';');
            }

            listener.onStats(levels.toString(), json == null ? null : json.append(']').toString());
        });
    }

    // The numbers, texts and flags of one RTCStats object; the maps and arrays are of no use to the client
    private static void appendJson(StringBuilder json, RTCStats stats) {
        if (json.length() > 1)
            json.append(',');

        json.append("{\"type\":");
        appendString(json, stats.getType());
        json.append(",\"id\":");
        appendString(json, stats.getId());

        for (Map.Entry<String, Object> member : stats.getMembers().entrySet()) {
            Object value = member.getValue();

            if (!(value instanceof Number || value instanceof String || value instanceof Boolean))
                continue;

            if (value instanceof Double && (((Double) value).isNaN() || ((Double) value).isInfinite()))
                continue;

            json.append(',');
            appendString(json, member.getKey());
            json.append(':');

            if (value instanceof String)
                appendString(json, (String) value);
            else
                json.append(value);
        }

        json.append('}');
    }

    private static void appendString(StringBuilder json, String text) {
        json.append('"');

        for (int i = 0; i < text.length(); i++) {
            char c = text.charAt(i);

            if (c == '"' || c == '\\')
                json.append('\\').append(c);
            else if (c < 0x20)
                json.append(String.format("\\u%04x", (int) c));
            else
                json.append(c);
        }

        json.append('"');
    }

    public void close() {
        voices.clear();

        // Before the tracks go with the connection, so that no picture comes after
        for (Map.Entry<String, VideoSink> sink : screenSinks.entrySet()) {
            VideoTrack track = screens.get(sink.getKey());

            if (track != null)
                track.removeSink(sink.getValue());
        }

        screenSinks.clear();
        screens.clear();
        connection.close();
        connection.dispose();
        microphone.dispose();
        microphoneSource.dispose();
    }

    /**
     * Replaces the SSRCs libwebrtc picked in each section we send in with the ones of the producer of
     * the SFU, which knows our streams by them alone: the first of the section, or of its group FID, is
     * the stream, the second of the group the stream of the resent packets. libwebrtc sends with what
     * the local description says.
     */
    private static String rewriteSsrcs(String sdp, Map<String, long[]> sends) {
        String[] sections = sdp.split("(?=\r\nm=)");
        StringBuilder result = new StringBuilder();

        for (String section : sections) {
            long[] ssrcs = null;

            for (Map.Entry<String, long[]> send : sends.entrySet()) {
                if (section.contains("\r\na=mid:" + send.getKey() + "\r\n")) {
                    ssrcs = send.getValue();
                    break;
                }
            }

            if (ssrcs != null)
                section = renameSsrcs(section, ssrcs[0], ssrcs[1]);

            result.append(section);
        }

        return result.toString();
    }

    private static String renameSsrcs(String section, long ssrc, long rtxSsrc) {
        Map<String, String> renames = new HashMap<>();
        String group = "\r\na=ssrc-group:FID ";
        String marker = "\r\na=ssrc:";
        int groupAt = section.indexOf(group);

        if (groupAt >= 0) {
            int from = groupAt + group.length();
            int end = section.indexOf("\r\n", from);
            String[] members = (end < 0 ? section.substring(from) : section.substring(from, end)).split(" ");

            renames.put(members[0], Long.toString(ssrc));

            if (members.length > 1 && rtxSsrc != 0)
                renames.put(members[1], Long.toString(rtxSsrc));
        } else {
            int at = section.indexOf(marker);

            if (at < 0)
                return section;

            int from = at + marker.length();
            renames.put(section.substring(from, section.indexOf(' ', from)), Long.toString(ssrc));
        }

        String[] lines = section.split("\r\n", -1);
        StringBuilder result = new StringBuilder();

        for (int i = 0; i < lines.length; i++) {
            String line = lines[i];

            if (line.startsWith("a=ssrc:")) {
                int space = line.indexOf(' ');
                String old = space < 0 ? line.substring(7) : line.substring(7, space);
                String renamed = renames.get(old);

                if (renamed != null)
                    line = "a=ssrc:" + renamed + (space < 0 ? "" : line.substring(space));
            } else if (line.startsWith("a=ssrc-group:")) {
                String[] parts = line.split(" ");

                for (int j = 1; j < parts.length; j++) {
                    String renamed = renames.get(parts[j]);

                    if (renamed != null)
                        parts[j] = renamed;
                }

                StringBuilder joined = new StringBuilder(parts[0]);

                for (int j = 1; j < parts.length; j++)
                    joined.append(' ').append(parts[j]);

                line = joined.toString();
            }

            if (i > 0)
                result.append("\r\n");

            result.append(line);
        }

        return result.toString();
    }

    private abstract class Observer implements SdpObserver {
        private final String step;

        Observer(String step) {
            this.step = step;
        }

        @Override public void onCreateSuccess(SessionDescription description) { }
        @Override public void onSetSuccess() { }

        @Override public void onCreateFailure(String error) {
            listener.onFailed(step + ": " + error);
        }

        @Override public void onSetFailure(String error) {
            listener.onFailed(step + ": " + error);
        }
    }
}
