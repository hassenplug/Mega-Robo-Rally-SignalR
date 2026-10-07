# Game Network Setup — TP-Link TL-WR802N (WISP Client Router Mode)

**Status:** Plan — not yet configured against hardware, **except** robot IPs: all 7 robots
have fixed addresses `192.168.0.101`–`.107` as of 2026-10-01 (table in
[RobotConnections.md](../documents/RobotConnections.md)). That is a `192.168.0.x` network, not
the subnets this plan proposes below, so §4 steps 3, 5 and 6 are done for the robots only;
the Pi's reservation and the rest of the plan are still open.
**Router:** TP-Link N300 Wireless Portable Nano Travel Router, TL-WR802N
**Last updated:** 2026-10-01
**Related:** [PROJECT_STATUS.md](../PROJECT_STATUS.md) §1 (Pi setup),
[documents/RobotConnections.md](../documents/RobotConnections.md) (why robot IPs must be
stable)

## 1. Goal

Give the Pi, all 6 robots, and the player-facing devices (phones and/or the planned
[ESP32 stations](../documents/ESP32_PLAYER_STATION_DESIGN.md)) one shared game network, while
also getting that network internet access by joining the home WiFi — without merging the
game devices into the home network's own address space.

## 2. Mode: WISP Client Router

The TL-WR802N supports this natively (TP-Link's own term is "WISP mode"): its wireless radio
joins an existing WiFi network as an uplink (the home router), while it broadcasts a second,
separate SSID downstream for its own LAN. In this mode its single Ethernet port becomes a
LAN port (not WAN) — useful for wiring the Pi in directly.

## 3. Topology

```
Home router (WiFi) ⇠⇢ [TL-WR802N wireless uplink]
                              │
                     TL-WR802N's own SSID (new, separate network)
                              │
        ┌─────────────────────┼─────────────────────┐
   Pi (wired, via the        6 robots           phones / ESP32
   router's one Ethernet    (WiFi)               stations (WiFi)
   port)
```

Wiring the Pi in rather than putting it on WiFi keeps the server off the same 2.4GHz radio
that every robot and player device is contending for.

## 4. Setup steps

1. **Admin page:** connect to the TL-WR802N's own WiFi, browse to `tplinkwifi.net` (or the IP
   printed on the device's label — varies by hardware revision).
2. **Operation Mode → WISP.** The wizard scans for the home WiFi, asks for its password (the
   uplink), then asks for a **new SSID/password** for the downstream network — this is what
   the Pi, robots, and phones/ESP32 stations join. Use a distinct name (e.g. `MRR-Game`) so
   it's obviously not the home network.
3. **Downstream LAN subnet:** set it to something that won't collide with the home router's
   subnet — e.g. if home is `192.168.1.x`, use `192.168.8.x` for the TL-WR802N's LAN side.
4. **Wire the Pi** into the router's single Ethernet port (LAN, in WISP mode).
5. **DHCP reservations (fixed IPs), by MAC address, for the Pi and every robot.** Not just
   tidiness — `RobotConnections`/`RobotConnection.cs` reads each robot's IP straight out of
   the `RobotBases`/`Robots` table and dials that address directly
   ([RobotConnections.md](../documents/RobotConnections.md)). Without a reservation, a robot
   that gets a different DHCP lease after a reboot leaves the server dialing a stale IP until
   the DB is corrected.
6. **Re-point the DB's stored robot IPs once, after the move.** These robots are joining a
   brand-new network for the first time, so whatever's currently in `RobotBases.IPAddress`
   almost certainly won't match. Either set the DHCP reservations to match the *existing* DB
   values, or update the DB to match the *new* addresses (the connection screen's "Update IP"
   sets `RobotBases.IPAddress` by hand).
7. **Keep using `mrobopi.local`**, not a hardcoded IP, for the Pi itself. Raspberry Pi OS's
   Avahi/mDNS works fine within a single subnet like this one, and `CLAUDE.md` already
   requires never hardcoding that hostname.

## 5. Capacity caveat — test before relying on it at an event

The TL-WR802N is a "nano" travel router, marketed for a handful of devices in a hotel room:
single-band 2.4GHz, one radio. This setup asks it to simultaneously carry 6 robots and up to
6 player devices (phones or ESP32 stations) — a heavier concurrent-client load than its usual
use case. Likely fine for a turn-based game with modest per-message payloads, but **do a dry
run with every device connected at once before an actual game night**, rather than assuming
it scales the way the marketing implies.

## 6. Reaching the Pi from a computer on the home network

WISP mode NATs the downstream network away from the home network by design (§2) — a home
computer will **not** be able to browse to the Pi's downstream IP or resolve
`mrobopi.local` (mDNS doesn't cross the router boundary) without extra configuration. Two
options, different tradeoffs:

1. **Port-forward on the TL-WR802N (recommended — keeps the isolation this plan is built
   for).** Forward the specific ports actually needed — `5000` for the web UI/API
   (`MRR/appsettings.json`'s `"Urls": "http://*:5000"`), `22` for SSH if wanted — to the Pi's
   downstream IP. Also set a DHCP reservation for the TL-WR802N itself on the **home**
   router, so its home-facing IP doesn't drift. From a home computer, connect to
   `<TL-WR802N's home-network IP>:5000` (there is no `mrobopi.local` shortcut across the NAT
   boundary).
2. **Switch the TL-WR802N to AP/Bridge mode instead of WISP.** Puts the Pi, robots, and
   phones/ESP32 stations on the exact same flat network as the rest of the house — any home
   device reaches `mrobopi.local` directly, no forwarding needed — but gives up the
   isolation §1–§5 are built around. That matters more than usual here:
   `MRR/appsettings.json`'s Admin API **runs arbitrary SQL**, gated only by a hardcoded API
   key (`"0555"`, `AllowRemote: true`, `AllowedNetworks` empty = any address, key still
   required). That same string, `0555`, is also the GM login code baked into
   `js/loadrobots.js` — visible in the page source to anyone. That's tolerable only because
   today just the isolated game LAN can reach it; bridging onto the home network would expose
   it to every other device in the house, with only that one already-public string in the
   way.

## 7. Open items

- Confirm the home router's LAN subnet before picking the downstream subnet in step 3, to
  guarantee no overlap.
- Decide whether player devices are phones, ESP32 stations, or a mix for the first real test
  on this network.
- After first connecting all 6 robots, check (using the connection screen's Update IP
  fields) that `RobotBases.IPAddress` is right for the new subnet.
- Decide between §6's port-forward vs. bridge approach if home-network access to the Pi is
  wanted, and configure it.
