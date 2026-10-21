ALTER TABLE AccountingSettings DROP COLUMN IsUserIdentityEncryptEnabled;

ALTER TABLE AccountingSettings ADD COLUMN IsDatabaseEncryptionEnabled INTEGER NOT NULL DEFAULT 0;
ALTER TABLE AccountingSettings ADD COLUMN IsDatabasePasswordSaveEnabled INTEGER NOT NULL DEFAULT 0;
ALTER TABLE AccountingSettings ADD COLUMN IsDatabaseLockEnabled INTEGER NOT NULL DEFAULT 0;

