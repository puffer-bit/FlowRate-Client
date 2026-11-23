-- Its own tab, Media statistics, in the Logging and debug one. A MediaPerfOverlayMode, 0 is off; the
-- frame counter became it, shown over every screen
ALTER TABLE DebugSettings ADD COLUMN MediaPerfOverlay INTEGER NOT NULL DEFAULT 0;
ALTER TABLE DebugSettings ADD COLUMN IsMediaPerfExpanded INTEGER NOT NULL DEFAULT 0;
-- MediaPerfSections, all of them
ALTER TABLE DebugSettings ADD COLUMN MediaPerfSections INTEGER NOT NULL DEFAULT 31;
ALTER TABLE DebugSettings ADD COLUMN MediaPerfIntervalMs INTEGER NOT NULL DEFAULT 1000;
ALTER TABLE DebugSettings ADD COLUMN ShowMediaPerfGraphs INTEGER NOT NULL DEFAULT 1;
-- A MediaPerfJournalMode, 0 is off
ALTER TABLE DebugSettings ADD COLUMN MediaPerfJournal INTEGER NOT NULL DEFAULT 0;
ALTER TABLE DebugSettings ADD COLUMN MediaPerfSummarySeconds INTEGER NOT NULL DEFAULT 10;
ALTER TABLE DebugSettings ADD COLUMN LogMediaPerfAnomalies INTEGER NOT NULL DEFAULT 1;

UPDATE DebugSettings SET MediaPerfOverlay = CASE WHEN ShowFrameStats <> 0 THEN 1 ELSE 0 END;

ALTER TABLE DebugSettings DROP COLUMN ShowFrameStats;
