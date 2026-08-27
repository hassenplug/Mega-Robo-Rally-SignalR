# Game Operation — Required Controls

Commands that need to happen for a game.

## Pre-Event

- [ ] Design boards
- [ ] Print packing list for tiles
- [ ] Edit preconfigured games (GameData)

## Pre-Game

- [ ] Test connection to robots
  - [ ] Search for robots to connect to and update IP
- [ ] Pick a pre-created game from a list (pick GameData)
- [ ] Start the game (load that game & start)
- [ ] Auto-connect to robots

## During Game

- [ ] A way to reconnect to robots
- **Program phase**
  - [ ] Show current status of players — cards programmed
- **Run phase**
  - [ ] Show current command (skip)
  - [ ] Next phase
- **End the current game**
  - [ ] Set flag in `CurrentGameData`: `GameInProgress = false`
  - [ ] Disconnect from robots (turn off & disconnect)

## Open Requirements

- GM screen needs a way to end the current game.
- `CurrentGameData` should have a flag to determine:
  - Whether a game is currently in progress (and whether we need to connect to the robots)
  - What should be displayed on the player interface (e.g. "Game setup in progress")


  Using the IsRunning flag in CurrentGameData
  When the pi boots, or app starts, if IsRunning, connect to the robots, and store that robots are connected.
  When IsRunning is turned off, disconnect from robots
  When a game is started, IsRunning should be turned on
  
Removing AllPlayers from main code

The latest code polls the db and directly sends that to the players, so we don't need to keep a current version of AllPlayers in memory, except during the process of creating commands

Create a design doccument for removing AllPlayers from all other places, and identify any place where it is still needed

## Robot Connection Screen

- [ ] Header buttons
  - [ ] Connect All
  - [ ] Disconnect All
  - [ ] Search

  - [ ] Update IP
- [ ] Show rows for all robots
  - [ ] Button with Robot Name and colored background - Button will toggle connection
    - [ ] Red (not connected)
    - [ ] Yellow (Connecting)
    - [ ] Green (Connected)
    - [ ] Purple (Searching)
