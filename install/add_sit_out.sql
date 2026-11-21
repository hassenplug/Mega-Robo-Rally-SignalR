-- Adds ShutDown value 5, "Sit Out" (tShutDown.SitOut), to an already-loaded `rally`
-- database. Robots.ShutDown has a foreign key to RobotShutDown, so this row must exist before
-- the GM screen's Sit Out toggle can set it. A fresh install gets it from install/MRRDatabase.sql.
-- Safe to run more than once.

INSERT INTO `RobotShutDown` (ShutDownID, Description, NextState, RobotActiveState)
VALUES (5, 'Sit Out', 5, 9)
ON DUPLICATE KEY UPDATE Description = VALUES(Description), NextState = VALUES(NextState),
                        RobotActiveState = VALUES(RobotActiveState);
