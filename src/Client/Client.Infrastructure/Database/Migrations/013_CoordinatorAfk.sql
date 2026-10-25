-- Whether the user is away from the keyboard in hybrid sessions on the coordinator, with the reason they gave
ALTER TABLE CoordinatorStates ADD COLUMN IsAfk INTEGER NOT NULL DEFAULT 0;
ALTER TABLE CoordinatorStates ADD COLUMN AfkReason TEXT NULL;
