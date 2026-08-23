# Game Operation — Required Controls

Commands that need to happen for a game.

## Pre-Event

- [ ] Design boards
- [ ] Print packing list for tiles
- [ ] Edit preconfigured games (GameData)

## Pre-Game

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
