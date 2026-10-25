-- Whether the voices played on the speakers are taken out of the microphone
ALTER TABLE AudioSettings ADD COLUMN IsEchoCancellationEnabled INTEGER NOT NULL DEFAULT 1;
