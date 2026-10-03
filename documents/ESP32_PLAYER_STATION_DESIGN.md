# ESP32-S3 CYD Player Input Stations (Replacing Phones)

**Status:** Design only — no firmware written yet. Board identified 2026-10-02 (§1.1).
**Date:** 2026-09-28 (updated 2026-10-02)
**Related:** [PHONE_LOGIN_DESIGN.md](PHONE_LOGIN_DESIGN.md) (documents the phone protocol this
replaces), [MRR/wwwroot/js/loadrobots.js](../MRR/wwwroot/js/loadrobots.js),
[MRR/wwwroot/js/datahub-connection.js](../MRR/wwwroot/js/datahub-connection.js),
[MRR/DataHub.cs](../MRR/DataHub.cs), [MRR.Contracts/AllDataPayload.cs](../MRR.Contracts/AllDataPayload.cs)

## 1. Goal

Replace the 6 player phones with 6 dedicated **Freenove ESP32-S3 CYD, 4.0", 320×480 IPS,
capacitive touch** units. Each unit is physically bound to one seat and shows only that
seat's hand — no shared login box, no GM functions. This is a genuinely separate firmware
codebase (Arduino/C++, not C#/.NET), the same relationship the `phone-kiosk-browser` Android
project has to the rest of this repo.

### 1.1 The board: Freenove ESP32-S3 Display, FNK0104

The unit in hand is the **Freenove ESP32-S3 Display, model FNK0104** (package label: "S",
revision code `A1B0`), described on the label as **4.0-inch, 320×480 IPS, C-Touch**
(capacitive). It is the board this design already assumed. Freenove's tutorial and support:
`http://freenove.com/fnk0104`, `support@freenove.com`.

**Not yet verified** — read from the product label only. Confirm against Freenove's FNK0104
tutorial before writing firmware, since each affects the build:

- display driver and touch controller ICs (determine the LVGL/TFT driver setup)
- flash and PSRAM size (decides whether card images can be baked in as C-arrays, §4)
- USB connector type and whether it is the ESP32-S3's native USB-Serial/JTAG or goes through
  a UART bridge chip (§7 assumes native)
- whether it has a battery/charging circuit, or only draws power over USB (§7 "charging")
- Arduino board settings and any Freenove-supplied LVGL/display library (§4 assumes
  Freenove's own tutorial path)

**Out of scope:** GM control panel (`gmindex.html` stays on its existing device), the
`board-viewer.html`/`connectscreen.html` displays, and any change to the phone UI itself —
phones and ESP32 stations can coexist during rollout since both just talk to the same
existing REST endpoints.

## 2. Decisions

- **Poll REST, don't implement SignalR on-device.** SignalR's wire protocol (negotiate
  handshake + `\x1e`-delimited JSON frames over WebSocket) has no mature Arduino/ESP-IDF
  client. The game is turn-based, not latency-sensitive, so a plain `GET /api/alldata` poll
  every ~500ms–1s is the right trade: trivial server load (6 devices × ~2 req/s) in exchange
  for skipping a from-scratch embedded WebSocket-protocol implementation.
- **Actions reuse the existing plain-GET endpoints as-is** — `/api/player/{command}/...`,
  `/api/setup/select/...`. These already have no auth and no body (see
  [PHONE_LOGIN_DESIGN.md](PHONE_LOGIN_DESIGN.md) §1 — "track, don't enforce" was already the
  server's posture for phones). **Zero server-side changes required to ship this.**
- **Seat identity is set via a first-boot picker, not baked in at compile time.** One
  firmware image for all 6 units; on first boot (or after a factory-reset gesture) the unit
  shows a "Pick your seat: 1 2 3 4 5 6" screen, writes the choice to NVS, and boots straight
  to that seat's view thereafter. This matches how seats get reassigned game to game today
  (todo.md Section 8 / `SetupPlayersFromOperatorData`) without needing 6 separately-flashed
  builds.
- **No cookie, no seat-matching-against-broadcast dance.** The phone's `applyLogin()` matches
  a cookie against `robots[].PlayerSeat` on every broadcast because any phone can be any
  seat. Here the seat is fixed per physical unit, so the firmware just filters `robots[]` for
  `PlayerSeat == storedSeat` once per poll — same lookup, simpler because there's no login
  state machine around it.
- **New top-level directory, not under `MRR/`.** `MRR/` is the .NET project; this is
  Arduino/ESP-IDF C++. Proposed: `esp32-player-station/` at repo root (sibling to `MRR/`,
  `documents/`, `install/`), holding the firmware project, card-image conversion script, and
  its own README.

## 3. Protocol mapping (phone → ESP32)

| Phone (`js/loadrobots.js`) | ESP32 equivalent |
|---|---|
| SignalR `AllDataUpdate` push | `GET /api/alldata` poll, ~500ms–1s interval |
| `applyLogin()` — cookie vs `PlayerSeat` | one-time NVS-stored seat, filter `robots[]` each poll |
| `showplayerprogram()` — render dealt/played cards | LVGL hand view, same comma-split of `CardsDealt`/`CardsPlayed` |
| `PlayCard()` → `GET /api/player/1/{playerid}/{cardid}/{loc}` | touch a card → same GET via `HTTPClient` |
| `confirmMessage()` → command 3 | touch message banner → same GET |
| `cycleDirection()`/`confirmDirection()` → commands 4/5 | touch direction arrow/confirm → same GETs |
| `showSetupScreen()` (GameState==1) → `/api/setup/select/{seat}/{pos}/{body}` | pre-game body/start-position picker, same GET |
| `canProgram()` (`gamestate` 2–4) | same gate, evaluated from the polled payload |

No new server endpoints are needed for this table. A later optimization (not now) would be a
slimmer `GET /api/player/state/{seat}` returning one robot's row instead of all 6, if the
full `/api/alldata` payload (a few KB for 6 robots) ever proves too much for the device's
JSON parsing — no evidence of that yet, so it's not part of this design.

## 4. Firmware architecture

- **Framework:** Arduino core for ESP32-S3 + LVGL (Freenove's own tutorial path for this
  board), `HTTPClient` for polling/posting, `ArduinoJson` for parsing.
- **WiFi:** hardcoded SSID/password for the closed game LAN, set at build time (no captive
  portal — this is trusted hardware on a private network, same trust model the phones
  already operate under).
- **Main loop:** poll `/api/alldata` → parse → find own `PlayerSeat` row → diff against last
  rendered state → redraw only what changed (avoid full-screen LVGL redraws every poll tick).
- **Screens:**
  1. **First-boot seat picker** — shown when NVS has no stored seat, or after a
     factory-reset gesture (mirrors the phone's hidden 5-tap logout gesture — e.g. long-press
     a corner for 5s). Writes seat number to NVS, reboots into the normal view.
  2. **Setup/seat-claim screen** (`gamestate==1`) — body + starting-position picker, driven by
     `GameConfig` in the payload, same data the phone's `renderSetupStatus()` uses.
  3. **Main hand screen** — played-card row (5 slots) + dealt-card row (up to 9), direction
     picker, message banner. Landscape 480×320 gives more room than the phone's narrow
     portrait table, so this is a fresh layout, not a pixel port of `index.html`.
  4. **Connection-lost screen** — shown when polling fails N consecutive times, mirroring
     `datahub-connection.js`'s retry-with-backoff behavior.
- **Card art:** convert the existing `MRR/wwwroot/images/type*.png` set to LVGL image
  C-arrays baked into firmware (simplest — avoids runtime PNG decode and network fetch of
  images every poll). A one-time conversion script (LVGL's `lv_img_conv` or similar) goes in
  `esp32-player-station/tools/`.

## 5. Files touched / created

- `esp32-player-station/` (new directory, new repo area — not touched by any existing C#
  build)
  - `platformio.ini` or `.ino` + supporting `.cpp`/`.h` (firmware)
  - `assets/` — converted card-image C-arrays
  - `tools/convert-images.*` — one-time PNG→LVGL conversion script
  - `README.md` — build/flash instructions, WiFi credential setup
- `CLAUDE.md` — add a row to the Design Documents table pointing at this doc, and a note in
  Project Layout that `esp32-player-station/` is a separate Arduino/C++ project (same pattern
  as the phone-kiosk-browser callout)
- **No changes to `MRR/` are required for the ESP32 side to function.**

## 6. Rollout plan

1. Build firmware against one unit while phones remain the primary input for the other 5
   seats — the REST endpoints don't care which client calls them, so this is safe to test
   live against a real game.
2. Verify full hand lifecycle on that one unit: seat picker → setup screen (pick body/start
   position) → dealt hand renders → play a card → direction picker → confirm → next turn.
3. Verify reconnect behavior: kill WiFi mid-game, confirm the connection-lost screen shows
   and the unit recovers on its own once WiFi returns, without needing a reboot.
4. Flash the remaining 5 units with the same image; assign seats via the first-boot picker
   at the table.
5. Run one full game with all 6 stations and no phones, confirm nothing regresses.

## 7. Deployment: USB-tethered to the Pi for charging + flashing

The 6 units connect to the Raspberry Pi via USB for power (charging), not for game data —
WiFi remains the data path (§2). The same USB-C cable also carries the ESP32-S3's **native**
USB-Serial/JTAG interface (no external USB-UART bridge chip on this family), so that
charging cable doubles as the flashing cable with no extra hardware.

- **Build off-device, flash from the Pi.** Compile the firmware on a dev machine (Arduino
  IDE/PlatformIO), producing a `.bin`; copy it to the Pi and flash with `esptool.py`
  (pure Python, runs fine on Raspberry Pi OS aarch64) rather than installing the full
  ESP32-S3 toolchain on the Pi — keeps the Pi's footprint limited to a tool it already needs
  for nothing else, on a box that's also running the game server and Sense HAT.
- **Entering download mode:** hold BOOT, tap RESET — standard ESP32-S3 procedure, documented
  by Freenove. The unit then enumerates as a new `/dev/ttyACM*` device.
- **No per-unit tracking needed.** Every unit runs identical firmware (seat is chosen by the
  first-boot picker, §2/§4, not baked in per build), so a flashing script can just iterate
  over whatever `/dev/ttyACM*` ports are present and write the same binary to each —
  `esp32-player-station/tools/flash-all.sh` (to be written alongside the firmware).
- **Powered USB hub likely required** — 6 boards charging simultaneously may exceed what the
  Pi 5's own USB ports comfortably supply alongside the Pi itself.
- **Don't hardcode `/dev/ttyACM0..5`** in the flash script — device paths on a shared hub
  aren't guaranteed stable across reboots/replugs; discover connected ports at run time.
- **Later refinement, not v1:** since units are already WiFi-connected and polling the REST
  API, wireless OTA (`ArduinoOTA` or a simple HTTP self-update endpoint) could handle routine
  firmware updates without a physical BOOT/RESET on all 6 units per revision. USB/`esptool`
  would remain the initial-provisioning and recovery path either way.

## 8. Open items / risks

- **Confirm FNK0104's hardware details** (list in §1.1) from Freenove's tutorial — the first
  thing to do once a unit is on the bench. A web search on 2026-10-02 did not find the
  FNK0104 spec sheet, so nothing about its driver/touch/memory is recorded as fact here.

- **Touch responsiveness under LVGL polling architecture** — since this polls rather than
  pushes, a card tap should be applied optimistically to the local screen (don't wait for the
  next poll to confirm) so the UI doesn't feel laggy; reconcile with the server's version on
  the next poll.
- **Power** — each unit needs USB-C power at the table; not addressed here (extension
  cords/power bank choice is a physical setup concern, not firmware).
- **Enclosure/mounting** — not addressed here.
- **Card image licensing/rights** — reusing `wwwroot/images/type*.png` is fine (same
  project's own assets), just noting the conversion step needs to happen once and be kept in
  sync if those images ever change.
