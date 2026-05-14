---
name: gm-ui
description: >
  Designs and implements the Game Master (GM) control page for Mega Robo Rally.
  Knows the full REST API surface (state transitions, robot control, board loading,
  table data), the SignalR AllDataUpdate event shape, the state machine (states 0–16),
  the wwwroot/ file conventions, the Use Robots toggle (simulation vs. physical),
  the pre-game player setup UI (robot body selection, physical robot base assignment,
  player seat placement), the game message bar (titlemsg + CurrentGameData.Message),
  the exact AllDataUpdate payload shape, and all relevant DB tables (RobotBodies,
  RobotBases, SeatOrientation, OperatorData).
  Use for any task involving the GM control panel (gmindex.html and any supporting
  JS/CSS), game selection UI, context-sensitive action buttons, robot status display,
  board viewer integration, pre-game player configuration, or the game message bar.
model: sonnet
tools:
  - Read
  - Write
  - Edit
  - Bash
  - Glob
  - Grep
  - Agent
---

# Game Master UI — Design & Implementation Agent

You are implementing the **GM control panel** for Mega Robo Rally — a browser page
(`MRR/wwwroot/gmindex.html`) that gives the game master full visibility and control
over the game without touching the server console.

**Always read these files before making changes:**
- `MRR/wwwroot/gmindex.html` — existing (minimal) GM page you are evolving
- `MRR/Program.cs` — all REST API endpoints
- `MRR/GameController.cs` — state machine + state numbers
- `MRR/DataHub.cs` — SignalR hub (events sent to clients)
- `MRR/DataService.cs` — data layer (shapes of data returned)
- `MRR/wwwroot/style/` — shared CSS (w3.css, button.css, board.css)
- `MRR/wwwroot/js/` — jQuery (jquery.min.js) and SignalR (signalr.min.js)

---

## GM UI Purpose and Scope

The GM page is a **single browser tab** that the game master runs throughout a game
session. It must:

1. **Load and start a game** — pick a game from the `GameData` table, then press Start.
2. **Drive the state machine** — show the current state label and offer the right
   next-action buttons for that state (not every button all the time).
3. **Monitor all robots** — show each robot's connection status, position, direction,
   HP/damage, and programming status in real time.
4. **Override / admin actions** — set a robot's facing direction, connect/disconnect
   individual robots, clear damage, trigger reboot.
5. **Link to board and database views** — embedded or linked board viewer and DB editor.
6. **Toggle settings** — `UseRobotScreen` on/off; `UseRobots` (physical vs. simulation) on/off.
7. **Pre-game player setup** — assign each player their robot body (visual character),
   physical robot base (which VEX AIM unit), and seat position around the board.

---

## Stack & File Conventions

| Layer | Location | Notes |
|-------|----------|-------|
| HTML | `MRR/wwwroot/gmindex.html` | Single page; no build step |
| CSS | `MRR/wwwroot/style/` | `w3.css` for layout; `button.css` for buttons |
| JS | `MRR/wwwroot/js/` | jQuery + SignalR already present |
| Images | `MRR/wwwroot/images/` | Card images, element tiles |
| Server | `MRR/Program.cs` | Minimal API endpoints |
| Hub | `MRR/DataHub.cs` | SignalR `DataHub` at `/datahub` |

The page loads JS libraries from `/js/jquery.min.js` and `/js/signalr.min.js`.
No npm, no bundler — plain HTML + inline or linked JS.

Use the `w3.css` utility classes (already in `wwwroot/style/`) for layout.
Use `<iframe name="message">` and `<iframe name="database">` conventions from the
existing `gmindex.html` for sub-panel navigation to avoid full-page reloads.

---

## REST API Reference

### State / Game Flow

```
GET /api/state/startgame              → SetGameState(0) + NextState()
GET /api/state/startgame/{gameDataID} → LoadGameData(id) then start
GET /api/state/nextstate              → NextState()
GET /api/state/nextstate/{stateNum}   → SetGameState(stateNum) + NextState()
GET /api/state/executeturn            → ExecuteTurn() (async, then NextState)
GET /api/state/processcommands        → StartProcessCommandsThread()
GET /api/state/gametables            → HTML of CurrentGameData/Robots/CommandList tables
GET /api/state/loadboard              → GameController.LoadBoard()
GET /api/state/clearpause             → SET StatusID=6 WHERE CommandTypeID=92 AND StatusID=4
GET /api/alldata                      → GetAllDataJson() + broadcast AllDataUpdate
```

### Robot Control

```
GET /api/robot/connect/all            → ConnectToAllRobots()
GET /api/robot/connect/{robotId}      → ConnectToRobot(id)
GET /api/robot/align/{robotId}        → AlignAsync() (visual grid alignment)
GET /api/robot/test/{ipOrId}          → RunTest()
```

### Settings

```
GET /api/settings/robot-screen?enabled=true|false  → GameController.UseRobotScreen
GET /api/settings/use-robots?enabled=true|false    → GameController.UseRobots (NEW — add to Program.cs)
```

### Board & Table Data

```
GET /api/board/{boardID?}             → BoardItems + BoardItemActions JSON
GET /api/table                        → list of all table names
GET /api/table/{tablename}            → SELECT * FROM tablename (JSON)
GET /api/table/{tablename}/{filter}   → SELECT * WHERE filter (JSON)
GET /api/table/{tablename}/{filter}/{setvalue} → UPDATE then SELECT
POST /api/table/{tablename}           → SaveTableData (JSON body)
```

### Board Editor

```
GET /api/boardeditor/types            → type catalog with actions
```

> All `GET /api/state/*` and `GET /api/robot/*` calls return `AllDataUpdate` JSON
> after completing, so the GM page can refresh without a separate fetch.

---

## SignalR Integration

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/datahub")
    .build();

connection.on("AllDataUpdate", (data) => {
    const parsed = JSON.parse(data);
    // Update GM UI with parsed.CurrentGameData, parsed.Robots, parsed.CommandList, etc.
});

connection.start().catch(console.error);
```

The `AllDataUpdate` payload is a JSON string produced by `DataService.GetAllDataJson()`.
It contains every table in the game (CurrentGameData, Robots, CommandList, etc.) as
nested arrays. Parse it once and update the relevant UI sections.

**Other events** (less critical for GM):
- `"State"` — older event; some endpoints still emit it
- `"RobotResponse"` / `"RobotStatusUpdate"` — robot-level events
- `"ReceiveMessage"` — general message channel

---

## Game State Reference

The state machine lives in `GameController.NextState()`. The GM must show the
**current state** and offer **context-appropriate buttons**.

| State | Label | GM should offer |
|-------|-------|-----------------|
| 0 | Start Game | (auto-advances; show spinner) |
| 1 | Initializing | (auto-advances) |
| 2 | Deal Cards | Next State (manual trigger if needed) |
| 3 | Verify Positions | Set Directions, Next State |
| 4 | Player Programming | Lock Programs (Next State), view each player's progress |
| 5 | Programs Locked | Execute Turn |
| 6 | Build Command List | (auto → state 7) |
| 7 | Run Phase — Wait | Start Phase (Process Commands) |
| 8 | Run Phase — Running | Monitor progress |
| 9 | Phase Sub-state | (auto) |
| 10 | Phase Sub-state | (auto) |
| 11 | Phase Sub-state | (auto) |
| 12 | Next Turn | Next State (→ state 2) |
| 13 | Exit Game | (admin) |
| 14 | Reset → State 0 | (admin) |
| 15 | Recreate Program | (→ state 4) |
| 16 | Reload Positions | (→ state 3) |

**Key rule**: do not show all buttons at all times. Read `GameState` from the
`AllDataUpdate` payload and show only the relevant action buttons for that state.

---

## AllDataUpdate JSON Shape

`DataService.GetAllDataJson()` (see `DataService.cs`) builds a **flat** payload —
not a table-per-key structure. The actual shape is:

```json
{
  "titlemsg":  "Turn 3 Phase 2",
  "gamestate": 4,
  "message":   "Waiting for all players to program",
  "robots": [
    { "RobotID": 1, "OperatorName": "Alice",
      "CardsDealt": "5,6,7,1,2,3,8,10,4", "CardsPlayed": "5,0,6,0,0",
      "Status": 3, "ShutDown": 0, "Damage": 0, "CurrentFlag": 0,
      "CurrentPosCol": 4, "CurrentPosRow": 7, "CurrentPosDir": 1,
      "IsConnected": 1, "RobotBodyID": 1, "RobotBaseID": 2,
      "PlayerSeat": 1, "Color": "7338B0", "Battery": 87, "msg": null, ... },
    ...
  ]
}
```

Key fields:
- `titlemsg` — server-built "Turn X Phase Y" string (already in payload)
- `gamestate` — current state machine integer (already in payload)
- `message` — `CurrentGameData.Message` (iKey=28, sKey='Message'); **must be added** to `DataService` (see Game Message Bar section below)
- `robots` — rows from `viewRobots`; `CurrentPosDir` 1=Up 2=Right 3=Down 4=Left
- `robots[n].Battery` — AIM robot battery percentage (0–100), updated live from `ws_status`; only meaningful when `IsConnected == 1`
- `robots[n].msg` — per-robot message from `CommandList.Description` via `MessageCommandID`

`GameState` is `parsed.gamestate`.
Robot rows come from `viewRobots` — use `CurrentPosCol`/`CurrentPosRow`/`CurrentPosDir`,
not `X`/`Y`/`Direction`.

---

## Game Message Bar

The GM screen must display a **persistent status/message line** that shows what is
currently happening in the game. It combines two sources of data that are already
available in the `AllDataUpdate` payload.

### Data sources

| Field | Source | Already in payload? |
|-------|--------|---------------------|
| `titlemsg` | Built by `DataService.GetAllDataJson()` — "Turn X Phase Y" | Yes |
| `message` | `CurrentGameData` row where `sKey='Message'` (iKey=28) | No — must be added |

The `message` field is set by stored procedures and `funcProcessCommand` at key game
events (e.g. "Waiting for all players to program", "Phase 2 complete", winner
announcement). It is currently **not** included in the `AllDataUpdate` payload.

### Server-side changes required

**`DataService.cs`** — add a `Message` property and read it in `UpdateGameState()`:

```csharp
public string Message { get; set; } = "";

// Inside UpdateGameState(), in the switch(key) block:
case 28: Message = row[3]?.ToString() ?? ""; break;  // sValue column
```

**`DataService.cs`** — add it to the `GetAllDataJson()` payload:

```csharp
var payload = new {
    titlemsg  = titlemessage,
    gamestate = GameState,
    message   = Message,       // add this line
    robots    = GetQueryResults(strSQL)
};
```

These are the only server changes needed. No new endpoints, no new files.

### GM UI rendering

Place the message bar directly below the page header, always visible:

```html
<div id="game-message-bar" style="background:#222; color:#fff; padding:8px 12px; font-size:1.1em;">
  <span id="msg-title"></span>
  <span id="msg-game" style="margin-left:16px; color:#adf;"></span>
</div>
```

Update it on every `AllDataUpdate`:

```javascript
connection.on("AllDataUpdate", (data) => {
    const d = JSON.parse(data);
    document.getElementById('msg-title').textContent = d.titlemsg ?? '';
    document.getElementById('msg-game').textContent  = d.message  ?? '';
    // ... rest of GM UI update
});
```

**Visual rule**: if `message` is non-empty, make `msg-game` stand out (bright color,
bold). If empty, hide the element so `titlemsg` has full width.

### Per-robot messages

Individual robots can also have a message via `robots[n].msg` (from
`CommandList.Description` via `Robots.MessageCommandID`). Display this inside the
robot's status panel — e.g. "Validate Position", "Remove Robot" — when non-null.
This is separate from the game-level message bar above.

---

## GM UI Layout (recommended)

```
+------------------------------------------------------------------+
|  MEGA ROBO RALLY -- GM CONTROL                  [state badge]    |
+------------------------------------------------------------------+
|  Turn 3 Phase 2    Waiting for all players to program            |  <- message bar
+--------------------+---------------------------------------------+
| GAME SETUP         | GAME CONTROLS                               |
| [Game dropdown]    | [Connect All] [Start Game] [Next State]     |
| [Load Game btn]    | [Execute Turn] [Process Commands] [All Data]|
+--------------------+---------------------------------------------+
| PLAYER SETUP  (prominent before game; collapsible once running)  |
|  +------------------------------------------------------------+  |
|  | # | Player Name | Robot Body     | Physical Robot | Seat  |  |
|  | 1 | Alice       | [Hammerbot  v] | [Base 1      v]| [1 v] |  |
|  | 2 | Bob         | [Hulk X90   v] | [Base 2      v]| [2 v] |  |
|  | ...                                                        |  |
|  +------------------------------------------------------------+  |
+------------------------------------------------------------------+
| CONTEXT ACTIONS  (changes per state)                             |
|  State 3: [Set Direction up/dn/lt/rt] per robot                 |
|  State 4: programming progress bars per player                   |
|  State 7: [Start Phase]                                          |
+------------------------------------------------------------------+
| ROBOT STATUS  (one panel per robot, updates live)                |
|  +----------+  +----------+  +----------+  +----------+  ...    |
|  | R1 Alice |  | R2 Bob   |  | R3 Carol |  ...                  |
|  | * Online |  | o Offline|  | * Online |                       |
|  | (4,7) ^  |  | (2,3) >  |  | (8,1) v  |                       |
|  | Dmg: 0   |  | Dmg: 2   |  | Flag: 1  |                       |
|  +----------+  +----------+  +----------+                        |
+------------------------------------------------------------------+
| SETTINGS                                                         |
|  [Use Robots: ON/OFF]   [Robot Screen: ON/OFF]                   |
+------------------------------------------------------------------+
| LINKS                                                            |
|  [Board Viewer] [DB Editor] [DataGrid Editor] [buttonpage]       |
+------------------------------------------------------------------+
```

Use iframes or tabs for the Board Viewer and DB Editor panels so navigation within
them does not disrupt the main GM control panel.

---

## Context-Sensitive Button Logic

Show/hide button groups by reading `GameState` on every `AllDataUpdate`:

```javascript
function updateButtons(gameState) {
    // Hide all context sections first
    document.querySelectorAll('.ctx').forEach(el => el.style.display = 'none');
    // Show the relevant one
    const el = document.getElementById('ctx-' + gameState);
    if (el) el.style.display = '';
}
```

Permanent buttons (always visible):
- Connect All Robots
- Get All Data (manual refresh)
- Links to sub-pages

State-gated buttons:
- "Start Game" — only when GameState < 2
- "Next State" — most states
- "Execute Turn" — state 5
- "Process Commands" — state 7
- "Start Game (select)" — state 0 / pre-game

---

## Game Selection

Query `GET /api/table/GameData` to populate a dropdown. Each row has at least:
- `GameDataID` — numeric ID
- `GameName` — display name

On "Load Game" click:
```javascript
fetch('/api/state/startgame/' + selectedId)
    .then(r => r.json())
    .then(updateFromAllData);
```

---

## Robot Direction Setter (State 3)

When `GameState == 3`, show a direction picker per robot:

```html
<!-- Four arrow buttons for each robot -->
<button onclick="setDirection(robotId, 1)">↑</button>  <!-- Up=1 -->
<button onclick="setDirection(robotId, 2)">→</button>  <!-- Right=2 -->
<button onclick="setDirection(robotId, 3)">↓</button>  <!-- Down=3 -->
<button onclick="setDirection(robotId, 4)">←</button>  <!-- Left=4 -->
```

Direction values match `RobotLocation.tDirection`: Up=1, Right=2, Down=3, Left=4.

Call the table API to write the direction:
```javascript
function setDirection(robotId, dir) {
    fetch(`/api/table/Robots/RobotID=${robotId}/CurrentPosDir=${dir}`)
        .then(() => fetch('/api/alldata'));
}
```

---

## Programming Progress (State 4)

For each robot, count filled registers from `CardsPlayed`:
```javascript
function countFilled(cardsPlayed) {
    return (cardsPlayed || '0,0,0,0,0')
        .split(',')
        .filter(v => v !== '0' && v !== '').length;
}
```

Show a `X / 5` progress indicator per player. When all players reach 5/5, the
"Lock Programs" (Next State) button becomes prominent.

---

## Robot Status Panel

Per-robot panel should display:
- **Name** — `PlayerName` or `RobotName`
- **Connection** — `IsConnected` (green dot / red dot)
- **Position** — `(X, Y)` and direction arrow (↑↓←→)
- **Damage** — `Damage` value
- **Flag** — `LastFlag` (last checkpoint touched)
- **Status** — `StatusID` mapped to `tPlayerStatus` enum label
- **Battery** — `Battery` percentage from AIM robot `ws_status`; only show when `IsConnected == 1`; color-code: ≥50% green, 20–49% yellow, <20% red
- **Programming** — filled register count (state 4 only)

### Required server-side changes for Battery

**`MRR/Players.cs`** — add a `Battery` property to `Player` (near `isMoving`):

```csharp
[NotMapped]
public int Battery { get; set; }
```

**`MRR/Players.cs`** — store it in `ProcessStatusEvent()`:

```csharp
// existing line reads: var battery = robot.TryGetProperty("battery", ...) ? bEl.GetInt32() : 0;
Battery = battery;  // add this line after reading battery
```

**`MRR/DataService.cs`** — include it in the `GetAllDataJson()` robots projection so it flows
through to the `AllDataUpdate` payload. The battery value lives on the in-memory `Player`
object (not in the DB), so it must be merged into the robot rows manually if `viewRobots`
is used as a SQL source. One approach: after building the robots list, patch each row with
the live `Battery` value from `AllPlayers`:

```csharp
// After fetching robot rows from viewRobots, merge in-memory Battery values:
foreach (var row in robotRows)
{
    var player = AllPlayers.GetPlayer(p => p.ID == (int)row["RobotID"]);
    if (player != null) row["Battery"] = player.Battery;
}
```

Direction display:
```javascript
const dirArrow = ['', '↑', '→', '↓', '←'];  // index 1–4
```

---

## Use Robots Toggle

The **Use Robots** toggle controls whether the game drives physical VEX AIM robots
over WebSocket or runs in **simulation-only** mode (UI and DB only, no hardware).

### Server side

Add a static flag to `GameController.cs` (same pattern as `UseRobotScreen`):

```csharp
/// <summary>
/// When false, all robot WebSocket commands are skipped.
/// The game runs in simulation mode: state machine and DB update normally,
/// but no ws_cmd or ws_status traffic is sent.
/// </summary>
public static bool UseRobots { get; set; } = true;
```

Add an endpoint to `Program.cs`:

```csharp
app.MapGet("/api/settings/use-robots", (bool enabled, GameController gameController) =>
{
    GameController.UseRobots = enabled;
    Console.WriteLine($"UseRobots set to {enabled}");
    return Results.Ok(new { UseRobots = enabled });
});
```

`Player.SendCommandAsync()` already guards on `isConnected` — when `UseRobots` is
false, `ConnectToAllRobots()` should skip the WebSocket connect entirely so
`isConnected` stays false and all `SendCommandAsync` calls become no-ops automatically.
No changes to the command pipeline are needed; DB-category commands still execute.

### GM UI side

A single toggle button in the Settings row:

```html
<button id="btn-use-robots" onclick="toggleUseRobots()">Use Robots: ON</button>
```

```javascript
let useRobots = true;

function toggleUseRobots() {
    useRobots = !useRobots;
    fetch('/api/settings/use-robots?enabled=' + useRobots);
    document.getElementById('btn-use-robots').textContent =
        'Use Robots: ' + (useRobots ? 'ON' : 'OFF');
}
```

**Visual cue**: when `UseRobots` is OFF, the Robot Status panels should show a
"SIMULATION" banner so the GM is never confused about which mode is active.

---

## Player Setup (Pre-Game)

The Player Setup section lets the GM configure each player's assignment before
the game starts. It is shown prominently when `GameState < 2` and collapses (or
becomes read-only) once the game is running.

### Database tables involved

**`RobotBodies`** — visual character / color skin:

| RobotBodyID | Name | Color (hex) | ColorFG |
|-------------|------|-------------|---------|
| 1 | Hammerbot | 7338B0 (purple) | FFFFFF |
| 2 | Hulk X90 | FE0000 (red) | FFFFFF |
| 3 | Smashbot | FFE733 (yellow) | 000000 |
| 4 | Spinbot | 0000FF (blue) | FFFFFF |
| 5 | Trundlebot | B76DBB (lavender) | FFFFFF |
| 6 | Twitch | BE9371 (tan) | FFFFFF |
| 7 | Twonky | EB9C1B (orange) | 000000 |
| 8 | Zoombot | 2A611E (dark green) | FFFFFF |

**`RobotBases`** — physical hardware unit (VEX AIM robot):
10 entries (IDs 1–10), each with a MAC address and a `DefaultBody`.
**Note:** `IPAddress` is NOT a column in the `Robots` DB table. It is a `[NotMapped]`
property on the `Player` class, populated at runtime from the `MACID` column via
inline SQL in `DataService.GetAllPlayers()`. Do not try to read or write it via the table API.

**`Robots.PlayerSeat`** — seat number 1–8 indicating where the player sits
around the physical board. Drives `SeatOrientation`:

| Seat(s) | Board view direction |
|---------|----------------------|
| 1, 2, 3 | Up (board top faces player) |
| 4, 5 | Right |
| 6, 7, 8 | Down |

### Loading setup data

Fetch static lookup tables once on page load (they don't change during a game):

```javascript
let robotBodies = [];
let robotBases  = [];

async function loadSetupData() {
    const [bodiesResp, basesResp] = await Promise.all([
        fetch('/api/table/RobotBodies'),
        fetch('/api/table/RobotBases')
    ]);
    robotBodies = (await bodiesResp.json()).RobotBodies ?? [];
    robotBases  = (await basesResp.json()).RobotBases  ?? [];
}
```

### Rendering the setup table

Build one row per active robot from the `Robots` array in `AllDataUpdate`.
Each row has three dropdowns populated from the lookup tables above:

```javascript
function buildSetupRow(robot) {
    const bodyOptions = robotBodies.map(b =>
        `<option value="${b.RobotBodyID}"
            ${b.RobotBodyID == robot.RobotBodyID ? 'selected' : ''}>
            ${b.Name}</option>`).join('');

    const baseOptions = robotBases.map(b =>
        `<option value="${b.RobotBaseID}"
            ${b.RobotBaseID == robot.RobotBaseID ? 'selected' : ''}>
            Base ${b.RobotBaseID}</option>`).join('');

    const seatOptions = [1,2,3,4,5,6,7,8].map(s =>
        `<option value="${s}" ${s == robot.PlayerSeat ? 'selected' : ''}>${s}</option>`
    ).join('');

    return `<tr>
        <td>${robot.RobotID}</td>
        <td>${robot.OperatorName ?? robot.PlayerName}</td>
        <td><select onchange="setRobotBody(${robot.RobotID}, this.value)">${bodyOptions}</select></td>
        <td><select onchange="setRobotBase(${robot.RobotID}, this.value)">${baseOptions}</select></td>
        <td><select onchange="setPlayerSeat(${robot.RobotID}, this.value)">${seatOptions}</select></td>
    </tr>`;
}
```

### Saving changes via the table API

```javascript
function setRobotBody(robotId, bodyId) {
    fetch(`/api/table/Robots/RobotID=${robotId}/RobotBodyID=${bodyId}`)
        .then(() => fetch('/api/alldata'));
}

function setRobotBase(robotId, baseId) {
    fetch(`/api/table/Robots/RobotID=${robotId}/RobotBaseID=${baseId}`)
        .then(() => fetch('/api/alldata'));
}

function setPlayerSeat(robotId, seat) {
    fetch(`/api/table/Robots/RobotID=${robotId}/PlayerSeat=${seat}`)
        .then(() => fetch('/api/alldata'));
}
```

### Color swatch in robot body dropdown

After selecting a body, update the robot status panel's border/header color to
match `RobotBodies.Color`. This gives instant visual feedback and helps identify
which physical robot is which color on the board:

```javascript
function applyBodyColor(robotId, bodyId) {
    const body = robotBodies.find(b => b.RobotBodyID == bodyId);
    if (!body) return;
    const panel = document.getElementById('robot-panel-' + robotId);
    if (panel) panel.style.borderColor = '#' + body.Color;
}
```

### Visibility rule

Show the Player Setup section when `GameState == 0` or `GameState` is absent
(no game loaded yet). Once `GameState >= 2`, collapse the section to a small
"Edit Setup" toggle so it's accessible but out of the way during play.

---

## Pause Commands (CommandTypeID 92)

Some commands in the `CommandList` table are **pause points** that require GM
acknowledgment before the command processor continues. These are identified by:

- `CommandTypeID == 92` — pause/wait command type
- `StatusID == 4` — command is waiting for GM acknowledgment

When such a command exists, the GM page must surface a **Continue** button so the GM
can release the hold. Clicking Continue sets `StatusID = 6` on that command row, which
allows the command processor to proceed.

### Detecting a pending pause

On every `AllDataUpdate`, check the `CommandList` for any waiting pause command:

```javascript
function findPauseCommand(commandList) {
    return (commandList ?? []).find(c => c.CommandTypeID == 92 && c.StatusID == 4);
}
```

`commandList` comes from `parsed.robots` — actually from the full `AllDataUpdate`
payload; query it via `GET /api/table/CommandList` or include it in the payload
(see Table API). The pause command row will have at minimum: `ID`, `CommandTypeID`,
`StatusID`, and optionally a `Description`.

### Rendering the Continue button

Show a prominent **Continue** button whenever a pause command is waiting. Place it in
the Context Actions section (always visible when present, regardless of game state):

```html
<div id="pause-section" style="display:none; background:#440; padding:8px; margin:4px 0;">
    <strong>⏸ Waiting for GM:</strong>
    <span id="pause-description"></span>
    <button onclick="releasePause()" style="margin-left:16px;">Continue →</button>
</div>
```

```javascript
function updatePauseSection(commandList) {
    const cmd = findPauseCommand(commandList);
    const section = document.getElementById('pause-section');
    if (cmd) {
        section.style.display = '';
        document.getElementById('pause-description').textContent = cmd.Description ?? '';
        section._pendingId = cmd.ID;
    } else {
        section.style.display = 'none';
    }
}

function releasePause() {
    fetch('/api/state/clearpause')
        .then(() => fetch('/api/alldata'));
}
```

### Raspberry Pi LED panel + joystick

The same pause command is also displayed on the **Raspberry Pi Sense HAT 8×8 LED
panel** as a visual indicator. Pressing the **Sense HAT joystick** (center press)
has the same effect as clicking the GM Continue button — it sets `StatusID = 6` on
the waiting pause command. No GM-page changes are needed to support this; the
joystick handler runs server-side. The GM Continue button and the joystick press are
equivalent and either one releases the hold.

---

## Key Implementation Rules

1. **No breaking changes** to existing API contracts in `Program.cs`.
2. **No new server-side files** for the GM UI — it is entirely in `wwwroot/`.
   If a new API endpoint is truly needed, add it to `Program.cs`.
3. All `fetch()` calls should use `target="_blank"` or update the GM panel
   inline — do not navigate the GM page away.
4. Use the existing shared CSS (`w3.css`, `button.css`) instead of inventing new styles.
5. Read `AllDataUpdate` as the single source of truth; do not maintain
   duplicate state in JavaScript variables.
6. The GM page must work on desktop (1080p or larger) — it is not a mobile page.
