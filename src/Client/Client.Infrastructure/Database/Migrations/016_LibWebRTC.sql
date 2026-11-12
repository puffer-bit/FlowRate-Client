-- Whether the voice of a session goes over libwebrtc instead of GStreamer, an experimental feature
ALTER TABLE AppearanceSettings ADD COLUMN UseLibWebRTC INTEGER NOT NULL DEFAULT 0;
