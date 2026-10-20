#!/bin/bash
# Open the GM status page (gmindex.html) full-screen once the game server answers.
# Installed to ~/.local/bin by install/git.sh; started at desktop login by
# ~/.config/autostart/mrr-kiosk.desktop. See PROJECT_STATUS.md section 1.8.
URL="http://127.0.0.1:5000/gmindex.html"
LOG="$HOME/mrr-kiosk.log"   # check this if the kiosk does not appear
exec >>"$LOG" 2>&1
echo "=== $(date) kiosk start: WAYLAND_DISPLAY='$WAYLAND_DISPLAY' DISPLAY='$DISPLAY'"

for i in $(seq 1 120); do
    curl -sf -o /dev/null "$URL" && break
    sleep 2
done

# Pi OS Bookworm+ names it "chromium"; older releases "chromium-browser".
BROWSER=$(command -v chromium || command -v chromium-browser) || { echo "no chromium installed"; exit 1; }

# Only force Wayland when the session is Wayland; on X11 that flag stops Chromium starting.
OZONE=()
[ -n "$WAYLAND_DISPLAY" ] && OZONE=(--ozone-platform=wayland)

exec "$BROWSER" --kiosk --noerrdialogs --disable-infobars --no-first-run \
    --disable-session-crashed-bubble --password-store=basic \
    "${OZONE[@]}" "$URL"
