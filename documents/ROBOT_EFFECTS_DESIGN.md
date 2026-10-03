# Robot Special Effects (LEDs + Speaker)

**Status:** Plan only — nothing implemented. Sound names and LED geometry below come from
[.claude/agents/aim-robot-api.md](../.claude/agents/aim-robot-api.md) and have **not been
tried on hardware** (only `play_sound` / `blinker` is already used in the repo,
[RobotScreenUI.cs:328](../MRR/RobotScreenUI.cs#L328)).
**Date:** 2026-10-02
**Related:** [RobotConnections.md](RobotConnections.md),
[MRR/Devices/RobotConnection.cs](../MRR/Devices/RobotConnection.cs),
[MRR/Players.cs](../MRR/Players.cs), [MRR/CommandProcess.cs](../MRR/CommandProcess.cs),
[MRR.Contracts/CommandList.cs](../MRR.Contracts/CommandList.cs)

## 1. Goal

Things that happen to a robot but can't be seen on the board — a laser fired or taken, a flag
touched, energy gained, a card dealt — should be *felt* at the table: a short LED pattern on
the robot's ring and a sound from its speaker. Effects are decoration only. They must never
change game state, never delay a turn, and never fail a turn.

## 2. What the code already gives us

- **The events already exist as commands.** `CreateCommands` emits a `CommandItem` for each
  (`MRR.Rules/CreateCommands.cs`): `FireCannon` (line 1349, `Value` = target robot ID),
  `Damage`, `Flag` (via `AddFlag`, line 1775, `Value` = flag number), `SetEnergy` (line 207,
  1561), `Dead`/`SetPlayerStatus`, `DealSpamCard`, `Option`/`PlayOptionCard`,
  `SetShutDownMode`, `Water`, `RobotPush`, `Randomizer`, `Archive`.
- **They are already declared robot-bound but do nothing.** `CommandList.cs` gives
  FireCannon, Damage, Flag, SetEnergy, Dead, PlayOptionCard, SetShutDownMode and GameWinner the
  category `RobotNoReply` and a legacy `BTCommand` code (`"3,2"` fire, `"3,4"` damage, `"3,5"`
  flag, `"3,3"` dead, `"3,6"` option, `"3,7"` win, `"3,8"` shutdown, `"3,9"` energy).
  `CommandProcess.ProcessCommand` (category 1/2 branch) sends each one to
  `Player.SendRobotCommandAsync`, which switches on `CommandMoveType` — and for these commands
  that is `0`, so **the `default:` branch does nothing**. The pipeline is wired; the effect
  itself is the missing piece.
- **RobotNoReply never blocks.** `CommandProcess` marks the command done as soon as it is
  sent (line 395-399), so an effect runs alongside the turn rather than holding it up.
- **Disconnected robots are already skipped** (`!robot.isConnected`, line 351), and simulation
  mode never touches `Player`/`RobotConnection` at all (RobotConnections.md §2). Effects
  inherit both for free.
- **LED/sound primitives exist or are one line each.** `RobotConnection.SetLedAsync(led, r, g,
  b)` for `all` / `light1`…`light6`; `SendCommandAsync(new { cmd_id = "play_sound", name,
  volume })` for sounds. `play_note` (note, octave 5–8, duration ≤4000 ms) allows custom
  jingles.

## 3. Design

### 3.1 One class, one entry point

New `MRR/Devices/RobotEffects.cs`: a static table mapping `SquareAction` (plus a little
context) to an `Effect` — an ordered list of steps, each step an LED setting, a sound, or a
pause. `Player` gets one method, `PlayEffectAsync(SquareAction action, int value)`, which
looks up the effect and runs it on its `RobotConnection`. `SendRobotCommandAsync`'s
`default:` branch calls it. **Effects are data, not scattered `if`s**, so retuning a colour
or swapping a sound is a one-line edit.

### 3.2 Non-negotiables

- **Fire and forget.** Run the effect on its own task; swallow and log exceptions. A dead
  socket or a bad sound name must never fault the command (CommandProcess already turns a
  faulted send into `isConnected = false`, which would wrongly drop the robot — so effects
  catch their own errors rather than letting them escape `SendRobotCommandAsync`).
- **One effect at a time per robot.** A per-robot `SemaphoreSlim`/queue so two events in the
  same phase (hit, then flag) play in order instead of tangling their LEDs. Cap the queue
  (drop the oldest) so a burst can't make effects lag minutes behind the game.
- **Always restore the LEDs.** The ring normally shows the robot's status colour
  (`Player.SendColorStatus`, `Players.cs:100-115`). Every effect ends by re-applying it.
- **Don't fight the flash loop.** `RobotConnection.Flash` (the "waiting for you" ring spin)
  drives the same LEDs. If `Flash` is on, skip the LED part of an effect and play sound only.
- **Failure-safe ordering.** The effect is triggered when the command executes, i.e. in
  turn order — the sound for a hit plays when the hit is *processed*, not when it was planned.

### 3.3 Proposed effects (all values are starting points to tune by ear/eye)

| Event | Source command | LEDs | Sound (`play_sound`) |
|---|---|---|---|
| Robot fires its laser | `FireCannon` (shooter) | Quick white-to-red sweep around the ring | `huah` |
| Robot is damaged (laser, pit edge, etc.) | `Damage` | Whole ring red, 2 fast pulses | `crash` |
| Robot touches its next flag | `Flag` (Value = flag #) | Gold; light `min(flag#,6)` LEDs, hold ~1 s | `tada` |
| Robot wins | win message / `GameWinner` | Rainbow spin ×3 | `cheer` |
| Robot gains energy | `SetEnergy` (only when it rose) | Short green pulse | `pickup` |
| Option card dealt | `Option` / `DealOption` | Blue pulse | `sparkle` |
| Option card played | `PlayOptionCard` | Purple flash | `flourish` |
| Spam card dealt | `DealSpamCard` | Orange pulse | `fail` |
| Robot destroyed | `Dead` / `SetPlayerStatus` 11 | Slow red fade out | `act_sad` |
| Robot reboots / respawns | reboot square | White flash, then status colour | `sensing` |
| Shut down / powers up | `SetShutDownMode` | Dim ring / ring back on | `pause` / `resume` |
| Pushed by another robot | `RobotPush` | Single yellow flash | `blinker` |
| Falls in water | `Water` | Cyan flash | `act_silly` |

Variants worth deciding after the first pass: the **flag #** can be played as `flag#` notes
with `play_note`, so the table can tell progress by ear; **energy** could be a rising note
per point gained.

### 3.4 "Shot" vs "damaged" — avoid the double effect

`FireCannon` carries the *target's* ID, and the `Damage` that follows is a separate command
with no source. If both play a hit effect, a laser hit would sound twice. Recommendation:
**`FireCannon` plays only the shooter's effect; the victim's effect comes from `Damage`.**
That means one rule covers lasers, pits, and any other damage, at the cost of not knowing who
shot you.

*Optional later:* light only the LED(s) facing the shooter. `FireCannon` has both robots'
positions, so the bearing is computable, but it needs the real LED-to-heading mapping
(angles in aim-robot-api.md, assumed front = 0°) checked on a robot first.

## 4. Settings

- **Volume** — a single constant at first (the repo already uses 80); then a GM-adjustable
  value.
- **Effects on/off** — a GM toggle. Six speakers on a table is a lot, and some game nights
  want LEDs only. Store it in `CurrentGameData` (next free `iKey`) so it follows the existing
  settings pattern rather than a new table (the database stays tables-only — see CLAUDE.md).
- **Per-effect mute** — not needed in v1.

## 5. Files

- `MRR/Devices/RobotEffects.cs` — **new**: the effect table and runner.
- `MRR/Players.cs` — add `PlayEffectAsync`; call it from `SendRobotCommandAsync`'s `default:`
  (so `SendRobotCommandAsync` stays the single entry, and `CommandMoveType` is untouched).
- `MRR/Devices/RobotConnection.cs` — add `PlaySoundAsync(name, volume)` and a small
  `RunLedSequenceAsync` helper beside `SetLedAsync` (RobotConnection is the only place a robot
  socket is used — RobotConnections.md "The rule").
- `MRR.Contracts/CommandList.cs` — only if a new effect needs a command type that is not yet
  `RobotNoReply` (e.g. `DealSpamCard`, `Option`, `Water`, `RobotPush` are `DB` today — they
  would need to become `RobotNoReply`, which changes what `CommandProcess` does with them, so
  check each against `ProcessDbCommand` first, and note `Archive` removal on flags,
  2026-10-01).
- No `MRR.Rules` change is expected: the commands are already emitted.

## 6. Build order

1. **Prove the plumbing with one effect.** Implement `PlaySoundAsync` and a Flag effect only;
   run a turn on a real robot and confirm sound plays, LEDs restore, the turn is not delayed.
2. **Audition every sound.** Play each name in §3.3 from a scratch endpoint or test and
   keep or swap. The built-in list in aim-robot-api.md is the only source for names.
3. **Add the rest** of the table, then the per-robot queue and flash-loop guard.
4. **Check `Dead`/respawn and win** — the least certain event sources (§7).
5. **Add the GM toggle and volume.**
6. **Full six-robot turn** with every effect on, to confirm they don't pile up or talk over
   each other.

## 7. Open questions / risks

- **Win has no command today.** `CreateCommands.cs:1464-1465` only shows a message; the
  `GameWinner` command is commented out, and PROJECT_STATUS.md §4.4 already notes winning
  doesn't end the game. A win effect needs that decision made first.
- **Reboot/respawn** — which command marks it (Pit/RebootToken, `SetPlayerStatus`) needs
  reading before wiring; the table row is provisional.
- **Command categories:** several events (`DealSpamCard`, `Option`, `Water`, `RobotPush`) are
  database-only commands today. Making them robot-bound is a behaviour change to confirm,
  not a free tag.
- **LED/sound latency over WiFi** — each call is a socket message; a six-step LED sequence is
  six messages per robot. Likely fine, but measure on the game LAN
  ([install/NETWORK_SETUP.md](../install/NETWORK_SETUP.md)) before building long animations.
- **Speaker loudness and overlap** — can't be judged from code; tune at the table.
- **LED sequence vs. movement** — nothing stops an effect from being mid-sequence when the
  next drive command arrives. That is harmless (separate subsystems), but confirm drives are
  not slowed while a sound plays.
