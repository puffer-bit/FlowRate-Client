-- The choice of libwebrtc for the voice is a setting of the voice, not of the appearance
ALTER TABLE AudioSettings ADD COLUMN UseLibWebRTC INTEGER NOT NULL DEFAULT 0;

UPDATE AudioSettings SET UseLibWebRTC = (SELECT UseLibWebRTC FROM AppearanceSettings WHERE AppearanceSettings.Id = 1)
WHERE Id = 1 AND EXISTS (SELECT 1 FROM AppearanceSettings WHERE Id = 1);

ALTER TABLE AppearanceSettings DROP COLUMN UseLibWebRTC;
