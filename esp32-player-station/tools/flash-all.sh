#!/usr/bin/env bash
# Flash the same compiled firmware to every ESP32-S3 station plugged into this machine.
#
# NOT YET RUN -- written from esptool's documented usage, never tried on a real unit.
# Try it on one unit first.
#
# Usage:  ./flash-all.sh path/to/esp32-player-station.ino.merged.bin
#   (Arduino IDE: Sketch > Export Compiled Binary, then copy the *.merged.bin here.)
#
# Needs esptool:  pip install esptool
# Ports are discovered at run time (/dev/ttyACM*); do not hardcode them -- hub ports aren't stable.

set -u

BIN="${1:-}"
if [[ -z "$BIN" || ! -f "$BIN" ]]; then
  echo "usage: $0 path/to/esp32-player-station.ino.merged.bin" >&2
  exit 2
fi

if command -v esptool.py >/dev/null 2>&1; then
  ESPTOOL=(esptool.py)
elif command -v esptool >/dev/null 2>&1; then
  ESPTOOL=(esptool)
else
  echo "esptool not found (pip install esptool)" >&2
  exit 2
fi

shopt -s nullglob
ports=(/dev/ttyACM*)
if [[ ${#ports[@]} -eq 0 ]]; then
  echo "no /dev/ttyACM* devices found; plug in the stations (hold BOOT + tap RESET if one won't show)" >&2
  exit 1
fi

ok=0
fail=0
for port in "${ports[@]}"; do
  echo "== $port =="
  # A merged image goes at offset 0 (it already contains bootloader + partitions + app).
  if "${ESPTOOL[@]}" --chip esp32s3 --port "$port" --baud 921600 write_flash 0x0 "$BIN"; then
    ok=$((ok + 1))
  else
    echo "!! flashing $port failed" >&2
    fail=$((fail + 1))
  fi
done

echo "done: $ok flashed, $fail failed"
[[ $fail -eq 0 ]]
