-- How screens are decoded, shown and encoded, see the Video tab of the settings
CREATE TABLE VideoSettings (
    Id                         INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    IsHardwareDecodingEnabled  INTEGER NOT NULL DEFAULT 1,
    Decoder                    TEXT,
    Encoder                    TEXT,
    IsZeroCopyEnabled          INTEGER NOT NULL DEFAULT 1,
    IsTimedPresentationEnabled INTEGER NOT NULL DEFAULT 1,
    PresentationBufferFrames   INTEGER NOT NULL DEFAULT 2,
    ShowFrameStats             INTEGER NOT NULL DEFAULT 0
) STRICT;

INSERT INTO VideoSettings (Id) VALUES (1);
