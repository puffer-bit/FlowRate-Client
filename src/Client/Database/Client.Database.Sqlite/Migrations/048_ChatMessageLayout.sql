-- How the messages of a chat are drawn: 0 a list as in Discord, 1 bubbles as in Telegram
ALTER TABLE AppearanceSettings ADD COLUMN ChatMessageLayout INTEGER NOT NULL DEFAULT 0;
