#!/bin/bash
# Open the GM status page (gmindex.html) full-screen once the game server answers.
# Installed to ~/.local/bin by install/git.sh --tft-screen; started at desktop login by
# ~/.config/autostart/mrr-kiosk.desktop. See PROJECT_STATUS.md section 1.8.
URL="http://127.0.0.1:5000/gmindex.html"
for i in $(seq 1 120); do
    curl -sf -o /dev/null "$URL" && break
    sleep 2
done
exec chromium --kiosk --noerrdialogs --disable-infobars --no-first-run \
    --disable-session-crashed-bubble --password-store=basic \
    --ozone-platform=wayland "$URL"
