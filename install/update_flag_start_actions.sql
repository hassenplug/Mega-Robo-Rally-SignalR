-- Brings an already-loaded `rally` database in line with install/MRRDatabase.sql (2026-10-01):
--   1. Flag squares (SquareType 100) keep only SquareAction 16 (Flag);
--      start squares (SquareType 110) keep only SquareAction 19 (Player Start).
--   2. RobotBases 1-7 get the fixed robot addresses (see documents/RobotConnections.md).
-- Safe to run more than once. Back up first:  mysqldump rally BoardItemActions RobotBases > backup.sql
-- Expected: the first DELETE removes 81 rows on a database loaded from the previous seed.

START TRANSACTION;

DELETE a FROM BoardItemActions a
JOIN BoardItems b ON b.BoardID = a.BoardID AND b.X = a.X AND b.Y = a.Y
WHERE (b.SquareType = 100 AND a.SquareAction <> 16)
   OR (b.SquareType = 110 AND a.SquareAction <> 19);

UPDATE RobotBases SET IPAddress = '192.168.0.101' WHERE RobotBaseID = 1;
UPDATE RobotBases SET IPAddress = '192.168.0.102' WHERE RobotBaseID = 2;
UPDATE RobotBases SET IPAddress = '192.168.0.103' WHERE RobotBaseID = 3;
UPDATE RobotBases SET IPAddress = '192.168.0.104' WHERE RobotBaseID = 4;
UPDATE RobotBases SET IPAddress = '192.168.0.105' WHERE RobotBaseID = 5;
UPDATE RobotBases SET IPAddress = '192.168.0.106', AIMID = 'AIM-427D7018' WHERE RobotBaseID = 6;
UPDATE RobotBases SET IPAddress = '192.168.0.107' WHERE RobotBaseID = 7;

COMMIT;
