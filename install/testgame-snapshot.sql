-- Test-game data snapshot: Robots, MoveCards, CurrentGameData.
-- Data only -- the schema (from install/MRRDatabase.sql) must already exist.
--
-- Reimport:
--   mysql -h localhost -u mrr -prallypass rally < install/testgame-snapshot.sql
--
-- Regenerate this file from the live database:
--   mysqldump -h localhost -u mrr -prallypass \
--     --no-create-info --skip-triggers --complete-insert --skip-extended-insert \
--     rally Robots MoveCards CurrentGameData > install/testgame-snapshot.sql
--   (then re-add the three DELETE FROM statements below each table's dump header,
--   as in this file, so reimport is idempotent)
--
/*M!999999\- enable the sandbox mode */
-- MariaDB dump 10.19-11.8.6-MariaDB, for debian-linux-gnu (aarch64)
--
-- Host: localhost    Database: rally
-- ------------------------------------------------------
-- Server version	11.8.6-MariaDB-0+deb13u1 from Debian

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*M!100616 SET @OLD_NOTE_VERBOSITY=@@NOTE_VERBOSITY, NOTE_VERBOSITY=0 */;

--
-- Dumping data for table `Robots`
--

DELETE FROM `Robots`;
SET @OLD_AUTOCOMMIT=@@AUTOCOMMIT, @@AUTOCOMMIT=0;
LOCK TABLES `Robots` WRITE;
/*!40000 ALTER TABLE `Robots` DISABLE KEYS */;
INSERT INTO `Robots` (`RobotID`, `OperatorName`, `RobotBaseID`, `RobotBodyID`, `CurrentFlag`, `Lives`, `Damage`, `ShutDown`, `PositionValid`, `Computer`, `Score`, `Status`, `CurrentPosRow`, `CurrentPosCol`, `CurrentPosDir`, `ArchivePosRow`, `ArchivePosCol`, `ArchivePosDir`, `ConnectStatusID`, `RobotBatteries`, `PhoneBatteries`, `Priority`, `Password`, `PlayerSeat`, `Energy`, `CardsDealt`, `CardsPlayed`, `CardCount`, `MessageCommandID`, `RobotName`, `RobotColor`, `RobotColorFG`, `StatusColor`, `LEDColor`, `PlayerStatus`, `ConnectStatusColor`, `ConnectStatusDesc`, `sDir`, `FlagEnergy`, `DirectionAdjustment`, `StatusToShow`, `PlayerMsg`, `IPAddress`) VALUES (1,'P1',1,1,0,3,0,0,0,0,0,4,5,1,1,5,1,1,20,0,0,1,'0001',1,3,'9,6,2,2','5,7,3,4,2',20,NULL,'Hammerbot','7338B0','FFFFFF','FF0000','FF0000','Inactive','FF0000','Not Conn','^','0/3',1,'X,X,X,X,X',NULL,'192.168.1.149');
INSERT INTO `Robots` (`RobotID`, `OperatorName`, `RobotBaseID`, `RobotBodyID`, `CurrentFlag`, `Lives`, `Damage`, `ShutDown`, `PositionValid`, `Computer`, `Score`, `Status`, `CurrentPosRow`, `CurrentPosCol`, `CurrentPosDir`, `ArchivePosRow`, `ArchivePosCol`, `ArchivePosDir`, `ConnectStatusID`, `RobotBatteries`, `PhoneBatteries`, `Priority`, `Password`, `PlayerSeat`, `Energy`, `CardsDealt`, `CardsPlayed`, `CardCount`, `MessageCommandID`, `RobotName`, `RobotColor`, `RobotColorFG`, `StatusColor`, `LEDColor`, `PlayerStatus`, `ConnectStatusColor`, `ConnectStatusDesc`, `sDir`, `FlagEnergy`, `DirectionAdjustment`, `StatusToShow`, `PlayerMsg`, `IPAddress`) VALUES (2,'P2',2,2,0,3,0,0,0,0,0,2,5,2,1,5,2,1,20,0,0,2,'0002',2,3,'5,5,4,3,3,2,2,2,1','0,0,0,0,0',20,NULL,'Hulk X90','FE0000','FFFFFF','FF0000','FF0000','Inactive','FF0000','Not Conn','^','0/3',1,'-,-,-,-,-',NULL,'192.168.1.206');
INSERT INTO `Robots` (`RobotID`, `OperatorName`, `RobotBaseID`, `RobotBodyID`, `CurrentFlag`, `Lives`, `Damage`, `ShutDown`, `PositionValid`, `Computer`, `Score`, `Status`, `CurrentPosRow`, `CurrentPosCol`, `CurrentPosDir`, `ArchivePosRow`, `ArchivePosCol`, `ArchivePosDir`, `ConnectStatusID`, `RobotBatteries`, `PhoneBatteries`, `Priority`, `Password`, `PlayerSeat`, `Energy`, `CardsDealt`, `CardsPlayed`, `CardCount`, `MessageCommandID`, `RobotName`, `RobotColor`, `RobotColorFG`, `StatusColor`, `LEDColor`, `PlayerStatus`, `ConnectStatusColor`, `ConnectStatusDesc`, `sDir`, `FlagEnergy`, `DirectionAdjustment`, `StatusToShow`, `PlayerMsg`, `IPAddress`) VALUES (3,'P3',3,3,0,3,0,0,0,0,0,4,6,5,1,6,5,1,20,0,0,3,'0003',3,3,'9,8,3,3','5,6,2,6,5',20,NULL,'Smashbot','FFE733','000000','FF0000','FF0000','Inactive','FF0000','Not Conn','^','0/3',1,'X,X,X,X,X',NULL,'192.168.1.106');
INSERT INTO `Robots` (`RobotID`, `OperatorName`, `RobotBaseID`, `RobotBodyID`, `CurrentFlag`, `Lives`, `Damage`, `ShutDown`, `PositionValid`, `Computer`, `Score`, `Status`, `CurrentPosRow`, `CurrentPosCol`, `CurrentPosDir`, `ArchivePosRow`, `ArchivePosCol`, `ArchivePosDir`, `ConnectStatusID`, `RobotBatteries`, `PhoneBatteries`, `Priority`, `Password`, `PlayerSeat`, `Energy`, `CardsDealt`, `CardsPlayed`, `CardCount`, `MessageCommandID`, `RobotName`, `RobotColor`, `RobotColorFG`, `StatusColor`, `LEDColor`, `PlayerStatus`, `ConnectStatusColor`, `ConnectStatusDesc`, `sDir`, `FlagEnergy`, `DirectionAdjustment`, `StatusToShow`, `PlayerMsg`, `IPAddress`) VALUES (4,'P4',4,4,0,3,0,0,0,0,0,4,6,6,1,6,6,1,20,0,0,4,'0004',4,3,'9,8,2,2','7,2,5,6,3',20,NULL,'Spinbot','0000FF','FFFFFF','FF0000','FF0000','Inactive','FF0000','Not Conn','^','0/3',2,'X,X,X,X,X',NULL,'192.168.1.215');
/*!40000 ALTER TABLE `Robots` ENABLE KEYS */;
UNLOCK TABLES;
COMMIT;
SET AUTOCOMMIT=@OLD_AUTOCOMMIT;

--
-- Dumping data for table `MoveCards`
--

DELETE FROM `MoveCards`;
SET @OLD_AUTOCOMMIT=@@AUTOCOMMIT, @@AUTOCOMMIT=0;
LOCK TABLES `MoveCards` WRITE;
/*!40000 ALTER TABLE `MoveCards` DISABLE KEYS */;
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (1,1,1,0,0,1773,10,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (1,1,2,0,0,1612,5,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (1,1,3,0,0,1739,13,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (1,1,4,0,0,1857,12,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (2,2,1,5,0,1571,2,0,2);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (2,2,2,0,0,1785,12,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (2,2,3,0,0,1710,10,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (2,2,4,2,0,1697,8,0,2);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (3,2,1,0,0,1854,14,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (3,2,2,0,0,1680,9,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (3,2,3,0,0,1837,16,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (3,2,4,0,0,1647,6,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (4,2,1,0,0,1724,5,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (4,2,2,0,0,1677,8,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (4,2,3,0,0,1713,11,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (4,2,4,0,0,1533,2,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (5,2,1,0,0,1528,1,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (5,2,2,0,0,1540,2,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (5,2,3,3,0,1617,3,0,2);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (5,2,4,0,0,1965,16,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (6,3,1,0,0,1974,19,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (6,3,2,0,0,1975,20,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (6,3,3,0,0,1952,19,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (6,3,4,0,0,1837,11,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (7,3,1,0,0,1829,12,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (7,3,2,0,0,1633,7,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (7,3,3,0,0,1678,7,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (7,3,4,0,0,1991,18,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (8,3,1,0,0,1919,17,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (8,3,2,0,0,1625,6,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (8,3,3,0,0,1869,17,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (8,3,4,0,0,1968,17,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (9,3,1,3,0,1732,7,0,2);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (9,3,2,0,0,1757,10,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (9,3,3,0,0,1590,2,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (9,3,4,5,0,1677,7,0,2);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (10,4,1,4,0,1617,3,0,2);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (10,4,2,0,0,1553,3,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (10,4,3,0,0,1916,18,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (10,4,4,0,0,1921,13,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (11,5,1,0,0,1856,15,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (11,5,2,0,0,1515,1,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (11,5,3,1,0,1509,1,0,2);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (11,5,4,0,0,1998,20,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (12,5,1,0,0,1964,18,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (12,5,2,0,0,1825,15,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (12,5,3,0,0,1736,12,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (12,5,4,3,0,1702,9,0,2);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (13,5,1,0,0,1803,11,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (13,5,2,0,0,1908,17,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (13,5,3,5,0,1631,4,0,2);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (13,5,4,0,0,1933,14,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (14,5,1,1,0,1772,9,0,2);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (14,5,2,0,0,1561,4,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (14,5,3,0,0,1989,20,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (14,5,4,0,0,1763,10,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (15,6,1,0,0,1846,13,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (15,6,2,0,0,1940,18,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (15,6,3,2,0,1663,5,0,2);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (15,6,4,0,0,1995,19,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (16,6,1,0,0,1986,20,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (16,6,2,0,0,1946,19,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (16,6,3,0,0,1771,14,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (16,6,4,4,0,1516,1,0,2);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (17,6,1,0,0,1769,8,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (17,6,2,0,0,1794,14,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (17,6,3,4,0,1665,6,0,2);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (17,6,4,0,0,1944,15,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (18,7,1,2,0,1725,6,0,2);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (18,7,2,0,0,1792,13,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (18,7,3,0,0,1784,15,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (18,7,4,1,0,1547,4,0,2);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (19,8,1,0,0,1882,16,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (19,8,2,0,0,1767,11,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (19,8,3,0,0,1689,8,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (19,8,4,0,0,1644,5,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (20,9,1,0,0,1653,4,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (20,9,2,0,0,1833,16,0,0);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (20,9,3,0,0,1707,9,0,1);
INSERT INTO `MoveCards` (`CardID`, `CardTypeID`, `Owner`, `PhasePlayed`, `Locked`, `Random`, `CurrentOrder`, `Executed`, `CardLocation`) VALUES (20,9,4,0,0,1536,3,0,1);
/*!40000 ALTER TABLE `MoveCards` ENABLE KEYS */;
UNLOCK TABLES;
COMMIT;
SET AUTOCOMMIT=@OLD_AUTOCOMMIT;

--
-- Dumping data for table `CurrentGameData`
--

DELETE FROM `CurrentGameData`;
SET @OLD_AUTOCOMMIT=@@AUTOCOMMIT, @@AUTOCOMMIT=0;
LOCK TABLES `CurrentGameData` WRITE;
/*!40000 ALTER TABLE `CurrentGameData` DISABLE KEYS */;
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('BoardCols',8,NULL,'Game',18);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('BoardID',2,'3x3x2x2','Game',20);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('BoardRows',8,NULL,'Game',19);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('Command',0,'none','x',4);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('CommandParameter',0,NULL,'Status',13);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('GameDataID',1,NULL,'Config',26);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('GameState',4,'Next Turn','Status',10);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('GameType',1,'Standard','Game',1);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('IsRunning',1,NULL,'Toggle',9);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('LaserDamage',1,NULL,'Game',6);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('LastUpdateTime',0,'1/1/70','Status',14);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('MaxDamage',10,NULL,'Game',17);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('Message',0,'Status Message','Status',28);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('OptionCount',-1,NULL,'Game',22);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('Phase',0,NULL,'Status',3);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('PhaseCount',5,'Added 130 commands','Game',16);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('PlayerListID',1,NULL,'Config',25);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('Players',5,NULL,'Game',23);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('ProgramsReady',0,NULL,'Status',11);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('RobotsActive',1,NULL,'Toggle',8);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('RobotsReady',0,NULL,'Status',12);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('SubCommand',0,NULL,'x',5);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('TotalFlags',4,NULL,'Game',7);
INSERT INTO `CurrentGameData` (`sKey`, `iValue`, `sValue`, `Category`, `iKey`) VALUES ('Turn',1,NULL,'Status',2);
/*!40000 ALTER TABLE `CurrentGameData` ENABLE KEYS */;
UNLOCK TABLES;
COMMIT;
SET AUTOCOMMIT=@OLD_AUTOCOMMIT;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*M!100616 SET NOTE_VERBOSITY=@OLD_NOTE_VERBOSITY */;

-- Dump completed on 2026-09-07 20:01:16
