-- What the client remembers about its stay on a coordinator
CREATE TABLE CoordinatorStates (
    CoordinatorInstanceId TEXT NOT NULL PRIMARY KEY,
    IsMicrophoneMuted     INTEGER NOT NULL DEFAULT 0,
    -- Where the user was, for a connection that is restored; all three are cleared once the
    -- coordinator is left on purpose
    ShownChatId           INTEGER,
    IsHybridSessionShown  INTEGER NOT NULL DEFAULT 0,
    HybridSessionId       INTEGER
) STRICT;
