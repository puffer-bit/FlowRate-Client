-- Whether the user silenced the voices of hybrid sessions on the coordinator, kept as the microphone mute is
ALTER TABLE CoordinatorStates ADD COLUMN IsSoundMuted INTEGER NOT NULL DEFAULT 0;
