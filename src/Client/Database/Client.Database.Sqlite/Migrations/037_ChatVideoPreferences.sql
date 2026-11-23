ALTER TABLE ChatAttachmentPreferences ADD COLUMN CompressVideos INTEGER NOT NULL DEFAULT 1;
ALTER TABLE ChatAttachmentPreferences ADD COLUMN VideoSide INTEGER;
