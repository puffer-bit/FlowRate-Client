-- Whether the voice gate opens only for what RNNoise takes for a voice, and whether it raises its threshold above the noise of the room
ALTER TABLE AudioSettings ADD COLUMN IsVoiceDetectionEnabled INTEGER NOT NULL DEFAULT 0;
ALTER TABLE AudioSettings ADD COLUMN IsAdaptiveThresholdEnabled INTEGER NOT NULL DEFAULT 0;
