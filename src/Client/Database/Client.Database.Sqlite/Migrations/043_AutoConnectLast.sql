-- The startup connection follows the last successful one unless the user picked a server and an account: a NULL
-- AutoConnectCoordinatorId (or AutoConnectUserId) stands for "the last one".
ALTER TABLE BehaviorSettings ADD COLUMN LastCoordinatorId TEXT NULL;
ALTER TABLE BehaviorSettings ADD COLUMN LastUserId INTEGER NULL;

-- The account that last connected to the server, for an explicit server with the last account
ALTER TABLE SavedCoordinators ADD COLUMN LastUserId INTEGER NULL;

-- Why a saved account lost its device here, a LoginLostReason: shown when it has to log in again
ALTER TABLE SavedIdentities ADD COLUMN LoginLostReason INTEGER NULL;

-- The server saved most recently stands for the last connection made before this version
UPDATE BehaviorSettings SET LastCoordinatorId =
    (SELECT CoordinatorId FROM SavedCoordinators ORDER BY SaveOrder DESC LIMIT 1);

-- Automatic connection is on for new installations; those that already saved a server keep their choice
UPDATE BehaviorSettings SET IsAutoConnectEnabled = 1 WHERE NOT EXISTS (SELECT 1 FROM SavedCoordinators);
