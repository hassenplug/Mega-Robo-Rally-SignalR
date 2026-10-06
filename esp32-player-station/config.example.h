// Copy this file to config.h (same folder) and edit. config.h is git-ignored so the game LAN's
// WiFi password never gets committed.

#pragma once

// The game LAN (see install/NETWORK_SETUP.md). The ESP32-S3 is 2.4 GHz only.
#define WIFI_SSID "CHANGE_ME"
#define WIFI_PASS "CHANGE_ME"

// The game server (the Raspberry Pi). An IP address is the most reliable choice; a hostname
// only works if the ESP32 can resolve it (".local" mDNS names generally can't be resolved by
// the plain WiFi stack). The port is the one in MRR/appsettings.json "Urls".
#define SERVER_HOST "192.168.0.10"
#define SERVER_PORT 5000

// How often to ask the server for the current state.
#define POLL_INTERVAL_MS 800

// Screen orientation: landscape either way up. 1 and 3 are 180 degrees apart; choose the one
// that puts the USB-C port where you want it. (Touch is remapped to match.)
#define SCREEN_ROTATION 1
