use rally;

#preset parameters
Update GameData set BoardID=85,PlayerListID=2 where GameDataID=2;

update CurrentGameData set iValue = 1 where sKey='RulesVersion';

#Start Game
call procGameStart(2);  #start game #2

#verify robots
#update Robots set PositionValid=0;

update Robots set PositionValid=1;

#Update Robots set MessageCommandID = 1;

#next
select funcGetNextGameState(); # should return state 4

#play first 5 cards
update MoveCards set PhasePlayed = -1;

update MoveCards set PhasePlayed = CurrentOrder where CurrentOrder < 6;  # autoprogram

call procMoveCardsCheckProgrammed();

#next
select funcGetNextGameState(); # should return state 4

call procUpdateCardPlayed(1,2,-1);


select * from viewRobots where RobotID=1;


