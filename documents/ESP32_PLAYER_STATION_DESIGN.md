# ESP32-S3 CYD Player Input Stations (Replacing Phones)

**Status:** First firmware written 2026-10-06 in [`esp32-player-station/`](../esp32-player-station/README.md)
— it **compiles but has never run on a board**. It differs from this design in a few ways, see
§9. Board identified 2026-10-02; driver, touch and toolchain verified from Freenove's tutorial
2026-10-04 (§1.1).
**Date:** 2026-09-28 (updated 2026-10-04)
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

**Verified 2026-10-04** against Freenove's published tutorial (the `freenove.com/tutorial`
page is JS-rendered; the same material is in
[github.com/Freenove/Freenove_ESP32_S3_Display](https://github.com/Freenove/Freenove_ESP32_S3_Display):
`Tutorial_With_Touch/Tutorial.pdf`, `Tutorial_With_Touch/Sketches/`, `Libraries/FNK0104S/`).
The repo covers four variants (A/B 2.8", N 3.5", S 4.0"); ours is **FNK0104S**.

- **Display:** 4.0" 320×480, driver **ST7796** (per the tutorial PDF and the repo's
  datasheet folder; the repo README says ST7789, which conflicts — trust the PDF)
- **Touch:** capacitive **FT6336U/G** over I2C — SDA 16, SCL 15, RST 18, INT 17
- **Other onboard:** USB-C (native USB; sketches use "USB CDC On Boot" = Enable), BOOT button
  on GPIO0, battery connector with ADC sense, SD card slot, speaker (ES8311 codec),
  microphone, WS2812 RGB LED, onboard antenna
- **Toolchain:** Arduino IDE, **esp32 core 3.2.0**, board "ESP32S3 Dev Module". Libraries come
  as zips in the repo's `Libraries/FNK0104S` and are installed via Add .ZIP Library; the
  tutorial warns **not to update them** (newer versions can break the build). **LVGL v8.4.0**,
  TFT_eSPI 2.5.43 + Freenove's TFT_eSPI_Setups. Sketches select the board with
  `#define FNK0104S_4P0_320x480_ST7796` at the top.
- **Reference sketches** to start from: 9.1 (WiFi), 11.1 (touch), 13.1/19.1 (LVGL),
  18.1 (LVGL multifunction)

**Still not verified** (not stated in the tutorial text reviewed — read from the board's
Model Info label or the Arduino Tools menu):

- flash and PSRAM size (decides whether card images can be baked in as C-arrays, §4)
- exact TFT pin assignments for FNK0104S (in the TFT_eSPI setup under `Libraries/FNK0104S`)
- whether the battery connector charges (TP4054 charger appears in the datasheet folder) —
  matters for §7 "charging"

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

- **Framework:** Arduino core for ESP32-S3 (**core 3.2.0**, pinned) + **LVGL 8.4.0** with
  TFT_eSPI and the FT6336U touch library, all from Freenove's `Libraries/FNK0104S` zips (see
  §1.1 — don't upgrade them). `HTTPClient` for polling/posting, `ArduinoJson` for parsing.
  Note LVGL 8.x APIs (`lv_img_conv`, `lv_img_dsc_t`), not 9.x.
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
  - `case/case.scad` + exported `.stl` files — 3D-printed enclosure (§7a)
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

## 7a. 3D-printed case (OpenSCAD)

A printed enclosure per station, designed in **OpenSCAD** so every dimension is a named
parameter. Files go in `esp32-player-station/case/` (`case.scad`, plus exported `.stl` files
checked in alongside it, since the Pi/printer workflow shouldn't require OpenSCAD).

**Requirements**
- **Expose the touch screen** — open window over the active glass area, no lip that blocks
  edge touches (the card row sits near the edges, §4).
- **Expose the USB-C port** — a cutout aligned with the connector, large enough for a
  fat-molded cable plug, not just the bare receptacle. This is also the charging and flashing
  port (§7), so it must stay reachable with the unit fully assembled.

**What the board photos show** (Freenove repo `Picture/FNK0104S_Top.png` / `_Bottom.png` — a
proportional view only; the repo publishes **no mechanical drawing or mm dimensions**):
- Landscape PCB, roughly 1.9:1. The glass is narrower than the PCB: the PCB extends past the
  glass on the left and right as **mounting ears**, each with a **mounting hole at both
  corners** (4 holes total) — natural screw points for the case.
- **USB-C is on the left short edge, centered vertically**, with **RESET above and BOOT below**
  it on the same edge. A single cutout there can expose the port and, if wanted, small
  pinholes/a slot for the buttons (BOOT+RESET are needed to enter download mode, §7 — though
  with native USB, `esptool` can usually reset the board itself).
- The **microphone hole** is on the front, top-left ear; the case must not cover it if audio
  input is ever used (not planned).
- Rear connectors (speaker, I2C, battery, UART, SD slot, RGB LED) are not needed in normal
  use. The **SD slot** is on the rear, lower right; leave it enclosed for v1.
- Orientation: with the USB-C on the left, the screen is landscape 480×320 (§4 layout). That
  puts the cable exiting on the player's left; decide per seat/table layout whether to mirror
  the mount (LVGL can rotate the display 180° instead if the cable should exit right).

**Measure before modeling** — take these from the actual unit with calipers and put them in
the parameter block; do not model from the photos:
- PCB length × width × thickness; total stack height (PCB + glass + rear components)
- glass outline and **active-area** window offsets from the PCB edges
- mounting-hole diameter and center positions relative to the PCB corners
- USB-C receptacle center height above the PCB, its width/height, and how far it protrudes
  past the PCB edge
- RESET/BOOT positions on the left edge

**Design approach**
- Two-part shell: **front bezel** (window + USB-C relief) and **rear shell**, joined by M2/M2.5
  screws through the existing four mounting holes into printed bosses or heat-set inserts —
  no board modification.
- Standoffs/posts under the mounting holes set the stack height so the glass sits flush with,
  or just proud of, the bezel opening.
- Parameter block at the top of `case.scad`:
  ```openscad
  // --- MEASURE FROM THE UNIT (mm) — placeholders until measured ---
  pcb_l = 0; pcb_w = 0; pcb_t = 0;        // board outline and thickness
  glass_l = 0; glass_w = 0;               // active glass area
  glass_off_x = 0; glass_off_y = 0;       // glass origin from PCB corner
  hole_d = 0; hole_inset_x = 0; hole_inset_y = 0;
  usb_w = 0; usb_h = 0; usb_z = 0;        // USB-C opening, height above PCB
  // --- print/fit tolerances ---
  clearance = 0.4;  wall = 2.0;  screw_d = 2.2;
  ```
- Generate both parts from the same parameters so a fit correction is a one-line change, and
  print one test unit first (§6 step 1 already has a single bench unit).
- Leave a **vent/relief** option for the rear shell; the ESP32-S3 running WiFi continuously and
  charging a battery will warm the enclosure, and a sealed shell traps that.
- Cable strain relief and desk-mounting (stand angle vs. flat) are open: a built-in 60–70°
  tilted stand tends to read better for a hand of cards than a flat case, but is a table-setup
  preference — parameterize `tilt_deg` rather than deciding now.

## 9. As built (2026-10-06)

The first firmware, in [`esp32-player-station/`](../esp32-player-station/README.md), departs from
the sections above:

- **No LVGL.** It draws straight with TFT_eSPI (coloured tiles with a letter and name). Only
  TFT_eSPI, FT6336U and ArduinoJson are needed; the other pinned Freenove zips aren't used.
  LVGL and the converted card art (§4) remain possible later.
- **Landscape 480×320**, as §4 wants (the panel is 320×480 and is rotated in software). The
  touch controller reports in the panel's native portrait frame, so the sketch remaps touch
  coordinates; `SCREEN_ROTATION` (1 or 3) in `config.h` chooses which way up. The remap is
  derived from TFT_eSPI's ST7796 rotation settings and is **untested on hardware**.
- **Polls a new `GET /api/state`**, not `/api/alldata` (§2/§3). `/api/alldata` broadcasts to every
  SignalR client on each call, so six stations would flood the phones and GM page. `/api/state`
  returns the same JSON with no broadcast. This is an additive change to `MRR/Program.cs`, so
  "no server changes" (§2) no longer holds.
- **TFT pins are now known** from Freenove's `FNK0104S_4.0_320x480_ST7796.h` setup: MOSI 11,
  SCLK 12, CS 10, DC 46, backlight 45, ST7796 with inversion on. Freenove's shipped
  `User_Setup_Select.h` does not list the FNK0104S, so the README has the manual edit.
- The program used about **83 %** of the default 1.3 MB app partition when built.

## 8. Open items / risks

- **Case dimensions** — none are published by Freenove; they must be measured from a unit
  (§7a). The case can't be finalized until then.

- **Remaining unverified hardware details** (flash/PSRAM size, TFT pins, battery charging —
  list in §1.1) — check on the bench with the first unit. Driver, touch controller and
  toolchain are now confirmed from Freenove's tutorial.

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
