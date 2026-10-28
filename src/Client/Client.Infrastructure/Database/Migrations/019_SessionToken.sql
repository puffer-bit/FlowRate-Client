-- The token of the device's session on the coordinator: logs in again without the second stage until the session
-- ends; the identity stays what logs in when it does
ALTER TABLE SavedIdentities ADD COLUMN SessionToken TEXT NULL;
