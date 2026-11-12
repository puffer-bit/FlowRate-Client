ALTER TABLE VideoSettings RENAME TO PreviousVideoSettings;
CREATE TABLE VideoSettings (
    Id INTEGER NOT NULL PRIMARY KEY CHECK (Id IN (1, 2)),
    IsHardwareDecodingEnabled INTEGER NOT NULL DEFAULT 1,
    Decoder TEXT, Encoder TEXT,
    IsZeroCopyEnabled INTEGER NOT NULL DEFAULT 1,
    IsTimedPresentationEnabled INTEGER NOT NULL DEFAULT 1,
    PresentationBufferFrames INTEGER NOT NULL DEFAULT 2,
    Av1Decoder TEXT, Av1Encoder TEXT,
    Vp8Encoder TEXT, Vp9Encoder TEXT, Vp8Decoder TEXT, Vp9Decoder TEXT,
    IsCodecSettingsWarningAcknowledged INTEGER NOT NULL DEFAULT 0
) STRICT;
INSERT INTO VideoSettings SELECT Id, IsHardwareDecodingEnabled, Decoder, Encoder,
    IsZeroCopyEnabled, IsTimedPresentationEnabled, PresentationBufferFrames,
    Av1Decoder, Av1Encoder, Vp8Encoder, Vp9Encoder, Vp8Decoder, Vp9Decoder,
    IsCodecSettingsWarningAcknowledged FROM PreviousVideoSettings;
DROP TABLE PreviousVideoSettings;
INSERT INTO VideoSettings (Id) VALUES (2);
ALTER TABLE CodecParameters RENAME TO PreviousCodecParameters;
CREATE TABLE CodecParameters (
    Profile INTEGER NOT NULL DEFAULT 1 CHECK (Profile IN (1, 2)),
    Factory TEXT NOT NULL, Name TEXT NOT NULL, Value TEXT NOT NULL,
    PRIMARY KEY (Profile, Factory, Name)
);
INSERT INTO CodecParameters (Factory, Name, Value) SELECT Factory, Name, Value FROM PreviousCodecParameters;
DROP TABLE PreviousCodecParameters;
CREATE TABLE CameraPreferences (
    Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    DeviceId TEXT, MaxHeight INTEGER NOT NULL DEFAULT 720,
    Framerate INTEGER NOT NULL DEFAULT 30, BitrateKbps INTEGER,
    LastWasCamera INTEGER NOT NULL DEFAULT 0
) STRICT;
