-- Whether the microphone button blinks when we talk while nothing is sent: the microphone off, or push
-- to talk not held. Both on by default.
ALTER TABLE BehaviorSettings ADD COLUMN IsMutedMicrophoneWarningEnabled INTEGER NOT NULL DEFAULT 1;
ALTER TABLE BehaviorSettings ADD COLUMN IsPushToTalkWarningEnabled INTEGER NOT NULL DEFAULT 1;
