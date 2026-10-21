-- The Hotkeys tab of the settings: whether the keys are heard at all and outside of the window
CREATE TABLE HotKeysSettings (
    Id              INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    IsEnabled       INTEGER NOT NULL DEFAULT 1,
    IsGlobalEnabled INTEGER NOT NULL DEFAULT 1
) STRICT;

INSERT INTO HotKeysSettings (Id) VALUES (1);

-- The keys of each action; an action without a row has nothing assigned
CREATE TABLE HotKeys (
    Action   INTEGER NOT NULL PRIMARY KEY,
    Gesture  TEXT NULL,
    IsGlobal INTEGER NOT NULL DEFAULT 1
) STRICT;
