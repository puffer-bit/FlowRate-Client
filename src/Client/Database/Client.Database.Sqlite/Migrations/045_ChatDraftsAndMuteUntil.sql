-- A chat may be muted for a while: the Unix time in milliseconds its notifications come back at, NULL while muted
-- until the user turns them on again
ALTER TABLE MutedChats ADD COLUMN MutedUntil INTEGER NULL;

-- The text typed in a chat and not sent, put back into its input when the chat is opened again
CREATE TABLE ChatDrafts (
    CoordinatorInstanceId TEXT NOT NULL,
    ChatId                INTEGER NOT NULL,
    Text                  TEXT NOT NULL,
    PRIMARY KEY (CoordinatorInstanceId, ChatId)
) STRICT, WITHOUT ROWID;
