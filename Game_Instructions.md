# Game Operation — Required Controls

Commands that need to happen for a game.  These should all be part of a GM screen

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

