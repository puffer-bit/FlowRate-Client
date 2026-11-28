-- Messages sent and not yet taken by the server: they are shown in their chat and go once the server can
-- take them, after a restart of the client too. CreatedAt is a Unix time in milliseconds
CREATE TABLE ChatOutbox (
    ClientMessageId       TEXT NOT NULL PRIMARY KEY,
    CoordinatorInstanceId TEXT NOT NULL,
    ChatId                INTEGER NOT NULL,
    CreatedAt             INTEGER NOT NULL,
    Text                  TEXT NULL,
    IsLinkPreviewDisabled INTEGER NOT NULL,
    ReplyToMessageId      INTEGER NULL,
    ReplySenderUserId     INTEGER NULL,
    ReplyText             TEXT NULL,
    ReplyHasAttachments   INTEGER NOT NULL
) STRICT;

CREATE INDEX IX_ChatOutbox_Coordinator ON ChatOutbox (CoordinatorInstanceId, CreatedAt);

-- The files of a waiting message in their order, read again from their paths
CREATE TABLE ChatOutboxFiles (
    ClientMessageId    TEXT NOT NULL,
    Position           INTEGER NOT NULL,
    Name               TEXT NOT NULL,
    Path               TEXT NULL,
    Size               INTEGER NOT NULL,
    IsImage            INTEGER NOT NULL,
    IsCompressed       INTEGER NOT NULL,
    IsMetadataStripped INTEGER NOT NULL,
    VideoSide          INTEGER NULL,
    PRIMARY KEY (ClientMessageId, Position)
) STRICT, WITHOUT ROWID;
