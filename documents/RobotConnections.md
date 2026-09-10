# Robot Connections — One Registry, Mode-Appropriate Access

**Status:** Implemented — landed as part of the Device Gateway split
(API_DECOMPOSITION_DESIGN.md §5.5, step 6) and the `AllPlayers` cache removal
(ALLPLAYERS_REMOVAL_DESIGN.md). Updated 2026-09-10 to record a second pass: `RobotConnection`
now polls the database for its own name/color/address instead of being handed them, connects
itself exactly once from its own constructor, and is replaced (never re-dialed in place) to
reconnect — which is also how a new game guarantees no robot carries a stale socket forward.
**Date:** 2026-09-10
**Related:** [ALLPLAYERS_REMOVAL_DESIGN.md](ALLPLAYERS_REMOVAL_DESIGN.md),
[API_DECOMPOSITION_DESIGN.md](API_DECOMPOSITION_DESIGN.md) §5.5,
[MRR/Devices/RobotConnections.cs](../MRR/Devices/RobotConnections.cs),
[MRR/Devices/RobotConnection.cs](../MRR/Devices/RobotConnection.cs)

## The rule

`RobotConnections` ([MRR/Devices/RobotConnections.cs](../MRR/Devices/RobotConnections.cs)) is
the **only** place a robot's live WebSocket transport is created or torn down. It is
registered as a singleton ([Program.cs:17](../MRR/Program.cs#L17)) and holds one
`RobotConnection` per robot, keyed on `RobotID`. Nothing else opens a `ClientWebSocket` to
an AIM robot — `Player` never owns a socket; it only forwards to whichever
`RobotConnection` is attached to it (`Player.Connection`,
[Players.cs:83](../MRR/Players.cs#L83)).

A `RobotConnection` connects itself: its constructor takes nothing but a `RobotID` and a
connection string, polls `Robots` (joined to `RobotBodies`/`RobotBases`) for its own name,
color, and IP address, and then dials the robot
([RobotConnection.cs:24-57](../MRR/Devices/RobotConnection.cs#L24)). `ConnectAsync` is
private for exactly this reason — it is never called a second time on a live object. To
reconnect a robot, `RobotConnections.Reconnect`/`ReconnectAll`
([RobotConnections.cs:76-125](../MRR/Devices/RobotConnections.cs#L76)) dispose the old
`RobotConnection` and construct a fresh one in its place.

`RobotConnections.Refresh()`, called on every `GetAllPlayers()`, only prunes: a robot no
longer in the `Robots` roster is disposed and dropped
([RobotConnections.cs:35-56](../MRR/Devices/RobotConnections.cs#L35)). It never creates an
entry for a robot it hasn't seen — a robot is only ever connected through an explicit
`Reconnect`/`ReconnectAll` call, never as a side effect of rebuilding the player list on a
broadcast. A robot already registered keeps its connection untouched, including a live
socket.

## The four situations

### 1. Programming mode — direct access to the connected list

While robots are being programmed (state 4) the phone action endpoint updates a robot's
status LED the instant a card is played or removed, by reaching straight into the
connection-backed roster:

```csharp
// Program.cs:177
dataService.AllPlayers.GetPlayer(pid)?.UpdateStatusLEDs();
```

`UpdateStatusLEDs()` → `SendColorStatus()` ([Players.cs:232](../MRR/Players.cs#L232),
[:110](../MRR/Players.cs#L110)) calls `Connection.SetLedAsync(...)` directly — no queued
command, no intermediate list. The same list is used again at the top of every turn (state
2) to reset every robot's status LED: `foreach (var p in AllPlayers) p.UpdateStatusLEDs();`
([GameController.cs:355](../MRR/GameController.cs#L355)).

### 2. Simulation mode — a list with no connections

Turn planning (state 6, `CreateCommands.ExecuteTurn()` in `MRR.Rules`) runs entirely on
`PlayerState` copies, not `Player` objects, so there is nothing to attach a socket to:

- `DataService.BuildTurnRequest()` populates `TurnRequest.Players` from
  `GetPlayerStatesFromDB()` ([DataService.Commands.cs:55](../MRR/DataService.Commands.cs#L55))
  — a direct `Robots` query into bare `PlayerState` rows
  ([DataService.Players.cs:142-191](../MRR/DataService.Players.cs#L142)), independent of the
  connection registry.
- `CreateCommands` deep-copies that into `workingPlayers` for the physics/collision
  simulation ([CreateCommands.cs:514](../MRR.Rules/CreateCommands.cs#L514)) and discards the
  copy once the turn's commands are built.
- `MRR.Rules` cannot reference `MySqlConnector` or any WebSocket type — enforced by the
  project's own reference list — so this list is structurally incapable of holding a robot
  connection, not just conventionally free of one.

### 3. Processing mode — the connected list

Command execution (states 7/8, `CommandProcess.cs`) looks the robot up in the same
connection-backed roster used by programming mode, checks `isConnected`, and sends the
command over the live socket:

```csharp
// CommandProcess.cs:289, 304
var robot = _dataService.AllPlayers.GetPlayer(p => p.ID == onecommand.RobotID);
...
if (!robot.isConnected) { /* mark unreachable, move on */ }
```

`robot.SendRobotCommandAsync(...)` ([CommandProcess.cs:326](../MRR/CommandProcess.cs#L326))
forwards through `Player` to the attached `RobotConnection`, the same object
`RobotConnections` owns.

### 4. Any other time — connected for the life of the game, not longer

Connections are established once a game becomes active and held open until it ends, rather
than opened per-phase and closed again:

- `GameController.LoadCurrentGame()` calls `ConnectToAllRobots()` only when a game is
  actually running and the physical robots are wanted:
  `if (RobotsActive != 0 && IsRunning) ConnectToAllRobots();`
  ([GameController.cs:453](../MRR/GameController.cs#L453)). This runs both at process
  startup (so a Pi reboot mid-game reconnects automatically) and at the end of `StartGame()`.
- **A new game closes every previous connection and opens fresh ones.**
  `ConnectToAllRobots()` ([GameController.cs:471-497](../MRR/GameController.cs#L471)) calls
  `DataService.ReconnectAllRobots()` → `RobotConnections.ReconnectAll()`, which disposes
  every current `RobotConnection` and constructs a brand-new, self-connecting one for each
  robot in the roster — never reuses an old socket, LED state, or LCD screen across a game
  boundary. Because `StartGame()` ends by calling `LoadCurrentGame()`, this reset runs
  automatically every time a game starts.
- Every other state transition and broadcast in between reuses the same
  `RobotConnections` entries — `Refresh()` only prunes robots that disappeared; it never
  disturbs a still-valid connection just because the caller rebuilt its own player list
  around it.
- `EndGame()` clears `IsRunning` and calls `DisconnectAllRobots()`
  ([GameController.cs:657-671](../MRR/GameController.cs#L657)), so connections do not
  outlive the game they belong to.
- The GM's manual "Connect"/"Disconnect" controls (`connectscreen.html`,
  `ConnectToRobot`/`DisconnectRobot`) use the same `Reconnect`/dispose mechanism for the case
  a single robot needs to be reconnected without restarting the game.

## What this document is not

There is a separate, unimplemented proposal in `.claude/agents/gm-ui.md` ("Use Robots
Toggle") for a GM-facing simulation-only mode that skips *all* physical robot traffic for
an entire game (as opposed to the turn-planning simulation in §2 above, which already
exists). That is a UI feature request, not a gap in this connection architecture, and is
out of scope here.

## Verification

- `grep -rn "new ClientWebSocket" MRR/` returns matches only inside
  `RobotConnection.cs` — confirms no second connection path exists.
- Play a card during programming (state 4) and confirm the robot's LED changes
  immediately, before the turn locks.
- Run a turn with a robot deliberately disconnected (unplugged/out of range): confirm
  `CommandProcess` logs "Robot not connected for Command" and the turn still completes for
  the other robots, rather than the disconnect ever reaching the planner
  (`CreateCommands`/`MRR.Rules`).
- Restart the host mid-game with `RobotsActive` on: confirm `LoadCurrentGame()`
  reconnects all robots without the GM having to press Connect manually.
- Start a new game while a robot already has an open connection from the previous game:
  confirm its LCD/LEDs reset (name redrawn, color reapplied) rather than the old screen
  state persisting, and that `RobotID`s not in the new roster are dropped.
- `grep -rn "public.*ConnectAsync" MRR/Devices/RobotConnection.cs` should show it as
  `private` — confirms nothing outside the constructor can call it directly.
