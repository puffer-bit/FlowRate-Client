-- Several coordinators can have used the same endpoint. Offer the one saved most recently.
ALTER TABLE SavedCoordinators ADD COLUMN SaveOrder INTEGER NOT NULL DEFAULT 0;

UPDATE SavedCoordinators SET SaveOrder = rowid;
