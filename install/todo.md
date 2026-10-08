# Mega Robo Rally — Project TODO

**Last updated:** 2026-10-08
**Legend:** `[x]` Done &nbsp; `[-]` Partial / In Progress &nbsp; `[ ]` Not started

Resolved items are removed from this file once done rather than kept as a checked-off log —
see git history / commit messages for what happened and when. Section numbers are kept stable
because other documents refer to them; sections with nothing open are omitted.

---

## Important

What to work on next, reordered by the user 2026-10-08.

- [ ] **Show AIM robot battery level on the GM UI** (Section 3)
  - `ws_status` already delivers `battery` (0–100%); `ProcessStatusEvent` reads it but discards it
  - Server: add `Player.Battery` property; store in `ProcessStatusEvent`; merge into `GetAllDataJson` robots payload
  - GM UI: show in each robot status panel when `IsConnected == 1`; color-code ≥50% green, 20–49% yellow, <20% red
  - Design spec in `.claude/agents/gm-ui.md` § Robot Status Panel

- [ ] **Robot screen: show why a button press is being requested** (`RobotScreenUI.cs`) —
  currently the touchscreen just blocks on a press with no context. Distinguish e.g. Phase
  advance, Remove (reboot/pit death), Place (respawn placement) so a player knows what they're
  confirming.

- [-] **Robot grid alignment** (`GridAlignmentAgent.cs`)
  - Code complete; constants need tuning against real board + lighting:
    `BlackLuminanceThreshold`, `MinBlackPixels`, `AlignedThreshold`, `NudgeDistanceMm`
  - Confirm the `ws_img` wire format against a live robot:
    - [ ] `GridAlignmentAgent.ExtractImageBytes()` succeeds and `AnalyzeImage` returns `HasLines=true` on a real board
    - [ ] Does `ws_img` stream frames continuously once connected, or only after a trigger command?
    - [ ] Is the frame rate controllable?
    - [ ] Does `ws_img` require `program_init` first, the way `ws_cmd` does?
    - See `tools/ai_agent/ws_img_format.md` for what to update if the format differs from raw JPEG.
      The same open questions are echoed in `tools/ai_agent/grid_alignment_agent.md` ("Known
      Unknowns") and `tools/ai_agent/image_processing.md` ("pending hardware validation") — one
      hardware session should close all three docs' unknowns at once

- [ ] **Link to the board viewer for the GM**, showing current robot positions (Section 3)

- [ ] **Web control panel for `mrrctl`** (start/stop/restart/status via REST, called from a
  "System" panel in `gmindex.html`) — designed but explicitly deferred pending an auth story;
  `mrrctl` itself is CLI-only today (`install/PROCESS_MANAGER.md` §11)

---

## On hold

Not being worked on now (user 2026-10-08).

- [ ] **Failed robot send silently applies the move anyway** (`CommandProcess.cs`,
  `DataService.Commands.cs` `ProcessDbCommand`) — `SendRobotCommandAsync`'s fault handler
  already stops the loop from hanging (sets `isConnected=false`, forces `StatusID=4`), but the
  next poll still calls `ProcessDbCommand(command, 5)`, which applies the move's position
  effect unconditionally. A robot whose send failed ends up with its DB position updated as
  though it moved — game state and the physical board quietly diverge, and nothing is visible
  to the GM. Needs a path that skips the position effect when the send is known to have
  failed. (`documents/API_DECOMPOSITION_DESIGN.md` §7, listed as High — the hang half of it is
  already fixed, the false-success half is not)

- [ ] **Haywire cards** — play 5 random cards from the deck. The card type exists; nothing deals
  or executes it (Spam damage is done). Trojan Horse is not part of this rules version.
  - [ ] Handle Haywire / Spam / option card notifications on the phone

- [-] **Option card effects** wired into phase processing (`CreateCommands.cs`)
  - Partial: ReverseGears, FourthGear, RammingGear referenced
  - Missing: Brakes, CrabLegs, Recompile, many others
  - Circuit Breaker confirmed **not used** in this rules version (2026-08-27) — do not
    implement it; the existing check in `CreateCommands.cs` (line ~604) is dead in practice

  **Note (2026-09-17, user instruction):** Option cards and Haywire cards are not being worked
  on yet. Do not remove "dead" code related to either (unwired `tOptionCardCommandType` entries,
  `OptionCard`/`OptionCardList`, Haywire handling in `CardList.cs`, etc.) until this is picked up.

- [ ] **Pushers** (`CreateCommands.cs`) — board element type not implemented at all
  - Activate only on specific phases (odd or even, marked per pusher)
  - Push robot one square; chain-pushes if another robot is in path

- [ ] **DB password committed in tracked `appsettings.json`** (`ConnectionStrings:Rally`,
  `pwd=rallypass`) in both `MRR/appsettings.json` and `MRR.Config/appsettings.json`
  (`PROJECT_STATUS.md` §4.7). Move to an untracked `appsettings.Production.json` or an
  environment variable before this repo is ever made public. Low priority: this is a totally
  closed system (isolated game network, no public exposure).

- [ ] **Every phone receives every player's hand** in the broadcast payload, not just its own
  (`documents/API_DECOMPOSITION_DESIGN.md` §7, Medium; the password leak this item used to
  also cover is already fixed). Needs per-seat SignalR groups or payload filtering. Low
  priority — closed system, consistent with the cookie-login item deciding "do not worry about
  sending all cards to all robots." Client-side login already hides it from a casual player;
  this item is only about the raw broadcast payload still carrying it.

---

## Section 1 — Game Mechanics
*Renegade rules completeness.*

- [-] Conveyor belts (`CreateCommands.cs`) — express-then-normal movement and chained belts are done
  - [ ] Merge conveyor belts (splitting paths converge)
  - [-] Conveyor belts can push a robot off the board (into a pit/off the edge) and through
    walls — off-board half fixed 2026-09-10: `InValidPos()` now guards the `SquareAction.Move`
    case in `CreateCommands.cs`, so a belt push toward an out-of-range square is skipped instead
    of writing a negative/out-of-bounds position. Still open: belt movement passing through a
    wall — it doesn't appear to run the same wall check as normal moves (see
    `CalcMoveDistance`'s wall check, Section 6).

---

## Section 2 — Robot Hardware
*VEX AIM physical integration.*

- [ ] Robot screen: show an arrow pointing toward the player's seat

- [ ] Implement ws_audio upload if server-side audio needed (`ws://{ip}:80/ws_audio`)
  - Wire format is documented (AIM WebSocket Library v1.0.1):
    - Byte 0: format (`0`=WAV, `1`=MP3)
    - Byte 1: volume (0–100)
    - Bytes 4–7: data length (little-endian uint32)
    - Bytes 32–63: filename (null-padded, 32 chars max)
    - Followed by: raw audio data (max 255 KB)
  - `play_file` cmd_id (`ws_cmd`) plays a file uploaded via `ws_audio`
  - Only needed if server wants to push custom audio to robots; built-in sounds via `play_sound` need no upload

---

## Section 3 — UI
*`wwwroot/`*

### GM Control Page *(in program section of index.html)*

- [-] Game controls — Start, Restart, Reboot buttons (mapped to game state transitions) on a menu that appears when clicking on "Robots" — **partial, 2026-09-15.** The "Robot" header
  is now tappable (GM view only, see below) and opens a menu (`#gmMenu` in `index.html`,
  `toggleGmMenu()`/`gmAction()` in `js/loadrobots.js`) hitting the same `/api/state/{action}`
  routes `gmindex.html`'s links already use. Built with the game-state actions that actually
  exist today — Start Game, Next State, End Game — rather than literal "Restart"/"Reboot"
  buttons, since neither of those is a real distinct action in the codebase (no "restart game"
  endpoint, and "Reboot" is the per-robot mechanic in Section 1, not a game-level control).

- [ ] Dynamic button area — display any buttons/actions that appear based on game state
  - e.g. "Advance Phase", "Skip Robot", admin overrides

---

## Section 5 — Network Setup

- [ ] `Urls: http://*:5000` in `appsettings.json` binds every network interface, not just the
  game LAN (`documents/API_DECOMPOSITION_DESIGN.md` §7, Low) — fine on an isolated game router,
  worth narrowing once the home-network port-forward is in place

---

## Section 6 — Infrastructure / Setup

- [ ] `SqlGateway.ExecuteSQL`/`GetQueryResults` silently swallow database errors and return
  empty results instead of surfacing them (`PROJECT_STATUS.md` §6.6) — a bad connection string
  or a bad query currently shows up later as an unrelated NullReferenceException instead of a
  clear DB error at the source

- [ ] Board editor `PUT` does its replace as a DELETE+INSERT with no surrounding transaction,
  and builds SQL by string concatenation rather than parameters (`Program.cs:453`,
  `documents/API_DECOMPOSITION_DESIGN.md` §7, Medium) — a failure mid-update can leave a board
  half-written, and the concatenation is worth checking for injection risk

- [ ] Split `DataService` further (`documents/API_DECOMPOSITION_DESIGN.md` §9 step 4, called out
  in `PROJECT_STATUS.md` as "the largest remaining piece"): extract a `RuleEffects` layer and a
  repository layer out of the current partial-class split. Sits on the turn-execution hot path
  (`ProcessDbCommand`, `CreateCommands`) — wants careful review, not a quick pass.

- [ ] Presentation-layer decomposition (`documents/API_DECOMPOSITION_DESIGN.md` §9 step 7 /
  §4 "landmine"): `RobotScreenUI` still writes game state, calls the DB, and broadcasts SignalR
  all from one method (`UpdateCardPlayed` then `SendAsync`) instead of separating render from
  report; per-seat SignalR groups (see the phone-hand-visibility item under On hold) are part
  of the same step

- [ ] Game-level turn pause, distinct from process-level pause (`install/PROCESS_MANAGER.md`
  §11): `GameController` holding the dispatch loop while continuing to serve phones, vs.
  `mrrctl pause` suspending the whole process. Explicitly out of scope when the process manager
  was built; still wanted.

- [ ] Remote alerting on repeated `mrr-server` restarts — no notification path exists yet
  (`install/PROCESS_MANAGER.md` §11)

- [-] `Robots.Password`'s source — found 2026-09-17 while designing the `GameState==1` seat-
  claim flow ("Operator Data Setup"). **Partially resolved 2026-09-21:** the new
  `DataService.SetupPlayersFromOperatorData()` (called from `GameController.StartGame()`) sets
  `Robots.Password = OperatorData.Password` when a preset roster auto-claims every seat. **Still
  open:** the interactive per-seat path, `DataService.SelectSeat` (used whenever players claim
  seats by hand instead of a preset roster), still never sets `Password` — a manually-claimed
  seat has no PIN. Note this is moot anyway until phone login actually checks a PIN, which it
  doesn't today — see `documents/PHONE_LOGIN_DESIGN.md`'s status note; the shipped login is a
  plain seat-number cookie, not password-based.

---

## Section 7 — Dead Code Removal

- [ ] `AdminApi.cs:155` computes `players = data.AllPlayers.Count` — cosmetic only, could become
  a `COUNT(*)` now that `AllPlayers` isn't the source of truth elsewhere; no correctness need

---

## Section 9 - Testing

- [ ] Add test coverage for other `CreateCommands` rules as they're touched (conveyor belts,
  pushers, lasers, option cards) — none of that is covered yet; `MRR.Tests` only has the
  position-bug regression tests so far. Run all tests from the repo root: `dotnet test MRR.Tests`.
