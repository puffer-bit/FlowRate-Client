-- Accounts log in as devices with keys of their own. The identity, session token and account password of the
-- earlier protocol stay until the account logs in once and moves over; the server password moves to the system
-- keyring (or the Secrets table where there is none) when the application starts.
ALTER TABLE SavedIdentities ADD COLUMN DeviceId TEXT NULL;
ALTER TABLE SavedIdentities ADD COLUMN DeviceKey TEXT NULL;
ALTER TABLE SavedIdentities ADD COLUMN IsRecoveryPhraseSaved INTEGER NOT NULL DEFAULT 0;

-- Secrets kept when the system has no keyring: only as safe as the encryption of this file
CREATE TABLE Secrets (
    Name  TEXT NOT NULL PRIMARY KEY,
    Value TEXT NOT NULL
) STRICT, WITHOUT ROWID;
