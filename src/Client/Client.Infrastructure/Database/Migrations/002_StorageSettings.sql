-- What of the chats is kept on this device, see the Data and storage tab of the settings
CREATE TABLE StorageSettings (
    Id                       INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    IsChatCacheEnabled       INTEGER NOT NULL DEFAULT 1,
    ChatCacheMessagesPerChat INTEGER NOT NULL DEFAULT 2000,
    MediaCacheLimitMegabytes INTEGER NOT NULL DEFAULT 512
) STRICT;

INSERT INTO StorageSettings (Id) VALUES (1);
