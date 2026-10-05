-- Adds the water + normal-belt square types to the board editor palette (template BoardID 0)
-- on an already-loaded `rally` database. A fresh install gets them from install/MRRDatabase.sql.
--   56 WaterNormalBelt     = element 10 + water
--   57 WaterNormalTurnCW   = element 11 + water
--   58 WaterNormalTurnCCW  = element 12 + water
-- (55 Water is already on the template at 5,5.) Each square carries the belt's Move/Rotate
-- actions plus SquareAction 55 (Water), matching the existing water square.
-- Safe to run more than once: the three template squares are deleted and re-inserted.

START TRANSACTION;

DELETE FROM BoardItemActions WHERE BoardID = 0 AND X = 5 AND Y IN (0, 1, 2);
DELETE FROM BoardItems       WHERE BoardID = 0 AND X = 5 AND Y IN (0, 1, 2);

INSERT INTO BoardItems (BoardID, X, Y, SquareType, Rotation) VALUES
(0,5,0,56,1),
(0,5,1,57,1),
(0,5,2,58,1);

INSERT INTO BoardItemActions (BoardID, X, Y, SquareAction, ActionSequence, Phase, Parameter) VALUES
(0,5,0,12,3,31,1),
(0,5,0,55,0,32,0),
(0,5,1,12,3,31,1),
(0,5,1,13,4,31,1),
(0,5,1,55,0,32,0),
(0,5,2,12,3,31,1),
(0,5,2,13,4,31,2),
(0,5,2,55,0,32,0);

COMMIT;
