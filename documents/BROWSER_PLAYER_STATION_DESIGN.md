# Browser Player Station (Pi Zero 2 W + 3–4" Capacitive Touch, USB to the Game Pi)

**Status:** **Not pursued** — the ESP32 station was chosen instead (2026-10-06). Kept for
reference only; nothing ordered, built or tested. Written 2026-10-06.
**Related:** [ESP32_PLAYER_STATION_DESIGN.md](ESP32_PLAYER_STATION_DESIGN.md) (the
alternative: no browser, custom firmware), [PHONE_LOGIN_DESIGN.md](PHONE_LOGIN_DESIGN.md),
[MRR/wwwroot/index.html](../MRR/wwwroot/index.html),
[MRR/wwwroot/js/loadrobots.js](../MRR/wwwroot/js/loadrobots.js),
[install/NETWORK_SETUP.md](../install/NETWORK_SETUP.md)

## 1. Goal

A dedicated per-seat player station that **runs a web browser** showing the existing phone UI
(`index.html`), has a **3"–4" capacitive touch display**, and **connects to the main
controller (the Raspberry Pi 5) over USB**. Six stations replace the six phones.

Because the station runs a real browser, it needs **no changes to `MRR/`** and none of the
custom firmware the ESP32 design needs: SignalR, the seat-number cookie (`mrr_seat`), the setup
screen and the direction picker all work as they do on a phone. That is the main reason to
prefer this over the ESP32 route; the cost is a heavier, more expensive unit.

## 2. Proposed design

| Part | Choice | Why |
|---|---|---|
| Computer | Raspberry Pi Zero 2 W | Runs Chromium; its micro-USB **OTG** port can act as a USB device (gadget mode) |
| Display | Waveshare 4" HDMI LCD (C), 720×720, capacitive | In the 3–4" range; I2C touch keeps the Zero's single data USB port free |
| Link to main Pi | USB Ethernet gadget (`g_ether`) over the Zero's OTG port | Gives each station an IP link to the game server over a USB cable |
| Software | Raspberry Pi OS Lite + Chromium in kiosk mode | Loads the same page the phones use |

### 2.1 Why this display, and why I2C touch

The Zero 2 W has **one** USB data port (OTG) and **no DSI connector**. That drives two choices:

- **HDMI display, not DSI.** DSI displays (see §6) need a bigger Pi.
- **Touch over I2C, not USB.** The Waveshare 4" (C) has a back switch selecting USB or I2C
  touch; the wiki says to set it to **I2C** for Raspberry Pi. If touch used USB, it would compete
  with the gadget link for the one port.

The panel is **square (720×720)**, not the phone's portrait shape, so `index.html` will need a
check (and probably a small CSS pass) to fit. Treat that as real work, not a given.

### 2.2 The USB link

- The Zero's micro-USB **OTG** port goes to a USB-A port on the main Pi (through a powered hub,
  §4). It carries **both data and power** for the Zero.
- On the Zero: `dtoverlay=dwc2` in `config.txt` and `modules-load=dwc2,g_ether` in
  `cmdline.txt` (the standard Ethernet-gadget recipe — see Adafruit's guide, §5). A `usb0`
  network interface then appears on both ends.
- On the main Pi: six `usb*` interfaces will appear, one per station. Proposed: bridge them into
  one subnet served by a small DHCP server, so every station reaches the game server at one
  fixed address. Give each gadget a **fixed MAC** (the `g_ether` `host_addr`/`dev_addr` options)
  so the interface names and addresses are stable across reboots and re-plugs.
- The server must listen on that interface: `Urls` in
  [MRR/appsettings.json](../MRR/appsettings.json) currently decides what it binds to. The
  station's kiosk URL should come from its own config file, not be hardcoded in the repo
  (project rule: never hardcode the server hostname).

### 2.3 Seat identity

`mrr_seat` is a plain browser cookie, so the station's Chromium profile can keep it between
boots — each station stays bound to its seat with no extra work. Provisioning a station to a
seat is then just logging in once on it.

## 3. Software setup (per station)

1. Flash Raspberry Pi OS Lite (64-bit) to the microSD card; enable SSH; set a unique hostname.
2. Enable the USB Ethernet gadget (§2.2).
3. Configure the display for 720×720. Waveshare's wiki gives
   `hdmi_group=2`, `hdmi_mode=87` and an `hdmi_timings=720 0 100 20 100 720 0 20 8 20 0 0 0 60 0 48000000 6`
   line for `config.txt`, plus a touch overlay for I2C. **I only read these off the wiki — I did
   not test them, and the overlay name on that page (`waveshare-4dpic-*`) is for the DPI
   family, so confirm the correct one for the HDMI (C) panel before relying on it.**
4. Install a minimal display stack and Chromium; autostart Chromium in kiosk mode pointing at
   the game server, with the cursor hidden and screen blanking off.
5. Make the whole image reproducible: a script in `install/` (or a flashable image) so six
   stations are identical except hostname and MAC.

## 4. Bill of materials (per station, ×6)

Items with a link were found by search on 2026-10-06; check price and stock yourself. Items
without a link are generic and I did not verify a specific product.

| Item | Notes | Link |
|---|---|---|
| Raspberry Pi Zero 2 W | 1 GHz quad-core, 512 MB RAM, mini-HDMI, micro-USB OTG | [raspberrypi.com](https://raspberrypi.com/products/raspberry-pi-zero-2-w) |
| 4" HDMI capacitive IPS LCD (C), 720×720 | 5-point touch, laminated glass, USB or I2C touch. Seen at **$72.95** at PiShop | [Waveshare wiki](https://www.waveshare.com/wiki/4inch_HDMI_LCD_%28C%29) · [PiShop](https://www.pishop.us/product/4inch-hdmi-capacitive-touch-ips-lcd-display-c-720-720-fully-laminated-screen/) |
| Mini-HDMI → HDMI cable/adapter | The Zero needs its own cable to the display — Waveshare says an extra HDMI cable is required. Check what the display ships with | — |
| Dupont/header wiring + 40-pin header for the Zero | Zero 2 W may ship without a header; I2C touch and any power wiring need it | — |
| microSD card, 16 GB+ | One per station | — |
| Micro-USB (OTG-capable) data cable to the main Pi | Must carry **data**, not power-only | — |

**Shared:**

| Item | Notes | Link |
|---|---|---|
| Powered USB 3 hub (≥ 6 ports, own supply) | Six stations plus the Pi's other devices will not fit the Pi 5's four ports or its power budget (the ESP32 doc reaches the same conclusion) | — |

**Roughly:** about **$15** for the Zero plus about **$73** for the display, so over **$90 per
station before cables, card and case — over $540 for six**. This is a ballpark from two prices I
saw, not a quote.

## 5. Reference reading

- [Raspberry Pi Zero 2 W](https://raspberrypi.com/products/raspberry-pi-zero-2-w) — official page
- [Waveshare 4inch HDMI LCD (C) wiki](https://www.waveshare.com/wiki/4inch_HDMI_LCD_%28C%29) —
  setup, resolution and touch notes
- [Adafruit: Turning your Pi Zero into a USB gadget — Ethernet gadget](https://learn.adafruit.com/turning-your-raspberry-pi-zero-into-a-usb-gadget/ethernet-gadget)
  — the `dwc2` / `g_ether` setup

## 6. Alternative: DSI display on a bigger Pi

If the Zero proves too slow or the square panel too awkward, a Pi with a **DSI** connector
(Pi 3/4/5, not the Zero) can use a DSI touch display, which also removes the HDMI cable. These
were returned by search and not checked against the Pi model you would use:

- [3.5" DSI Capacitive Touch Display (640×480) — The Pi Hut](https://thepihut.com/products/3-5-dsi-capacitive-touch-display-for-raspberry-pi-640x480)
- [Waveshare 3.5inch DSI LCD (E), 640×480](https://www.waveshare.com/product/3.5inch-dsi-lcd-e.htm)
- [Waveshare 3.5inch DSI LCD (H), 480×800](https://www.waveshare.com/product/displays/3.5inch-dsi-lcd-h.htm)

A full Pi costs more and is bigger than a Zero, and **USB gadget mode needs a Pi whose USB
port can act as a device** (the Pi 4's USB-C can; its USB-A ports cannot) — confirm before
choosing one.

## 7. Open items and risks

- **Chromium on 512 MB.** The Zero 2 W is tight for a browser. Unverified: whether `index.html`
  and the SignalR connection run smoothly. **Test one unit before ordering six.**
- **Square display vs. the phone layout** (§2.1).
- **Display power.** I did not confirm how the display is powered (its USB-C port, or the Pi's
  header) or its current draw. This decides the hub's power budget.
- **Touch overlay and I2C wiring** for the HDMI (C) panel (§3 step 3).
- **Six-interface USB networking** on the main Pi — bridge, DHCP and stable naming are a
  proposal, not tested (§2.2).
- **Cost** versus the ESP32 route and versus just keeping phones.
- **Enclosure.** Not designed. The ESP32 doc's OpenSCAD approach could be reused once the
  display's mechanical drawing is in hand (Waveshare's wiki has one; I did not read the
  dimensions).
- **Security:** the station runs the game UI with no login, the same trust model as the phones
  on the closed game LAN. Don't expose the USB-side network beyond the game Pi.

## 8. Suggested build order

1. Buy **one** Zero 2 W and one display; bring up the display and touch.
2. Get Chromium kiosk showing `index.html` from the game server over the USB link.
3. Fix the layout for 720×720; check touch accuracy and the direction picker.
4. Run a full hand lifecycle (seat claim → program → direction → next turn).
5. Decide on the other five units, the hub and the enclosure.
