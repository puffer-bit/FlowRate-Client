CREATE TABLE IF NOT EXISTS LocalChatReads (
    CoordinatorInstanceId TEXT NOT NULL,
    UserId INTEGER NOT NULL,
    ChatId INTEGER NOT NULL,
    LastReadMessageId INTEGER NOT NULL,
    PRIMARY KEY (CoordinatorInstanceId, UserId, ChatId)
);

CREATE TABLE IF NOT EXISTS PushReadAccounts (
    CoordinatorInstanceId TEXT NOT NULL PRIMARY KEY,
    UserId INTEGER NOT NULL,
    FOREIGN KEY (CoordinatorInstanceId) REFERENCES PushKeys(CoordinatorInstanceId) ON DELETE CASCADE
);
