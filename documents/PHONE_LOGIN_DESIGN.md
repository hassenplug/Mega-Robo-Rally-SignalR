# Phone Login + Connection Tracking

**Status:** Design — not yet implemented.
**Date:** 2026-09-11
**Related:** `install/todo.md` Section 8 ("Players will have to log in and the browser
will hold a cookie of the player login"), `documents/API_DECOMPOSITION_DESIGN.md` §7
(phone-broadcast defects — the "every phone sees every hand" item is explicitly deferred,
not covered by this doc).

## 1. Trigger

Phones are supplied by the operator (trusted hardware, not public devices), but today
there is no login at all: `MRR/wwwroot/index.html` shows every robot's row on one shared
page, and tapping a row just sets a client-side JS variable (`CurrentPlayer` in
`loadrobots.js:34`) — any phone can act as any player, and nothing tracks which physical
phone is "with" which robot.

`install/todo.md` (Section 8) already anticipated this. The DB even has a per-robot PIN
sitting unused for exactly this purpose: `Robots.Password` / `OperatorData.Password`
(`install/MRRDatabase.sql:367`), copied from `OperatorData` into `Robots` at game start
(`GameController.cs:248-258`), loaded onto the in-memory `Player` object
(`DataService.Players.cs:116`), but never read back or verified anywhere
(`MRR.Contracts/AllDataPayload.cs:33-36` documents this explicitly).

**Goal:** let a phone log in as a specific robot/player, persist that identity across
page reloads via a cookie, and track which phone (SignalR connection) is currently
connected to which robot.

## 2. Decisions

- **Track, don't enforce.** Login establishes and persists identity for UX purposes; it
  does not change how `/api/player/{command}/{playerId}/...` trusts its `{playerId}`
  today. Locking that down (verifying the caller's login matches the RobotID it's acting
  as) is a separate, later task.
- **Lightweight custom cookie**, not ASP.NET Core's built-in cookie-auth middleware — a
  server-side in-memory token map, no new DB tables, no Data Protection key persistence
  to manage.
- **Scope is login + connection tracking only.** The related open item (every phone
  receives every player's hand in the broadcast) is explicitly deferred, not bundled in.

## 3. Design

### 3.1 Credential

Reuse `Robots.Password` as-is — no schema change. It's only populated once a game has
been started (`GameController.StartGame()` copies it from `OperatorData`), so phone login
only works after that point, same as every other per-robot field on `Robots`.

### 3.2 New singleton: `MRR/Services/PhoneSessionRegistry.cs` (namespace `MRR.Services`)

In-memory, two maps:
- `token -> RobotID` — resolves the login cookie.
- `SignalR ConnectionId -> RobotID` (reverse-lookup friendly) — the actual "phone
  connected to this robot" tracking.

Methods: `Login(robotId) -> token`, `TryResolveToken(token, out robotId)`,
`Connect(connectionId, robotId)`, `Disconnect(connectionId)`, `IsConnected(robotId)`.
Registered as a singleton in `Program.cs` next to the other `AddSingleton<...>()` calls
(~line 21).

**Operational tradeoff:** this is in-memory only, so a process restart
(`mrrctl restart`, a crash/health-recovery cycle, a deploy) clears every login. The
`mrr_player` cookie itself survives (long `Max-Age`), but `whoami` (below) will then
return 401 — the front end must treat that as "not logged in" and show the login form
again, not an error.

### 3.3 Password check — avoid SQL injection, don't reuse string-built queries for this

`DataService` has no parameterized-query helper (`MRR/Data/SqlGateway.cs` is raw string
concatenation throughout — a known, already-flagged issue elsewhere, see
`install/todo.md` Section 6). Don't add another place where user input reaches SQL text.
Instead, in a new `DataService.Players.cs` method
`VerifyPlayerPassword(int robotId, string submittedPassword)`: fetch the stored password
by `RobotID` (an `int`, safe to interpolate) with the existing `GetQueryResults`/row-
mapping pattern, then compare the two strings **in C#**. The submitted password never
enters a SQL string.

### 3.4 Three new endpoints in `Program.cs` (near the existing `/api/player/...` routes,
`Program.cs:166-194`)

- `POST /api/player/login` — body `{ RobotId, Password }`. Verifies via
  `VerifyPlayerPassword`; on success calls `sessionRegistry.Login(robotId)`, sets cookie
  `mrr_player` (`HttpOnly`, `SameSite=Lax`, ~30 day `Max-Age`; no `Secure` flag since the
  game LAN is plain HTTP), returns `{ robotId, robotName, priority }`. On failure,
  `Results.Unauthorized()`.
- `GET /api/player/whoami` — reads the cookie, resolves via the registry, returns the
  same shape or 401. Called by the phone on page load to restore its identity.
- `POST /api/player/logout` — clears the cookie and the registry entry.

### 3.5 `MRR/DataHub.cs` — currently an empty `Hub` subclass

Inject `PhoneSessionRegistry`. Override:
- `OnConnectedAsync()`: read `Context.GetHttpContext()?.Request.Cookies["mrr_player"]`,
  resolve it, and if found call `registry.Connect(Context.ConnectionId, robotId)`.
- `OnDisconnectedAsync(Exception?)`: `registry.Disconnect(Context.ConnectionId)`.

No SignalR groups needed for this scope (no broadcast filtering yet) — just the
connect/disconnect bookkeeping.

### 3.6 Surface the tracking where the GM/phones already look

Add `PhoneConnected` (`bool`) to `RobotData` (`MRR.Contracts/AllDataPayload.cs`), same
pattern as the existing `ConnectStatusID` (physical robot connection). Populate it in
`DataService.GetRobotsFromTable()` by asking the (now-injected) `PhoneSessionRegistry` per
robot. This makes "is a phone currently logged into this robot" visible in the same
`AllDataUpdate` broadcast every page already consumes (`index.html`, `gmindex.html`) — no
new polling endpoint needed just for display.

### 3.7 Front end — `MRR/wwwroot/index.html` + `js/loadrobots.js`

- On load, call `GET /api/player/whoami`.
  - 401 → show a small login form: a dropdown built from the next `datapacket.robots`
    list (or a static seat picker) + a PIN field, POSTing to `/api/player/login`.
  - 200 → hide the login form.
- On successful login (fresh or restored), auto-call the existing `showplayerprogram(...)`
  for that robot's `Priority` so the phone opens straight to its own hand — matching "log
  in directly into the game" rather than requiring a tap afterward.
- Leave the existing shared table and tap-to-switch behavior alone (per the "track, don't
  enforce" decision — nothing stops a logged-in phone from also viewing another row).

### 3.8 Housekeeping

Once built, check off the "Players will have to log in..." line in `install/todo.md`
Section 8.

## 4. Files touched

- `MRR/Services/PhoneSessionRegistry.cs` (new)
- `MRR/DataService.Players.cs` (`VerifyPlayerPassword` + a small by-RobotID info lookup)
- `MRR/DataService.cs` (constructor takes `PhoneSessionRegistry`; `GetRobotsFromTable`
  populates `PhoneConnected`)
- `MRR/Program.cs` (register singleton; 3 new endpoints)
- `MRR/DataHub.cs` (connect/disconnect tracking)
- `MRR.Contracts/AllDataPayload.cs` (`PhoneConnected` field)
- `MRR/wwwroot/index.html`, `MRR/wwwroot/js/loadrobots.js` (login form + whoami bootstrap)
- `install/todo.md` (check off the login line when done)

## 5. Verification

- `dotnet build` (repo root) — no compile errors.
- `curl -i -X POST /api/player/login` with a wrong PIN → 401, no `Set-Cookie`.
- Same with the correct PIN (from `OperatorData`/`Robots.Password` seed data) → 200 +
  `Set-Cookie: mrr_player=...`.
- `curl -i --cookie "mrr_player=<token>" /api/player/whoami` → 200 with robot info;
  without the cookie → 401.
- Open the phone UI in two separate browser profiles, log each into a different robot,
  confirm the next `AllDataUpdate` broadcast shows `PhoneConnected=true` for both and
  `false` for the rest; close one tab and confirm it flips back to `false`.
- Restart the game host (`dotnet run` restart, or `mrrctl restart` if installed) with a
  phone still "logged in": confirm `whoami` now 401s and the page falls back to the login
  form instead of erroring.
