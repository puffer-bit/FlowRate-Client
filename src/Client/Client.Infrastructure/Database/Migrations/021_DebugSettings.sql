-- The Logging and debug tab of the settings
CREATE TABLE DebugSettings (
    Id                INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    -- A JournalLevel, NULL for the level of the build
    JournalLevel      INTEGER,
    WriteToFiles      INTEGER NOT NULL DEFAULT 1,
    -- NativeLogLevel of each, 0 is off
    GStreamerLogLevel INTEGER NOT NULL DEFAULT 0,
    LibWebRTCLogLevel INTEGER NOT NULL DEFAULT 0,
    -- Moved here from the Video tab
    ShowFrameStats    INTEGER NOT NULL DEFAULT 0
) STRICT;

INSERT INTO DebugSettings (Id, ShowFrameStats)
SELECT 1, COALESCE((SELECT ShowFrameStats FROM VideoSettings WHERE Id = 1), 0);

ALTER TABLE VideoSettings DROP COLUMN ShowFrameStats;
