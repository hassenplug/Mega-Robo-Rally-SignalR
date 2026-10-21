-- Adds the merging belt square types to the board editor palette (template BoardID 0) on an
-- already-loaded `rally` database. A fresh install gets them from install/MRRDatabase.sql.
--   13 NormalMergeLeft       23 FastMergeLeft        = fed from the left (and behind)
--   14 NormalMergeRight      24 FastMergeRight       = fed from the right (and behind)
--   15 NormalMergeBothSides  25 FastMergeBothSides   = fed from left and right
--   16 NormalMergeAll        26 FastMergeAll         = fed from left, right and behind
-- Normal merges (column 6) carry the normal belt's Move (12, seq 3) plus SquareAction 26
-- (OptionalRotate, seq 4 -- same step as the turning belts' Rotate). Fast merges (column 7)
-- carry the fast belt's Moves (seq 1 and 3) plus OptionalRotate at seq 2 and 4, like the fast
-- turns. OptionalRotate turns a robot only when the conveyor carried it in from the side: from
-- the left turns left, from the right turns right.
-- Safe to run more than once: the eight template squares are deleted and re-inserted.

START TRANSACTION;

DELETE FROM BoardItemActions WHERE BoardID = 0 AND X IN (6, 7) AND Y IN (0, 1, 2, 3);
DELETE FROM BoardItems       WHERE BoardID = 0 AND X IN (6, 7) AND Y IN (0, 1, 2, 3);

INSERT INTO BoardItems (BoardID, X, Y, SquareType, Rotation) VALUES
(0,6,0,13,1),
(0,6,1,14,1),
(0,6,2,15,1),
(0,6,3,16,1),
(0,7,0,23,1),
(0,7,1,24,1),
(0,7,2,25,1),
(0,7,3,26,1);

INSERT INTO BoardItemActions (BoardID, X, Y, SquareAction, ActionSequence, Phase, Parameter) VALUES
(0,6,0,12,3,31,1),
(0,6,0,26,4,31,1),
(0,6,1,12,3,31,1),
(0,6,1,26,4,31,1),
(0,6,2,12,3,31,1),
(0,6,2,26,4,31,1),
(0,6,3,12,3,31,1),
(0,6,3,26,4,31,1),
(0,7,0,12,1,31,1),
(0,7,0,26,2,31,1),
(0,7,0,12,3,31,1),
(0,7,0,26,4,31,1),
(0,7,1,12,1,31,1),
(0,7,1,26,2,31,1),
(0,7,1,12,3,31,1),
(0,7,1,26,4,31,1),
(0,7,2,12,1,31,1),
(0,7,2,26,2,31,1),
(0,7,2,12,3,31,1),
(0,7,2,26,4,31,1),
(0,7,3,12,1,31,1),
(0,7,3,26,2,31,1),
(0,7,3,12,3,31,1),
(0,7,3,26,4,31,1);

COMMIT;
