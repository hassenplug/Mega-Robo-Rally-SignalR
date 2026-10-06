# ESP32 Player Station (Freenove FNK0104S)

One station per seat: a Freenove **ESP32-S3 Display, FNK0104S** (4.0", 320×480, capacitive touch)
that shows one player's hand and sends their moves to the game server. It replaces a player's
phone. Design background: [../documents/ESP32_PLAYER_STATION_DESIGN.md](../documents/ESP32_PLAYER_STATION_DESIGN.md).

**Status.** The sketch **compiles** (checked with `arduino-cli` against the exact versions below).
It has **never been run on a real board**, and the flash script has never been run. Expect to fix
things on the bench — start with one unit.

## What it does

- Polls `GET /api/state` (about every 0.8 s) and draws this seat's robot: name and colour, flag/
  energy/card counts, status, any message, the 5 program registers and the hand (up to 12 cards).
- **Tap a hand card** → it goes into the first empty register. **Tap a register** → it goes back to
  the hand. **Tap the message** → confirms it. (Same calls the phone UI makes.)
- **Arrow + "Set Direction"** appear while the robot's direction isn't set yet. **"Shut Down"**
  appears while programming (game state 4).
- Before the game starts (state 1) it shows the robot / start-position picker for your seat.
- Cards are drawn as coloured tiles with a letter and name (U, R, L, B, 1, 2, 3, A, P, S, H). The
  phone's card images are not used.

## Seat identity

The first time a unit boots (or after a reset) it asks **"Pick this station's seat"** (1–6) and
stores the answer in flash. It then boots straight to that seat. To forget the seat, **press and hold
the top-left corner of the screen for 5 seconds**; the unit restarts at the picker.

## 1. Server side

Nothing to install, but check two things:

- The server must be reachable from the station's WiFi. `Urls` in `MRR/appsettings.json` is
  `http://*:5000` by default, which listens on every interface.
- The station uses a new read-only endpoint, **`GET /api/state`** (added to `MRR/Program.cs` for
  this). It returns the same JSON as `/api/alldata` **without** the SignalR broadcast that
  `/api/alldata` does on every call. **Rebuild and restart the server** so the endpoint exists.
  Check from any browser: `http://<server>:5000/api/state`.

## 2. One-time PC setup (Arduino IDE)

Versions are pinned because Freenove's tutorial warns newer library versions can break the build.

1. Install **Arduino IDE 2.x**.
2. **ESP32 board support:** File → Preferences → *Additional boards manager URLs*, add
   `https://espressif.github.io/arduino-esp32/package_esp32_index.json`. Then Tools → Board →
   Boards Manager → install **"esp32" by Espressif Systems, version 3.2.0** (not a newer one).
3. **ArduinoJson:** Tools → Manage Libraries → install **ArduinoJson 7.x** (built here with 7.4.2).
4. **Freenove libraries.** Clone or download
   <https://github.com/Freenove/Freenove_ESP32_S3_Display> and open `Libraries/FNK0104S/`. In the
   Arduino IDE use Sketch → Include Library → **Add .ZIP Library** for each of:
   - `TFT_eSPI_v2.5.43.zip`
   - `FT6336U_v1.0.2.zip`

   Do **not** add the other zips (LVGL, audio, etc.) — this sketch doesn't use them.
5. **TFT_eSPI setup files** (this tells TFT_eSPI which pins the FNK0104S uses). Your Arduino
   libraries folder is normally `Documents/Arduino/libraries/`.
   1. Unzip `TFT_eSPI_Setups_v1.2.zip`. Copy its **`TFT_eSPI_Setups`** folder into the libraries
      folder, next to `TFT_eSPI`.
   2. Copy the zip's **`User_Setup_Select.h`** over
      `Documents/Arduino/libraries/TFT_eSPI/User_Setup_Select.h`.
   3. **Edit that `User_Setup_Select.h`** — the version Freenove ships does not list the FNK0104S:
      - Make exactly one `#define` active, and make it
        `#define FNK0104S_4P0_320x480_ST7796` (comment out any other, such as
        `#define FNK0104B_2P8_240x320_ILI9341`).
      - Just before the final `#endif` of the `#ifdef FNK0086A…` chain, add:
        ```c
        #elif defined FNK0104S_4P0_320x480_ST7796
        #include <../TFT_eSPI_Setups/FNK0104S_4.0_320x480_ST7796.h>
        ```
      That setup file selects the ST7796 driver and the board's TFT pins (MOSI 11, SCLK 12, CS 10,
      DC 46, backlight 45).

## 3. Configure the station

In this folder, copy **`config.example.h` to `config.h`** and edit it:

| Setting | Meaning |
|---|---|
| `WIFI_SSID`, `WIFI_PASS` | The game LAN. ESP32-S3 is **2.4 GHz only**. |
| `SERVER_HOST`, `SERVER_PORT` | The game server. Prefer an **IP address** — `.local` names usually don't resolve on the ESP32. Port is the one in `appsettings.json` `Urls` (5000). |
| `POLL_INTERVAL_MS` | How often to poll (default 800). |
| `SCREEN_ROTATION` | `1` or `3`: landscape either way up (180° apart). Pick the one that puts the USB-C port where you want it. Touch is remapped to match. |

`config.h` is git-ignored so the WiFi password isn't committed. Don't hardcode the server's
hostname anywhere checked in.

## 4. Build and upload from the Arduino IDE

1. Open `esp32-player-station/esp32-player-station.ino`.
2. Tools menu:
   - **Board:** *ESP32S3 Dev Module*
   - **USB CDC On Boot:** *Enabled* (Freenove's sketches use this)
   - **Partition Scheme:** the default builds, but the program uses **~83 %** of the default app
     partition. If the build ever says it's too large, pick a larger scheme (e.g. *Huge APP*) —
     check the board's real flash size first (see "Not yet verified").
3. Plug the unit into the PC with a **data** USB-C cable (not charge-only).
4. Tools → **Port:** choose the new COM port, then click **Upload**.
5. If no port appears or the upload can't connect, put the board in **download mode**: hold
   **BOOT**, tap **RESET**, release BOOT, then pick the new port and upload again. Tap **RESET**
   afterwards to run it.
6. Open the Serial Monitor at **115200** if you want logs.

## 5. First boot at the table

1. Power on. It shows "Connecting to WiFi…", then "Connecting…". If it says **"Server not
   reachable"**, check `SERVER_HOST`/`SERVER_PORT`, that the server is running with the new
   `/api/state`, and that the unit and server are on the same network.
2. Pick the seat number on the screen.
3. In game state 1 it shows the robot and start-position picker when it's that seat's turn.
4. Once the game is running, play a card, set the direction, confirm, and check it matches what the
   GM page shows.

## 6. Flashing many units

All units run the **same** program (the seat is chosen on each unit), so build once and flash the
same file to each.

- **Arduino IDE:** repeat step 4 for each unit.
- **From a Raspberry Pi with `esptool`** (as in design doc §7): in the Arduino IDE use
  Sketch → **Export Compiled Binary**, and copy
  `esp32-player-station.ino.merged.bin` to the Pi. Then run
  [`tools/flash-all.sh`](tools/flash-all.sh) with that file. **That script has not been run.**

## 7. Troubleshooting

| Symptom | Likely cause |
|---|---|
| Build error about `FNK0104S…` or wrong pins / blank screen | The `User_Setup_Select.h` edit (§2 step 5) is missing or another board is still `#define`d |
| `config.h: No such file` | Copy `config.example.h` to `config.h` (§3) |
| Screen works, but taps land mirrored/rotated (e.g. the left card responds on the right) | The touch remap for the landscape rotation is untested. Switch `SCREEN_ROTATION` between 1 and 3 in `config.h`; if neither fixes it, the mapping in `mapTouch()` needs changing |
| Screen stays black | Backlight pin 45; check the TFT_eSPI setup is the FNK0104S one |
| "Server not reachable" | Wrong host/port, firewall, server not rebuilt with `/api/state` |
| Colours look inverted | The setup file turns inversion on for this panel; make sure it is the FNK0104S one |
| Upload fails | Use a data cable; try BOOT + RESET (§4 step 5) |

## Not yet verified (check on the first unit)

- Everything on the physical board: display, touch alignment, WiFi, all touch targets.
- The board's **flash and PSRAM size** (read the Model Info label or the Tools menu). The build
  assumes the default 4 MB flash layout.
- Whether text fits and card tiles look right at this size.
- Landscape touch mapping (`mapTouch()` in the sketch) was derived from the TFT_eSPI source, not tested.
- Cards that appear in a hand beyond 12 are not shown.
- Polling blocks for up to ~2.5 s when the server is unreachable, during which touches are
  not read.
