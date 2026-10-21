-- Whether the application looks for a newer release when it starts, and which releases it takes
ALTER TABLE BehaviorSettings ADD COLUMN IsUpdateCheckOnStartupEnabled INTEGER NOT NULL DEFAULT 1;
ALTER TABLE BehaviorSettings ADD COLUMN UpdateChannel INTEGER NOT NULL DEFAULT 0;
