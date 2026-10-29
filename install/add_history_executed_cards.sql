-- Brings an already-loaded `rally` database in line with install/MRRDatabase.sql (2026-10-28):
-- HistoryRobotTurns.ExecutedCards -- the cards each register actually ran, with Spam replaced
-- by the card drawn in its place (DataService.SaveSpamReplacementsToHistory). Older rows stay
-- NULL; GetTurnHistory falls back to MoveCards for them.

ALTER TABLE HistoryRobotTurns ADD COLUMN `ExecutedCards` varchar(30) DEFAULT NULL AFTER `MoveCards`;
