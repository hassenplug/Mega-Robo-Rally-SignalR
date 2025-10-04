use rally;

#select TABLE_NAME  from information_schema.TABLES t where TABLE_SCHEMA ="rally" order by TABLE_TYPE , TABLE_NAME  ;

#preset parameters
Update GameData set BoardID=85,PlayerListID=2 where GameDataID=2;

#update CurrentGameData set iValue = 2 where sKey='GameDataID';
update CurrentGameData set iValue = 1 where sKey='RulesVersion';
#update CurrentGameData set iValue = 0 where sKey='GameState';

Select * from OperatorData;

#Start Game
call procGameStart(2);  #start game #2

#Check Cards
Select CardsDealt,vr.* from viewRobots vr ;


#verify robots
#update Robots set PositionValid=0;

#next
select funcGetNextGameState(); # should return state 4

#play first 5 cards
update MoveCards set PhasePlayed = -1;

update MoveCards set PhasePlayed = CurrentOrder where CurrentOrder < 6;  # autoprogram

#show cards played
Select * from viewRobots vr ;


#select * from CurrentGameData cgd where iKey=10;

call procMoveCardsCheckProgrammed();

#next
select funcGetNextGameState(); # should return state 4

# run mono here
select * from CurrentGameData;


call procUpdateCardPlayed(1,2,-1);