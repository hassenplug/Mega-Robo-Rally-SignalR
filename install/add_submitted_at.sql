-- Brings an already-loaded `rally` database in line with install/MRRDatabase.sql (2026-11-27):
-- the time each player submitted their program, for the game results screen
-- (documents/GAME_RESULTS_DESIGN.md §4.2).
--   Robots.SubmittedAt -- set by DataService.RebuildRobotCardsSummary when Status becomes 4,
--                         copied into HistoryRobotTurns.SavedAt by DataService.SaveToHistory
-- Run this BEFORE deploying the matching code: every card a player picks writes
-- Robots.SubmittedAt, so the new code fails on a database without the column.

ALTER TABLE Robots ADD COLUMN `SubmittedAt` datetime(3) DEFAULT NULL AFTER `Hostname`;
