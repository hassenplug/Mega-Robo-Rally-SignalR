-- MRRDatabase.sql -- Mega Robo Rally complete database creation script
-- Generated from SRRDatabase20260429.sql
-- Database: rally | Server: mrobopi3 | User: mrr/rallypass
--
-- Run as root or privileged user to create DB and grant access.
-- Safe to run on a clean install or to reset an existing rally DB.

SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT;
SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS;
SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION;
SET NAMES utf8mb4;
SET @OLD_TIME_ZONE=@@TIME_ZONE;
SET TIME_ZONE='+00:00';
SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0;
SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0;
SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO';
SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0;

CREATE DATABASE IF NOT EXISTS rally DEFAULT CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci;
USE rally;

-- ===== TABLES =====

-- BluetoothDongles
DROP TABLE IF EXISTS `BluetoothDongles`;
CREATE TABLE `BluetoothDongles` (
  `DongleID` int(11) NOT NULL,
  `DongleMAC` varchar(20) DEFAULT NULL,
  `Active` int(11) DEFAULT NULL,
  PRIMARY KEY (`DongleID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- GameTypes (referenced by Boards, CurrentGameData trigger)
DROP TABLE IF EXISTS `GameTypes`;
CREATE TABLE `GameTypes` (
  `GameType` int(11) NOT NULL,
  `Description` varchar(45) DEFAULT NULL,
  `LaserDamage` int(11) DEFAULT NULL,
  `PhaseCount` int(11) DEFAULT NULL,
  `RuleVersion` int(11) DEFAULT NULL,
  PRIMARY KEY (`GameType`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- GameState (referenced by CurrentGameData trigger)
DROP TABLE IF EXISTS `GameState`;
CREATE TABLE `GameState` (
  `GameStateID` int(11) NOT NULL,
  `NextGameStateID` int(11) DEFAULT NULL,
  `GameStateDescription` varchar(20) DEFAULT NULL,
  `WaitForUser` int(11) DEFAULT NULL,
  `AutoRefresh` int(11) DEFAULT NULL,
  `Continue` int(11) DEFAULT NULL,
  `BGColor` varchar(10) DEFAULT NULL,
  `ButtonText` varchar(25) DEFAULT NULL,
  PRIMARY KEY (`GameStateID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- Boards (referenced by BoardItems, BoardItemActions, CurrentGameData trigger)
DROP TABLE IF EXISTS `Boards`;
CREATE TABLE `Boards` (
  `BoardID` int(11) NOT NULL,
  `BoardName` varchar(45) DEFAULT NULL,
  `X` int(11) DEFAULT 0,
  `Y` int(11) DEFAULT 0,
  `GameType` int(11) DEFAULT NULL,
  `Players` int(11) DEFAULT NULL,
  `TotalFlags` int(11) DEFAULT NULL,
  `LaserDamage` int(11) DEFAULT NULL,
  `PhaseCount` int(11) DEFAULT 5,
  PRIMARY KEY (`BoardID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- BoardSquares
DROP TABLE IF EXISTS `BoardSquares`;
CREATE TABLE `BoardSquares` (
  `ID` int(11) NOT NULL,
  `Name` varchar(20) DEFAULT NULL,
  `Show` tinyint(1) DEFAULT 1,
  `ShowParameterID` int(11) DEFAULT NULL,
  `Filename` varchar(45) DEFAULT NULL,
  `Rotation` int(11) DEFAULT 0,
  PRIMARY KEY (`ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- BoardSegmentList
DROP TABLE IF EXISTS `BoardSegmentList`;
CREATE TABLE `BoardSegmentList` (
  `BoardID` int(11) NOT NULL,
  `X` int(11) DEFAULT NULL,
  `Y` int(11) DEFAULT NULL,
  `BoardSegmentID` int(11) DEFAULT NULL,
  `Rotation` int(11) DEFAULT NULL,
  PRIMARY KEY (`BoardID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- BoardItems (large static board tile data)
DROP TABLE IF EXISTS `BoardItems`;
CREATE TABLE `BoardItems` (
  `BoardID` int(11) NOT NULL,
  `X` int(11) NOT NULL,
  `Y` int(11) NOT NULL,
  `SquareType` int(11) DEFAULT NULL,
  `Rotation` int(11) DEFAULT NULL,
  PRIMARY KEY (`BoardID`,`X`,`Y`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- BoardItemActions (large static board action data)
DROP TABLE IF EXISTS `BoardItemActions`;
CREATE TABLE `BoardItemActions` (
  BoardID int(11) NOT NULL,
  `X` int(11) NOT NULL,
  `Y` int(11) NOT NULL,
  `SquareAction` int(11) DEFAULT NULL,
  `ActionSequence` int(11) DEFAULT NULL,
  `Phase` int(11) DEFAULT NULL,
  `Parameter` int(11) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci COMMENT='	';

-- MoveCardTypes
DROP TABLE IF EXISTS `MoveCardTypes`;
CREATE TABLE `MoveCardTypes` (
  `CardTypeID` int(11) NOT NULL,
  `Description` varchar(10) DEFAULT NULL,
  `ShortDescription` varchar(1) DEFAULT NULL,
  `Value` int(11) DEFAULT NULL,
  `FileName` varchar(10) DEFAULT NULL,
  PRIMARY KEY (`CardTypeID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- MoveCardLocations
DROP TABLE IF EXISTS `MoveCardLocations`;
CREATE TABLE `MoveCardLocations` (
  `LocationID` int(11) NOT NULL,
  `Description` varchar(45) DEFAULT NULL,
  `DealPriority` int(11) DEFAULT NULL,
  PRIMARY KEY (`LocationID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- MoveCardsCompleteList
DROP TABLE IF EXISTS `MoveCardsCompleteList`;
CREATE TABLE `MoveCardsCompleteList` (
  `SetID` int(11) NOT NULL DEFAULT 1,
  `CardID` int(11) NOT NULL,
  `CardTypeID` int(11) DEFAULT 0,
  PRIMARY KEY (`SetID`,`CardID`),
  KEY `fk_MoveCardsCompleteList_MoveCardTypes1_idx` (`CardTypeID`),
  CONSTRAINT `fk_MoveCardsCompleteList_MoveCardTypes1` FOREIGN KEY (`CardTypeID`) REFERENCES `MoveCardTypes` (`CardTypeID`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- RobotBases
DROP TABLE IF EXISTS `RobotBases`;
CREATE TABLE `RobotBases` (
  `RobotBaseID` int(11) NOT NULL,
  `Port` varchar(20) DEFAULT 'COM00',
  `BatteryStatus` int(11) DEFAULT NULL,
  `MACID` varchar(25) DEFAULT NULL,
  `DefaultBody` int(11) DEFAULT NULL,
  PRIMARY KEY (`RobotBaseID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- RobotBodies
DROP TABLE IF EXISTS `RobotBodies`;
CREATE TABLE `RobotBodies` (
  `RobotBodyID` int(11) NOT NULL,
  Name varchar(20) DEFAULT 'Name',
  Color varchar(6) DEFAULT 'FFFFFF',
  `BodyActive` int(11) DEFAULT NULL,
  ColorFG varchar(6) DEFAULT '000000',
  PRIMARY KEY (`RobotBodyID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- RobotStatus
DROP TABLE IF EXISTS `RobotStatus`;
CREATE TABLE `RobotStatus` (
  `RobotStatusID` int(11) NOT NULL,
  Description varchar(20) DEFAULT 'Unknown',
  ShortDescription varchar(20) DEFAULT 'Unknown',
  `Active` int(11) DEFAULT NULL,
  `Programming` int(11) DEFAULT NULL,
  `StatusColor` varchar(8) DEFAULT NULL,
  `LEDColor` varchar(8) DEFAULT NULL,
  PRIMARY KEY (`RobotStatusID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- RobotShutDown
DROP TABLE IF EXISTS `RobotShutDown`;
CREATE TABLE `RobotShutDown` (
  `ShutDownID` int(11) NOT NULL,
  `Description` varchar(20) DEFAULT NULL,
  `NextState` int(11) DEFAULT NULL,
  `RobotActiveState` int(11) DEFAULT NULL,
  PRIMARY KEY (`ShutDownID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- RobotDirections
DROP TABLE IF EXISTS `RobotDirections`;
CREATE TABLE `RobotDirections` (
  `DirID` int(11) NOT NULL,
  `DirDescription` varchar(10) DEFAULT NULL,
  `ShortDirDesc` varchar(5) DEFAULT NULL,
  `NextDirection` int(11) DEFAULT NULL,
  PRIMARY KEY (`DirID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- SeatOrientation
DROP TABLE IF EXISTS `SeatOrientation`;
CREATE TABLE `SeatOrientation` (
  `SeatID` int(11) NOT NULL,
  `Direction` int(11) DEFAULT NULL,
  PRIMARY KEY (`SeatID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- PhaseCounter
DROP TABLE IF EXISTS `PhaseCounter`;
CREATE TABLE `PhaseCounter` (
  `ID` int(11) NOT NULL,
  PRIMARY KEY (`ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- CommandCategories
DROP TABLE IF EXISTS `CommandCategories`;
CREATE TABLE `CommandCategories` (
  `CommandCatID` int(11) NOT NULL,
  `Description` varchar(45) DEFAULT NULL,
  `RobotCommand` int(11) DEFAULT NULL,
  `DBCommand` int(11) DEFAULT NULL,
  `PiCommand` int(11) DEFAULT NULL,
  PRIMARY KEY (`CommandCatID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- CommandStatusLookup
DROP TABLE IF EXISTS `CommandStatusLookup`;
CREATE TABLE `CommandStatusLookup` (
  `StatusID` int(11) NOT NULL,
  `StatusDescription` varchar(20) DEFAULT NULL,
  `StatusColor` varchar(10) DEFAULT NULL,
  PRIMARY KEY (`StatusID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- CommandLookup
DROP TABLE IF EXISTS `CommandLookup`;
CREATE TABLE `CommandLookup` (
  `CommandTypeID` int(11) NOT NULL,
  `CommandTypeDescription` varchar(30) DEFAULT NULL,
  `CommandEnabled` int(11) DEFAULT NULL,
  PRIMARY KEY (`CommandTypeID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- GameCommandTiming
DROP TABLE IF EXISTS `GameCommandTiming`;
CREATE TABLE `GameCommandTiming` (
  `TimingID` int(11) NOT NULL,
  `Description` varchar(45) DEFAULT NULL,
  PRIMARY KEY (`TimingID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- GameCommandList
DROP TABLE IF EXISTS `GameCommandList`;
CREATE TABLE `GameCommandList` (
  `CommandID` int(11) NOT NULL,
  `CommandTiming` int(11) DEFAULT NULL,
  `CommandTypeID` int(11) DEFAULT NULL,
  `Description` varchar(45) DEFAULT NULL,
  PRIMARY KEY (`CommandID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- GameData
DROP TABLE IF EXISTS `GameData`;
CREATE TABLE `GameData` (
  `GameDataID` int(11) NOT NULL,
  `GameType` int(11) DEFAULT 1,
  `TotalFlags` int(11) DEFAULT 5,
  `LaserDamage` int(11) DEFAULT 1,
  BoardName varchar(200) DEFAULT '-',
  Description varchar(45) DEFAULT 'Default',
  GameCode varchar(12) DEFAULT '-',
  `PhaseCount` int(11) DEFAULT 5,
  `BoardCols` int(11) DEFAULT 1,
  `BoardRows` int(11) DEFAULT 1,
  `OptionCount` int(11) DEFAULT -1,
  `BoardID` int(11) DEFAULT 0,
  `PlayerListID` int(11) DEFAULT 1,
  `RulesVersion` int(11) DEFAULT 1,
  PRIMARY KEY (`GameDataID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- CurrentGameData
DROP TABLE IF EXISTS `CurrentGameData`;
CREATE TABLE `CurrentGameData` (
  `sKey` varchar(45) NOT NULL,
  `iValue` int(11) DEFAULT NULL,
  `sValue` varchar(45) DEFAULT NULL,
  `Category` varchar(45) DEFAULT NULL,
  `iKey` int(11) NOT NULL,
  PRIMARY KEY (`sKey`),
  KEY `secondary` (`iKey`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- Options
DROP TABLE IF EXISTS `Options`;
CREATE TABLE `Options` (
  `OptionID` int(11) NOT NULL,
  `Name` varchar(45) DEFAULT NULL,
  `Text` varchar(511) DEFAULT NULL,
  `SRR_Text` varchar(511) DEFAULT NULL,
  `EditorType` int(11) DEFAULT NULL,
  `Quantity` int(11) DEFAULT NULL,
  `Damage` int(11) DEFAULT NULL,
  `ActionSequence` int(11) DEFAULT NULL,
  `CurrentOrder` int(11) DEFAULT NULL,
  `OptType` int(11) DEFAULT NULL,
  `Functional` int(11) DEFAULT NULL,
  PRIMARY KEY (`OptionID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- OperatorData
DROP TABLE IF EXISTS `OperatorData`;
CREATE TABLE `OperatorData` (
  `OperatorListID` int(11) NOT NULL,
  `RobotID` int(11) NOT NULL,
  `OperatorName` varchar(45) DEFAULT NULL,
  `Paid` int(11) DEFAULT 0,
  `RobotBodyID` int(11) DEFAULT NULL,
  `IsActive` int(11) DEFAULT 1,
  `Password` varchar(10) DEFAULT NULL,
  `PlayerSeat` int(11) DEFAULT 5,
  `StartPosition` int(11) DEFAULT NULL,
  PRIMARY KEY (`OperatorListID`,`RobotID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- RobotCommands
DROP TABLE IF EXISTS `RobotCommands`;
CREATE TABLE `RobotCommands` (
  `CommandType` int(11) NOT NULL,
  `Value` int(11) NOT NULL,
  `Description` varchar(45) DEFAULT NULL,
  PRIMARY KEY (`CommandType`,`Value`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- RobotMessages
DROP TABLE IF EXISTS `RobotMessages`;
CREATE TABLE `RobotMessages` (
  `MessageID` int(11) NOT NULL,
  `Message` varchar(45) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- Robots (live game table, populated by procGameNew)
DROP TABLE IF EXISTS `Robots`;
CREATE TABLE `Robots` (
  `RobotID` int(11) NOT NULL DEFAULT 0,
  `OperatorName` varchar(20) DEFAULT NULL,
  `RobotBaseID` int(11) DEFAULT 0,
  `RobotBodyID` int(11) DEFAULT 0,
  `CurrentFlag` int(11) DEFAULT 0,
  `Lives` int(11) DEFAULT 3,
  `Damage` int(11) DEFAULT 0,
  `ShutDown` int(11) DEFAULT 0,
  `PositionValid` int(11) DEFAULT 0,
  `Computer` int(11) DEFAULT 0,
  `Score` int(11) DEFAULT 0,
  `Status` int(11) DEFAULT 0,
  `CurrentPosRow` int(11) DEFAULT 0,
  `CurrentPosCol` int(11) DEFAULT 0,
  `CurrentPosDir` int(11) DEFAULT 0,
  `ArchivePosRow` int(11) DEFAULT 0,
  `ArchivePosCol` int(11) DEFAULT 0,
  `ArchivePosDir` int(11) DEFAULT 0,
  `IsConnected` int(11) DEFAULT 1,
  `RobotBatteries` int(11) DEFAULT 0,
  `PhoneBatteries` int(11) DEFAULT 0,
  `Priority` int(11) DEFAULT 0,
  `Password` varchar(10) DEFAULT NULL,
  `PlayerSeat` int(11) DEFAULT 0,
  `Energy` int(11) DEFAULT 3,
  `CardsDealt` varchar(30) DEFAULT NULL,
  `CardsPlayed` varchar(20) DEFAULT NULL,
  `MessageCommandID` int(11) DEFAULT NULL,
  PRIMARY KEY (`RobotID`),
  KEY `fk_Players_RobotBases_idx` (`RobotBaseID`),
  KEY `fk_Players_RobotBodies1_idx` (`RobotBodyID`),
  KEY `fk_Players_PlayerStatus1_idx` (`Status`),
  KEY `fk_Robots_RobotShutdown_idx` (`ShutDown`),
  CONSTRAINT `fk_Players_PlayerStatus1` FOREIGN KEY (`Status`) REFERENCES `RobotStatus` (`RobotStatusID`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_Players_RobotBases` FOREIGN KEY (`RobotBaseID`) REFERENCES `RobotBases` (`RobotBaseID`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_Players_RobotBodies1` FOREIGN KEY (`RobotBodyID`) REFERENCES `RobotBodies` (`RobotBodyID`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_Robot_RobotShutDown` FOREIGN KEY (`ShutDown`) REFERENCES `RobotShutDown` (`ShutDownID`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- MoveCards (live game table)
DROP TABLE IF EXISTS `MoveCards`;
CREATE TABLE `MoveCards` (
  `CardID` int(11) NOT NULL DEFAULT 0,
  `CardTypeID` int(11) DEFAULT -1,
  `Owner` int(11) NOT NULL DEFAULT -1,
  `PhasePlayed` int(11) DEFAULT 0,
  `Locked` int(11) DEFAULT 0,
  `Random` int(11) DEFAULT 0,
  `CurrentOrder` int(11) DEFAULT 0,
  `Executed` int(11) DEFAULT 0,
  `CardLocation` int(11) DEFAULT 0,
  PRIMARY KEY (`CardID`,`Owner`),
  KEY `fk_MoveCards_MoveCardTypes1_idx` (`CardTypeID`),
  CONSTRAINT `fk_MoveCards_MoveCardTypes1` FOREIGN KEY (`CardTypeID`) REFERENCES `MoveCardTypes` (`CardTypeID`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- CommandList (live game table)
DROP TABLE IF EXISTS `CommandList`;
CREATE TABLE `CommandList` (
  `CommandID` int(11) NOT NULL AUTO_INCREMENT,
  `Turn` int(11) DEFAULT NULL,
  `Phase` int(11) DEFAULT NULL,
  `CommandTypeID` int(11) DEFAULT NULL,
  `Parameter` int(11) DEFAULT NULL,
  `RobotID` int(11) DEFAULT NULL,
  `CommandSequence` int(11) DEFAULT NULL,
  `CommandSubSequence` int(11) DEFAULT NULL,
  `StatusID` int(11) DEFAULT NULL,
  `BTCommand` varchar(10) DEFAULT NULL,
  `Description` varchar(50) DEFAULT NULL,
  `PositionRow` int(11) DEFAULT NULL,
  `PositionCol` int(11) DEFAULT NULL,
  `PositionDir` int(11) DEFAULT NULL,
  `ParameterB` int(11) DEFAULT 0,
  `CommandCatID` int(11) DEFAULT NULL,
  PRIMARY KEY (`CommandID`)
) ENGINE=InnoDB AUTO_INCREMENT=1 DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- RobotOptions (live game table)
DROP TABLE IF EXISTS `RobotOptions`;
CREATE TABLE `RobotOptions` (
  `RobotID` int(11) NOT NULL,
  `OptionID` int(11) NOT NULL,
  `DestroyWhenDamaged` int(11) DEFAULT NULL,
  `Quantity` int(11) DEFAULT NULL,
  `IsActive` int(11) DEFAULT NULL,
  `PhasePlayed` int(11) DEFAULT NULL,
  `DataValue` int(11) DEFAULT NULL,
  PRIMARY KEY (`RobotID`,`OptionID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- StatusLEDs (live game table)
DROP TABLE IF EXISTS `StatusLEDs`;
CREATE TABLE `StatusLEDs` (
  `LEDID` int(11) NOT NULL,
  `R` int(11) DEFAULT 0,
  `G` int(11) DEFAULT 0,
  `B` int(11) DEFAULT 0,
  `Sort` int(11) DEFAULT 0,
  `Brightness` int(11) DEFAULT 100,
  `Color` varchar(6) DEFAULT '000000',
  PRIMARY KEY (`LEDID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- HistoryRobots (history table)
DROP TABLE IF EXISTS `HistoryRobots`;
CREATE TABLE `HistoryRobots` (
  `GameID` int(11) NOT NULL DEFAULT 0,
  `Turn` int(11) NOT NULL DEFAULT 0,
  `RobotID` int(11) NOT NULL DEFAULT 0,
  `OperatorName` varchar(20) DEFAULT NULL,
  `RobotBaseID` int(11) DEFAULT 0,
  `RobotBodyID` int(11) DEFAULT 0,
  `CurrentFlag` int(11) DEFAULT 0,
  `Lives` int(11) DEFAULT 3,
  `Damage` int(11) DEFAULT 0,
  `ShutDown` int(11) DEFAULT 0,
  `Computer` int(11) DEFAULT 0,
  `Score` int(11) DEFAULT 0,
  `Status` int(11) DEFAULT 0,
  `CurrentPosRow` int(11) DEFAULT 0,
  `CurrentPosCol` int(11) DEFAULT 0,
  `CurrentPosDir` int(11) DEFAULT 0,
  `ArchivePosRow` int(11) DEFAULT 0,
  `ArchivePosCol` int(11) DEFAULT 0,
  `ArchivePosDir` int(11) DEFAULT 0,
  `Priority` int(11) DEFAULT 0,
  PRIMARY KEY (`RobotID`,`Turn`,`GameID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- HistoryMoveCards (history table)
DROP TABLE IF EXISTS `HistoryMoveCards`;
CREATE TABLE `HistoryMoveCards` (
  `GameID` int(11) NOT NULL DEFAULT 0,
  `Turn` int(11) NOT NULL DEFAULT 0,
  `CardID` int(11) NOT NULL DEFAULT 0,
  `Owner` int(11) NOT NULL DEFAULT -1,
  `PhasePlayed` int(11) DEFAULT 0,
  `Locked` int(11) DEFAULT 0,
  PRIMARY KEY (`GameID`,`Turn`,`CardID`,`Owner`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- HistoryRobotOptions (history table)
DROP TABLE IF EXISTS `HistoryRobotOptions`;
CREATE TABLE `HistoryRobotOptions` (
  `GameID` int(11) NOT NULL,
  `Turn` int(11) NOT NULL,
  `RobotID` int(11) NOT NULL,
  `OptionID` int(11) NOT NULL,
  `DestroyWhenDamaged` int(11) DEFAULT NULL,
  `Quantity` int(11) DEFAULT NULL,
  `IsActive` int(11) DEFAULT NULL,
  `PhasePlayed` int(11) DEFAULT NULL,
  `DataValue` int(11) DEFAULT NULL,
  PRIMARY KEY (`GameID`,`Turn`,`RobotID`,`OptionID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- ===== SEED DATA =====

-- BluetoothDongles
INSERT INTO `BluetoothDongles` VALUES (0,'00:0C:78:33:50:8E',1),(1,'00:0C:78:33:DE:E6',1);

-- GameTypes
INSERT INTO `GameTypes` VALUES
(0,'Standard',1,5,0),
(1,'King of the Hill',0,5,0),
(2,'10 Turn',0,1,0),
(3,'Standard 23',1,5,1),
(4,'Capture the Flag',NULL,NULL,NULL),
(5,'Musical Chairs',NULL,NULL,NULL),
(6,'Standard V2',NULL,NULL,NULL);

-- GameState
INSERT INTO `GameState` VALUES
(0,0,'New Game',1,0,0,'00ff00','Start Game'),
(1,1,'Set Start Positions',0,1,1,'f0f0f0','[wait for positions]'),
(2,2,'Next Turn',1,0,1,'00ff00','Next Turn'),
(3,3,'Verify Positions',1,0,0,'ff8888','Verify Positions'),
(4,3,'Program Robots',1,1,0,'ffff00','[wait for programs]'),
(5,3,'Execute Turn',1,0,1,'00ff00','Execute Turn'),
(6,6,'Executing...',0,1,1,'f0f0f0','[wait for execute]'),
(7,7,'Run Phase',1,0,0,'00ff00','Run Phase:[phase]'),
(8,8,'Running...',0,1,0,'f0f0f0','[running]'),
(9,6,'Continue Running',0,0,1,'ffff00','Continue'),
(10,8,'Remove Robot',1,0,0,'ff0000','Remove [robotID]'),
(11,8,'Game Winner',1,0,0,'8888ff','Winner [robotID]'),
(12,8,'End of game',1,0,0,'8888ff','End of Game'),
(13,14,'Exit Game',1,0,0,'8888ff','Shut Down Robots'),
(14,1,'[run exit]',0,0,0,'8888ff','[run exit]'),
(15,3,'Create Programs',0,1,0,'88ff88','Generate Programs'),
(16,3,'Restore Positions',1,0,0,'ff0000','Restore'),
(17,17,'C# Failed',1,0,0,'ff0000','System Crashed'),
(18,8,'Lay Bridge',1,0,0,'ffff00','Bridge before [robotID]'),
(19,8,'Lay Mine',1,0,0,'ffff00','Mine at [robotID]'),
(20,8,'Lay Big One',1,0,0,'ffff00','Big One at [robotID]'),
(21,21,'Load XML Boards',1,0,0,'0000ff','Load XML Boards'),
(22,22,'Test Board Save',1,0,0,'ff0000','Test Load Save'),
(23,0,'Reset Board',1,0,0,'88ff88','Reset Board'),
(24,7,'Test Run PTO',0,1,1,'0000ff','Test PTO');

-- BoardSquares
INSERT INTO `BoardSquares` VALUES
(0,'Blank',1,NULL,NULL,0),
(10,'Normal Belt',1,NULL,NULL,1),
(11,'Normal Turn CW',1,NULL,NULL,1),
(12,'Normal Turn CCW',1,NULL,NULL,1),
(20,'Fast Belt',1,NULL,NULL,1),
(21,'Fast Turn CW',1,NULL,NULL,1),
(22,'Fast Turn CCW',1,NULL,NULL,1),
(31,'Gear CW (G)',1,NULL,NULL,0),
(32,'Gear CCW (R)',1,NULL,NULL,0),
(40,'Pit',1,NULL,NULL,0),
(41,'Trap Door',1,NULL,NULL,0),
(42,'Edge',0,NULL,NULL,1),
(43,'Corner Edge',0,NULL,NULL,1),
(50,'Pusher',1,NULL,NULL,1),
(55,'Water',1,NULL,NULL,0),
(60,'Cannon',1,NULL,NULL,1),
(61,'Randomizer',1,NULL,NULL,0),
(70,'Crusher',1,NULL,NULL,0),
(80,'Flamer',1,NULL,NULL,1),
(90,'Wrench',1,NULL,NULL,0),
(91,'Wrench Hammer',1,NULL,NULL,0),
(100,'Flag',1,16,NULL,1),
(105,'King',1,NULL,NULL,1),
(110,'Start Square',1,19,NULL,1),
(200,'Blank Wall',1,NULL,NULL,1);

-- Boards
INSERT INTO `Boards` VALUES
(1,'tst6x6b',5,5,0,0,2,1,5),
(2,'Simple5x5x6px2f',5,5,0,NULL,2,1,5),
(3,'6x6x6px4f',8,8,0,NULL,4,1,5),
(4,'Table-size-Board',21,14,0,NULL,1,1,5),
(5,'6x6x6x6_test',8,8,0,NULL,6,1,5),
(6,'3x3x4x2',5,5,0,NULL,2,1,5),
(7,'3x3x2x2',5,5,0,NULL,2,1,5),
(8,'../Boards/CS16-TenPlayerA.srx',14,14,0,NULL,5,1,5),
(9,'../Boards/BWPublic-21c.srx',15,13,1,NULL,3,1,5),
(10,'../Boards/NX16-FastBoard4.srx',15,13,0,NULL,5,1,5);

-- CommandCategories
INSERT INTO `CommandCategories` VALUES
(1,'Robot wReply',1,0,0),
(2,'Robot No Reply',1,0,0),
(3,'DB',0,1,0),
(4,'PI',0,0,1),
(5,'Node ',0,0,0),
(6,'User Input',0,0,0),
(7,'Connection',1,0,0);

-- CommandStatusLookup
INSERT INTO `CommandStatusLookup` VALUES
(0,'Unknown','ffaaaa'),
(1,'Waiting','ffaaff'),
(2,'Ready','00ff00'),
(3,'Script Command','aaffaa'),
(4,'In Progress','ffff00'),
(5,'Script Complete','ffffaa'),
(6,'Complete','aaaaaa'),
(7,'Connecting','ff0000');

-- CommandLookup
INSERT INTO `CommandLookup` VALUES
(0,'Board Dimension',0),
(1,'Square Location',0),
(2,'Square Template',0),
(3,'Player Location',1),
(10,'Unknown',0),
(11,'None',0),
(12,'Move',1),
(13,'Rotate',1),
(14,'Damage',1),
(15,'Archive',1),
(16,'Flag',1),
(17,'Deal Option',1),
(18,'Block Direction (wall)',0),
(19,'Player Start',0),
(20,'Dead',1),
(21,'Robot Push',0),
(22,'Set Lives',1),
(23,'Explosive Damage',0),
(24,'Deal Move Card',1),
(30,'Phase Start',0),
(31,'Phase Step',0),
(32,'Phase End',0),
(40,'Log data',1),
(41,'Game Winner',1),
(42,'Play Card',1),
(43,'Play Option Card',1),
(49,'Begin Board Effects',0),
(50,'Pushed Move',0),
(51,'Pushed Move Rotate',0),
(52,'Board Move',0),
(53,'Board Move Rotate',0),
(54,'Board Rotate',0),
(55,'Water',0),
(56,'Deleted Move',0),
(57,'Start Bot Move',1),
(58,'Stop Bot Move',1),
(60,'Fire Cannon',0),
(61,'Randomizer',0),
(62,'Grab Flag',0),
(63,'Set Player Status',1),
(64,'Damage Points',1),
(65,'Deal Option',1),
(66,'Destroy Option',1),
(67,'Set Option Count',1),
(68,'Set Damage Points',1),
(69,'Set Energy',1),
(70,'BT Connect',1),
(71,'BT Disconnect',1),
(73,'Deal Spam Card',1),
(80,'Mine',0),
(81,'Lay Bridge',0),
(82,'SetShutdownMode',1),
(83,'Touch Flag',1),
(84,'TouchKotHFlag',1),
(85,'TouchLastManFlag',1),
(89,'Enable Robots',1),
(90,'Test Python',1),
(91,'Set Current Game Data',1),
(92,'Set Button Text',1),
(95,'End Of Game',1),
(96,'Delete Robot',1),
(97,'Set Game State',1),
(98,'Shut Down Game',1);

-- CurrentGameData (25 static configuration rows)
INSERT INTO `CurrentGameData` VALUES
('BoardCols',5,NULL,'Game',18),
('BoardID',7,'3x3x2x2','Game',20),
('BoardRows',5,NULL,'Game',19),
('Command',0,'none','x',4),
('CommandParameter',0,NULL,'Status',13),
('GameDataID',2,NULL,'Config',26),
('GameState',2,'Next Turn','Status',10),
('GameType',0,'Standard','Game',1),
('IsRunning',1,NULL,'Toggle',9),
('LaserDamage',1,NULL,'Game',6),
('LastUpdateTime',0,'1/1/70','Status',14),
('MaxDamage',10,NULL,'Game',17),
('Message',0,'Status Message','Status',28),
('OptionCount',-1,NULL,'Game',22),
('Phase',0,NULL,'Status',3),
('PhaseCount',5,'Added 130 commands','Game',16),
('PlayerListID',1,NULL,'Config',25),
('Players',5,NULL,'Game',23),
('ProgramsReady',0,NULL,'Status',11),
('RobotsActive',1,NULL,'Toggle',8),
('RobotsReady',0,NULL,'Status',12),
('RulesVersion',1,NULL,'Config',27),
('SubCommand',0,NULL,'x',5),
('TotalFlags',2,NULL,'Game',7),
('Turn',1,NULL,'Status',2);

-- GameCommandTiming
INSERT INTO `GameCommandTiming` VALUES
(1,'Robot Connection (each restart)'),
(2,'Start of game'),
(3,'Each Turn'),
(4,'Each Phase'),
(5,'End of Game/Disconnect');

-- GameCommandList
INSERT INTO `GameCommandList` VALUES
(1,1,94,'Load Robot Connections'),
(2,2,94,'Start Game (Connect)'),
(3,2,36,'Setup Game'),
(4,3,37,'Setup Turn'),
(5,5,95,'End of game'),
(6,5,94,'Disconnect ');

-- GameData
INSERT INTO `GameData` VALUES
(1,0,2,1,'../Boards/TST-9x9-2p.srx','tst6x6b','1',5,5,5,-1,1,1,1),
(2,0,2,1,'3x3x2x2','3x3x2x2','2',5,5,5,-1,7,1,1),
(3,0,4,1,'../Boards/GC16-Saturday1.srx','6x6x6px4f','3',5,8,8,-1,3,1,1),
(4,0,2,1,'3x3x4x2','3x3x4x2','4',5,5,5,-1,6,1,1),
(5,0,6,1,'6x6x6x6_test','6x6x6x6_test','5',5,8,8,-1,5,1,1),
(6,0,5,1,'../Boards/GC21B.srx','Default','6',5,15,13,-1,6,1,1),
(7,0,4,1,'-','Default','7',5,1,1,-1,1,1,1),
(8,0,4,1,'-','Default','8',5,1,1,-1,1,1,1),
(9,0,4,1,'-','Default','9',5,1,1,-1,1,1,1),
(10,0,4,1,'-','Default','10',5,1,1,-1,1,1,1);

-- MoveCardLocations
INSERT INTO `MoveCardLocations` VALUES
(0,'Deck',3),
(1,'Hand',2),
(2,'Played',5),
(3,'Discard',4),
(4,'Locked',1),
(5,'Played Spam',5);

-- MoveCardTypes
INSERT INTO `MoveCardTypes` VALUES
(0,'Unknown','-',0,'Blank'),
(1,'U-Turn','U',2,'UTurn'),
(2,'Right Turn','R',1,'RTurn'),
(3,'Left Turn','L',-1,'LTurn'),
(4,'Backward 1','B',-1,'Back1'),
(5,'Forward 1','1',1,'Forward1'),
(6,'Forward 2','2',2,'Forward2'),
(7,'Forward 3','3',3,'Forward3'),
(8,'Again','A',0,'Again'),
(9,'Power Up','P',0,'PowerUp'),
(10,'Spam','S',0,'Spam'),
(11,'Haywire','H',0,'Haywire');

-- MoveCardsCompleteList (4 deck templates)
INSERT INTO `MoveCardsCompleteList` VALUES
(1,1,1),(1,2,1),(1,3,1),(1,4,1),(1,5,1),(1,6,1),
(2,1,1),(2,2,1),(2,3,1),(2,4,1),(2,5,1),(2,6,1),(2,7,1),(2,8,1),
(3,1,1),(3,8,1),(3,15,1),(3,22,1),(3,29,1),(3,36,1),(3,43,1),(3,50,1),(3,57,1),(3,64,1),(3,71,1),
(4,1,1),
(1,7,2),(1,9,2),(1,11,2),(1,13,2),(1,15,2),(1,17,2),(1,19,2),(1,21,2),(1,23,2),(1,25,2),(1,27,2),(1,29,2),(1,31,2),(1,33,2),(1,35,2),(1,37,2),(1,39,2),(1,41,2),
(2,9,2),(2,11,2),(2,13,2),(2,15,2),(2,17,2),(2,19,2),(2,21,2),(2,23,2),(2,25,2),(2,27,2),(2,29,2),(2,31,2),(2,33,2),(2,35,2),(2,37,2),(2,39,2),(2,41,2),(2,43,2),(2,45,2),(2,47,2),(2,49,2),(2,51,2),(2,53,2),(2,55,2),
(3,2,2),(3,9,2),(3,16,2),(3,23,2),(3,30,2),(3,37,2),(3,44,2),(3,51,2),(3,58,2),(3,65,2),(3,72,2),
(4,2,2),(4,3,2),(4,4,2),(4,5,2),
(1,8,3),(1,10,3),(1,12,3),(1,14,3),(1,16,3),(1,18,3),(1,20,3),(1,22,3),(1,24,3),(1,26,3),(1,28,3),(1,30,3),(1,32,3),(1,34,3),(1,36,3),(1,38,3),(1,40,3),(1,42,3),
(2,10,3),(2,12,3),(2,14,3),(2,16,3),(2,18,3),(2,20,3),(2,22,3),(2,24,3),(2,26,3),(2,28,3),(2,30,3),(2,32,3),(2,34,3),(2,36,3),(2,38,3),(2,40,3),(2,42,3),(2,44,3),(2,46,3),(2,48,3),(2,50,3),(2,52,3),(2,54,3),(2,56,3),
(3,3,3),(3,10,3),(3,17,3),(3,24,3),(3,31,3),(3,38,3),(3,45,3),(3,52,3),(3,59,3),(3,66,3),(3,73,3),
(4,6,3),(4,7,3),(4,8,3),(4,9,3),
(1,43,4),(1,44,4),(1,45,4),(1,46,4),(1,47,4),(1,48,4),
(2,57,4),(2,58,4),(2,59,4),(2,60,4),(2,61,4),(2,62,4),(2,63,4),(2,64,4),
(3,4,4),(3,11,4),(3,18,4),(3,25,4),(3,32,4),(3,39,4),(3,46,4),(3,53,4),(3,60,4),(3,67,4),(3,74,4),
(4,10,4),
(1,49,5),(1,50,5),(1,51,5),(1,52,5),(1,53,5),(1,54,5),(1,55,5),(1,56,5),(1,57,5),(1,58,5),(1,59,5),(1,60,5),(1,61,5),(1,62,5),(1,63,5),(1,64,5),(1,65,5),(1,66,5),
(2,65,5),(2,66,5),(2,67,5),(2,68,5),(2,69,5),(2,70,5),(2,71,5),(2,72,5),(2,73,5),(2,74,5),(2,75,5),(2,76,5),(2,77,5),(2,78,5),(2,79,5),(2,80,5),(2,81,5),(2,82,5),(2,83,5),(2,84,5),(2,85,5),(2,86,5),(2,87,5),(2,88,5),
(3,5,5),(3,12,5),(3,19,5),(3,26,5),(3,33,5),(3,40,5),(3,47,5),(3,54,5),(3,61,5),(3,68,5),(3,75,5),
(4,11,5),(4,12,5),(4,13,5),(4,14,5),
(1,67,6),(1,68,6),(1,69,6),(1,70,6),(1,71,6),(1,72,6),(1,73,6),(1,74,6),(1,75,6),(1,76,6),(1,77,6),(1,78,6),
(2,89,6),(2,90,6),(2,91,6),(2,92,6),(2,93,6),(2,94,6),(2,95,6),(2,96,6),(2,97,6),(2,98,6),(2,99,6),(2,100,6),(2,101,6),(2,102,6),(2,103,6),(2,104,6),
(3,6,6),(3,13,6),(3,20,6),(3,27,6),(3,34,6),(3,41,6),(3,48,6),(3,55,6),(3,62,6),(3,69,6),(3,76,6),
(4,15,6),(4,16,6),(4,17,6),
(1,79,7),(1,80,7),(1,81,7),(1,82,7),(1,83,7),(1,84,7),
(2,105,7),(2,106,7),(2,107,7),(2,108,7),(2,109,7),(2,110,7),(2,111,7),(2,112,7),
(3,7,7),(3,14,7),(3,21,7),(3,28,7),(3,35,7),(3,42,7),(3,49,7),(3,56,7),(3,63,7),(3,70,7),(3,77,7),
(4,18,7),
(4,19,8),
(4,20,9);

-- OperatorData
INSERT INTO `OperatorData` VALUES
(1,1,'P1',0,1,1,'0001',1,1),
(1,2,'P2',0,2,1,'0002',2,2),
(1,3,'P3',0,3,1,'0003',3,3),
(1,4,'P4',0,4,1,'0004',4,4),
(1,5,'P5',0,5,1,'0005',6,5),
(1,6,'P6',0,6,1,'0006',5,6),
(1,7,'P7',0,7,1,'0007',7,7),
(1,8,'P8',0,8,1,'0008',8,8),
(2,1,'P1',0,1,1,'0001',1,1),
(2,2,'P2',0,2,1,'0002',2,2),
(2,3,'P3',0,3,1,'0003',6,3),
(2,4,'P4',0,4,1,'0004',5,4),
(2,5,'P5',0,6,1,'0005',4,5),
(2,6,'P6',0,7,1,'0006',3,6);

-- PhaseCounter
INSERT INTO `PhaseCounter` VALUES (1),(2),(3),(4),(5);

-- RobotBases
INSERT INTO `RobotBases` VALUES
(1,NULL,NULL,'192.168.1.153',1),
(2,NULL,NULL,'192.168.1.163',2),
(3,NULL,NULL,'192.168.1.206',3),
(4,NULL,NULL,'192.168.1.215',4),
(5,NULL,NULL,'192.168.1.228',5),
(6,NULL,NULL,'192.168.1.107',6),
(7,NULL,NULL,'192.168.1.106',7),
(8,NULL,NULL,'00:16:53:0A:37:26',8),
(9,NULL,NULL,'00:16:53:0A:36:D5',9),
(10,NULL,NULL,'00:16:53:0A:36:67',10);

-- RobotBodies
INSERT INTO `RobotBodies` VALUES
(1,'Hammerbot','7338B0',1,'FFFFFF'),
(2,'Hulk X90','FE0000',1,'FFFFFF'),
(3,'Smashbot','FFE733',1,'000000'),
(4,'Spinbot','0000FF',1,'FFFFFF'),
(5,'Trundlebot','B76DBB',1,'FFFFFF'),
(6,'Twitch','BE9371',1,'FFFFFF'),
(7,'Twonky','EB9C1B',1,'000000'),
(8,'Zoombot','2A611E',1,'FFFFFF');

-- RobotCommands
INSERT INTO `RobotCommands` VALUES
(1,0,'Move Back'),(1,1,'Move 0'),(1,2,'Move 1'),(1,3,'Move 2'),(1,4,'Move 3'),
(2,0,'Turn Left'),(2,1,'Turn 0'),(2,2,'Turn Right'),(2,3,'U-Turn'),
(3,0,'PTO Off'),(3,1,'PTO On'),(3,2,'LED Laser'),(3,4,'Damaged'),(3,5,'Flag'),(3,6,'Active Option'),(3,7,'Game Winner'),(3,8,'Dead'),(3,9,'Set Energy'),
(4,0,'Set Shut Down');

-- RobotDirections
INSERT INTO `RobotDirections` VALUES
(0,'None','-',1),
(1,'Up','^',2),
(2,'Right','&gt;',3),
(3,'Down','V',4),
(4,'Left','&lt;',1);

-- RobotMessages
INSERT INTO `RobotMessages` VALUES
(0,NULL),
(1,'Validate Position'),
(2,'Remove Robot'),
(3,'Next Phase'),
(4,'Robot Direction');

-- RobotShutDown
INSERT INTO `RobotShutDown` VALUES
(0,'None',0,1),
(1,'Next Turn',4,1),
(2,'Currently',0,9),
(3,'Reset',2,1),
(4,'Clear & Currently',2,1);

-- RobotStatus
INSERT INTO `RobotStatus` VALUES
(0,'Unknown','Unknown',0,0,'FFFFFF','FFFFFF'),
(1,'Waiting For Cards','Wait',1,1,'FFFFFF','FFFFFF'),
(2,'Ready to Program','Program',1,1,'CCFFCC','003333'),
(3,'Programming','Program',1,1,'AAFFAA','008888'),
(4,'Ready to Run','Ready',1,1,'00FF00','00FF00'),
(5,'Move In Progress','Moving',1,0,'0000FF','0000FF'),
(6,'Moving','Moving',1,0,'0000FF','0000FF'),
(7,'Connection Failing','Connect',1,0,'FFA500','FFA500'),
(8,'Connected','Connect',1,0,'AAAAFF','000088'),
(9,'Shut Down','Shut Down',0,0,'FFFF00','FFFF00'),
(10,'Not Active','Inactive',0,0,'FF0000','FF0000'),
(11,'Dead','Dead',0,0,'FF0000','FF0000'),
(12,'Move Complete','Done',1,0,'88FF88','88FF88'),
(13,'Program Locked','Locked In',1,0,'55FF55','55FF55'),
(14,'Laser Fired','Laser',1,0,'FFFF00','FFFF00');

-- SeatOrientation
INSERT INTO `SeatOrientation` VALUES (1,1),(2,1),(3,1),(4,2),(5,2),(6,3),(7,3),(8,3);

-- Options (upgrade card definitions)
INSERT INTO `Options` VALUES
(1,'Ablative Coat ([quantity])','Your robot is now covered with a special coat that takes 3 points of damage from any direction or source. Discard after your robot takes a total of 3 points of damage.','When you die you will lose this option as it tries to take damage',1,3,-1,-1,20,0,9),
(2,'Abort Switch','Instead of revealing a program card- ignore it and draw a new one randomly from the deck. Once switch is activated- draw program cards randomly from the deck for the remainder of the turn.','',9,-2,0,-1,94,1,0),
(3,'Big Gun ([quantity])','You may fire the Big Gun instead of firing your robot''s main laser. The Big Gun causes 2 points of damage in addition to pushing your robot back 1 square.','',8,5,2,-1,10,2,1),
(4,'Big Jet','When activated- your robot flies forward 8 squares before landing and executing its program. The Big Jet allows you to fly over walls and other robots. Your robot takes 2 points of damage when it lands.','',2,1,0,-1,70,3,5),
(5,'Bio Option','When you receive this option- immediately take another option and place it on Bio Option. Each time your robot powers down- discard the other option and draw another. If you exchange either Bio Option or the other option to prevent damage- discard both options (does not apply if Bio Option variant is used).','',0,-1,0,-1,17,9,1),
(6,'Brakes','Your robot may now choose to move zero when it is executing a Move 1. Priority is that of the Move 1.','This option is Turn Programmed- not Runtime',2,-2,0,-1,74,3,8),
(7,'Bridge Layer ([quantity])','When activated- place a bridge token in the square in front of your robot. Treat robots moving over this square as if they were moving over open floor.','',2,2,0,9,21,4,7),
(8,'Buzz Bomb ([quantity])','When activated (and each turn until the buzz bomb explodes)- take five program cards and use them to program the buzz bomb. If the bomb hits a robot or a wall- the bomb explodes.','',0,3,4,-1,84,6,0),
(9,'Circuit Breaker','Any time your robot ends a turn with 3 or more points of damage- it will automatically begin the next turn powered down.','',1,-2,3,-1,58,0,9),
(10,'Conditional','After programming all five registers- you may place one of the remaining program cards on this option. This conditional program may then be substituted for any program card in your registers before cards for that phase are revealed. Discard the conditional program at the end of the turn- but keep this option.','',9,-1,0,-1,36,1,0),
(11,'Converter','When your robot is damaged- place an energy counter on this option instead of taking a damage chit. When your robot executes its next movement card- remove an energy counter and move 1 extra square. If there are more than two counters on this option at any time- it explodes for 2 points of damage.','',1,-1,2,-1,6,0,7),
(12,'Crab Legs','You may place a Move 1 in the same register as a Rotate Left or Rotate Right card- and during that phase your robot will move 1 square to the left or right- respectively- without rotating.','',7,-1,0,-1,21,3,6),
(13,'Double Barrel Laser','Your robot''s main laser has been modified to fire two shots. May be used with {Fire Control} and/or {High Power Laser.}','',1,-1,1,-1,86,7,9),
(14,'Drone Launcher ([quantity])','You may launch a drone instead of firing your robot''s main laser. Drones fly 3 squares toward the target robot each register phase- and explode for 2 points of damage in addition to pushing the target robot back 1 square. Priority of drones: 880- 870- 860.','',8,3,2,-1,69,2,0),
(15,'Dual Processor','You may place both a movement and a rotate program card in a single register.When executing the movement card- move one square less and then execute the rotate card. If the rotate card is a U-Turn- move two squares less.','',7,-1,0,-1,87,3,5),
(16,'Extra Memory ([quantity])','Your robot receives one extra program card per turn. This option does not prevent your robot from being destroyed when it has reached 10 points of damage.','',1,1,0,-1,29,0,9),
(17,'Fire Control','You have targeting control of your robot''s main laser. When scoring a point of damage- you may choose to use the damage to lock a register or destroy a particular option.','',9,-1,0,-1,85,7,0),
(18,'Flywheel','After programming all five registers- you may place one of the remaining movement cards on this option. In a subsequent turn that card may be added to the program cards dealt to you. For example- this gives an undamaged robot 10 program cards (9 normal and 1 from the flywheel).','',5,-1,0,-1,35,6,8),
(19,'Fourth Gear','Your robot may now choose to move forward 4 squares when it is executing a Move 3. Priority is that of the Move 3.','This option is Phase Programmed- not Runtime',2,-2,0,-1,20,1,8),
(20,'Frog Legs','You may now treat your robot as if it were flying when it is executing a Move 2 or Move 3. This option cannot be activated while your robot is flying.','This option is Phase Programmed- not Runtime',2,-2,0,-1,98,1,6),
(21,'Goo Dropper ([quantity])','When activated- place a goo token in your robots square. If a robot passes over or stops on the goo- the robot cannot leave that square until it attempts to move a total of four squares in any direction.','',2,3,0,9,27,4,4),
(22,'Gyroscopic Stabilizer','On any turn you choose to activate this option- your robot is not rotated by gears or conveyor belts.','',4,-2,0,-1,41,6,9),
(23,'High Power Laser','Your robot''s main laser has been modified to shoot through one wall or robot to reach a target robot. If you shoot through a robot- that robot also receives damages. May be used with {Fire Control} and/or {Double Barrel Laser}.','',1,-2,0,-1,27,7,8),
(24,'Homing Device','1) You may place a homing device token on a target robot instead of firing your robot''s main laser. 2) When activated- ignore your hand and move forward 3 squares during each register phase if doing so will bring you closer to the target robot. Otherwise- rotate right.','',8,-1,0,-1,9,9,1),
(25,'Interceptor','You may place an intercept token on a target robot instead of firing your robot''s main laser. After cards are dealt on subsequent turns- you may choose to exchange cards with the player whose robot has your intercept token. Take the intercept token back.','',8,-1,0,-1,65,2,1),
(26,'Mechanical Arm','Any time your robot ends a register phase on one of the four squares bordering a checkpoint- it may use the mechanical arm to \"tag\" the checkpoint. A wall will block the arm- but another robot on the checkpoint will not.','This option is Enabled or disabled for Each Phase- not Runtime.',2,-2,0,-1,97,1,5),
(27,'Mine Layer ([quantity])','When activated- place a mine token in your robot''s square. If a robot passes over or stops on the mine- the mine explodes.','',2,3,4,9,92,4,7),
(28,'Mini Howitzer ([quantity])','You now have the option of firing a mini howitzer instead of your main laser. The mini howitzer will cause 1 point of damage in addition to pushing the target robot 1 square away from you. After 5 shots- discard this option.','',8,5,0,-1,69,2,5),
(29,'Missile Launcher ([quantity])','You may launch a missile instead of firing your robot''s main laser. When launched- place the missile in your robot''s square. During each subsequent phase- move the missile forward 2 squares. Priority of missiles: 735- 725- 715.','',8,3,4,-1,67,2,1),
(30,'Option Damping Field','When activated- all options(except this one) within a 3-square radius of your robot are deactivated or cannot be used. Devices already released by options continue to function normally.','',2,-2,0,-1,30,6,7),
(31,'Overload Override','You may place two program cards in a single register and execute both in that register phase- or you may leave a register unprogrammed. Your robot takes a point of damage each time this option is used.','',7,-2,0,-1,47,3,5),
(32,'Portable Teleporter ([quantity])','When activated- place a portable teleporter token in your robot''s square. Treat the portable teleporter as if it were a teleporter board element.','',2,1,0,9,45,4,4),
(33,'Power Down Shield','When your robot powers down- a shield comes out on each of the robot''s four sides. Each shield protects the robot from 1 point of damage per register phase. When the robot powers up- the shields retract.','',1,-2,0,-1,85,0,8),
(34,'Presser Beam','You now have the option of firing a presser beam instead of your robot''s main laser. The presser beam will push a target robot 1 squares away from you.','',8,-1,0,-1,91,2,5),
(35,'Proximity Mine ([quantity])','When activated- place a proximity mine token in your robot''s square. If a robot passes within 1 square of the mine- the mine explodes.','',2,3,4,9,99,4,5),
(36,'Radio Control','You now have the option of using a radio control beam instead of your robot''s main laser. The radio control beam can only target a robot within 6 squares- and it replaces that robot''s entire program with a copy of your robot''s program. In cases of card priority- the target robot moves after your robot.','',8,-2,0,-1,22,2,5),
(37,'Ramming Gear','When your robot pushes another robot- that robot receives a point of damage in addition to being pushed. Even if the target robot can''t be moved- it still receives a point of damage.','',1,-2,0,-1,12,8,9),
(38,'Rear Laser','Your robot has a rear-firing laser in addition to its main laser.','The turret will also affect the direction of the rear laser',1,-2,0,-1,96,8,9),
(39,'Recompile','You may receive a new hand once per turn before your robot is programmed. Your robot takes a point of damage after you receive the new hand.','Your robot will take 1 point of damage BEFORE you receive your new hand.  You must Enable Recompile and submit your hand and you will receive a new hand.',10,-2,1,-1,44,6,8),
(40,'Re-engineering Unit','When your robot pushes another robot- you may exchange this option for an option on the other robot.','',8,-1,0,-1,31,1,0),
(41,'Reflector ([direction])','When your robot is hit by a laser- your robot takes damages and the laser is reflected back to its source. Program the direction the reflector faces by turning this card to indicate front- back- right or left.','',6,-2,0,-1,22,6,8),
(42,'Retro-Rockets ([quantity])','When activated- your robot flies back 2 squares per fuel token before executing its program.','',2,3,0,-1,16,3,5),
(43,'Reverse Gears','Your robot may now choose to back up 2 squares when it is executing a Back-Up. Priority is that of the Back-Up.','This option is Turn Programmed- not Runtime',2,-2,0,-1,16,1,8),
(44,'Robo Copter ([quantity])','Program by placing an unused movement card on this option. During each register phase- execute the movement card and then execute your program card. While robo copter is active- your robot is flying.','This option may only be used once',4,1,0,-1,29,6,7),
(45,'Scrambler','You now have the option of firing a scrambler instead of your robot''s main laser. The scrambler allows you to replace the next programmed card of a target robot with a random one from the deck. This option cannot be used on the fifth register phase.','',8,-2,0,-1,1,2,5),
(46,'Scrambler Bomb ([quantity])','When activated- place a scrambler token in your robot''s square. At the beginning of the next turn- all robots within 6 squares of the bomb execute program cards at random for the entire turn.','',2,1,0,9,15,4,4),
(47,'Self-Destruct ([quantity])','Program by placing this card in a register. The option will be destroyed at the beginning of that register phase. If destroyed or exchanged to prevent damage- this option explodes.','',3,1,16,-1,71,5,8),
(48,'Shield ([direction])','Your robot now has a shield that protects the robot from 1 point of damage per register phase. Program the direction the shield faces by turning this card to indicate front- back- right or left.','',6,-2,0,-1,12,6,8),
(49,'Superior Archive Copy','You may withdraw your next archive copy undamaged- even if you discard this option when you robot is destroyed.','',1,-2,0,-1,48,0,9),
(50,'The Big One ([quantity])','When activated- place a Big One token in your robot''s square. At the beginning of the next turn- The Big One explodes.','',3,1,64,-1,4,4,7),
(51,'Tractor Beam','You now have the option of firing a tractor beam instead of your robot\\''s main laser. The tractor beam will pull a target robot 1 square toward you. The beam may not be used if the target robot is in an adjacent square.','',8,-2,0,-1,75,2,5),
(52,'Turret ([direction])','Your robot now has a turret for its main laser and optional weapons. Program the direction the turret faces by turning this card to indicate front- back- right or left.','The turret will also affect the direction of the rear laser',6,-2,0,-1,65,6,8),
(53,'Explosive Laser ([quantity])','Your robot has an optional Explosive Laser.  When fired, target robot will receive 2 points of explosive damage.','Custom SRR Option',4,2,2,-1,98,4,8),
(55,'Point Sucker','When your robot fires it''s main laser, you can remove points from the target','Custom SRR Option',2,1,0,-7,97,0,8),
(56,'EMP ([quantity])','When activated, your robot will transmit an EMP before Phase 1, locking in your program and clearing the programs for all other robots.  Your robot will play out its program, then reset for one turn to clear the locked program.  No damage will be added or cleared by the EMP.','Custom SRR Option',4,1,0,-1,90,0,8),
(57,'Damage Eraser ([quantity])','Before Phase 1, all damage is erased from your robot','Custom SRR Option',4,1,0,-1,58,0,8),
(58,'Reboot','Your robot will immediately shut down and repair all damage','Custom SRR Option',10,-1,0,-1,20,0,8),
(59,'Additional Laser ([quantity])','Your robot''s main laser has been modified to fire an additional shot. May be used with {Fire Control} and/or {High Power Laser.}','Custom SRR Option',1,1,0,-1,28,0,8);

-- BoardItemActions seed data (board action definitions for all boards)
LOCK TABLES `BoardItemActions` WRITE;
/*!40000 ALTER TABLE `BoardItemActions` DISABLE KEYS */;
INSERT INTO `BoardItemActions` VALUES
(8,0,0,14,0,0,100),
(8,0,1,14,0,0,100),
(8,0,2,14,0,0,100),
(8,0,3,14,0,0,100),
(8,0,4,14,0,0,100),
(8,0,5,14,0,0,100),
(8,0,6,14,0,0,100),
(8,0,7,14,0,0,100),
(8,0,8,14,0,0,100),
(8,0,9,14,0,0,100),
(8,0,10,14,0,0,100),
(8,0,11,14,0,0,100),
(8,0,12,14,0,0,100),
(8,0,13,14,0,0,100),
(8,1,0,14,0,0,100),
(8,1,1,12,3,31,3),
(8,1,1,13,4,31,2),
(8,1,2,12,3,31,3),
(8,1,3,12,3,31,3),
(8,1,4,12,3,31,2),
(8,1,4,13,4,31,2),
(8,1,7,14,7,16,-10),
(8,1,7,15,8,31,1),
(8,1,7,17,9,16,2),
(8,1,9,18,0,31,1),
(8,1,10,13,4,31,1),
(8,1,11,13,4,31,2),
(8,1,12,14,7,16,-1),
(8,1,12,15,8,31,1),
(8,1,12,16,10,31,5),
(8,1,13,14,0,0,100),
(8,2,0,14,0,0,100),
(8,2,1,12,3,31,4),
(8,2,2,14,7,16,-1),
(8,2,2,15,8,31,1),
(8,2,2,16,10,31,3),
(8,2,3,12,3,31,2),
(8,2,3,13,4,31,1),
(8,2,4,12,3,31,1),
(8,2,4,13,4,31,2),
(8,2,8,18,0,31,1),
(8,2,9,13,4,31,1),
(8,2,10,13,4,31,2),
(8,2,11,14,0,0,100),
(8,2,12,13,4,31,1),
(8,2,13,14,0,0,100),
(8,3,0,14,0,0,100),
(8,3,1,12,3,31,4),
(8,3,2,12,3,31,2),
(8,3,2,13,4,31,1),
(8,3,3,12,3,31,1),
(8,3,3,13,4,31,2),
(8,3,9,13,4,31,2),
(8,3,10,14,0,0,100),
(8,3,11,13,4,31,1),
(8,3,12,13,4,31,2),
(8,3,13,14,0,0,100),
(8,4,0,14,0,0,100),
(8,4,1,12,3,31,4),
(8,4,1,13,4,31,2),
(8,4,2,12,3,31,1),
(8,4,2,13,4,31,2),
(8,4,5,14,7,16,-1),
(8,4,5,15,8,31,1),
(8,4,5,19,1,31,1),
(8,4,7,14,7,16,-1),
(8,4,7,15,8,31,1),
(8,4,7,19,1,31,6),
(8,4,8,18,0,31,1),
(8,4,10,13,4,31,1),
(8,4,11,13,4,31,2),
(8,4,12,18,0,31,2),
(8,4,13,14,0,0,100),
(8,5,0,14,0,0,100),
(8,5,4,14,7,16,-1),
(8,5,4,15,8,31,1),
(8,5,4,19,1,31,2),
(8,5,8,14,7,16,-1),
(8,5,8,15,8,31,1),
(8,5,8,19,1,31,7),
(8,5,9,18,0,31,2),
(8,5,11,12,1,31,2),
(8,5,11,13,2,31,1),
(8,5,11,12,3,31,2),
(8,5,11,13,4,31,1),
(8,5,12,12,1,31,1),
(8,5,12,12,3,31,1),
(8,5,13,14,0,0,100),
(8,6,0,14,0,0,100),
(8,6,3,14,7,16,-1),
(8,6,3,15,8,31,1),
(8,6,3,19,1,31,3),
(8,6,6,14,7,16,-1),
(8,6,6,15,8,31,1),
(8,6,6,16,10,31,1),
(8,6,9,14,7,16,-1),
(8,6,9,15,8,31,1),
(8,6,9,19,1,31,8),
(8,6,11,12,1,31,2),
(8,6,11,12,3,31,2),
(8,6,12,14,7,16,-5),
(8,6,12,15,8,31,1),
(8,6,12,17,9,16,1),
(8,6,13,14,0,0,100),
(8,7,0,14,0,0,100),
(8,7,1,18,0,31,2),
(8,7,2,18,0,31,2),
(8,7,4,14,7,16,-1),
(8,7,4,15,8,31,1),
(8,7,4,19,1,31,4),
(8,7,8,14,7,16,-1),
(8,7,8,15,8,31,1),
(8,7,8,19,1,31,9),
(8,7,11,12,1,31,2),
(8,7,11,12,3,31,2),
(8,7,13,14,0,0,100),
(8,8,0,14,0,0,100),
(8,8,5,14,7,16,-1),
(8,8,5,15,8,31,1),
(8,8,5,19,1,31,5),
(8,8,7,14,7,16,-1),
(8,8,7,15,8,31,1),
(8,8,7,19,1,31,10),
(8,8,11,12,1,31,2),
(8,8,11,12,3,31,2),
(8,8,13,14,0,0,100),
(8,9,0,14,0,0,100),
(8,9,1,12,3,31,2),
(8,9,1,13,4,31,1),
(8,9,2,12,3,31,1),
(8,9,2,13,4,31,1),
(8,9,9,12,1,31,2),
(8,9,9,13,2,31,1),
(8,9,9,12,3,31,2),
(8,9,9,13,4,31,1),
(8,9,10,12,1,31,1),
(8,9,10,12,3,31,1),
(8,9,11,12,1,31,1),
(8,9,11,13,2,31,2),
(8,9,11,12,3,31,1),
(8,9,11,13,4,31,2),
(8,9,12,55,0,32,0),
(8,9,13,14,0,0,100),
(8,10,0,14,0,0,100),
(8,10,1,12,3,31,2),
(8,10,2,12,3,31,4),
(8,10,2,13,4,31,2),
(8,10,3,12,3,31,1),
(8,10,3,13,4,31,1),
(8,10,9,12,1,31,2),
(8,10,9,12,3,31,2),
(8,10,10,55,0,32,0),
(8,10,11,55,0,32,0),
(8,10,12,55,0,32,0),
(8,10,13,14,0,0,100),
(8,11,0,14,0,0,100),
(8,11,1,12,3,31,2),
(8,11,2,14,7,16,-1),
(8,11,2,15,8,31,1),
(8,11,2,16,10,31,4),
(8,11,3,12,3,31,4),
(8,11,3,13,4,31,2),
(8,11,4,12,3,31,1),
(8,11,4,13,4,31,1),
(8,11,5,12,3,31,2),
(8,11,5,13,4,31,1),
(8,11,6,12,3,31,1),
(8,11,7,12,1,31,1),
(8,11,7,12,3,31,1),
(8,11,8,12,1,31,1),
(8,11,8,12,3,31,1),
(8,11,9,12,1,31,1),
(8,11,9,13,2,31,2),
(8,11,9,12,3,31,1),
(8,11,9,13,4,31,2),
(8,11,10,55,0,32,0),
(8,11,11,14,7,16,-1),
(8,11,11,15,8,31,1),
(8,11,11,16,10,31,2),
(8,11,12,55,0,32,0),
(8,11,13,14,0,0,100),
(8,12,0,14,0,0,100),
(8,12,1,12,3,31,3),
(8,12,1,13,4,31,1),
(8,12,2,12,3,31,3),
(8,12,3,12,3,31,3),
(8,12,4,12,3,31,4),
(8,12,4,13,4,31,1),
(8,12,5,12,3,31,3),
(8,12,5,13,4,31,1),
(8,12,6,12,3,31,2),
(8,12,6,13,4,31,2),
(8,12,7,14,7,16,-10),
(8,12,7,15,8,31,1),
(8,12,7,17,9,16,2),
(8,12,9,55,0,32,0),
(8,12,10,55,0,32,0),
(8,12,11,55,0,32,0),
(8,12,12,55,0,32,0),
(8,12,13,14,0,0,100),
(8,13,0,14,0,0,100),
(8,13,1,14,0,0,100),
(8,13,2,14,0,0,100),
(8,13,3,14,0,0,100),
(8,13,4,14,0,0,100),
(8,13,5,14,0,0,100),
(8,13,6,14,0,0,100),
(8,13,7,14,0,0,100),
(8,13,8,14,0,0,100),
(8,13,9,14,0,0,100),
(8,13,10,14,0,0,100),
(8,13,11,14,0,0,100),
(8,13,12,14,0,0,100),
(8,13,13,14,0,0,100),
(9,0,0,14,0,0,100),
(9,0,1,14,0,0,100),
(9,0,2,14,0,0,100),
(9,0,3,14,0,0,100),
(9,0,4,14,0,0,100),
(9,0,5,14,0,0,100),
(9,0,6,14,0,0,100),
(9,0,7,14,0,0,100),
(9,0,8,14,0,0,100),
(9,0,9,14,0,0,100),
(9,0,10,14,0,0,100),
(9,0,11,14,0,0,100),
(9,0,12,14,0,0,100),
(9,1,0,14,0,0,100),
(9,1,1,14,0,0,100),
(9,1,2,12,3,31,3),
(9,1,2,13,4,31,2),
(9,1,3,12,3,31,3),
(9,1,4,12,3,31,3),
(9,1,5,12,3,31,3),
(9,1,7,12,3,31,1),
(9,1,7,13,4,31,1),
(9,1,8,12,3,31,2),
(9,1,8,13,4,31,1),
(9,1,9,12,3,31,1),
(9,1,9,13,4,31,1),
(9,1,10,12,3,31,2),
(9,1,10,13,4,31,1),
(9,1,11,12,3,31,1),
(9,1,11,13,4,31,1),
(9,1,12,14,0,0,100),
(9,2,0,14,0,0,100),
(9,2,1,55,0,32,0),
(9,2,2,12,3,31,4),
(9,2,3,13,4,31,2),
(9,2,4,19,1,31,8),
(9,2,7,12,3,31,4),
(9,2,7,13,4,31,2),
(9,2,8,12,3,31,1),
(9,2,8,13,4,31,2),
(9,2,9,12,3,31,4),
(9,2,9,13,4,31,2),
(9,2,10,12,3,31,1),
(9,2,10,13,4,31,2),
(9,2,11,12,3,31,4),
(9,2,12,14,0,0,100),
(9,3,0,14,0,0,100),
(9,3,1,55,0,32,0),
(9,3,2,12,1,31,3),
(9,3,2,13,2,31,2),
(9,3,2,12,3,31,3),
(9,3,2,13,4,31,2),
(9,3,3,12,1,31,3),
(9,3,3,12,3,31,3),
(9,3,4,12,3,31,3),
(9,3,9,19,1,31,1),
(9,3,10,13,4,31,1),
(9,3,11,12,3,31,4),
(9,3,12,14,0,0,100),
(9,4,0,14,0,0,100),
(9,4,1,55,0,32,0),
(9,4,2,12,1,31,4),
(9,4,2,12,3,31,4),
(9,4,3,13,4,31,2),
(9,4,4,19,1,31,7),
(9,4,9,12,3,31,1),
(9,4,10,12,1,31,1),
(9,4,10,12,3,31,1),
(9,4,11,12,1,31,1),
(9,4,11,13,2,31,1),
(9,4,11,12,3,31,1),
(9,4,11,13,4,31,1),
(9,4,12,14,0,0,100),
(9,5,0,14,0,0,100),
(9,5,1,55,0,32,0),
(9,5,2,12,3,31,4),
(9,5,9,19,1,31,2),
(9,5,10,13,4,31,1),
(9,5,11,12,1,31,4),
(9,5,11,12,3,31,4),
(9,5,12,14,0,0,100),
(9,6,0,14,0,0,100),
(9,6,1,55,0,32,0),
(9,6,6,16,10,31,1),
(9,6,11,12,3,31,4),
(9,6,12,14,0,0,100),
(9,7,0,14,0,0,100),
(9,7,1,13,4,31,1),
(9,7,6,16,10,31,2),
(9,7,11,13,4,31,2),
(9,7,12,14,0,0,100),
(9,8,0,14,0,0,100),
(9,8,1,12,3,31,2),
(9,8,6,16,10,31,1),
(9,8,11,55,0,32,0),
(9,8,12,14,0,0,100),
(9,9,0,14,0,0,100),
(9,9,1,12,1,31,2),
(9,9,1,12,3,31,2),
(9,9,2,13,4,31,1),
(9,9,3,19,1,31,6),
(9,9,11,55,0,32,0),
(9,9,12,14,0,0,100),
(9,10,0,14,0,0,100),
(9,10,1,12,1,31,3),
(9,10,1,13,2,31,1),
(9,10,1,12,3,31,3),
(9,10,1,13,4,31,1),
(9,10,2,12,1,31,3),
(9,10,2,12,3,31,3),
(9,10,3,12,3,31,3),
(9,10,8,19,1,31,3),
(9,10,9,13,4,31,2),
(9,10,10,12,1,31,2),
(9,10,10,12,3,31,2),
(9,10,11,55,0,32,0),
(9,10,12,14,0,0,100),
(9,11,0,14,0,0,100),
(9,11,1,12,3,31,2),
(9,11,2,13,4,31,1),
(9,11,3,19,1,31,5),
(9,11,8,12,3,31,1),
(9,11,9,12,1,31,1),
(9,11,9,12,3,31,1),
(9,11,10,12,1,31,1),
(9,11,10,13,2,31,2),
(9,11,10,12,3,31,1),
(9,11,10,13,4,31,2),
(9,11,11,55,0,32,0),
(9,11,12,14,0,0,100),
(9,12,0,14,0,0,100),
(9,12,1,12,3,31,2),
(9,12,2,12,3,31,3),
(9,12,2,13,4,31,2),
(9,12,3,12,3,31,2),
(9,12,3,13,4,31,2),
(9,12,4,12,3,31,3),
(9,12,4,13,4,31,2),
(9,12,5,12,3,31,2),
(9,12,5,13,4,31,2),
(9,12,8,19,1,31,4),
(9,12,9,13,4,31,2),
(9,12,10,12,3,31,2),
(9,12,11,55,0,32,0),
(9,12,12,14,0,0,100),
(9,13,0,14,0,0,100),
(9,13,1,12,3,31,3),
(9,13,1,13,4,31,1),
(9,13,2,12,3,31,4),
(9,13,2,13,4,31,1),
(9,13,3,12,3,31,3),
(9,13,3,13,4,31,1),
(9,13,4,12,3,31,4),
(9,13,4,13,4,31,1),
(9,13,5,12,3,31,3),
(9,13,5,13,4,31,1),
(9,13,7,12,3,31,1),
(9,13,8,12,3,31,1),
(9,13,9,12,3,31,1),
(9,13,10,12,3,31,1),
(9,13,10,13,4,31,2),
(9,13,11,14,0,0,100),
(9,13,12,14,0,0,100),
(9,14,0,14,0,0,100),
(9,14,1,14,0,0,100),
(9,14,2,14,0,0,100),
(9,14,3,14,0,0,100),
(9,14,4,14,0,0,100),
(9,14,5,14,0,0,100),
(9,14,6,14,0,0,100),
(9,14,7,14,0,0,100),
(9,14,8,14,0,0,100),
(9,14,9,14,0,0,100),
(9,14,10,14,0,0,100),
(9,14,11,14,0,0,100),
(9,14,12,14,0,0,100),
(10,0,0,14,0,0,100),
(10,0,1,14,0,0,100),
(10,0,2,14,0,0,100),
(10,0,3,14,0,0,100),
(10,0,4,14,0,0,100),
(10,0,5,14,0,0,100),
(10,0,6,14,0,0,100),
(10,0,7,14,0,0,100),
(10,0,8,14,0,0,100),
(10,0,9,14,0,0,100),
(10,0,10,14,0,0,100),
(10,0,11,14,0,0,100),
(10,0,12,14,0,0,100),
(10,1,0,14,0,0,100),
(10,1,1,12,3,31,3),
(10,1,1,13,4,31,2),
(10,1,2,12,3,31,3),
(10,1,3,12,3,31,3),
(10,1,4,12,3,31,2),
(10,1,4,13,4,31,2),
(10,1,5,55,0,32,0),
(10,1,6,14,7,16,-5),
(10,1,6,15,8,31,1),
(10,1,6,17,9,16,1),
(10,1,7,55,0,32,0),
(10,1,8,12,3,31,3),
(10,1,8,13,4,31,2),
(10,1,9,12,3,31,3),
(10,1,10,12,3,31,3),
(10,1,11,12,3,31,2),
(10,1,11,13,4,31,2),
(10,1,12,14,0,0,100),
(10,2,0,14,0,0,100),
(10,2,1,12,3,31,4),
(10,2,2,14,7,16,-1),
(10,2,2,15,8,31,1),
(10,2,2,16,10,31,2),
(10,2,4,12,3,31,3),
(10,2,4,13,4,31,1),
(10,2,5,12,3,31,3),
(10,2,6,12,3,31,3),
(10,2,7,12,3,31,3),
(10,2,8,12,3,31,4),
(10,2,8,13,4,31,1),
(10,2,9,14,0,0,100),
(10,2,10,14,7,16,-1),
(10,2,10,15,8,31,1),
(10,2,10,16,10,31,4),
(10,2,11,12,1,31,2),
(10,2,11,12,3,31,2),
(10,2,12,14,0,0,100),
(10,3,0,14,0,0,100),
(10,3,1,12,3,31,4),
(10,3,10,14,0,0,100),
(10,3,11,12,1,31,2),
(10,3,11,12,3,31,2),
(10,3,12,14,0,0,100),
(10,4,0,14,0,0,100),
(10,4,1,12,3,31,4),
(10,4,1,13,4,31,2),
(10,4,2,12,3,31,1),
(10,4,2,13,4,31,1),
(10,4,6,14,7,16,-1),
(10,4,6,15,8,31,1),
(10,4,6,19,1,31,7),
(10,4,10,12,1,31,2),
(10,4,10,13,2,31,1),
(10,4,10,12,3,31,2),
(10,4,10,13,4,31,1),
(10,4,11,12,1,31,1),
(10,4,11,13,2,31,2),
(10,4,11,12,3,31,1),
(10,4,11,13,4,31,2),
(10,4,12,14,0,0,100),
(10,5,0,14,0,0,100),
(10,5,1,55,0,32,0),
(10,5,2,12,3,31,4),
(10,5,10,12,1,31,2),
(10,5,10,12,3,31,2),
(10,5,11,55,0,32,0),
(10,5,12,14,0,0,100),
(10,6,0,14,0,0,100),
(10,6,1,55,0,32,0),
(10,6,2,12,3,31,4),
(10,6,5,14,7,16,-1),
(10,6,5,15,8,31,1),
(10,6,5,19,1,31,8),
(10,6,7,14,7,16,-1),
(10,6,7,15,8,31,1),
(10,6,7,19,1,31,6),
(10,6,10,12,1,31,2),
(10,6,10,12,3,31,2),
(10,6,11,55,0,32,0),
(10,6,12,14,0,0,100),
(10,7,0,14,0,0,100),
(10,7,1,14,7,16,-10),
(10,7,1,15,8,31,1),
(10,7,1,17,9,16,2),
(10,7,2,12,3,31,4),
(10,7,3,14,7,16,-1),
(10,7,3,15,8,31,1),
(10,7,3,19,1,31,1),
(10,7,6,14,7,16,-1),
(10,7,6,15,8,31,1),
(10,7,6,16,10,31,1),
(10,7,9,14,7,16,-1),
(10,7,9,15,8,31,1),
(10,7,9,19,1,31,5),
(10,7,10,12,3,31,2),
(10,7,11,14,7,16,-10),
(10,7,11,15,8,31,1),
(10,7,11,17,9,16,2),
(10,7,12,14,0,0,100),
(10,8,0,14,0,0,100),
(10,8,1,55,0,32,0),
(10,8,2,12,1,31,4),
(10,8,2,12,3,31,4),
(10,8,5,14,7,16,-1),
(10,8,5,15,8,31,1),
(10,8,5,19,1,31,2),
(10,8,7,14,7,16,-1),
(10,8,7,15,8,31,1),
(10,8,7,19,1,31,4),
(10,8,10,12,3,31,2),
(10,8,11,55,0,32,0),
(10,8,12,14,0,0,100),
(10,9,0,14,0,0,100),
(10,9,1,55,0,32,0),
(10,9,2,12,1,31,4),
(10,9,2,12,3,31,4),
(10,9,10,12,3,31,2),
(10,9,11,55,0,32,0),
(10,9,12,14,0,0,100),
(10,10,0,14,0,0,100),
(10,10,1,12,1,31,3),
(10,10,1,13,2,31,2),
(10,10,1,12,3,31,3),
(10,10,1,13,4,31,2),
(10,10,2,12,1,31,4),
(10,10,2,13,2,31,1),
(10,10,2,12,3,31,4),
(10,10,2,13,4,31,1),
(10,10,6,14,7,16,-1),
(10,10,6,15,8,31,1),
(10,10,6,19,1,31,3),
(10,10,10,12,3,31,3),
(10,10,10,13,4,31,1),
(10,10,11,12,3,31,2),
(10,10,11,13,4,31,2),
(10,10,12,14,0,0,100),
(10,11,0,14,0,0,100),
(10,11,1,12,1,31,4),
(10,11,1,12,3,31,4),
(10,11,2,18,0,31,4),
(10,11,3,18,0,31,4),
(10,11,4,18,0,31,4),
(10,11,5,18,0,31,4),
(10,11,6,18,0,31,4),
(10,11,7,18,0,31,4),
(10,11,8,18,0,31,4),
(10,11,9,18,0,31,4),
(10,11,11,12,3,31,2),
(10,11,12,14,0,0,100),
(10,12,0,14,0,0,100),
(10,12,1,12,1,31,4),
(10,12,1,12,3,31,4),
(10,12,2,14,7,16,-1),
(10,12,2,15,8,31,1),
(10,12,2,16,10,31,5),
(10,12,3,12,3,31,2),
(10,12,3,13,4,31,1),
(10,12,4,12,3,31,1),
(10,12,4,13,4,31,1),
(10,12,5,12,3,31,2),
(10,12,5,13,4,31,1),
(10,12,6,13,4,31,1),
(10,12,7,12,3,31,1),
(10,12,7,13,4,31,1),
(10,12,8,12,3,31,2),
(10,12,8,13,4,31,1),
(10,12,9,12,3,31,1),
(10,12,9,13,4,31,1),
(10,12,10,14,7,16,-1),
(10,12,10,15,8,31,1),
(10,12,10,16,10,31,3),
(10,12,11,12,3,31,2),
(10,12,12,14,0,0,100),
(10,13,0,14,0,0,100),
(10,13,1,12,3,31,4),
(10,13,1,13,4,31,2),
(10,13,2,12,3,31,1),
(10,13,3,12,3,31,1),
(10,13,3,13,4,31,2),
(10,13,4,13,4,31,2),
(10,13,5,13,4,31,1),
(10,13,6,13,4,31,2),
(10,13,7,13,4,31,1),
(10,13,8,13,4,31,2),
(10,13,9,12,3,31,4),
(10,13,9,13,4,31,2),
(10,13,10,12,3,31,1),
(10,13,11,12,3,31,1),
(10,13,11,13,4,31,2),
(10,13,12,14,0,0,100),
(10,14,0,14,0,0,100),
(10,14,1,14,0,0,100),
(10,14,2,14,0,0,100),
(10,14,3,14,0,0,100),
(10,14,4,14,0,0,100),
(10,14,5,14,0,0,100),
(10,14,6,14,0,0,100),
(10,14,7,14,0,0,100),
(10,14,8,14,0,0,100),
(10,14,9,14,0,0,100),
(10,14,10,14,0,0,100),
(10,14,11,14,0,0,100),
(10,14,12,14,0,0,100),
(0,1,0,12,3,31,1),
(0,1,1,12,3,31,1),
(0,1,1,13,4,31,1),
(0,1,2,12,3,31,1),
(0,1,2,13,4,31,2),
(0,2,0,12,1,31,1),
(0,2,0,12,3,31,1),
(0,2,1,12,1,31,1),
(0,2,1,13,2,31,1),
(0,2,1,12,3,31,1),
(0,2,1,13,4,31,1),
(0,2,2,12,1,31,1),
(0,2,2,13,2,31,2),
(0,2,2,12,3,31,1),
(0,2,2,13,4,31,2),
(0,3,1,13,4,31,1),
(0,3,2,13,4,31,2),
(0,4,0,14,0,0,100),
(0,4,2,14,0,0,100),
(0,4,3,14,0,0,100),
(0,5,5,55,0,32,0),
(0,9,0,14,7,16,-1),
(0,9,0,15,8,31,1),
(0,9,0,17,9,16,1),
(0,9,1,14,7,16,-1),
(0,9,1,15,8,31,1),
(0,9,1,17,9,16,2),
(0,10,0,14,7,16,-1),
(0,10,0,15,8,31,1),
(0,10,0,16,10,31,1),
(0,10,0,19,1,31,10),
(0,11,0,14,7,16,-1),
(0,11,0,15,8,31,1),
(0,11,0,19,1,31,1),
(0,0,2,18,0,31,1),
(2,0,0,14,0,0,100),
(2,1,0,18,0,31,1),
(2,2,0,18,0,31,1),
(2,3,0,18,0,31,1),
(2,4,0,14,0,0,100),
(2,0,1,18,0,31,4),
(2,1,1,19,1,31,6),
(2,2,1,19,1,31,5),
(2,3,1,19,1,31,4),
(2,3,1,16,10,31,2),
(2,4,1,18,0,31,2),
(2,0,2,18,0,31,4),
(2,1,2,12,3,31,1),
(2,1,2,13,4,31,1),
(2,2,2,12,3,31,1),
(2,3,2,12,1,31,1),
(2,3,2,12,3,31,1),
(2,4,2,18,0,31,2),
(2,0,3,18,0,31,4),
(2,1,3,19,1,31,1),
(2,1,3,16,10,31,1),
(2,2,3,19,1,31,2),
(2,3,3,19,1,31,3),
(2,4,3,18,0,31,2),
(2,0,4,14,0,0,100),
(2,1,4,18,0,31,3),
(2,2,4,18,0,31,3),
(2,3,4,18,0,31,3),
(2,4,4,14,0,0,100),
(3,0,0,14,0,0,100),
(3,1,0,18,0,31,1),
(3,2,0,18,0,31,1),
(3,3,0,18,0,31,1),
(3,4,0,18,0,31,1),
(3,5,0,18,0,31,1),
(3,6,0,18,0,31,1),
(3,7,0,14,0,0,100),
(3,0,1,18,0,31,4),
(3,1,1,16,10,31,4),
(3,6,1,16,10,31,1),
(3,7,1,18,0,31,2),
(3,0,2,18,0,31,4),
(3,1,2,12,1,31,1),
(3,1,2,12,3,31,1),
(3,2,2,12,1,31,3),
(3,2,2,13,2,31,2),
(3,2,2,12,3,31,3),
(3,2,2,13,4,31,2),
(3,3,2,12,1,31,4),
(3,3,2,12,3,31,4),
(3,4,2,12,1,31,4),
(3,4,2,12,3,31,4),
(3,5,2,12,1,31,4),
(3,5,2,12,3,31,4),
(3,6,2,12,1,31,4),
(3,6,2,12,3,31,4),
(3,7,2,18,0,31,2),
(3,0,3,18,0,31,4),
(3,1,3,12,1,31,1),
(3,1,3,13,2,31,1),
(3,1,3,12,3,31,1),
(3,1,3,13,4,31,1),
(3,2,3,12,1,31,4),
(3,2,3,13,2,31,1),
(3,2,3,12,3,31,4),
(3,2,3,13,4,31,1),
(3,3,3,17,9,16,2),
(3,4,3,12,3,31,2),
(3,4,3,13,4,31,1),
(3,5,3,12,3,31,2),
(3,6,3,12,3,31,3),
(3,6,3,13,4,31,1),
(3,7,3,18,0,31,2),
(3,0,4,18,0,31,4),
(3,1,4,12,3,31,2),
(3,2,4,12,3,31,2),
(3,3,4,12,3,31,2),
(3,4,4,12,3,31,1),
(3,4,4,13,4,31,2),
(3,5,4,17,9,16,2),
(3,6,4,12,3,31,1),
(3,7,4,18,0,31,2),
(3,0,5,18,0,31,4),
(3,6,5,12,3,31,1),
(3,7,5,18,0,31,2),
(3,0,6,18,0,31,4),
(3,1,6,16,10,31,2),
(3,1,6,19,1,31,1),
(3,2,6,19,1,31,2),
(3,3,6,19,1,31,3),
(3,4,6,19,1,31,4),
(3,5,6,19,1,31,5),
(3,6,6,16,10,31,3),
(3,6,6,19,1,31,6),
(3,7,6,18,0,31,2),
(3,0,7,14,0,0,100),
(3,1,7,18,0,31,3),
(3,2,7,18,0,31,3),
(3,3,7,18,0,31,3),
(3,4,7,18,0,31,3),
(3,5,7,18,0,31,3),
(3,6,7,18,0,31,3),
(3,7,7,14,0,0,100),
(4,0,0,18,0,31,3),
(4,1,0,18,0,31,3),
(4,2,0,18,0,31,3),
(4,3,0,18,0,31,3),
(4,4,0,18,0,31,3),
(4,5,0,18,0,31,3),
(4,6,0,18,0,31,3),
(4,7,0,18,0,31,3),
(4,8,0,18,0,31,3),
(4,9,0,18,0,31,3),
(4,10,0,18,0,31,3),
(4,11,0,18,0,31,3),
(4,12,0,18,0,31,3),
(4,13,0,18,0,31,3),
(4,14,0,18,0,31,3),
(4,15,0,18,0,31,3),
(4,16,0,18,0,31,3),
(4,17,0,18,0,31,3),
(4,18,0,18,0,31,3),
(4,19,0,18,0,31,3),
(4,20,0,18,0,31,3),
(4,0,1,18,0,31,1),
(4,20,1,18,0,31,2),
(4,0,2,18,0,31,4),
(4,2,2,14,7,16,-1),
(4,2,2,15,8,31,1),
(4,2,2,19,1,31,6),
(4,10,2,14,7,16,-1),
(4,10,2,15,8,31,1),
(4,10,2,19,1,31,5),
(4,18,2,14,7,16,-1),
(4,18,2,15,8,31,1),
(4,18,2,19,1,31,4),
(4,20,2,18,0,31,2),
(4,0,3,18,0,31,4),
(4,20,3,18,0,31,2),
(4,0,4,18,0,31,4),
(4,20,4,18,0,31,2),
(4,0,5,18,0,31,4),
(4,20,5,18,0,31,2),
(4,0,6,18,0,31,4),
(4,20,6,18,0,31,2),
(4,0,7,18,0,31,4),
(4,9,7,14,7,16,-1),
(4,9,7,15,8,31,1),
(4,9,7,16,10,31,1),
(4,9,7,19,1,31,7),
(4,20,7,18,0,31,2),
(4,0,8,18,0,31,4),
(4,20,8,18,0,31,2),
(4,0,9,18,0,31,4),
(4,20,9,18,0,31,2),
(4,0,10,18,0,31,4),
(4,20,10,18,0,31,2),
(4,0,11,18,0,31,4),
(4,2,11,14,7,16,-1),
(4,2,11,15,8,31,1),
(4,2,11,19,1,31,1),
(4,10,11,14,7,16,-1),
(4,10,11,15,8,31,1),
(4,10,11,19,1,31,2),
(4,18,11,14,7,16,-1),
(4,18,11,15,8,31,1),
(4,18,11,19,1,31,3),
(4,20,11,18,0,31,2),
(4,0,12,18,0,31,4),
(4,20,12,18,0,31,2),
(4,0,13,18,0,31,1),
(4,1,13,18,0,31,1),
(4,2,13,18,0,31,1),
(4,3,13,18,0,31,1),
(4,4,13,18,0,31,1),
(4,5,13,18,0,31,1),
(4,6,13,18,0,31,1),
(4,7,13,18,0,31,1),
(4,8,13,18,0,31,1),
(4,9,13,18,0,31,1),
(4,10,13,18,0,31,1),
(4,11,13,18,0,31,1),
(4,12,13,18,0,31,1),
(4,13,13,18,0,31,1),
(4,14,13,18,0,31,1),
(4,15,13,18,0,31,1),
(4,16,13,18,0,31,1),
(4,17,13,18,0,31,1),
(4,18,13,18,0,31,1),
(4,19,13,18,0,31,1),
(4,20,13,18,0,31,1),
(5,0,0,18,0,31,3),
(5,1,0,18,0,31,3),
(5,2,0,18,0,31,3),
(5,3,0,18,0,31,3),
(5,4,0,18,0,31,3),
(5,5,0,18,0,31,3),
(5,6,0,18,0,31,3),
(5,7,0,18,0,31,3),
(5,0,1,18,0,31,4),
(5,1,1,16,10,31,4),
(5,2,1,12,3,31,1),
(5,2,1,13,4,31,2),
(5,3,1,12,3,31,1),
(5,4,1,12,1,31,1),
(5,4,1,12,3,31,1),
(5,5,1,12,1,31,1),
(5,5,1,13,2,31,2),
(5,5,1,12,3,31,1),
(5,5,1,13,4,31,2),
(5,6,1,16,10,31,3),
(5,7,1,18,0,31,2),
(5,0,2,18,0,31,4),
(5,1,2,12,3,31,1),
(5,1,2,13,4,31,2),
(5,2,2,12,3,31,1),
(5,2,2,13,4,31,1),
(5,3,2,13,4,31,2),
(5,5,2,12,1,31,1),
(5,5,2,12,3,31,1),
(5,7,2,18,0,31,2),
(5,0,3,18,0,31,4),
(5,1,3,12,3,31,1),
(5,2,3,13,4,31,1),
(5,4,3,16,10,31,1),
(5,5,3,12,1,31,1),
(5,5,3,12,3,31,1),
(5,7,3,18,0,31,2),
(5,0,4,18,0,31,4),
(5,1,4,12,3,31,1),
(5,1,4,13,4,31,2),
(5,2,4,12,3,31,1),
(5,3,4,12,3,31,1),
(5,4,4,12,3,31,1),
(5,5,4,12,3,31,1),
(5,5,4,13,4,31,2),
(5,7,4,18,0,31,2),
(5,0,5,18,0,31,4),
(5,1,5,16,10,31,2),
(5,6,5,16,10,31,5),
(5,7,5,18,0,31,2),
(5,0,6,18,0,31,4),
(5,1,6,19,1,31,1),
(5,2,6,19,1,31,2),
(5,3,6,19,1,31,3),
(5,3,6,16,10,31,6),
(5,4,6,19,1,31,4),
(5,5,6,19,1,31,5),
(5,6,6,19,1,31,6),
(5,7,6,18,0,31,2),
(5,0,7,18,0,31,1),
(5,1,7,18,0,31,1),
(5,2,7,18,0,31,1),
(5,3,7,18,0,31,1),
(5,4,7,18,0,31,1),
(5,5,7,18,0,31,1),
(5,6,7,18,0,31,1),
(5,7,7,18,0,31,1),
(1,0,0,14,0,0,100),
(1,1,0,18,0,31,1),
(1,2,0,18,0,31,1),
(1,3,0,18,0,31,1),
(1,4,0,14,0,0,100),
(1,0,1,18,0,31,1),
(1,2,1,16,10,31,1),
(1,3,1,12,3,31,1),
(1,3,1,13,4,31,2),
(1,4,1,18,0,31,1),
(1,0,2,18,0,31,1),
(1,3,2,12,3,31,1),
(1,4,2,18,0,31,1),
(1,0,3,18,0,31,1),
(1,1,3,16,10,31,2),
(1,2,3,19,1,31,1),
(1,3,3,12,1,31,1),
(1,3,3,12,3,31,1),
(1,4,3,18,0,31,1),
(1,0,4,14,0,0,100),
(1,1,4,18,0,31,1),
(1,2,4,18,0,31,1),
(1,3,4,18,0,31,1),
(1,4,4,14,0,0,100),
(6,0,0,18,0,31,3),
(6,1,0,18,0,31,3),
(6,2,0,18,0,31,3),
(6,3,0,18,0,31,3),
(6,4,0,18,0,31,3),
(6,0,1,18,0,31,4),
(6,1,1,19,1,31,4),
(6,2,1,12,1,31,1),
(6,2,1,12,3,31,1),
(6,3,1,19,1,31,3),
(6,4,1,18,0,31,2),
(6,0,2,18,0,31,4),
(6,1,2,16,10,31,2),
(6,2,2,16,10,31,1),
(6,3,2,12,3,31,1),
(6,4,2,18,0,31,2),
(6,0,3,18,0,31,4),
(6,1,3,19,1,31,1),
(6,2,3,12,3,31,1),
(6,3,3,19,1,31,2),
(6,4,3,18,0,31,2),
(6,0,4,18,0,31,1),
(6,1,4,18,0,31,1),
(6,2,4,18,0,31,1),
(6,3,4,18,0,31,1),
(6,4,4,18,0,31,1),
(7,0,0,18,0,31,3),
(7,1,0,18,0,31,3),
(7,2,0,18,0,31,3),
(7,3,0,18,0,31,3),
(7,4,0,18,0,31,3),
(7,0,1,18,0,31,4),
(7,2,1,16,10,31,1),
(7,4,1,18,0,31,2),
(7,0,2,18,0,31,4),
(7,1,2,12,3,31,1),
(7,2,2,12,1,31,1),
(7,2,2,12,3,31,1),
(7,3,2,12,3,31,1),
(7,4,2,18,0,31,2),
(7,0,3,18,0,31,4),
(7,1,3,19,1,31,1),
(7,2,3,16,10,31,2),
(7,3,3,19,1,31,2),
(7,4,3,18,0,31,2),
(7,0,4,18,0,31,1),
(7,1,4,18,0,31,1),
(7,2,4,18,0,31,1),
(7,3,4,18,0,31,1),
(7,4,4,18,0,31,1);
/*!40000 ALTER TABLE `BoardItemActions` ENABLE KEYS */;
UNLOCK TABLES;
UNLOCK TABLES;

-- BoardItems seed data (board tile definitions for all boards)
LOCK TABLES `BoardItems` WRITE;
/*!40000 ALTER TABLE `BoardItems` DISABLE KEYS */;
INSERT INTO `BoardItems` VALUES
(0,0,0,0,0),
(0,0,2,200,1),
(0,1,0,10,1),
(0,1,1,11,1),
(0,1,2,12,1),
(0,2,0,20,1),
(0,2,1,21,1),
(0,2,2,22,1),
(0,3,1,31,0),
(0,3,2,32,0),
(0,4,0,40,0),
(0,4,2,42,1),
(0,4,3,43,1),
(0,5,5,55,1),
(0,9,0,90,0),
(0,9,1,91,0),
(0,10,0,100,0),
(0,11,0,110,1),
(1,0,0,43,4),
(1,0,1,200,4),
(1,0,2,200,4),
(1,0,3,200,4),
(1,0,4,40,0),
(1,1,0,200,1),
(1,1,1,0,0),
(1,1,2,0,0),
(1,1,3,100,0),
(1,1,4,200,3),
(1,2,0,200,1),
(1,2,1,100,0),
(1,2,2,0,0),
(1,2,3,110,1),
(1,2,4,200,3),
(1,3,0,200,1),
(1,3,1,12,4),
(1,3,2,10,1),
(1,3,3,20,1),
(1,3,4,200,3),
(1,4,0,42,1),
(1,4,1,200,2),
(1,4,2,200,2),
(1,4,3,200,2),
(1,4,4,40,0),
(2,0,0,43,4),
(2,0,1,200,4),
(2,0,2,200,4),
(2,0,3,200,4),
(2,0,4,43,3),
(2,1,0,200,1),
(2,1,1,110,3),
(2,1,2,11,1),
(2,1,3,110,1),
(2,1,4,200,3),
(2,2,0,200,1),
(2,2,1,110,3),
(2,2,2,10,4),
(2,2,3,110,1),
(2,2,4,200,3),
(2,3,0,200,1),
(2,3,1,110,3),
(2,3,2,20,4),
(2,3,3,110,1),
(2,3,4,200,3),
(2,4,0,43,1),
(2,4,1,200,2),
(2,4,2,200,2),
(2,4,3,200,2),
(2,4,4,40,0),
(3,0,0,43,4),
(3,0,1,200,4),
(3,0,2,200,4),
(3,0,3,200,4),
(3,0,4,200,4),
(3,0,5,200,4),
(3,0,6,200,4),
(3,0,7,43,3),
(3,1,0,200,1),
(3,1,1,100,0),
(3,1,2,20,1),
(3,1,3,21,1),
(3,1,4,10,2),
(3,1,5,0,0),
(3,1,6,110,1),
(3,1,7,200,3),
(3,2,0,200,1),
(3,2,1,0,0),
(3,2,2,22,3),
(3,2,3,21,4),
(3,2,4,10,2),
(3,2,5,0,0),
(3,2,6,110,1),
(3,2,7,200,3),
(3,3,0,200,1),
(3,3,1,0,0),
(3,3,2,20,4),
(3,3,3,91,0),
(3,3,4,10,2),
(3,3,5,0,0),
(3,3,6,110,1),
(3,3,7,200,3),
(3,4,0,200,1),
(3,4,1,0,0),
(3,4,2,20,4),
(3,4,3,11,2),
(3,4,4,12,1),
(3,4,5,0,0),
(3,4,6,110,1),
(3,4,7,200,3),
(3,5,0,200,1),
(3,5,1,0,0),
(3,5,2,20,4),
(3,5,3,10,2),
(3,5,4,91,0),
(3,5,5,0,0),
(3,5,6,110,1),
(3,5,7,200,3),
(3,6,0,200,1),
(3,6,1,100,1),
(3,6,2,20,4),
(3,6,3,11,3),
(3,6,4,10,3),
(3,6,5,10,3),
(3,6,6,110,1),
(3,6,7,200,3),
(3,7,0,43,1),
(3,7,1,200,2),
(3,7,2,200,2),
(3,7,3,200,2),
(3,7,4,200,2),
(3,7,5,200,2),
(3,7,6,200,2),
(3,7,7,43,2),
(4,0,0,200,1),
(4,0,1,200,4),
(4,0,2,200,4),
(4,0,3,200,4),
(4,0,4,200,4),
(4,0,5,200,4),
(4,0,6,200,4),
(4,0,7,200,4),
(4,0,8,200,4),
(4,0,9,200,4),
(4,0,10,200,4),
(4,0,11,200,4),
(4,0,12,200,4),
(4,0,13,200,3),
(4,1,0,200,1),
(4,1,1,0,0),
(4,1,2,0,0),
(4,1,3,0,0),
(4,1,4,0,0),
(4,1,5,0,0),
(4,1,6,0,0),
(4,1,7,0,0),
(4,1,8,0,0),
(4,1,9,0,0),
(4,1,10,0,0),
(4,1,11,0,0),
(4,1,12,0,0),
(4,1,13,200,3),
(4,2,0,200,1),
(4,2,1,0,0),
(4,2,2,110,3),
(4,2,3,0,0),
(4,2,4,0,0),
(4,2,5,0,0),
(4,2,6,0,0),
(4,2,7,0,0),
(4,2,8,0,0),
(4,2,9,0,0),
(4,2,10,0,0),
(4,2,11,110,1),
(4,2,12,0,0),
(4,2,13,200,3),
(4,3,0,200,1),
(4,3,1,0,0),
(4,3,2,0,0),
(4,3,3,0,0),
(4,3,4,0,0),
(4,3,5,0,0),
(4,3,6,0,0),
(4,3,7,0,0),
(4,3,8,0,0),
(4,3,9,0,0),
(4,3,10,0,0),
(4,3,11,0,0),
(4,3,12,0,0),
(4,3,13,200,3),
(4,4,0,200,1),
(4,4,1,0,0),
(4,4,2,0,0),
(4,4,3,0,0),
(4,4,4,0,0),
(4,4,5,0,0),
(4,4,6,0,0),
(4,4,7,0,0),
(4,4,8,0,0),
(4,4,9,0,0),
(4,4,10,0,0),
(4,4,11,0,0),
(4,4,12,0,0),
(4,4,13,200,3),
(4,5,0,200,1),
(4,5,1,0,0),
(4,5,2,0,0),
(4,5,3,0,0),
(4,5,4,0,0),
(4,5,5,0,0),
(4,5,6,0,0),
(4,5,7,0,0),
(4,5,8,0,0),
(4,5,9,0,0),
(4,5,10,0,0),
(4,5,11,0,0),
(4,5,12,0,0),
(4,5,13,200,3),
(4,6,0,200,1),
(4,6,1,0,0),
(4,6,2,0,0),
(4,6,3,0,0),
(4,6,4,0,0),
(4,6,5,0,0),
(4,6,6,0,0),
(4,6,7,0,0),
(4,6,8,0,0),
(4,6,9,0,0),
(4,6,10,0,0),
(4,6,11,0,0),
(4,6,12,0,0),
(4,6,13,200,3),
(4,7,0,200,1),
(4,7,1,0,0),
(4,7,2,0,0),
(4,7,3,0,0),
(4,7,4,0,0),
(4,7,5,0,0),
(4,7,6,0,0),
(4,7,7,0,0),
(4,7,8,0,0),
(4,7,9,0,0),
(4,7,10,0,0),
(4,7,11,0,0),
(4,7,12,0,0),
(4,7,13,200,3),
(4,8,0,200,1),
(4,8,1,0,0),
(4,8,2,0,0),
(4,8,3,0,0),
(4,8,4,0,0),
(4,8,5,0,0),
(4,8,6,0,0),
(4,8,7,0,0),
(4,8,8,0,0),
(4,8,9,0,0),
(4,8,10,0,0),
(4,8,11,0,0),
(4,8,12,0,0),
(4,8,13,200,3),
(4,9,0,200,1),
(4,9,1,0,0),
(4,9,2,0,0),
(4,9,3,0,0),
(4,9,4,0,0),
(4,9,5,0,0),
(4,9,6,0,0),
(4,9,7,100,0),
(4,9,8,0,0),
(4,9,9,0,0),
(4,9,10,0,0),
(4,9,11,0,0),
(4,9,12,0,0),
(4,9,13,200,3),
(4,10,0,200,1),
(4,10,1,0,0),
(4,10,2,110,3),
(4,10,3,0,0),
(4,10,4,0,0),
(4,10,5,0,0),
(4,10,6,0,0),
(4,10,7,0,0),
(4,10,8,0,0),
(4,10,9,0,0),
(4,10,10,0,0),
(4,10,11,110,1),
(4,10,12,0,0),
(4,10,13,200,3),
(4,11,0,200,1),
(4,11,1,0,0),
(4,11,2,0,0),
(4,11,3,0,0),
(4,11,4,0,0),
(4,11,5,0,0),
(4,11,6,0,0),
(4,11,7,0,0),
(4,11,8,0,0),
(4,11,9,0,0),
(4,11,10,0,0),
(4,11,11,0,0),
(4,11,12,0,0),
(4,11,13,200,3),
(4,12,0,200,1),
(4,12,1,0,0),
(4,12,2,0,0),
(4,12,3,0,0),
(4,12,4,0,0),
(4,12,5,0,0),
(4,12,6,0,0),
(4,12,7,0,0),
(4,12,8,0,0),
(4,12,9,0,0),
(4,12,10,0,0),
(4,12,11,0,0),
(4,12,12,0,0),
(4,12,13,200,3),
(4,13,0,200,1),
(4,13,1,0,0),
(4,13,2,0,0),
(4,13,3,0,0),
(4,13,4,0,0),
(4,13,5,0,0),
(4,13,6,0,0),
(4,13,7,0,0),
(4,13,8,0,0),
(4,13,9,0,0),
(4,13,10,0,0),
(4,13,11,0,0),
(4,13,12,0,0),
(4,13,13,200,3),
(4,14,0,200,1),
(4,14,1,0,0),
(4,14,2,0,0),
(4,14,3,0,0),
(4,14,4,0,0),
(4,14,5,0,0),
(4,14,6,0,0),
(4,14,7,0,0),
(4,14,8,0,0),
(4,14,9,0,0),
(4,14,10,0,0),
(4,14,11,0,0),
(4,14,12,0,0),
(4,14,13,200,3),
(4,15,0,200,1),
(4,15,1,0,0),
(4,15,2,0,0),
(4,15,3,0,0),
(4,15,4,0,0),
(4,15,5,0,0),
(4,15,6,0,0),
(4,15,7,0,0),
(4,15,8,0,0),
(4,15,9,0,0),
(4,15,10,0,0),
(4,15,11,0,0),
(4,15,12,0,0),
(4,15,13,200,3),
(4,16,0,200,1),
(4,16,1,0,0),
(4,16,2,0,0),
(4,16,3,0,0),
(4,16,4,0,0),
(4,16,5,0,0),
(4,16,6,0,0),
(4,16,7,0,0),
(4,16,8,0,0),
(4,16,9,0,0),
(4,16,10,0,0),
(4,16,11,0,0),
(4,16,12,0,0),
(4,16,13,200,3),
(4,17,0,200,1),
(4,17,1,0,0),
(4,17,2,0,0),
(4,17,3,0,0),
(4,17,4,0,0),
(4,17,5,0,0),
(4,17,6,0,0),
(4,17,7,0,0),
(4,17,8,0,0),
(4,17,9,0,0),
(4,17,10,0,0),
(4,17,11,0,0),
(4,17,12,0,0),
(4,17,13,200,3),
(4,18,0,200,1),
(4,18,1,0,0),
(4,18,2,110,3),
(4,18,3,0,0),
(4,18,4,0,0),
(4,18,5,0,0),
(4,18,6,0,0),
(4,18,7,0,0),
(4,18,8,0,0),
(4,18,9,0,0),
(4,18,10,0,0),
(4,18,11,110,1),
(4,18,12,0,0),
(4,18,13,200,3),
(4,19,0,200,1),
(4,19,1,0,0),
(4,19,2,0,0),
(4,19,3,0,0),
(4,19,4,0,0),
(4,19,5,0,0),
(4,19,6,0,0),
(4,19,7,0,0),
(4,19,8,0,0),
(4,19,9,0,0),
(4,19,10,0,0),
(4,19,11,0,0),
(4,19,12,0,0),
(4,19,13,200,3),
(4,20,0,200,1),
(4,20,1,200,2),
(4,20,2,200,2),
(4,20,3,200,2),
(4,20,4,200,2),
(4,20,5,200,2),
(4,20,6,200,2),
(4,20,7,200,2),
(4,20,8,200,2),
(4,20,9,200,2),
(4,20,10,200,2),
(4,20,11,200,2),
(4,20,12,200,2),
(4,20,13,200,3),
(5,0,0,200,1),
(5,0,1,200,4),
(5,0,2,200,4),
(5,0,3,200,4),
(5,0,4,200,4),
(5,0,5,200,4),
(5,0,6,200,4),
(5,0,7,200,3),
(5,1,0,200,1),
(5,1,1,100,0),
(5,1,2,12,3),
(5,1,3,10,3),
(5,1,4,12,2),
(5,1,5,100,0),
(5,1,6,110,1),
(5,1,7,200,3),
(5,2,0,200,1),
(5,2,1,12,3),
(5,2,2,11,4),
(5,2,3,31,0),
(5,2,4,10,2),
(5,2,5,0,0),
(5,2,6,110,1),
(5,2,7,200,3),
(5,3,0,200,1),
(5,3,1,10,4),
(5,3,2,32,0),
(5,3,3,0,0),
(5,3,4,10,2),
(5,3,5,0,0),
(5,3,6,110,1),
(5,3,7,200,3),
(5,4,0,200,1),
(5,4,1,20,4),
(5,4,2,0,0),
(5,4,3,100,0),
(5,4,4,10,2),
(5,4,5,0,0),
(5,4,6,110,1),
(5,4,7,200,3),
(5,5,0,200,1),
(5,5,1,22,4),
(5,5,2,20,1),
(5,5,3,20,1),
(5,5,4,12,1),
(5,5,5,0,0),
(5,5,6,110,1),
(5,5,7,200,3),
(5,6,0,200,1),
(5,6,1,100,0),
(5,6,2,0,0),
(5,6,3,0,0),
(5,6,4,0,0),
(5,6,5,100,0),
(5,6,6,110,1),
(5,6,7,200,3),
(5,7,0,200,1),
(5,7,1,200,2),
(5,7,2,200,2),
(5,7,3,200,2),
(5,7,4,200,2),
(5,7,5,200,2),
(5,7,6,200,2),
(5,7,7,200,3),
(6,0,0,200,1),
(6,0,1,200,4),
(6,0,2,200,4),
(6,0,3,200,4),
(6,0,4,200,3),
(6,1,0,200,1),
(6,1,1,110,3),
(6,1,2,100,0),
(6,1,3,110,2),
(6,1,4,200,3),
(6,2,0,200,1),
(6,2,1,20,3),
(6,2,2,100,0),
(6,2,3,10,1),
(6,2,4,200,3),
(6,3,0,200,1),
(6,3,1,110,4),
(6,3,2,10,4),
(6,3,3,110,1),
(6,3,4,200,3),
(6,4,0,200,1),
(6,4,1,200,2),
(6,4,2,200,2),
(6,4,3,200,2),
(6,4,4,200,3),
(7,0,0,200,1),
(7,0,1,200,4),
(7,0,2,200,4),
(7,0,3,200,4),
(7,0,4,200,3),
(7,1,0,200,1),
(7,1,1,0,0),
(7,1,2,10,2),
(7,1,3,110,1),
(7,1,4,200,3),
(7,2,0,200,1),
(7,2,1,100,0),
(7,2,2,20,2),
(7,2,3,100,0),
(7,2,4,200,3),
(7,3,0,200,1),
(7,3,1,0,0),
(7,3,2,10,1),
(7,3,3,110,1),
(7,3,4,200,3),
(7,4,0,200,1),
(7,4,1,200,2),
(7,4,2,200,2),
(7,4,3,200,2),
(7,4,4,200,3),
(8,0,0,43,4),
(8,0,1,42,4),
(8,0,2,42,4),
(8,0,3,42,4),
(8,0,4,42,4),
(8,0,5,42,4),
(8,0,6,42,4),
(8,0,7,42,4),
(8,0,8,42,4),
(8,0,9,42,4),
(8,0,10,42,4),
(8,0,11,42,4),
(8,0,12,42,4),
(8,0,13,43,3),
(8,1,0,42,1),
(8,1,1,12,3),
(8,1,2,10,3),
(8,1,3,10,3),
(8,1,4,12,2),
(8,1,5,0,0),
(8,1,6,0,0),
(8,1,7,91,0),
(8,1,8,0,0),
(8,1,9,200,1),
(8,1,10,31,0),
(8,1,11,32,0),
(8,1,12,100,0),
(8,1,13,42,3),
(8,2,0,42,1),
(8,2,1,10,4),
(8,2,2,100,0),
(8,2,3,11,2),
(8,2,4,12,1),
(8,2,5,0,0),
(8,2,6,0,0),
(8,2,7,0,0),
(8,2,8,200,1),
(8,2,9,31,0),
(8,2,10,32,0),
(8,2,11,40,0),
(8,2,12,31,0),
(8,2,13,42,3),
(8,3,0,42,1),
(8,3,1,10,4),
(8,3,2,11,2),
(8,3,3,12,1),
(8,3,4,0,0),
(8,3,5,0,0),
(8,3,6,0,0),
(8,3,7,0,0),
(8,3,8,0,0),
(8,3,9,32,0),
(8,3,10,40,0),
(8,3,11,31,0),
(8,3,12,32,0),
(8,3,13,42,3),
(8,4,0,42,1),
(8,4,1,12,4),
(8,4,2,12,1),
(8,4,3,0,0),
(8,4,4,0,0),
(8,4,5,110,1),
(8,4,6,0,0),
(8,4,7,110,3),
(8,4,8,200,1),
(8,4,9,0,0),
(8,4,10,31,0),
(8,4,11,32,0),
(8,4,12,200,2),
(8,4,13,42,3),
(8,5,0,42,1),
(8,5,1,0,0),
(8,5,2,0,0),
(8,5,3,0,0),
(8,5,4,110,1),
(8,5,5,0,0),
(8,5,6,0,0),
(8,5,7,0,0),
(8,5,8,110,3),
(8,5,9,200,2),
(8,5,10,0,0),
(8,5,11,21,2),
(8,5,12,20,1),
(8,5,13,42,3),
(8,6,0,42,1),
(8,6,1,0,0),
(8,6,2,0,0),
(8,6,3,110,1),
(8,6,4,0,0),
(8,6,5,0,0),
(8,6,6,100,0),
(8,6,7,0,0),
(8,6,8,0,0),
(8,6,9,110,3),
(8,6,10,0,0),
(8,6,11,20,2),
(8,6,12,90,0),
(8,6,13,42,3),
(8,7,0,42,1),
(8,7,1,200,2),
(8,7,2,200,2),
(8,7,3,0,0),
(8,7,4,110,1),
(8,7,5,0,0),
(8,7,6,0,0),
(8,7,7,0,0),
(8,7,8,110,3),
(8,7,9,0,0),
(8,7,10,0,0),
(8,7,11,20,2),
(8,7,12,0,0),
(8,7,13,42,3),
(8,8,0,42,1),
(8,8,1,0,0),
(8,8,2,0,0),
(8,8,3,0,0),
(8,8,4,0,0),
(8,8,5,110,1),
(8,8,6,0,0),
(8,8,7,110,3),
(8,8,8,0,0),
(8,8,9,0,0),
(8,8,10,0,0),
(8,8,11,20,2),
(8,8,12,0,0),
(8,8,13,42,3),
(8,9,0,42,1),
(8,9,1,11,2),
(8,9,2,11,1),
(8,9,3,0,0),
(8,9,4,0,0),
(8,9,5,0,0),
(8,9,6,0,0),
(8,9,7,0,0),
(8,9,8,0,0),
(8,9,9,21,2),
(8,9,10,20,1),
(8,9,11,22,1),
(8,9,12,55,1),
(8,9,13,42,3),
(8,10,0,42,1),
(8,10,1,10,2),
(8,10,2,12,4),
(8,10,3,11,1),
(8,10,4,0,0),
(8,10,5,0,0),
(8,10,6,0,0),
(8,10,7,0,0),
(8,10,8,0,0),
(8,10,9,20,2),
(8,10,10,55,1),
(8,10,11,55,1),
(8,10,12,55,1),
(8,10,13,42,3),
(8,11,0,42,1),
(8,11,1,10,2),
(8,11,2,100,0),
(8,11,3,12,4),
(8,11,4,11,1),
(8,11,5,11,2),
(8,11,6,10,1),
(8,11,7,20,1),
(8,11,8,20,1),
(8,11,9,22,1),
(8,11,10,55,1),
(8,11,11,100,0),
(8,11,12,55,1),
(8,11,13,42,3),
(8,12,0,42,1),
(8,12,1,11,3),
(8,12,2,10,3),
(8,12,3,10,3),
(8,12,4,11,4),
(8,12,5,11,3),
(8,12,6,12,2),
(8,12,7,91,0),
(8,12,8,0,0),
(8,12,9,55,1),
(8,12,10,55,1),
(8,12,11,55,1),
(8,12,12,55,1),
(8,12,13,42,3),
(8,13,0,43,1),
(8,13,1,42,2),
(8,13,2,42,2),
(8,13,3,42,2),
(8,13,4,42,2),
(8,13,5,42,2),
(8,13,6,42,2),
(8,13,7,42,2),
(8,13,8,42,2),
(8,13,9,42,2),
(8,13,10,42,2),
(8,13,11,42,2),
(8,13,12,42,2),
(8,13,13,43,2),
(9,0,0,43,4),
(9,0,1,42,4),
(9,0,2,42,4),
(9,0,3,42,4),
(9,0,4,42,4),
(9,0,5,42,4),
(9,0,6,42,4),
(9,0,7,42,4),
(9,0,8,42,4),
(9,0,9,42,4),
(9,0,10,42,4),
(9,0,11,42,4),
(9,0,12,43,3),
(9,1,0,42,1),
(9,1,1,40,0),
(9,1,2,12,3),
(9,1,3,10,3),
(9,1,4,10,3),
(9,1,5,10,3),
(9,1,6,91,0),
(9,1,7,11,1),
(9,1,8,11,2),
(9,1,9,11,1),
(9,1,10,11,2),
(9,1,11,11,1),
(9,1,12,42,3),
(9,2,0,42,1),
(9,2,1,55,1),
(9,2,2,10,4),
(9,2,3,32,0),
(9,2,4,110,3),
(9,2,5,0,0),
(9,2,6,0,0),
(9,2,7,12,4),
(9,2,8,12,1),
(9,2,9,12,4),
(9,2,10,12,1),
(9,2,11,10,4),
(9,2,12,42,3),
(9,3,0,42,1),
(9,3,1,55,1),
(9,3,2,22,3),
(9,3,3,20,3),
(9,3,4,10,3),
(9,3,5,0,0),
(9,3,6,0,0),
(9,3,7,0,0),
(9,3,8,0,0),
(9,3,9,110,1),
(9,3,10,31,0),
(9,3,11,10,4),
(9,3,12,42,3),
(9,4,0,42,1),
(9,4,1,55,1),
(9,4,2,20,4),
(9,4,3,32,0),
(9,4,4,110,3),
(9,4,5,0,0),
(9,4,6,0,0),
(9,4,7,0,0),
(9,4,8,0,0),
(9,4,9,10,1),
(9,4,10,20,1),
(9,4,11,21,1),
(9,4,12,42,3),
(9,5,0,42,1),
(9,5,1,55,1),
(9,5,2,10,4),
(9,5,3,0,0),
(9,5,4,0,0),
(9,5,5,0,0),
(9,5,6,0,0),
(9,5,7,0,0),
(9,5,8,0,0),
(9,5,9,110,1),
(9,5,10,31,0),
(9,5,11,20,4),
(9,5,12,42,3),
(9,6,0,42,1),
(9,6,1,55,1),
(9,6,2,0,0),
(9,6,3,0,0),
(9,6,4,0,0),
(9,6,5,0,0),
(9,6,6,100,0),
(9,6,7,0,0),
(9,6,8,0,0),
(9,6,9,0,0),
(9,6,10,0,0),
(9,6,11,10,4),
(9,6,12,42,3),
(9,7,0,42,1),
(9,7,1,31,0),
(9,7,2,0,0),
(9,7,3,0,0),
(9,7,4,0,0),
(9,7,5,0,0),
(9,7,6,100,0),
(9,7,7,0,0),
(9,7,8,0,0),
(9,7,9,0,0),
(9,7,10,0,0),
(9,7,11,32,0),
(9,7,12,42,3),
(9,8,0,42,1),
(9,8,1,10,2),
(9,8,2,0,0),
(9,8,3,0,0),
(9,8,4,0,0),
(9,8,5,0,0),
(9,8,6,100,0),
(9,8,7,0,0),
(9,8,8,0,0),
(9,8,9,0,0),
(9,8,10,0,0),
(9,8,11,55,1),
(9,8,12,42,3),
(9,9,0,42,1),
(9,9,1,20,2),
(9,9,2,31,0),
(9,9,3,110,3),
(9,9,4,0,0),
(9,9,5,0,0),
(9,9,6,0,0),
(9,9,7,0,0),
(9,9,8,0,0),
(9,9,9,0,0),
(9,9,10,0,0),
(9,9,11,55,1),
(9,9,12,42,3),
(9,10,0,42,1),
(9,10,1,21,3),
(9,10,2,20,3),
(9,10,3,10,3),
(9,10,4,0,0),
(9,10,5,0,0),
(9,10,6,0,0),
(9,10,7,0,0),
(9,10,8,110,1),
(9,10,9,32,0),
(9,10,10,20,2),
(9,10,11,55,1),
(9,10,12,42,3),
(9,11,0,42,1),
(9,11,1,10,2),
(9,11,2,31,0),
(9,11,3,110,3),
(9,11,4,0,0),
(9,11,5,0,0),
(9,11,6,0,0),
(9,11,7,0,0),
(9,11,8,10,1),
(9,11,9,20,1),
(9,11,10,22,1),
(9,11,11,55,1),
(9,11,12,42,3),
(9,12,0,42,1),
(9,12,1,10,2),
(9,12,2,12,3),
(9,12,3,12,2),
(9,12,4,12,3),
(9,12,5,12,2),
(9,12,6,0,0),
(9,12,7,0,0),
(9,12,8,110,1),
(9,12,9,32,0),
(9,12,10,10,2),
(9,12,11,55,1),
(9,12,12,42,3),
(9,13,0,42,1),
(9,13,1,11,3),
(9,13,2,11,4),
(9,13,3,11,3),
(9,13,4,11,4),
(9,13,5,11,3),
(9,13,6,91,0),
(9,13,7,10,1),
(9,13,8,10,1),
(9,13,9,10,1),
(9,13,10,12,1),
(9,13,11,40,0),
(9,13,12,42,3),
(9,14,0,43,1),
(9,14,1,42,2),
(9,14,2,42,2),
(9,14,3,42,2),
(9,14,4,42,2),
(9,14,5,42,2),
(9,14,6,42,2),
(9,14,7,42,2),
(9,14,8,42,2),
(9,14,9,42,2),
(9,14,10,42,2),
(9,14,11,42,2),
(9,14,12,43,2),
(10,0,0,43,4),
(10,0,1,42,4),
(10,0,2,42,4),
(10,0,3,42,4),
(10,0,4,42,4),
(10,0,5,42,4),
(10,0,6,42,4),
(10,0,7,42,4),
(10,0,8,42,4),
(10,0,9,42,4),
(10,0,10,42,4),
(10,0,11,42,4),
(10,0,12,43,3),
(10,1,0,42,1),
(10,1,1,12,3),
(10,1,2,10,3),
(10,1,3,10,3),
(10,1,4,12,2),
(10,1,5,55,1),
(10,1,6,90,0),
(10,1,7,55,1),
(10,1,8,12,3),
(10,1,9,10,3),
(10,1,10,10,3),
(10,1,11,12,2),
(10,1,12,42,3),
(10,2,0,42,1),
(10,2,1,10,4),
(10,2,2,100,0),
(10,2,3,0,0),
(10,2,4,11,3),
(10,2,5,10,3),
(10,2,6,10,3),
(10,2,7,10,3),
(10,2,8,11,4),
(10,2,9,40,0),
(10,2,10,100,0),
(10,2,11,20,2),
(10,2,12,42,3),
(10,3,0,42,1),
(10,3,1,10,4),
(10,3,2,0,0),
(10,3,3,0,0),
(10,3,4,0,0),
(10,3,5,0,0),
(10,3,6,0,0),
(10,3,7,0,0),
(10,3,8,0,0),
(10,3,9,0,0),
(10,3,10,40,0),
(10,3,11,20,2),
(10,3,12,42,3),
(10,4,0,42,1),
(10,4,1,12,4),
(10,4,2,11,1),
(10,4,3,0,0),
(10,4,4,0,0),
(10,4,5,0,0),
(10,4,6,110,4),
(10,4,7,0,0),
(10,4,8,0,0),
(10,4,9,0,0),
(10,4,10,21,2),
(10,4,11,22,1),
(10,4,12,42,3),
(10,5,0,42,1),
(10,5,1,55,1),
(10,5,2,10,4),
(10,5,3,0,0),
(10,5,4,0,0),
(10,5,5,0,0),
(10,5,6,0,0),
(10,5,7,0,0),
(10,5,8,0,0),
(10,5,9,0,0),
(10,5,10,20,2),
(10,5,11,55,1),
(10,5,12,42,3),
(10,6,0,42,1),
(10,6,1,55,1),
(10,6,2,10,4),
(10,6,3,0,0),
(10,6,4,0,0),
(10,6,5,110,1),
(10,6,6,0,0),
(10,6,7,110,3),
(10,6,8,0,0),
(10,6,9,0,0),
(10,6,10,20,2),
(10,6,11,55,1),
(10,6,12,42,3),
(10,7,0,42,1),
(10,7,1,91,0),
(10,7,2,10,4),
(10,7,3,110,1),
(10,7,4,0,0),
(10,7,5,0,0),
(10,7,6,100,0),
(10,7,7,0,0),
(10,7,8,0,0),
(10,7,9,110,3),
(10,7,10,10,2),
(10,7,11,91,0),
(10,7,12,42,3),
(10,8,0,42,1),
(10,8,1,55,1),
(10,8,2,20,4),
(10,8,3,0,0),
(10,8,4,0,0),
(10,8,5,110,1),
(10,8,6,0,0),
(10,8,7,110,3),
(10,8,8,0,0),
(10,8,9,0,0),
(10,8,10,10,2),
(10,8,11,55,1),
(10,8,12,42,3),
(10,9,0,42,1),
(10,9,1,55,1),
(10,9,2,20,4),
(10,9,3,0,0),
(10,9,4,0,0),
(10,9,5,0,0),
(10,9,6,0,0),
(10,9,7,0,0),
(10,9,8,0,0),
(10,9,9,0,0),
(10,9,10,10,2),
(10,9,11,55,1),
(10,9,12,42,3),
(10,10,0,42,1),
(10,10,1,22,3),
(10,10,2,21,4),
(10,10,3,0,0),
(10,10,4,0,0),
(10,10,5,0,0),
(10,10,6,110,2),
(10,10,7,0,0),
(10,10,8,0,0),
(10,10,9,0,0),
(10,10,10,11,3),
(10,10,11,12,2),
(10,10,12,42,3),
(10,11,0,42,1),
(10,11,1,20,4),
(10,11,2,200,4),
(10,11,3,200,4),
(10,11,4,200,4),
(10,11,5,200,4),
(10,11,6,200,4),
(10,11,7,200,4),
(10,11,8,200,4),
(10,11,9,200,4),
(10,11,10,0,0),
(10,11,11,10,2),
(10,11,12,42,3),
(10,12,0,42,1),
(10,12,1,20,4),
(10,12,2,100,0),
(10,12,3,11,2),
(10,12,4,11,1),
(10,12,5,11,2),
(10,12,6,31,0),
(10,12,7,11,1),
(10,12,8,11,2),
(10,12,9,11,1),
(10,12,10,100,0),
(10,12,11,10,2),
(10,12,12,42,3),
(10,13,0,42,1),
(10,13,1,12,4),
(10,13,2,10,1),
(10,13,3,12,1),
(10,13,4,32,0),
(10,13,5,31,0),
(10,13,6,32,0),
(10,13,7,31,0),
(10,13,8,32,0),
(10,13,9,12,4),
(10,13,10,10,1),
(10,13,11,12,1),
(10,13,12,42,3),
(10,14,0,43,1),
(10,14,1,42,2),
(10,14,2,42,2),
(10,14,3,42,2),
(10,14,4,42,2),
(10,14,5,42,2),
(10,14,6,42,2),
(10,14,7,42,2),
(10,14,8,42,2),
(10,14,9,42,2),
(10,14,10,42,2),
(10,14,11,42,2),
(10,14,12,43,2);
/*!40000 ALTER TABLE `BoardItems` ENABLE KEYS */;
UNLOCK TABLES;

--UNLOCK TABLES;

-- ===== VIEWS =====

DROP VIEW IF EXISTS `viewBoard`;
CREATE VIEW `viewBoard` AS select `Boards`.`BoardID` AS `BoardID`,`Boards`.`BoardName` AS `BoardName`,max(`BoardItems`.`X`) AS `MaxX`,max(`BoardItems`.`Y`) AS `MaxY` from (`Boards` join `BoardItems` on(`Boards`.`BoardID` = `BoardItems`.`BoardID`)) group by `Boards`.`BoardID`,`Boards`.`BoardName`;

DROP VIEW IF EXISTS `viewCommandList`;
CREATE VIEW `viewCommandList` AS select `cl`.`CommandID` AS `CommandID`,`cl`.`Turn` AS `Turn`,`cl`.`Phase` AS `Phase`,`cl`.`CommandCatID` AS `CommandCatID`,`cl`.`CommandTypeID` AS `CommandTypeID`,`cl`.`Parameter` AS `Parameter`,`cl`.`ParameterB` AS `ParameterB`,`cl`.`RobotID` AS `RobotID`,`cl`.`CommandSequence` AS `CommandSequence`,`cl`.`CommandSubSequence` AS `CommandSubSequence`,`cl`.`StatusID` AS `StatusID`,`cl`.`BTCommand` AS `BTCommand`,`cl`.`Description` AS `Description`,`cl`.`PositionRow` AS `PositionRow`,`cl`.`PositionCol` AS `PositionCol`,`cl`.`PositionDir` AS `PositionDir`,`cc`.`Description` AS `CatDes`,`cs`.`StatusDescription` AS `StatusDescription`,`cs`.`StatusColor` AS `StatusColor` from ((`CommandList` `cl` join `CommandCategories` `cc` on(`cl`.`CommandCatID` = `cc`.`CommandCatID`)) join `CommandStatusLookup` `cs` on(`cl`.`StatusID` = `cs`.`StatusID`));

DROP VIEW IF EXISTS `viewCommandListActive`;
CREATE VIEW `viewCommandListActive` AS select `CommandList`.`CommandID` AS `CommandID`,`CommandList`.`Turn` AS `Turn`,`CommandList`.`Phase` AS `Phase`,`CommandList`.`CommandTypeID` AS `CommandTypeID`,`CommandList`.`Parameter` AS `Parameter`,`CommandList`.`RobotID` AS `RobotID`,`CommandList`.`CommandSequence` AS `CommandSequence`,`CommandList`.`CommandSubSequence` AS `CommandSubSequence`,`CommandList`.`StatusID` AS `StatusID`,`CommandList`.`BTCommand` AS `BTCommand`,`CommandList`.`Description` AS `Description`,`CommandList`.`PositionRow` AS `PositionRow`,`CommandList`.`PositionCol` AS `PositionCol`,`CommandList`.`PositionDir` AS `PositionDir`,`CommandList`.`ParameterB` AS `ParameterB`,`CommandList`.`CommandCatID` AS `CommandCatID` from `CommandList` where `CommandList`.`StatusID` >= 2 and `CommandList`.`StatusID` <= 4;

DROP VIEW IF EXISTS `viewCurrentGame`;
CREATE VIEW `viewCurrentGame` AS select `CurrentGameData`.`sKey` AS `sKey`,`CurrentGameData`.`iValue` AS `iValue`,`CurrentGameData`.`sValue` AS `sValue`,`CurrentGameData`.`Category` AS `Category` from `CurrentGameData`;

DROP VIEW IF EXISTS `viewMoveCards`;
CREATE VIEW `viewMoveCards` AS select `MoveCards`.`CardID` AS `CardID`,`MoveCardTypes`.`Description` AS `Desc`,`MoveCardTypes`.`ShortDescription` AS `ShortDesc`,`MoveCards`.`Owner` AS `Owner`,`MoveCards`.`PhasePlayed` AS `PhasePlayed`,`MoveCards`.`Executed` AS `Executed`,`MoveCards`.`Locked` AS `Locked`,`MoveCardTypes`.`FileName` AS `FileName`,`MoveCardLocations`.`Description` AS `Location` from ((`MoveCards` join `MoveCardTypes` on(`MoveCards`.`CardTypeID` = `MoveCardTypes`.`CardTypeID`)) join `MoveCardLocations` on(`MoveCards`.`CardLocation` = `MoveCardLocations`.`LocationID`));

DROP VIEW IF EXISTS `viewOptions`;
CREATE VIEW `viewOptions` AS select `Options`.`OptionID` AS `OptionID`,`Options`.`Name` AS `Name`,`Options`.`SRR_Text` AS `SRR_Text`,`Options`.`EditorType` AS `EditorType`,`Options`.`Quantity` AS `Quantity`,`Options`.`Damage` AS `Damage` from `Options` where `Options`.`Functional` > 7;

DROP VIEW IF EXISTS `viewRobotOptions`;
CREATE VIEW `viewRobotOptions` AS select `RobotOptions`.`RobotID` AS `RobotID`,`RobotOptions`.`OptionID` AS `OptionID`,`viewOptions`.`Name` AS `Name`,`viewOptions`.`SRR_Text` AS `SRR_Text`,`viewOptions`.`EditorType` AS `EditorType`,`RobotOptions`.`DestroyWhenDamaged` AS `DestroyWhenDamaged`,`RobotOptions`.`Quantity` AS `Quantity`,`RobotOptions`.`IsActive` AS `IsActive`,`RobotOptions`.`PhasePlayed` AS `PhasePlayed`,`RobotOptions`.`DataValue` AS `DataValue`,`viewOptions`.`Damage` AS `Damage` from (`viewOptions` join `RobotOptions` on(`viewOptions`.`OptionID` = `RobotOptions`.`OptionID`)) order by `viewOptions`.`Name`;

DROP VIEW IF EXISTS `viewRobots`;
CREATE VIEW `viewRobots` AS select `Robots`.`RobotID` AS `RobotID`,`RobotBodies`.`Name` AS `RobotName`,`RobotBodies`.`Color` AS `RobotColor`,`RobotBodies`.`ColorFG` AS `RobotColorFG`,`Robots`.`CurrentFlag` AS `CurrentFlag`,`RobotStatus`.`StatusColor` AS `StatusColor`,`RobotStatus`.`LEDColor` AS `LEDColor`,`RobotStatus`.`ShortDescription` AS `PlayerStatus`,`Robots`.`Status` AS `StatusID`,`Robots`.`CurrentPosCol` AS `X`,`Robots`.`CurrentPosRow` AS `Y`,`Robots`.`CurrentPosDir` AS `Dir`,`RobotDirections`.`ShortDirDesc` AS `sDir`,`Robots`.`ArchivePosCol` AS `AX`,`Robots`.`ArchivePosRow` AS `AY`,`Robots`.`Score` AS `Score`,`Robots`.`OperatorName` AS `OperatorName`,`Robots`.`PositionValid` AS `PositionValid`,`Robots`.`Priority` AS `Priority`,`Robots`.`ShutDown` AS `ShutDown`,`Robots`.`Password` AS `Password`,`Robots`.`PlayerSeat` AS `PlayerSeat`,`Robots`.`Energy` AS `Energy`,concat(`Robots`.`CurrentFlag`,'/',`Robots`.`Energy`) AS `FlagEnergy`,`so`.`Direction` AS `PlayerViewDirection`,`so`.`Direction` AS `DirectionAdjustment`,`Robots`.`CardsDealt` AS `CardsDealt`,`Robots`.`CardsPlayed` AS `CardsPlayed`,if(`played`.`ShowCardsPlayed` is null or `RobotStatus`.`Active` = 0,`RobotStatus`.`ShortDescription`,`played`.`ShowCardsPlayed`) AS `StatusToShow`,ifnull(`cl`.`Description`,'') AS `msg` from ((((((`Robots` join `RobotBodies` on(`Robots`.`RobotBodyID` = `RobotBodies`.`RobotBodyID`)) join `RobotStatus` on(if(`Robots`.`IsConnected` = 1,`Robots`.`Status`,10) = `RobotStatus`.`RobotStatusID`)) join `RobotDirections` on(`Robots`.`CurrentPosDir` = `RobotDirections`.`DirID`)) join `SeatOrientation` `so` on(`Robots`.`PlayerSeat` = `so`.`SeatID`)) left join (select `mc`.`Owner` AS `Owner`,group_concat(if(`mc`.`CardID` is null,'-',if(`mc`.`Executed`,`mct`.`ShortDescription`,'X')) order by `mc`.`PhasePlayed` ASC separator ',') AS `ShowCardsPlayed` from (`MoveCards` `mc` join `MoveCardTypes` `mct` on(`mc`.`CardTypeID` = `mct`.`CardTypeID`)) where `mc`.`PhasePlayed` > 0 group by `mc`.`Owner` order by `mc`.`Owner`) `played` on(`Robots`.`RobotID` = `played`.`Owner`)) left join `CommandList` `cl` on(`Robots`.`MessageCommandID` = `cl`.`CommandID`)) order by `Robots`.`Priority`;

DROP VIEW IF EXISTS `viewRobotsInit`;
CREATE VIEW `viewRobotsInit` AS select `Robots`.`RobotID` AS `RobotID`,`RobotBodies`.`Name` AS `RobotName`,`RobotBodies`.`Color` AS `RobotColor`,`RobotBodies`.`ColorFG` AS `RobotColorFG`,`Robots`.`OperatorName` AS `OperatorName`,`Robots`.`Password` AS `Password`,`Robots`.`PlayerSeat` AS `PlayerSeat`,`RobotBases`.`MACID` AS `MACID` from ((((`Robots` join `RobotBodies` on(`Robots`.`RobotBodyID` = `RobotBodies`.`RobotBodyID`)) join `RobotDirections` on(`Robots`.`CurrentPosDir` = `RobotDirections`.`DirID`)) join `SeatOrientation` `so` on(`Robots`.`PlayerSeat` = `so`.`SeatID`)) join `RobotBases` on(`Robots`.`RobotBaseID` = `RobotBases`.`RobotBaseID`)) order by `Robots`.`RobotID`;

DROP VIEW IF EXISTS `viewRobotsMicro`;
CREATE VIEW `viewRobotsMicro` AS select `Robots`.`RobotID` AS `RobotID`,`RobotBodies`.`Name` AS `RobotName`,`RobotBodies`.`Color` AS `RobotColor`,`RobotBodies`.`ColorFG` AS `RobotColorFG`,`Robots`.`CurrentFlag` AS `CurrentFlag`,`RobotStatus`.`StatusColor` AS `StatusColor`,`RobotStatus`.`LEDColor` AS `LEDColor`,`RobotStatus`.`ShortDescription` AS `PlayerStatus`,`Robots`.`Status` AS `StatusID`,`Robots`.`CurrentPosCol` AS `X`,`Robots`.`CurrentPosRow` AS `Y`,`Robots`.`CurrentPosDir` AS `Dir`,`RobotDirections`.`ShortDirDesc` AS `sDir`,`Robots`.`ArchivePosCol` AS `AX`,`Robots`.`ArchivePosRow` AS `AY`,`Robots`.`Score` AS `Score`,`Robots`.`OperatorName` AS `OperatorName`,`Robots`.`PositionValid` AS `PositionValid`,`Robots`.`Priority` AS `Priority`,`Robots`.`ShutDown` AS `ShutDown`,`Robots`.`Password` AS `Password`,`Robots`.`PlayerSeat` AS `PlayerSeat`,`Robots`.`Energy` AS `Energy`,concat(`Robots`.`CurrentFlag`,'/',`Robots`.`Energy`) AS `FlagEnergy`,`so`.`Direction` AS `PlayerViewDirection`,`so`.`Direction` AS `DirectionAdjustment`,`Robots`.`CardsDealt` AS `CardsDealt`,`Robots`.`CardsPlayed` AS `CardsPlayed`,if(`played`.`ShowCardsPlayed` is null or `RobotStatus`.`Active` = 0,`RobotStatus`.`ShortDescription`,`played`.`ShowCardsPlayed`) AS `StatusToShow`,`cl`.`Description` AS `msg` from ((((((`Robots` join `RobotBodies` on(`Robots`.`RobotBodyID` = `RobotBodies`.`RobotBodyID`)) join `RobotStatus` on(if(`Robots`.`IsConnected` = 1,`Robots`.`Status`,10) = `RobotStatus`.`RobotStatusID`)) join `RobotDirections` on(`Robots`.`CurrentPosDir` = `RobotDirections`.`DirID`)) join `SeatOrientation` `so` on(`Robots`.`PlayerSeat` = `so`.`SeatID`)) left join (select `mc`.`Owner` AS `Owner`,group_concat(if(`mc`.`CardID` is null,'-',if(`mc`.`Executed`,`mct`.`ShortDescription`,'X')) order by `mc`.`PhasePlayed` ASC separator ',') AS `ShowCardsPlayed` from (`MoveCards` `mc` join `MoveCardTypes` `mct` on(`mc`.`CardTypeID` = `mct`.`CardTypeID`)) where `mc`.`PhasePlayed` > 0 group by `mc`.`Owner` order by `mc`.`Owner`) `played` on(`Robots`.`RobotID` = `played`.`Owner`)) left join `CommandList` `cl` on(`Robots`.`MessageCommandID` = `cl`.`CommandID`)) order by `Robots`.`Priority`;

DROP VIEW IF EXISTS `viewRobotsOld`;
CREATE VIEW `viewRobotsOld` AS select `Robots`.`RobotID` AS `RobotID`,`RobotBodies`.`Name` AS `RobotName`,`RobotBodies`.`Color` AS `RobotColor`,`RobotBodies`.`ColorFG` AS `RobotColorFG`,`Robots`.`CurrentFlag` AS `CurrentFlag`,`RobotStatus`.`StatusColor` AS `StatusColor`,`RobotStatus`.`LEDColor` AS `LEDColor`,`RobotStatus`.`ShortDescription` AS `PlayerStatus`,`Robots`.`Status` AS `StatusID`,`Robots`.`CurrentPosCol` AS `X`,`Robots`.`CurrentPosRow` AS `Y`,`Robots`.`CurrentPosDir` AS `Dir`,`RobotDirections`.`ShortDirDesc` AS `sDir`,`Robots`.`ArchivePosCol` AS `AX`,`Robots`.`ArchivePosRow` AS `AY`,`Robots`.`Score` AS `Score`,`Robots`.`OperatorName` AS `OperatorName`,`Robots`.`PositionValid` AS `PositionValid`,`Robots`.`Priority` AS `Priority`,`Robots`.`ShutDown` AS `ShutDown`,`Robots`.`Password` AS `Password`,`Robots`.`PlayerSeat` AS `PlayerSeat`,`Robots`.`Energy` AS `Energy`,concat(`Robots`.`CurrentFlag`,'/',`Robots`.`Energy`) AS `FlagEnergy`,`so`.`Direction` AS `PlayerViewDirection`,`so`.`Direction` AS `DirectionAdjustment`,`dealt`.`CardsDealt` AS `CardsDealt`,`played`.`CardsPlayed` AS `CardsPlayed`,if(`played`.`ShowCardsPlayed` is null or `RobotStatus`.`Active` = 0,`RobotStatus`.`ShortDescription`,`played`.`ShowCardsPlayed`) AS `StatusToShow` from ((((((`Robots` join `RobotBodies` on(`Robots`.`RobotBodyID` = `RobotBodies`.`RobotBodyID`)) join `RobotStatus` on(if(`Robots`.`IsConnected` = 1,`Robots`.`Status`,10) = `RobotStatus`.`RobotStatusID`)) join `RobotDirections` on(`Robots`.`CurrentPosDir` = `RobotDirections`.`DirID`)) join `SeatOrientation` `so` on(`Robots`.`PlayerSeat` = `so`.`SeatID`)) left join (select `mc`.`Owner` AS `Owner`,group_concat(`mct`.`ShortDescription` order by `mct`.`CardTypeID` ASC separator ',') AS `CardsDealt` from (`MoveCards` `mc` join `MoveCardTypes` `mct` on(`mc`.`CardTypeID` = `mct`.`CardTypeID`)) where `mc`.`CardLocation` = 1 group by `mc`.`Owner`) `dealt` on(`Robots`.`RobotID` = `dealt`.`Owner`)) left join (select `mc`.`Owner` AS `Owner`,group_concat(if(`mc`.`CardID` is null,'-',if(`mc`.`Executed`,`mct`.`ShortDescription`,'X')) order by `mc`.`PhasePlayed` ASC separator ',') AS `ShowCardsPlayed`,group_concat(`mct`.`ShortDescription` order by `mc`.`PhasePlayed` ASC separator ',') AS `CardsPlayed` from (`MoveCards` `mc` join `MoveCardTypes` `mct` on(`mc`.`CardTypeID` = `mct`.`CardTypeID`)) where `mc`.`PhasePlayed` > 0 group by `mc`.`Owner` order by `mc`.`Owner`) `played` on(`Robots`.`RobotID` = `played`.`Owner`)) order by `Robots`.`Priority`;

DROP VIEW IF EXISTS `viewRobotsRefresh`;
CREATE VIEW `viewRobotsRefresh` AS select `Robots`.`RobotID` AS `RobotID`,`Robots`.`CurrentFlag` AS `CurrentFlag`,`RobotStatus`.`StatusColor` AS `StatusColor`,`RobotStatus`.`LEDColor` AS `LEDColor`,`RobotStatus`.`ShortDescription` AS `PlayerStatus`,`Robots`.`Status` AS `StatusID`,`Robots`.`CurrentPosCol` AS `X`,`Robots`.`CurrentPosRow` AS `Y`,`Robots`.`CurrentPosDir` AS `Dir`,`RobotDirections`.`ShortDirDesc` AS `sDir`,`Robots`.`ArchivePosCol` AS `AX`,`Robots`.`ArchivePosRow` AS `AY`,`Robots`.`Score` AS `Score`,`Robots`.`PositionValid` AS `PositionValid`,`Robots`.`Priority` AS `Priority`,`Robots`.`ShutDown` AS `ShutDown`,`Robots`.`Energy` AS `Energy`,concat(`Robots`.`CurrentFlag`,'/',`Robots`.`Energy`) AS `FlagEnergy`,`Robots`.`CardsDealt` AS `CardsDealt`,`Robots`.`CardsPlayed` AS `CardsPlayed`,if(`played`.`ShowCardsPlayed` is null or `RobotStatus`.`Active` = 0,`RobotStatus`.`ShortDescription`,`played`.`ShowCardsPlayed`) AS `StatusToShow`,`cl`.`Description` AS `msg` from ((((`Robots` join `RobotStatus` on(if(`Robots`.`IsConnected` = 1,`Robots`.`Status`,10) = `RobotStatus`.`RobotStatusID`)) join `RobotDirections` on(`Robots`.`CurrentPosDir` = `RobotDirections`.`DirID`)) left join (select `mc`.`Owner` AS `Owner`,group_concat(if(`mc`.`CardID` is null,'-',if(`mc`.`Executed`,`mct`.`ShortDescription`,'X')) order by `mc`.`PhasePlayed` ASC separator ',') AS `ShowCardsPlayed` from (`MoveCards` `mc` join `MoveCardTypes` `mct` on(`mc`.`CardTypeID` = `mct`.`CardTypeID`)) where `mc`.`PhasePlayed` > 0 group by `mc`.`Owner` order by `mc`.`Owner`) `played` on(`Robots`.`RobotID` = `played`.`Owner`)) left join `CommandList` `cl` on(`Robots`.`MessageCommandID` = `cl`.`CommandID`)) order by `Robots`.`Priority`;

-- ===== FUNCTIONS =====

DROP FUNCTION IF EXISTS `funcDealSpamToPlayer`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` FUNCTION `funcDealSpamToPlayer`(p_RobotID int) RETURNS int(11)
BEGIN
	
    
    
    declare maxid int;
    
    select max(cardID)+1 into maxid from MoveCards where `Owner`=p_RobotID;
    insert into MoveCards (CardID, CardTypeID, `Owner`, CardLocation) values (maxid, 10, p_RobotID, 3) ; 
    
    return maxid;

END ;;
DELIMITER ;

DROP FUNCTION IF EXISTS `funcGetNextCard`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` FUNCTION `funcGetNextCard`(p_player int, p_usedSpam int) RETURNS int(11)
BEGIN


    DECLARE cCardID INT;
    DECLARE cCardLoc INT;

	
	update MoveCards set CardLocation = 5 where `Owner` = p_player and CardID = p_usedSpam;
	

	select CardID, CardLocation into cCardID, cCardLoc
	from MoveCards 
    where `Owner` = p_player and (CardLocation = 0 or CardLocation = 3)
    order by CurrentOrder limit 1;

	
	if (cCardLoc <> 0) then
   
		
		Update MoveCards m0 
		set CardLocation = 0
		where CardLocation = 3;
        
		
		Update MoveCards mc inner join MoveCardLocations mcl on mc.CardLocation = mcl.LocationID
		Set mc.Random = ROUND(500.0 * RAND() )+mcl.DealPriority*500,CurrentOrder = 0  ;
		
		
		Update MoveCards m1 inner join
		(
			Select mc.CardID, mc.Owner, count(mc.CardID) as cnt, mc.CardLocation  from MoveCards mc 
			inner join MoveCards mc2 on mc.Owner = mc2.Owner and mc.Random >=mc2.Random
			group by mc.CardID, mc.Owner , mc.CardLocation
		order by mc.Owner, cnt
		) ij
		on m1.Owner = ij.Owner and m1.CardID=ij.CardID
		set m1.CurrentOrder = ij.cnt;
        
        
		select CardID, CardLocation into cCardID, cCardLoc
		from MoveCards 
		where `Owner` = p_player and CardLocation = 0
		order by CurrentOrder limit 1;

    end if ;
    
	
	Update MoveCards set CardLocation = 1 where `Owner` = p_player and CardID = cCardID;
    
	
    return cCardID;

END ;;
DELIMITER ;

DROP FUNCTION IF EXISTS `funcGetNextGameState`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` FUNCTION `funcGetNextGameState`() RETURNS int(11)
BEGIN
	DECLARE cState INT;
	DECLARE cTurn INT;
	DECLARE cPhase INT;
	DECLARE cResult INT;
    DECLARE cStartingState int;
    
    repeat
		select iValue into cState from CurrentGameData where sKey = 'GameState';
    
		set cStartingState = cState;

		CASE cState
		WHEN 0 THEN
			
			
			call procGameNew();
			set cState = 2;
			update CurrentGameData set iValue=0 where iKey=2; 
			update CurrentGameData set iValue=0 where iKey=3; 
		WHEN 1 THEN
			
			set cState = 2;
		WHEN 2 THEN
			
			call procResetPlayers();
			
			call procMoveCardsShuffleAndDeal();
			set cState = 3; 
			update CurrentGameData set iValue=iValue+1 where iKey=2; 

		WHEN 3 THEN 
			
			select count(*) into cResult from Robots where PositionValid=0;
			if cResult = 0 then 
				set cState = 4;
			end if;
		WHEN 4 THEN
			
			Select Count(*) into cResult from Robots where (Status <> 4 and Status < 9) ; 
			if cResult = 0 then
				set cState = 5;
			end if;
		WHEN 5 THEN
			
            Update Robots set `Status` = 13;
			call procCurrentPosSave();
			set cState = 6;
		WHEN 6 THEN
			
			set cState = 6;
		WHEN 7 THEN
			
            
			set cState = 8;
            
		WHEN 8 THEN
			
			begin
			end;
		WHEN 9 THEN
			
			
			set cState = 8;
		WHEN 10 THEN
			
			
			
			set cState = 8;
		WHEN 11 THEN
			
			
			
			set cState = 8;
		WHEN 12 THEN
			
			set cState = 2;
		WHEN 13 THEN
			
			Delete from CommandList where CommandTypeID = 70;
			
			set cState = 0;
			set cStartingState = cState;
		WHEN 14 THEN
			
			
			begin
			end;
		WHEN 15 THEN
			
			
			begin
			end;
		WHEN 16 THEN
			
			
			call procCurrentPosLoad();
			set cState = 3;
			
		ELSE
			begin
			
			end;
		END CASE;
		
		update CurrentGameData set iValue=cState where sKey="GameState";
		
		
        
	until (cState = cStartingState)
	end repeat;

	return cState;
    
END ;;
DELIMITER ;

DROP FUNCTION IF EXISTS `funcGetNextOption`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` FUNCTION `funcGetNextOption`(p_RobotID int) RETURNS int(11)
BEGIN
	DECLARE cOptionID INT;
    
    
	Select Options.OptionID into cOptionID from `Options`
		left join (Select * from RobotOptions where RobotID=p_RobotID) as RO
        on `Options`.OptionID=RO.OptionID 
        where isnull(`RO`.RobotID) and Options.Functional>7
        order by CurrentOrder 
        limit 1;    
	
    Update Options set CurrentOrder = CurrentOrder+100 where OptionID = cOptionID;

	return cOptionID;
    
END ;;
DELIMITER ;

DROP FUNCTION IF EXISTS `funcGetProgramReadyState`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` FUNCTION `funcGetProgramReadyState`() RETURNS int(11)
BEGIN
	DECLARE cResult INT;

	
	select count(*) into cResult from Robots where MessageID > 0; 
	if cResult = 0 then
    
		
		Select Count(*) into cResult from Robots where (Status <> 4 and Status < 9) ; 
		if cResult = 0 then 
			return 5; 
		else
			return 4; 
		end if ;
	else
		return 3;
	end if;
    
    
END ;;
DELIMITER ;

DROP FUNCTION IF EXISTS `funcMarkCommandsReady`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` FUNCTION `funcMarkCommandsReady`() RETURNS int(11)
BEGIN
	DECLARE result INT;
	
	
	DECLARE cSequence INT;
	DECLARE cRecords INT;

    
    select count(CommandID) into result from CommandList where StatusID>=2 and StatusID <=4;
    if result > 0 then 
		return result; 
    end if;
    
    
    
    
    

    select count(CommandID), min(CommandSequence) into cRecords, cSequence from CommandList 
		where StatusID=1;
		
    
    if (cRecords = 0) then 
		return 0; 
		
	end if;
    
    
    
    Update CommandList set StatusID=2 where CommandSequence=cSequence; 
    select count(CommandID) into result from CommandList where StatusID>=2 and StatusID <=4;
    
    return result; 
    
    
END ;;
DELIMITER ;

DROP FUNCTION IF EXISTS `funcProcessCommand`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` FUNCTION `funcProcessCommand`(p_CommandID int, p_NewStatus int) RETURNS int(11)
BEGIN
	DECLARE result INT;
	DECLARE cTurn INT;
	DECLARE cPhase INT;
	DECLARE cSequence INT;
	DECLARE cRecords INT;
    DECLARE cState INT;
    
    declare cRow int;
    declare cCol int;
    declare cDir int;
    declare cParameter int;
    declare cParameterB int;
    declare cRobotID int;
    declare cType int;
    declare cDone int;
    declare cRobotsActive int;
    declare cStatus int;
    declare cDescription varchar(50);
    
    declare cPhaseCount int;
    























    


	select CommandTypeID, RobotID, Parameter, ParameterB, PositionRow, PositionCol, PositionDir, Turn, `Phase`, Description, StatusID
		into cType, cRobotID, cParameter, cParameterB, cRow, cCol, cDir, cTurn, cPhase, cDescription, cStatus 
        from CommandList 
        where CommandID = p_CommandID;
            
	
    if (p_NewStatus = -1) then
		set p_NewStatus = 6; 
	end if;

	case cType
	  WHEN 3 THEN 
		set p_NewStatus = 5; 
	  WHEN 15 THEN 
		Update Robots set ArchivePosRow = cRow, 
			ArchivePosCol = cCol,
			ArchivePosDir = 0
			where RobotID = cRobotID;
	  WHEN 14 THEN 
		Update Robots set Damage = cParameter where RobotID = cRobotID;
	  WHEN 73 then
        
        select `funcDealSpamToPlayer`(cRobotID ) into result;
	  WHEN 16 THEN 
		Update Robots set CurrentFlag = cParameter where RobotID = cRobotID;
	  WHEN 22 THEN 
		Update Robots set Lives = cParameter where RobotID = cRobotID;
	  When 24 then 
		update MoveCards set Owner = cRobotID where CardID = cParameter;
	  
		
		
		
        
	  When 41 then 
		
		update CurrentGameData set iValue =  11 where iKey = 10;
        update CurrentGameData set iValue =  cRobotID where iKey = 13;
	  When 95 then 
		
		update CurrentGameData set iValue = 12 where iKey = 10;
	  When 42 then 
		update MoveCards set Executed=1 where CardID = cParameter and `Owner`= cRobotID; 
		
		update CurrentGameData set sValue = "Played Card" where iKey = 21; 
	  When 97 then 
		
		update CurrentGameData set iValue = cParameter where iKey = 10;
        update CurrentGameData set iValue =  cRobotID where iKey = 13;
	  When 96 then 
		Delete from Robots where RobotID = cRobotID;
	  When 63 then 
		Update Robots set Status = cParameter where RobotID = cRobotID;
	  When 64 then 
		Update Robots set DamagePoints = cParameter where RobotID = cRobotID;
	  When 65 then 
		Begin
		End;
	  When 66 then 
		Delete from RobotOptions where RobotID = cRobotID and OptionID = cParameter;
	  When 67 then 
		Update RobotOptions set Quantity = cParameterB where RobotID = cRobotID and OptionID = cParameter;
	  When 68 then 
		
		update CurrentGameData set iValue =  cParameter where iKey = 17;

	  When 82 then 
		Update Robots set `ShutDown` = cParameter where RobotID = cRobotID ;
	  When 30 then 
		Begin
		End;
	  When 21 then 
		Begin
		End;
	  When 49 then 
		Begin
		End;
	  When 60 then 
		Begin
		End;
	  When 56 then 
		Begin
		End;
	  When 55 then 
		Begin
		End;
	  When 18 then 
		Begin
		End;
	  When 43 then 
		Begin
		End;
	  When 91 then 
		update CurrentGameData set iValue = cParameterB where iKey = cParameter;
	  When 92 then 
		begin
        end;
	  When 17 then 
		if cParameter=0 then
			
			
			set cParameter = funcGetNextOption(cRobotID);
		end if;
		
		insert into RobotOptions (RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive,PhasePlayed,DataValue)
		Select  cRobotID, OptionID, false, Quantity, false, 0, 0 from `Options` where OptionID=cParameter;
		
	  
	  
	  
		
	  
	  
	  
	  ELSE
		begin
        end;
	END CASE;
			
        
	if p_NewStatus=5 then 
		if cCol>=0 and cRow>=0 then 
			Update Robots set CurrentPosRow = cRow, 
				CurrentPosCol = cCol,
				CurrentPosDir = cDir,
				Score = cParameterB
				where RobotID = cRobotID;
		end if;
				
		set p_NewStatus=6; 
	end if;
	
	if (p_CommandID > 0) then 
		update CommandList set StatusID = p_NewStatus where CommandID = p_CommandID; 
	end if;
		
        
	return p_NewStatus;
end ;;
DELIMITER ;

-- ===== STORED PROCEDURES =====

DROP PROCEDURE IF EXISTS `procCardPlayed`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procCardPlayed`(in p_Card varchar(1), in p_Player int)
BEGIN
    DECLARE inProgramming INT;
    DECLARE NewStatus INT;
    DECLARE PhaseCount INT;
    DECLARE ProgramCount INT;
    DECLARE TargetPhase int;
    DECLARE uCardID int;
    DECLARE uPhasePlayed int;
    
	select Programming from Robots inner join RobotStatus on Robots.Status = RobotStatus.RobotStatusID 
		where Robots.RobotID=p_Player into inProgramming;
		
	if inProgramming = 1 then 
		select iValue into PhaseCount from CurrentGameData where sKey="PhaseCount" ;
        
        
        Select min(CardID) into uCardID from MoveCards inner join MoveCardTypes on MoveCards.CardTypeID = MoveCardTypes.CardTypeID 
			where `Owner` = p_Player and CardLocation = 1 and MoveCardTypes.ShortDescription = p_Card;
        if uCardID is not null then
			
            Select min(pc.ID) into uPhasePlayed from PhaseCounter pc left join MoveCards mc on pc.ID = mc.PhasePlayed and mc.Owner = p_Player where mc.CardTypeID is null; 
            
            if uPhasePlayed is not null and uPhasePlayed <= PhaseCount then 
				Update MoveCards set PhasePlayed = uPhasePlayed, CardLocation=2 where `Owner` = p_Player and  CardID = uCardID and CardLocation=1 ;
            end if;
        end if ;
        
		
        set NewStatus = 3;
        
		select count(*) as p into ProgramCount from MoveCards where `Owner` = p_Player and CardLocation = 2 ;
        if PhaseCount = ProgramCount then
			set NewStatus = 4;
        end if;
        
        call procUpdateRobotCards(p_Player);
                
		Update Robots set `Status` = NewStatus where RobotID = p_Player;
        
	end if ;
END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procCurrentPosLoad`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procCurrentPosLoad`()
BEGIN
    
	DECLARE iGameID INT;
    DECLARE iTurn INT;
    
    
	select iValue into iGameID from CurrentGameData where sKey="GameDataID" ;
	select iValue into iTurn from CurrentGameData where sKey="Turn" ;
    
    call procResetGame();
    
    insert into Robots (RobotID, OperatorName, RobotBaseID, RobotBodyID, 
		CurrentFlag, Lives, Damage, `ShutDown`, Computer, Score, `Status`,
        CurrentPosRow, CurrentPosCol, CurrentPosDir,
        ArchivePosRow, ArchivePosCol, ArchivePosDir, Priority, PositionValid)
    select RobotID, OperatorName, RobotBaseID, RobotBodyID, 
		CurrentFlag, Lives, Damage, `ShutDown`, Computer, Score, `Status`,
        CurrentPosRow, CurrentPosCol, CurrentPosDir,
        ArchivePosRow, ArchivePosCol, ArchivePosDir, Priority, 0
    from HistoryRobots where GameID = iGameID and Turn = iTurn;
    
	call procGameNewAddCards();

	
    
    
   	update MoveCards
    inner join HistoryMoveCards on MoveCards.CardID = HistoryMoveCards.CardID
    set MoveCards.`Owner` = HistoryMoveCards.`Owner`, 
	  MoveCards.PhasePlayed = HistoryMoveCards.PhasePlayed, 
	  MoveCards.Locked = HistoryMoveCards.Locked 
      where HistoryMoveCards.GameID = iGameID and HistoryMoveCards.Turn = iTurn;
    
    Insert into RobotOptions (RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue)
    Select RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue
    From HistoryRobotOptions  where GameID = iGameID and Turn = iTurn;
    
END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procCurrentPosSave`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procCurrentPosSave`()
BEGIN
	
	DECLARE iGameID INT;
    DECLARE iTurn INT;
    
    
	select iValue into iGameID from CurrentGameData where sKey="GameDataID" ;
	select iValue into iTurn from CurrentGameData where sKey="Turn" ;

    
    delete from HistoryRobots where GameID = iGameID and Turn = iTurn;
    
    insert into HistoryRobots (GameID, Turn, RobotID, OperatorName, RobotBaseID, RobotBodyID, 
		CurrentFlag, Lives, Damage, `ShutDown`, Computer, Score, `Status`,
        CurrentPosRow, CurrentPosCol, CurrentPosDir,
        ArchivePosRow, ArchivePosCol, ArchivePosDir, Priority)
    select iGameID, iTurn, RobotID, OperatorName, RobotBaseID, RobotBodyID, 
		CurrentFlag, Lives, Damage, `ShutDown`, Computer, Score, `Status`,
        CurrentPosRow, CurrentPosCol, CurrentPosDir,
        ArchivePosRow, ArchivePosCol, ArchivePosDir, Priority
    from Robots;
    
    delete from HistoryMoveCards where GameID = iGameID and Turn = iTurn;

	insert into HistoryMoveCards (GameID, Turn, CardID, `Owner`, PhasePlayed, Locked)
    Select iGameID, iTurn, CardID, `Owner`, PhasePlayed, Locked
    from MoveCards where `Owner`>0;
    
    delete from HistoryRobotOptions where GameID = iGameID and Turn = iTurn;

    Insert into HistoryRobotOptions (GameID, Turn, RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue)
    Select iGameID, iTurn, RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue
    From RobotOptions;
END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procDealOptionToRobot`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procDealOptionToRobot`(IN p_RobotID int)
BEGIN
	DECLARE cOptionID INT;
    DECLARE cQuantity INT;
    
    
	Select Options.OptionID into cOptionID from `Options`
		left join (Select * from RobotOptions where RobotID=p_RobotID) as RO
        on `Options`.OptionID=RO.OptionID 
        where isnull(`RO`.RobotID) and Options.Functional>7
        order by CurrentOrder 
        limit 1;    
        
	if cOptionID > 0 then
		insert into RobotOptions (RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive,PhasePlayed,DataValue)
		Select  p_RobotID, OptionID, false, Quantity, false, 0, if(EditorType=6,1,0) from `Options` where OptionID=cOptionID;
		
	else
		
		Select Options.OptionID, Options.Quantity into cOptionID, cQuantity from `Options`
			where Options.Quantity > -1 and Options.Functional>7 
			order by CurrentOrder 
			limit 1;    
        if cQuantity > 0 then
			update RobotOptions set Quantity = Quantity + cQuantity where RobotID=p_RobotID and OptionID=cOptionID;
        else
			
			insert into RobotOptions (RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive,PhasePlayed,DataValue)
			Select  p_RobotID, OptionID, false, Quantity, false, 0, if(EditorType=6,1,0) from `Options` where OptionID=cOptionID;
        end if;
	end if;
    Update Options set CurrentOrder = CurrentOrder+100 where OptionID = cOptionID;

END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procGameFillPrograms`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procGameFillPrograms`()
BEGIN
	DECLARE tRobot INT;
	DECLARE tPhase INT;
	DECLARE tCard INT;
	DECLARE tRows INT;
	DECLARE tCounter INT;

   	
    set tCounter = 0;

	REPEAT
		
        
		SELECT RobotID, PhaseID
		FROM MoveCards RIGHT JOIN (SELECT Robots.RobotID, PhaseCounter.ID as PhaseID
		FROM PhaseCounter, Robots) as AllRobotPhases ON (MoveCards.PhasePlayed = AllRobotPhases.PhaseID) AND (MoveCards.Owner = AllRobotPhases.RobotID)
		WHERE (((MoveCards.CardID) Is Null)) limit 1 into tRobot, tPhase;
		
        if (ROW_COUNT() > 0) then 
			Select CardID from MoveCards where Owner=tRobot and PhasePlayed = -1 order by CurrentOrder, CardID limit 1 into tCard;
		
			Update MoveCards set PhasePlayed = tPhase, Random = 1 where CardID=tCard;    
		end if;
        
		
        set tRows = ROW_COUNT();
        set tCounter = tCounter+1;
        if (tCounter > 300) then 
			set tRows = 0;
		end if;
        
        
	UNTIL tRows=0 END REPEAT;
   	
    call procMoveCardsCheckProgrammed();

 
END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procGameNew`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procGameNew`()
BEGIN
    declare iFlags int;
    
    call procResetGame();
        
    insert into Robots (RobotID, OperatorName, RobotBaseID, RobotBodyID, `Status`, Priority, `Password`,
		CurrentPosCol,CurrentPosRow,CurrentPosDir,ArchivePosCol,ArchivePosRow,ArchivePosDir,PlayerSeat)	
    Select  RobotID, OperatorName, RobotID, RobotBodyID, 1, OperatorData.PlayerSeat, `Password` , 
		ba.X, ba.Y, bi.Rotation, ba.X, ba.Y, bi.Rotation,OperatorData.PlayerSeat
    from OperatorData 
    inner join BoardItemActions ba on OperatorData.StartPosition = ba.Parameter 
		and ba.SquareAction = 19  
	inner join BoardItems bi on bi.X = ba.X and bi.Y = ba.Y 
    inner join CurrentGameData pl on OperatorData.OperatorListID = pl.iValue and pl.sKey = "PlayerListID"
    inner join CurrentGameData bl on ba.BoardID = bl.iValue and bi.BoardID = bl.iValue and bl.sKey = "BoardID"
    where IsActive>0 ;
    
    select count(*) into iFlags from BoardItems bi inner join CurrentGameData cg on bi.BoardID=cg.iValue and cg.sKey = "BoardID" and bi.SquareType=100;
    
	update CurrentGameData set iValue=iFlags where sKey='TotalFlags';
   
    
	
   
    insert into StatusLEDs (LEDID, Color, Sort) 
    
    select RobotID, '000000',RobotID from Robots limit 8;
    
    
    
    Update `Options` set CurrentOrder = ROUND(100.0 * RAND() );
    
    
    
END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procGameNewAddCards`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procGameNewAddCards`()
BEGIN
	DECLARE NewsetID int DEFAULT 1;
	DECLARE playercount int DEFAULT 8;
	DECLARE lPhaseCount int DEFAULT 5;
	DECLARE RulesVersion int ;
    
   	
	Delete from MoveCards;

    select count(*) from Robots into playercount;
    if playercount > 8 then 
		set NewsetID = 2;
    end if ;
    
	select iValue into lPhaseCount from CurrentGameData where sKey="PhaseCount" ;
    if lPhaseCount = 1 then 
		set NewsetID = 3;
    end if ;

	select iValue into RulesVersion from CurrentGameData where sKey="RulesVersion" ;
    if RulesVersion = 1 then
		set NewsetID = 4;
		Insert into MoveCards (CardID, CardTypeID,`Owner`,CardLocation)
		select CardID, CardTypeID, Robots.RobotID, 0 
		from MoveCardsCompleteList, Robots where SetID = NewsetID;
	else
		
		Insert into MoveCards (CardID, CardTypeID)
		select CardID, CardTypeID 
		from MoveCardsCompleteList where SetID = NewsetID;
    end if;        
    

END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procGameStart`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procGameStart`(in p_GameDataID int)
BEGIN


	Update CurrentGameData set iValue = 0 where iKey = 10; 
	Update CurrentGameData set iValue = p_GameDataID where iKey = 26; 
    
    select funcGetNextGameState();
END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procGetReadyCommands`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procGetReadyCommands`()
this_proc:BEGIN
	DECLARE result INT;
	DECLARE cTurn INT;
	DECLARE cPhase INT;
	DECLARE cSequence INT;
	DECLARE cRecords INT;
    
    select iValue into result from CurrentGameData where iKey = 10; 
    if result != 8 then
		select * from viewCommandListActive;
		LEAVE this_proc;
    end if;

    
    select count(*) into result from viewCommandListActive ;
    if result > 0 then 
		select * from viewCommandListActive ;
		LEAVE this_proc;
    end if;
    
    
    select iValue into cTurn from CurrentGameData where sKey = 'Turn';
    
    

    select count(CommandID), min(CommandSequence) into cRecords, cSequence from CommandList 
		where Turn=cTurn and StatusID=1;
    
    if (cRecords = 0) then 
		select * from viewCommandListActive;
		LEAVE this_proc;
	end if;
    
    
    Update CommandList set StatusID=2 where Turn=cTurn and CommandSequence=cSequence; 
    
    
    select * from viewCommandListActive;
    
    
    
    
END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procKickstart`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procKickstart`()
BEGIN

	Update CurrentGameData set iValue = 8 where sKey="GameState";
	call procCommandUpdateStatus(-1,0);
END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procMoveCardsCheckOne`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procMoveCardsCheckOne`(in p_Player int)
BEGIN
    DECLARE RequiredCards INT;
    DECLARE cards INT;
    DECLARE programmed INT;
    DECLARE locked INT;
    
    DECLARE NewStatus INT;
    DECLARE cstatus INT;

	select iValue into RequiredCards from CurrentGameData where sKey="PhaseCount" ;
	
	select count(CardID),
		sum(if(MoveCards.PhasePlayed > 0 && MoveCards.PhasePlayed < 6,1,0)) ,
		sum(if(MoveCards.Locked > 0,1,0)) 
		into cards, programmed, locked
		from MoveCards where Owner = p_Player;
		
	set NewStatus = 0;
	
	IF cards < 5 then
		set NewStatus = 1; 
	elseif programmed = RequiredCards then
		set NewStatus = 4; 
	elseif programmed > locked then
		set NewStatus = 3; 
	else
		set NewStatus = 2; 
	END IF;
	
	Update Robots set `Status` = NewStatus where RobotID = p_Player;

	
	select iValue into cstatus from CurrentGameData where sKey="GameState" ;
	if ((cstatus=3) or (cstatus=4)) then 
		call procGameNextState();
	end if;
END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procMoveCardsCheckProgrammed`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procMoveCardsCheckProgrammed`()
BEGIN
	
    
	
    
	DECLARE bDone INT;
    DECLARE cards INT;
    DECLARE programmed INT;
    DECLARE locked INT;
    DECLARE robot INT;
    DECLARE cstatus INT;
    
    DECLARE NewStatus INT;
    DECLARE NextGameState INT;
    DECLARE IncompleteRobots INT;
    DECLARE RequiredCards INT;

    
    DECLARE curs CURSOR FOR  
		SELECT count(CardID) as countCards, 
			sum(if(MoveCards.PhasePlayed > 0 && MoveCards.PhasePlayed < 6,1,0)) as countProgrammed,
			sum(if(MoveCards.Locked > 0,1,0)) as countLocked,
            Robots.RobotID as rID,
            Robots.`Status` as CurrentStatus
            
        FROM Robots inner join RobotStatus on Robots.`Status` = RobotStatus.RobotStatusID 
        inner join MoveCards on Robots.RobotID = MoveCards.`Owner`
        WHERE RobotStatus.Programming = 1
        Group By rID, CurrentStatus;
        
	DECLARE CONTINUE HANDLER FOR NOT FOUND SET bDone = 1;
    
    set NextGameState = 8; 
    set IncompleteRobots = 0;
    
	select iValue into RequiredCards from CurrentGameData where sKey="PhaseCount" ;
    
    OPEN curs;

	SET bDone = 0;
	REPEAT
		FETCH curs INTO cards,programmed,locked, robot, cstatus;

		set NewStatus = 0;
        
		IF cards < 5 then
			set NewStatus = 1; 
		elseif programmed = RequiredCards then
			set NewStatus = 4; 
		elseif programmed > locked then
			set NewStatus = 3; 
		else
			set NewStatus = 2; 
		END IF;
        
        if (NewStatus <> 4) then 
			Set IncompleteRobots = IncompleteRobots + 1;
        end if;
        
        if cstatus != NewStatus then 
			update Robots set `Status` = NewStatus where RobotID = robot;
		end if;
	UNTIL bDone END REPEAT;

	CLOSE curs;    
    
    
	
	
	
    
    
    
	select iValue into cstatus from CurrentGameData where sKey="GameState" ;
    
	
    
END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procMoveCardsShuffleAndDeal`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procMoveCardsShuffleAndDeal`()
BEGIN
	
	
    
    
    
    

	DECLARE bDone INT;
    DECLARE cards INT;
    DECLARE robot INT;
    DECLARE locked INT;
    DECLARE damage INT;
    DECLARE NewCardCount INT;
    DECLARE LastLockedCard INT;
    DECLARE lPhaseCount INT;
    DECLARE lOptionCards int;
    DECLARE lRulesVersion int;

    DECLARE curs CURSOR FOR  
		SELECT Robots.Damage as totalDamage,
			Robots.RobotID as rID
            
        FROM Robots inner join RobotStatus on Robots.`Status` = RobotStatus.RobotStatusID 
        WHERE RobotStatus.Programming = 1;
        
	DECLARE CONTINUE HANDLER FOR NOT FOUND SET bDone = 1;
    
	select iValue into lRulesVersion from CurrentGameData where sKey="RulesVersion" ;

	
    
    
	Update MoveCards Set MoveCards.`Locked` = if(CardLocation=4,1,0), `Executed` = 0, `Random` = 0;
    
    if ROW_COUNT() = 0 then 
		call procGameNewAddCards();
	end if;
    
	select iValue into lPhaseCount from CurrentGameData where sKey="PhaseCount" ;
    
    if lPhaseCount = 1 then 
		call procUpdatePlayerPriority();

		
		Update MoveCards Set PhasePlayed = -1;
        
        
        Update MoveCards, Robots set Owner=RobotID where 10-FLOOR((CardID-1)/7)=Priority;
        
	elseif lRulesVersion = 1 then
    
		
        
        delete from MoveCards where CardLocation = 2 and CardTypeID=10; 
        
        
        update MoveCards
        set CardLocation = 3, PhasePlayed = 0
        where CardLocation = 1 or CardLocation = 2;
    
		
		Update MoveCards mc inner join MoveCardLocations mcl on mc.CardLocation = mcl.LocationID
        Set mc.Random = ROUND(500.0 * RAND() )+mcl.DealPriority*500,CurrentOrder = 0  ;
        
        
		Update MoveCards m1 inner join
		(
			Select mc.CardID, mc.Owner, count(mc.CardID) as cnt, mc.CardLocation  from MoveCards mc 
			inner join MoveCards mc2 on mc.Owner = mc2.Owner and (mc.Random >mc2.Random or (mc.Random=mc2.Random and mc.CardID >= mc2.CardID))
			group by mc.CardID, mc.Owner , mc.CardLocation
		order by mc.Owner, cnt
		) ij
		on m1.Owner = ij.Owner and m1.CardID=ij.CardID
		set m1.CurrentOrder = ij.cnt;

        
        Update MoveCards m0 inner join (select Owner from MoveCards where CardLocation = 0 group by Owner having Count(CardID)<9) lt9
		on m0.Owner = lt9.Owner 
		set CardLocation = 0
        where CardLocation = 3;

        
        Update MoveCards set CardLocation = 1 where CurrentOrder <= 9;

		update Robots rb inner join (	select mc.Owner, GROUP_CONCAT(mc.CardTypeID order by mc.CardTypeID desc) gctl
			from MoveCards mc 
			where mc.CardLocation =1
			group by mc.Owner) ctl on rb.RobotID = ctl.Owner
		set CardsDealt = ctl.gctl, CardsPlayed = "0,0,0,0,0";        
        
	else


		
        
		OPEN curs;
		SET bDone = 0;
		REPEAT
			FETCH curs INTO damage,robot;

			if damage > 4 then
				set LastLockedCard = 10 - damage;
                
                
                select Count(CardID) into NewCardCount from MoveCards where `Owner` = robot;
                if NewCardCount < 5 then
					Update MoveCards set `Owner` = robot, Random=1 where `Owner` = -1 order by CurrentOrder, CardID limit 5;
					call procGameFillPrograms();
                end if;
			else
				set LastLockedCard = 6;
			end if;
			
			
			Update MoveCards set MoveCards.`Locked` = 1 where `Owner` = robot and PhasePlayed >= LastLockedCard and PhasePlayed < 6;
		UNTIL bDone END REPEAT;
		CLOSE curs;    

		Update MoveCards Set MoveCards.CurrentOrder = ROUND(500.0 * RAND() ), `Owner` = -1, PhasePlayed = -1, Random = 0  where MoveCards.`Locked` = 0 and PhasePlayed < 6;
		
		
		
		OPEN curs;
		SET bDone = 0;
		REPEAT
			FETCH curs INTO damage,robot;

			set lOptionCards = 0;
            Select ifnull(Sum(Quantity),0) into lOptionCards from RobotOptions where RobotID = robot and (OptionID=16); 
            
			set NewCardCount = 9 - damage + lOptionCards;
            
			if bDone = 0 then 

				while NewCardCount > 0 do
					Update MoveCards set `Owner` = robot where `Owner` = -1 order by CurrentOrder, CardID limit 1;
					set NewCardCount = NewCardCount -1 ;
				end while;
			end if;
			
		UNTIL bDone END REPEAT;
		CLOSE curs;    
		
		call procMoveCardsCheckProgrammed();
	end if ;

END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procProcessOption`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procProcessOption`(IN p_OptionID int, IN p_RobotID int)
BEGIN

declare rowsout int;

	CASE p_OptionID
	WHEN 58 THEN 
		
		Update Robots set ShutDown=2, Damage=0 where RobotID=p_RobotID; 
	WHEN 39 THEN 
		Update MoveCards set `Owner` = -2 where `Owner` = p_RobotID and Locked = 0; 
        
        set rowsout = ROW_COUNT();
		Update MoveCards set `Owner` = p_RobotID where `Owner` = -1 order by CurrentOrder, CardID limit rowsout;
		Update MoveCards set `Owner` = -1, CurrentOrder = CurrentOrder+100 where `Owner` = -2; 
        Update Robots set Damage=Damage+1 where RobotID = p_RobotID;
	end case;

END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procResetGame`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procResetGame`()
BEGIN
    
	DECLARE iGameID INT;
    declare iCols int;
    declare iRows int;
    declare iFlags int;
    declare iPlayers int;
    declare iBoardID int;
    
    
    
    
    
    select iValue into iGameID from CurrentGameData where sKey = 'GameDataID';
    
    update CurrentGameData inner join GameData on GameData.GameDataID = iGameID
		set CurrentGameData.iValue = 
			if (CurrentGameData.sKey = 'GameType',GameData.GameType,
				if (CurrentGameData.sKey = 'LaserDamage',GameData.LaserDamage,
					if(CurrentGameData.sKey = 'PhaseCount',GameData.PhaseCount,
						if(CurrentGameData.sKey = 'BoardID',GameData.BoardID,
							if(CurrentGameData.sKey = 'BoardCols',GameData.BoardCols,
								if(CurrentGameData.sKey = 'BoardRows',GameData.BoardRows,
									if(CurrentGameData.sKey = 'OptionCount',GameData.OptionCount,
										if(CurrentGameData.sKey = 'TotalFlags',GameData.TotalFlags,
											if(CurrentGameData.sKey = 'RulesVersion',GameData.RulesVersion,
												if(CurrentGameData.sKey = 'PlayerListID',GameData.PlayerListID,
													CurrentGameData.iValue
												)
											)
										)
									)
								)
							)
						)
					)
                )
			);

      
            
    delete from MoveCards;
    delete from CommandList;
    delete from RobotOptions;
    delete from StatusLEDs;
    
    
    Delete from Robots;
    

END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procResetPlayers`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procResetPlayers`()
BEGIN


	declare useDamage INT;
    declare pcnd INT;
    declare RulesVersion INT;
    
	select iValue into useDamage from CurrentGameData where sKey="LaserDamage" ;
	
	select iValue into RulesVersion from CurrentGameData where sKey="RulesVersion" ;
    
    set useDamage = useDamage * 2;

	
	update Robots inner join RobotShutDown on Robots.`ShutDown`=RobotShutDown.ShutDownID 
    Set `ShutDown` =  NextState 
    where Robots.`ShutDown` > 0;    
    
    
    Update Robots inner join RobotOptions on Robots.RobotID = RobotOptions.RobotID and RobotOptions.OptionID = 9 set ShutDown = 4 where Damage >= 3;
    
    update Robots set Status=2 where ShutDown=0;

	Update Robots set Damage = 10, ShutDown=0, Lives = Lives -1, Status=11
		where Damage > 9 or Status=11;
        
	Update Robots set Lives=1 where pcnd=1 and Lives=0;
    
    
    Update MoveCards inner join Robots on MoveCards.Owner = Robots.RobotID 
		set PhasePlayed = 0, Owner = -1 where PhasePlayed > 5 and (Robots.Status = 11 or Robots.ShutDown > 0);


	Update Robots inner join RobotOptions on Robots.RobotID = RobotOptions.RobotID and RobotOptions.OptionID = 49
		set Damage=0, 
		ShutDown=0, 
		CurrentPosRow=ArchivePosRow, CurrentPosCol=ArchivePosCol, CurrentPosDir=ArchivePosDir, 
        Status=1, 
		PositionValid=0
		where Status=11 and Lives > 0;
	
    
	Update Robots 
		set Damage=useDamage, 
		ShutDown=0, 
		CurrentPosRow=ArchivePosRow, CurrentPosCol=ArchivePosCol, CurrentPosDir=ArchivePosDir, 
        Status=1, 
		PositionValid=0
		where Status=11 and Lives > 0;
        
	Update RobotOptions set PhasePlayed = 0;


END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procRobotConnectionStatus`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procRobotConnectionStatus`(IN p_Robot int, in p_connection int)
BEGIN
	DECLARE cCommandID INT;




	select CommandID
		into cCommandID
		from CommandList where CommandTypeID = p_connection and RobotID=p_Robot;


	if (cCommandID is null) then 
		insert into CommandList (GameDataID, Turn, Phase, CommandSequence,  
                 CommandTypeID, RobotID, StatusID, Description) 
                 values (1, 0, 0, p_Robot, p_connection, p_Robot, 1, "Connection");
                 
		select CommandID
			into cCommandID
			from CommandList where CommandTypeID = p_connection and RobotID=p_Robot;
            
    end if;


	call procCommandUpdateStatus(cCommandID, 2);
	
END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procSetRobotDirection`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procSetRobotDirection`(p_RobotID int,p_Direction int)
BEGIN
    update Robots set PositionValid=1, CurrentPosDir=p_Direction where RobotID = p_Robot;	
END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procSetStatus`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procSetStatus`()
BEGIN








        
Update StatusLEDs 
	inner join viewRobots vr on StatusLEDs.LEDID = vr.RobotID
	set StatusLEDs.Color=vr.LEDColor;
    
	
    

Update StatusLEDs
	inner join viewRobots vr on StatusLEDs.LEDID = vr.RobotID
	set Color='FF0000'
	where PositionValid=0;

Update StatusLEDs
	inner join CommandList cl on StatusLEDs.LEDID = cl.RobotID
	set Color='FF8800'
    
	
    
	where CommandTypeID=70 and StatusID=7;


END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procTestActiveRobots`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procTestActiveRobots`()
BEGIN
  DECLARE done BOOLEAN DEFAULT FALSE;
  DECLARE _id BIGINT UNSIGNED;
  DECLARE cur CURSOR FOR SELECT RobotID FROM Robots;
  DECLARE CONTINUE HANDLER FOR NOT FOUND SET done := TRUE;

  OPEN cur;

  testLoop: LOOP
    FETCH cur INTO _id;
    IF done THEN
      LEAVE testLoop;
    END IF;
    
    call procRobotConnectionStatus(_id,70);
    
  END LOOP testLoop;

  CLOSE cur;
  
  
  
  
  
  
  
  
	DO SLEEP(2);

	
    
    
    
    
    
    
END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procUpdateCardPlayed`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procUpdateCardPlayed`(IN p_Player int , in p_CardTypeID int, in p_PhasePlayed int)
BEGIN
    DECLARE inProgramming INT;
    DECLARE NewStatus INT;
    DECLARE PhaseCount INT;
    DECLARE ProgramCount INT;
    DECLARE TargetPhase int;
    declare vCardID int;
    
	select Programming from Robots inner join RobotStatus on Robots.Status = RobotStatus.RobotStatusID 
		where Robots.RobotID=p_Player into inProgramming;
		
	if inProgramming = 1 then 
		set vCardID = -1;
		select iValue into PhaseCount from CurrentGameData where sKey="PhaseCount" ;
        
        if p_PhasePlayed = -1 then 
            Select min(pc.ID) into p_PhasePlayed from PhaseCounter pc left join MoveCards mc on pc.ID = mc.PhasePlayed and mc.Owner = p_Player where mc.CardTypeID is null; 
            
            if p_PhasePlayed is null or p_PhasePlayed > PhaseCount then 
				set p_PhasePlayed = -1;
                set p_CardTypeID = 0;
            end if;
        end if;
        
        if p_CardTypeID > 0 then
			select min(CardID) into vCardID from MoveCards where `Owner`= p_Player and CardLocation=1 and CardTypeID=p_CardTypeID;
            if vCardID is null then 
				set vCardID = -1;
			end if;
        end if;
        
		
		Update MoveCards set PhasePlayed = -1,CardLocation = 1 where `Owner` = p_Player and PhasePlayed=p_PhasePlayed and CardLocation=2;
		Update MoveCards set PhasePlayed = p_PhasePlayed, CardLocation=2 where `Owner` = p_Player and  CardID = vCardID and CardLocation=1 ;
		
		
        set NewStatus = 3;
        
		select count(*) as p into ProgramCount from MoveCards where `Owner` = p_Player and CardLocation = 2 ;
        if PhaseCount = ProgramCount then
			set NewStatus = 4;
        end if;
        
        call procUpdateRobotCards(p_Player);
                
		Update Robots set `Status` = NewStatus where RobotID = p_Player;
        
	end if ;
END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procUpdatePlayerPriority`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procUpdatePlayerPriority`()
BEGIN
	declare robotCount int;
    
	
	Update Robots set Priority = Priority - 1;
	select count(RobotID) from Robots into robotCount;
	
	Update Robots set Priority = robotCount where Priority = 0;

END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procUpdateRobotCards`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procUpdateRobotCards`( in p_Player int)
BEGIN
    DECLARE sCardsDealt varchar(30);
    DECLARE sCardsPlayed varchar(20);

	select GROUP_CONCAT(mc.CardTypeID order by mc.CardTypeID desc)  into sCardsDealt
	from MoveCards mc 
	where mc.CardLocation =1 and owner =p_Player
    group by owner
    ;
	   
    select GROUP_CONCAT(IFNULL( mcs.CardTypeID,0) order by pc.ID  )  into sCardsPlayed 
    from PhaseCounter pc 
     left join (select * from MoveCards mc where `Owner` = p_Player) mcs on pc.ID = mcs.PhasePlayed
    # group by owner
     ;
     
	Update Robots set CardsDealt = sCardsDealt, CardsPlayed = sCardsPlayed where RobotID = p_Player;

END ;;
DELIMITER ;

DROP PROCEDURE IF EXISTS `procVerifyPosition`;
DELIMITER $$
CREATE DEFINER=`mrr`@`%` PROCEDURE `procVerifyPosition`(IN p_Robot int)
BEGIN
	
	
	declare Passed INT;
    declare invalid int ;
    declare posRow int ;
    declare posCol int ;
    declare posDir int ;
    
    Set Passed = 1;
    
	Select CurrentPosRow, CurrentPosCol, CurrentPosDir into posRow, posCol, posDir from Robots  where RobotID = p_Robot ;
    Select count(RobotID)  into invalid from Robots where CurrentPosRow = posRow && CurrentPosCol = posCol;
    
    if (posDir=0 || posRow = 0 || posCol = 0 || invalid > 1) then
		set Passed = 0;
	end if ;
    
    update Robots set PositionValid=Passed where RobotID = p_Robot;
END ;;
DELIMITER ;

-- ===== TRIGGERS =====

DROP TRIGGER IF EXISTS `CommandList_BEFORE_INSERT`;
DELIMITER $$
CREATE TRIGGER `CommandList_BEFORE_INSERT` BEFORE INSERT ON `CommandList` FOR EACH ROW
BEGIN
    DECLARE cResult INT;
    Select max(CommandID) + 1 into cResult from CommandList;
    if (cResult is null) then
        begin
        end;
    else
        Set New.CommandID = cResult;
    end if;
END $$
DELIMITER ;

DROP TRIGGER IF EXISTS `CurrentGameData_BEFORE_UPDATE`;
DELIMITER $$
CREATE TRIGGER `CurrentGameData_BEFORE_UPDATE` BEFORE UPDATE ON `CurrentGameData` FOR EACH ROW
BEGIN
    declare sMessage varchar(45);
    if New.sKey = "GameState" or new.iKey = 10 then
        if new.sValue <> old.sValue then
            begin
            end;
        else
            select ButtonText into sMessage from GameState where GameStateID=new.iValue;
            set new.sValue = sMessage;
        end if;
    elseif new.sKey = "GameType" or new.iKey = 1 then
        select Description into sMessage from GameTypes where GameType=new.iValue;
        set new.sValue = sMessage;
    elseif new.sKey = "BoardID" or new.iKey = 20 then
        select BoardName into sMessage from Boards where BoardID=new.iValue;
        set new.sValue = sMessage;
    end if;
END $$
DELIMITER ;

DROP TRIGGER IF EXISTS `GameData_BEFORE_UPDATE`;
DELIMITER $$
CREATE TRIGGER `GameData_BEFORE_UPDATE` BEFORE UPDATE ON `GameData` FOR EACH ROW
BEGIN
    declare sBoardName varchar(45);
    declare iGameType int;
    declare iLaser int;
    declare iPhaseCount int;
    declare iPlayers int;
    declare iTotalFlags int;
    declare iBoardCols int;
    declare iBoardRows int;
    if new.BoardID <> old.BoardID or (old.BoardID is null and new.BoardID is not null) then
        Select GameType,BoardName,LaserDamage,PhaseCount,Players,TotalFlags,X,Y
            into iGameType,sBoardName,iLaser,iPhaseCount,iPlayers,iTotalFlags,iBoardCols,iBoardRows
            from Boards where BoardID = new.BoardID;
        set new.LaserDamage = iLaser;
        set new.BoardName = sBoardName;
        set new.GameType = iGameType;
        set new.PhaseCount = iPhaseCount;
        set new.TotalFlags = iTotalFlags;
        set new.BoardCols = iBoardCols;
        set new.BoardRows = iBoardRows;
    end if;
END $$
DELIMITER ;

DROP TRIGGER IF EXISTS `Robots_BEFORE_UPDATE`;
DELIMITER $$
CREATE TRIGGER `Robots_BEFORE_UPDATE` BEFORE UPDATE ON `Robots` FOR EACH ROW
BEGIN
    if NEW.Damage > 9 then
        Set NEW.`Status` = 11;
        set new.ShutDown = 0;
    end if;
    if New.ShutDown = 4 then
        Set New.Damage = 0;
        set New.ShutDown = 2;
    end if;
    IF NEW.`ShutDown` = 2 THEN
        Set NEW.`Status` = 9;
    END IF;
END $$
DELIMITER ;

DROP TRIGGER IF EXISTS `Robots_AFTER_UPDATE`;
DELIMITER $$
CREATE TRIGGER `Robots_AFTER_UPDATE` AFTER UPDATE ON `Robots` FOR EACH ROW
BEGIN
    call procSetStatus();
END $$
DELIMITER ;

DROP TRIGGER IF EXISTS `StatusLEDs_BEFORE_UPDATE`;
DELIMITER $$
CREATE TRIGGER `StatusLEDs_BEFORE_UPDATE` BEFORE UPDATE ON `StatusLEDs` FOR EACH ROW
BEGIN
    if new.Color<>'' then
        set New.R=conv(substring(New.Color,1,2),16,10);
        set New.G=conv(substring(New.Color,3,2),16,10);
        set New.B=conv(substring(New.Color,5,2),16,10);
    end if;
END $$
DELIMITER ;

-- ===== USERS =====

CREATE USER IF NOT EXISTS 'mrr'@'%' IDENTIFIED BY 'rallypass';
GRANT ALL PRIVILEGES ON rally.* TO 'mrr'@'%';
FLUSH PRIVILEGES;

-- ===== RESTORE SESSION VARIABLES =====

SET TIME_ZONE=@OLD_TIME_ZONE;
SET SQL_MODE=@OLD_SQL_MODE;
SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS;
SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS;
SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT;
SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS;
SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION;
SET SQL_NOTES=@OLD_SQL_NOTES;

-- MRRDatabase.sql complete
