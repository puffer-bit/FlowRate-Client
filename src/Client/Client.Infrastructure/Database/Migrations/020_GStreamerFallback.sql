-- libwebrtc is the engine of the voice and the screens for everyone, those who never chose it as well; GStreamer stays
-- an experimental fallback, off until chosen
ALTER TABLE AudioSettings ADD COLUMN UseGStreamer INTEGER NOT NULL DEFAULT 0;

ALTER TABLE AudioSettings DROP COLUMN UseLibWebRTC;
