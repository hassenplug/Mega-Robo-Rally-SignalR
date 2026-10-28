# Game Network Setup — Ubiquiti UniFi Travel Router (UTR) as Client-Uplink Router

**Status:** Plan — not yet configured against hardware, **except** robot IPs: all 7 robots
have fixed addresses `192.168.0.101`–`.107` as of 2026-10-01 (table in
[RobotConnections.md](../documents/RobotConnections.md)). That is a `192.168.0.x` network, not
the subnets this plan proposes below, so §4 steps 3, 5 and 6 are done for the robots only;
the Pi's reservation and the rest of the plan are still open.
**Router:** Ubiquiti UniFi Travel Router, model `UTR` (confirmed from the unit's underside label:
"Travel Router, M/N: UTR, Ubiquiti Inc."; box SKU `UTR-US`), replacing the TP-Link
TL-WR802N on 2026-10-25. Its device ID / MAC-style identifier is `A4F8FF74B964` (box and unit
label) — the likely basis for the home router's DHCP reservation (§6); confirm it against the
client list, since a label ID is not always the WAN-side MAC.
**Specs** (from third-party retailer listings and one review, which disagree in places — check
Ubiquiti's spec sheet): dual-band 2.4/5 GHz, 2×2, 2 × 1 GbE RJ45, USB-C power (5 V / 2 A,
adapter sold separately; ~5 W max), uplink from Ethernet, WiFi, or USB tethering from a phone,
WireGuard VPN, 1.14" status display.
**Last updated:** 2026-10-25
**Related:** [PROJECT_STATUS.md](../PROJECT_STATUS.md) §1 (Pi setup),
[documents/RobotConnections.md](../documents/RobotConnections.md) (why robot IPs must be
stable)

## 1. Goal

Give the Pi, all 6 robots, and the player-facing devices (phones and/or the planned
[ESP32 stations](../documents/ESP32_PLAYER_STATION_DESIGN.md)) one shared game network, while
also getting that network internet access by joining the home WiFi — without merging the
game devices into the home network's own address space.

## 2. Mode: client uplink over WiFi, own LAN downstream

The goal is unchanged: the router's radio joins the home WiFi as an uplink while it
broadcasts a second, separate SSID for its own LAN. The old TL-WR802N called this "WISP
mode"; reviews list a WiFi-uplink (WISP) role for the UTR, but its exact name and whether it
needs the UniFi app to set up are **not yet confirmed** — record them here after setup. Wire the Pi
into a LAN port, not the WAN port.

## 3. Topology

```
Home router (WiFi) ⇠⇢ [UTR wireless uplink]
                              │
                     the UTR's own SSID (new, separate network)
                              │
        ┌─────────────────────┼─────────────────────┐
   Pi (wired, via a          6 robots           phones / ESP32
   router LAN port)         (WiFi)               stations (WiFi)
```

Wiring the Pi in rather than putting it on WiFi keeps the server off the same 2.4GHz radio
that every robot and player device is contending for.

## 4. Setup steps

1. **Admin page:** connect to the UTR's own WiFi and follow the setup flow in Ubiquiti's
   docs (the address, and whether setup is through the UniFi app, are unverified here).
2. **Set the uplink to the home WiFi.** Give it the home WiFi's password (the uplink), then a
   **new SSID/password** for the downstream network — this is what
   the Pi, robots, and phones/ESP32 stations join. Use a distinct name (e.g. `MRR-Game`) so
   it's obviously not the home network.
3. **Downstream LAN subnet:** set it to something that won't collide with the home router's
   subnet — e.g. if home is `192.168.1.x`, use a different one (e.g. `192.168.8.x`) for the UTR's LAN side.
4. **Wire the Pi** into a LAN port on the router (not the WAN port, which is the uplink in a
   wired setup).
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

The UTR is a travel router, marketed for a handful of devices in a hotel room. The old
TL-WR802N was single-band 2.4GHz; the UTR is dual-band (client limit not found), but the
robots and ESP32 stations are 2.4GHz-only, so its 2.4GHz radio is still the one that matters.
It is also the radio listed at the lower rate (300 Mbps class) and ~13 dBm. This setup asks the router to simultaneously carry 6 robots and up
to 6 player devices (phones or ESP32 stations) — a heavier concurrent-client load than its
usual use case. Likely fine for a turn-based game with modest per-message payloads, but **do a dry
run with every device connected at once before an actual game night**, rather than assuming
it scales the way the marketing implies.

## 6. Reaching the Pi from a computer on the home network

Client-uplink mode NATs the downstream network away from the home network by design (§2) — a home
computer will **not** be able to browse to the Pi's downstream IP or resolve
`mrobopi.local` (mDNS doesn't cross the router boundary) without extra configuration. Two
options, different tradeoffs:

1. **Port-forward on the UTR (recommended — keeps the isolation this plan is built
   for).** Forward the specific ports actually needed — `5000` for the web UI/API
   (`MRR/appsettings.json`'s `"Urls": "http://*:5000"`), `22` for SSH if wanted — to the Pi's
   downstream IP. Also set a DHCP reservation for the UTR itself on the **home**
   router, so its home-facing IP doesn't drift. From a home computer, connect to
   `<UTR's home-network IP>:5000` (there is no `mrobopi.local` shortcut across the NAT
   boundary).
2. **Switch the UTR to a bridge/AP-style mode instead of NAT.** Puts the Pi, robots, and
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
