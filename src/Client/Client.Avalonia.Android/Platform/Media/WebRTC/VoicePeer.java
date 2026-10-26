package com.flowrate.rtc;

import android.content.Context;
import android.media.AudioManager;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

import org.webrtc.AudioSource;
import org.webrtc.AudioTrack;
import org.webrtc.MediaConstraints;
import org.webrtc.MediaStreamTrack;
import org.webrtc.PeerConnection;
import org.webrtc.PeerConnectionFactory;
import org.webrtc.RTCStats;
import org.webrtc.RtpTransceiver;
import org.webrtc.SdpObserver;
import org.webrtc.SessionDescription;
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

        /** "mid=level;..." of the voices that arrive, and "local=level" of the microphone; levels 0..1. */
        void onLevels(String levels);
    }

    private static PeerConnectionFactory factory;
    private static int communicationUsers;
    private static int previousMode;
    private static boolean previousSpeakerphone;

    private final Listener listener;
    private final PeerConnection connection;
    private final AudioSource microphoneSource;
    private final AudioTrack microphone;

    // The voice each section plays, by mid. Kept from onTrack: getTransceivers disposes the wrappers
    // it handed out before, which breaks any other caller holding them
    private final Map<String, AudioTrack> voices = new ConcurrentHashMap<>();

    /** Loads libwebrtc and opens the audio devices once per process. */
    public static synchronized void initialize(Context context) {
        if (factory != null)
            return;

        PeerConnectionFactory.initialize(PeerConnectionFactory.InitializationOptions.builder(context)
                .createInitializationOptions());

        JavaAudioDeviceModule audioDevices = JavaAudioDeviceModule.builder(context)
                .setUseHardwareAcousticEchoCanceler(true)
                .setUseHardwareNoiseSuppressor(true)
                .createAudioDeviceModule();

        try {
            factory = PeerConnectionFactory.builder()
                    .setAudioDeviceModule(audioDevices)
                    .createPeerConnectionFactory();
        } finally {
            // The factory holds its own native reference.
            audioDevices.release();
        }
    }

    /** Routes the sound for a call, on the loudspeaker, or gives it back to the media. */
    public static synchronized void setCommunicationMode(Context context, boolean isOn) {
        AudioManager audio = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);

        if (isOn) {
            if (communicationUsers++ != 0)
                return;

            previousMode = audio.getMode();
            previousSpeakerphone = audio.isSpeakerphoneOn();
            audio.setMode(AudioManager.MODE_IN_COMMUNICATION);
            audio.setSpeakerphoneOn(true);
        } else {
            // A replacement peer can start before the previous one finishes closing.
            if (communicationUsers == 0 || --communicationUsers != 0)
                return;

            audio.setSpeakerphoneOn(previousSpeakerphone);
            audio.setMode(previousMode);
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

                if (transceiver.getMid() != null && track instanceof AudioTrack)
                    voices.put(transceiver.getMid(), (AudioTrack) track);
            }
        });

        microphoneSource = factory.createAudioSource(new MediaConstraints());
        microphone = factory.createAudioTrack("microphone", microphoneSource);
    }

    /**
     * Runs one offer/answer round: applies the description of the SFU, puts the microphone into the
     * sections we send in, answers, and gives each of those sections the SSRC the SFU expects there.
     *
     * @param sendMids  the sections we send in
     * @param sendSsrcs the SSRC of each of them, as the SFU created its producer
     */
    public void negotiate(String offer, final String[] sendMids, final long[] sendSsrcs) {
        final Map<String, Long> sends = new HashMap<>();
        for (int i = 0; i < sendMids.length; i++)
            sends.put(sendMids[i], sendSsrcs[i]);

        connection.setRemoteDescription(new Observer("set-remote-description") {
            @Override public void onSetSuccess() {
                for (RtpTransceiver transceiver : connection.getTransceivers()) {
                    String mid = transceiver.getMid();

                    if (mid != null && sends.containsKey(mid)) {
                        transceiver.setDirection(RtpTransceiver.RtpTransceiverDirection.SEND_ONLY);
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

    /** Reads how loud every voice is now, see Listener.onLevels. */
    public void requestLevels() {
        connection.getStats(report -> {
            StringBuilder levels = new StringBuilder();

            for (RTCStats stats : report.getStatsMap().values()) {
                Map<String, Object> members = stats.getMembers();

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

            listener.onLevels(levels.toString());
        });
    }

    public void close() {
        voices.clear();
        connection.close();
        connection.dispose();
        microphone.dispose();
        microphoneSource.dispose();
    }

    /**
     * Replaces the SSRC libwebrtc picked in each section we send in with the one of the producer
     * of the SFU, which knows our stream by it alone. libwebrtc sends with what the local
     * description says.
     */
    private static String rewriteSsrcs(String sdp, Map<String, Long> sends) {
        String[] sections = sdp.split("(?=\r\nm=)");
        StringBuilder result = new StringBuilder();

        for (String section : sections) {
            Long ssrc = null;

            for (Map.Entry<String, Long> send : sends.entrySet()) {
                if (section.contains("\r\na=mid:" + send.getKey() + "\r\n")) {
                    ssrc = send.getValue();
                    break;
                }
            }

            int start = section.indexOf("\r\na=ssrc:");

            if (ssrc != null && start >= 0) {
                int from = start + "\r\na=ssrc:".length();
                String old = section.substring(from, section.indexOf(' ', from));
                section = section.replace("a=ssrc:" + old + " ", "a=ssrc:" + ssrc + " ");
            }

            result.append(section);
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
