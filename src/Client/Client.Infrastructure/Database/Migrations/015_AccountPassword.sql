-- The password of the user's account on the coordinator, kept when the user asked to remember it; Password is
-- the one of the server
ALTER TABLE SavedIdentities ADD COLUMN AccountPassword TEXT NULL;
