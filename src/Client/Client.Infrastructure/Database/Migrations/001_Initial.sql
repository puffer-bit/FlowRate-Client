-- Settings tables hold a single row each, created here with the defaults

CREATE TABLE AccountingSettings (
    Id                           INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    IsUserIdentityEncryptEnabled INTEGER NOT NULL DEFAULT 0
) STRICT;

CREATE TABLE AppearanceSettings (
    Id                    INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    ThemeType             INTEGER NOT NULL DEFAULT 0,
    -- The language of the interface as its code such as "ru"; NULL follows the operating system
    LanguageCode          TEXT NULL,
    -- Colors chosen instead of the ones of the theme, as "#RRGGBB"; NULL keeps the color of the theme
    PrimaryColor          TEXT NULL,
    SecondaryColor        TEXT NULL,
    DangerColor           TEXT NULL,
    SaveColor             TEXT NULL,
    -- Whether the interface is animated, and how fast: AnimationSpeed is 0 slow, 1 normal, 2 fast
    IsAnimationEnabled    INTEGER NOT NULL DEFAULT 1,
    AnimationSpeed        INTEGER NOT NULL DEFAULT 1,
    -- The size the interface is drawn at, in percent of its usual size
    InterfaceScale        INTEGER NOT NULL DEFAULT 100,
    -- Whether the main window draws its own title bar instead of the one of the system
    UseCustomTitleBar     INTEGER NOT NULL DEFAULT 0,
    -- Whether the experimental features are enabled as a whole; each keeps its own choice besides
    IsExperimentalEnabled INTEGER NOT NULL DEFAULT 0,
    -- Colors of the marks and texts that tell a state, chosen like the ones of the buttons
    SuccessColor          TEXT NULL,
    WarningColor          TEXT NULL,
    ErrorColor            TEXT NULL
) STRICT;

CREATE TABLE AudioSettings (
    Id                   INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    InputDeviceId        TEXT NULL,
    InputDeviceName      TEXT NULL,
    InputVolume          REAL NOT NULL DEFAULT 1.0,
    -- The microphone is sent only while the user speaks, from a peak level in dBFS
    IsVoiceGateEnabled   INTEGER NOT NULL DEFAULT 1,
    VoiceGateThresholdDb REAL NOT NULL DEFAULT -32.0,
    -- How much background noise is taken out of the microphone, a NoiseSuppressionLevel
    NoiseSuppression     INTEGER NOT NULL DEFAULT 2,
    -- The sounds of the interface: whether they are played and how loud, from 0.0 to 1.0
    AreSoundsEnabled     INTEGER NOT NULL DEFAULT 1,
    SoundsVolume         REAL NOT NULL DEFAULT 0.7,
    -- The folder of the sound pack in use, NULL for the built-in sounds
    SoundPack            TEXT NULL,
    -- The interface sounds the user switched off one by one: their file names without the extension, separated by commas
    DisabledSounds       TEXT NOT NULL DEFAULT ''
) STRICT;

CREATE TABLE BehaviorSettings (
    Id                                   INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    IsAutoConnectEnabled                 INTEGER NOT NULL DEFAULT 0,
    AutoConnectCoordinatorId             TEXT NULL,
    AutoConnectUserId                    INTEGER NULL,
    IsFallbackConnectionEnabled          INTEGER NOT NULL DEFAULT 0,
    IsFallbackConnectionWarningEnabled   INTEGER NOT NULL DEFAULT 1,
    IsPropagatedConnectionWarningEnabled INTEGER NOT NULL DEFAULT 1,
    CloseButtonAction                    INTEGER NOT NULL DEFAULT 0
) STRICT;

-- How the screen was shared the last time, offered again in the dialog that starts a share
CREATE TABLE ScreenShareSettings (
    Id          INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    MaxHeight   INTEGER NOT NULL DEFAULT 1080,
    Framerate   INTEGER NOT NULL DEFAULT 30,
    -- The bitrate the user set in kbit/s; NULL counts it from the size and the frame rate
    BitrateKbps INTEGER NULL
) STRICT;

INSERT INTO AccountingSettings (Id) VALUES (1);
INSERT INTO AppearanceSettings (Id) VALUES (1);
INSERT INTO AudioSettings (Id) VALUES (1);
INSERT INTO BehaviorSettings (Id) VALUES (1);
INSERT INTO ScreenShareSettings (Id) VALUES (1);

-- The sections of the settings dialog that are collapsed; a section that is not listed is shown
CREATE TABLE CollapsedSections (
    SectionKey TEXT NOT NULL PRIMARY KEY
) STRICT;

-- The list of single sounds in the audio settings is long, so it starts folded
INSERT INTO CollapsedSections (SectionKey) VALUES ('audio.soundEvents');

CREATE TABLE SavedCoordinators (
    CoordinatorId TEXT NOT NULL PRIMARY KEY,
    Name          TEXT NULL,
    Address       TEXT NOT NULL,
    Port          INTEGER NOT NULL
) STRICT;

CREATE INDEX IX_SavedCoordinators_Address_Port ON SavedCoordinators (Address, Port);

-- User ids are only unique within a coordinator
CREATE TABLE SavedIdentities (
    CoordinatorId TEXT NOT NULL REFERENCES SavedCoordinators (CoordinatorId) ON DELETE CASCADE,
    UserId        INTEGER NOT NULL,
    UserIdentity  TEXT NOT NULL,
    Username      TEXT NOT NULL,
    Password      TEXT NULL,
    PRIMARY KEY (CoordinatorId, UserId)
) STRICT;

-- Volume is the percent of the original level, so 100 leaves it as it is
CREATE TABLE RememberedUsers (
    CoordinatorInstanceId   TEXT NOT NULL,
    UserId                  INTEGER NOT NULL,
    Username                TEXT NOT NULL,
    Ip                      TEXT NULL,
    RegionName              TEXT NULL,
    RegionCode              TEXT NULL,
    DisplayName             TEXT NULL,
    Prefix                  TEXT NULL,
    AvatarUrl               TEXT NULL,
    State                   INTEGER NOT NULL,
    Status                  INTEGER NOT NULL,
    FirstConnectionTime     TEXT NOT NULL,
    LastConnectionTime      TEXT NOT NULL,
    Volume                  INTEGER NOT NULL DEFAULT 100,
    IsVolumeOverrideEnabled INTEGER NOT NULL DEFAULT 0,
    IsMuted                 INTEGER NOT NULL DEFAULT 0,
    IsBlocked               INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (CoordinatorInstanceId, UserId)
) STRICT;

-- One certificate per address; host names are stored lower case
CREATE TABLE TrustedCertificates (
    Host          TEXT NOT NULL,
    Port          INTEGER NOT NULL,
    Fingerprint   TEXT NOT NULL,
    Subject       TEXT NOT NULL,
    NotAfter      TEXT NOT NULL,
    TrustedAt     TEXT NOT NULL,
    CoordinatorId TEXT NULL,
    PRIMARY KEY (Host, Port)
) STRICT;

-- The cached tail of a chat: a run of its newest messages without gaps, current at Revision.
-- The coordinator id changes with the server database, so a recreated server starts with no cache
CREATE TABLE ChatCaches (
    CoordinatorInstanceId TEXT NOT NULL,
    ChatId                INTEGER NOT NULL,
    Revision              INTEGER NOT NULL,
    -- The tail goes down to the first message of the chat
    ReachesStart          INTEGER NOT NULL,
    PRIMARY KEY (CoordinatorInstanceId, ChatId)
) STRICT;

CREATE TABLE ChatCacheMessages (
    CoordinatorInstanceId TEXT NOT NULL,
    ChatId                INTEGER NOT NULL,
    MessageId             INTEGER NOT NULL,
    -- The revision of the chat at which the message was last changed
    Revision              INTEGER NOT NULL,
    -- The message this one answers: its quote lives in Payload and follows the changes of that message
    ReplyToMessageId      INTEGER NULL,
    -- The message as JSON
    Payload               TEXT NOT NULL,
    PRIMARY KEY (CoordinatorInstanceId, ChatId, MessageId),
    FOREIGN KEY (CoordinatorInstanceId, ChatId)
        REFERENCES ChatCaches (CoordinatorInstanceId, ChatId) ON DELETE CASCADE
) STRICT, WITHOUT ROWID;

CREATE INDEX IX_ChatCacheMessages_ReplyToMessageId
    ON ChatCacheMessages (CoordinatorInstanceId, ChatId, ReplyToMessageId)
    WHERE ReplyToMessageId IS NOT NULL;

-- The chats whose notifications the user muted; a chat that is not listed notifies
CREATE TABLE MutedChats (
    CoordinatorInstanceId TEXT NOT NULL,
    ChatId                INTEGER NOT NULL,
    PRIMARY KEY (CoordinatorInstanceId, ChatId)
) STRICT, WITHOUT ROWID;

-- The keys coordinators encrypt their push notifications with; a push names its key by KeyId
CREATE TABLE PushKeys (
    CoordinatorInstanceId TEXT NOT NULL PRIMARY KEY,
    -- Random bytes in lowercase hex
    KeyId                 TEXT NOT NULL UNIQUE,
    -- An AES-256 key in base64
    Key                   TEXT NOT NULL
) STRICT, WITHOUT ROWID;
