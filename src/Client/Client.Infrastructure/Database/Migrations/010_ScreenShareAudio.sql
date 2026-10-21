-- The sound of a shown screen is played on settings of its own, apart from the voice of the user
ALTER TABLE RememberedUsers ADD COLUMN ScreenVolume INTEGER NOT NULL DEFAULT 100;
ALTER TABLE RememberedUsers ADD COLUMN IsScreenVolumeOverrideEnabled INTEGER NOT NULL DEFAULT 0;
ALTER TABLE RememberedUsers ADD COLUMN IsScreenMuted INTEGER NOT NULL DEFAULT 0;

-- Whether the sound goes along with a shared screen, and on Linux the application it is taken from
ALTER TABLE ScreenShareSettings ADD COLUMN IsAudioEnabled INTEGER NOT NULL DEFAULT 0;
ALTER TABLE ScreenShareSettings ADD COLUMN AudioAppKey TEXT NULL;
