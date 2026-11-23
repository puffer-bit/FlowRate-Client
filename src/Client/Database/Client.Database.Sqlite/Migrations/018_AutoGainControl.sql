-- Whether a quiet microphone is brought up to the level of speech; on, as it always was before
ALTER TABLE AudioSettings ADD COLUMN IsAutoGainControlEnabled INTEGER NOT NULL DEFAULT 1;
