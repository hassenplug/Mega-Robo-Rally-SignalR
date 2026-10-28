-- Brings an already-loaded `rally` database in line with install/MRRDatabase.sql (2026-10-27):
--   1. RobotBases.AIMID is renamed to Hostname (widened to varchar(25)).
--   2. Robots gets a denormalized Hostname column -- the name RobotConnection dials.
--      StartGame()/SetRobotBase copy it from RobotBases.
--   3. IPAddress is dropped from both tables; robots are reached by hostname only.
-- Run once (ALTER TABLE is not transactional in MySQL). Back up first:
--   mysqldump rally RobotBases Robots > backup.sql

ALTER TABLE RobotBases CHANGE COLUMN `AIMID` `Hostname` varchar(25) DEFAULT NULL;
ALTER TABLE Robots ADD COLUMN `Hostname` varchar(25) DEFAULT NULL AFTER `IPAddress`;

-- Fill the current game's rows so robots can connect without restarting the game.
UPDATE Robots r JOIN RobotBases b ON b.RobotBaseID = r.RobotBaseID SET r.Hostname = b.Hostname;

ALTER TABLE RobotBases DROP COLUMN `IPAddress`;
ALTER TABLE Robots DROP COLUMN `IPAddress`;
