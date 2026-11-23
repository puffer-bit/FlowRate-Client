CREATE TABLE IF NOT EXISTS UserNotes (
    CoordinatorInstanceId TEXT NOT NULL,
    UserId INTEGER NOT NULL,
    Text TEXT NOT NULL,
    PRIMARY KEY (CoordinatorInstanceId, UserId)
);
