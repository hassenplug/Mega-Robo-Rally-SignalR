#!/bin/bash
# install/git.sh - provision a fresh Raspberry Pi for Mega Robo Rally.
#
# Before running (see PROJECT_STATUS.md section 1.1):
#   1. Write Raspberry Pi OS (64-bit) to the SD card with Raspberry Pi Imager. In its
#      settings: hostname mrobopi, user mrr, enable SSH, configure the network.
#   2. Boot, then:  ssh mrr@mrobopi
#   3. sudo apt install -y git
#      git clone https://github.com/hassenplug/Mega-Robo-Rally-SignalR.git
#      cd Mega-Robo-Rally-SignalR && ./install/git.sh
#
# Usage: ./install/git.sh [options]      (run as user mrr, NOT with sudo)
#   --sense-hat   Pi has a Sense HAT: enable SPI and make the game host require /dev/spidev0.0.
#                 Default is no Sense HAT (e.g. Pi 4 + SPI TFT screen), where SPI is left alone.
#   --tft-screen  Pi 4 + GeeekPi 3.5" SPI touch screen: add the display overlay to config.txt
#                 and open gmindex.html full-screen in Chromium at desktop login. Needs the
#                 Raspberry Pi OS desktop image (Chromium + auto-login). Not with --sense-hat.
#   --remote-db   Let other machines connect to MariaDB (comments out bind-address).
#                 Exposes the database to the whole network - game LAN only. The game itself
#                 does not need it: appsettings.json connects to 127.0.0.1.
#   --reset-db    Drop and reload the rally database even if it already has tables.
#                 DESTROYS all games, boards and players in it.
#   --no-start    Install the services but do not start them.
#
# Safe to re-run: every step checks before it changes anything, and the database is left
# alone once it has tables unless --reset-db is given. Reboot afterwards (see the end).
# Not run by the author on a blank Pi - read the output of the first run.
set -euo pipefail

SENSE_HAT=no; TFT_SCREEN=no; REMOTE_DB=no; RESET_DB=no; NO_START=no
for a in "$@"; do
    case "$a" in
        --sense-hat) SENSE_HAT=yes ;;
        --tft-screen) TFT_SCREEN=yes ;;
        --remote-db) REMOTE_DB=yes ;;
        --reset-db)  RESET_DB=yes ;;
        --no-start)  NO_START=yes ;;
        -h|--help)   sed -n '2,/^set -euo/p' "$0" | sed '$d'; exit 0 ;;
        *) echo "unknown option: $a (try --help)" >&2; exit 2 ;;
    esac
done

[ "$SENSE_HAT" = no ] || [ "$TFT_SCREEN" = no ] || \
    { echo "--sense-hat and --tft-screen both need SPI0 - pick one" >&2; exit 2; }

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INSTALL="$REPO/install"
DOTNET_DIR="$HOME/.dotnet"
BOOT_CFG=/boot/firmware/config.txt
# GeeekPi 3.5" panel: ILI9486 display + XPT2046 touch on SPI0, driven by the kernel's own
# piscreen overlay (no vendor LCD-show script). rotate=0 is landscape on this panel.
TFT_OVERLAY='dtoverlay=piscreen,drm,speed=16000000,rotate=0'
DB_CNF=/etc/mysql/mariadb.conf.d/50-server.cnf

info() { echo; echo "==> $*"; }
warn() { echo "!! $*" >&2; }
die()  { echo "FAILED: $*" >&2; exit 1; }

# ---------------------------------------------------------------- preconditions
[ "$(id -u)" -ne 0 ] || die "run as user mrr, not root (the script uses sudo where it needs it)"
[ "$(id -un)" = mrr ] || die "must run as user 'mrr' (the services and paths assume it); you are '$(id -un)'"
[ -f "$INSTALL/MRRDatabase.sql" ] || die "cannot find $INSTALL/MRRDatabase.sql - run this from a full clone of the repo"
sudo -v || die "sudo is required"
[ "$REPO" = /home/mrr/Mega-Robo-Rally-SignalR ] || \
    warn "repo is at $REPO; the services expect /home/mrr/Mega-Robo-Rally-SignalR (MRR_REPO in /etc/default/mrr)"
[ "$(hostname)" = mrobopi ] || \
    warn "hostname is '$(hostname)', not mrobopi - appsettings.json and /etc/default/mrr assume mrobopi"

# ---------------------------------------------------------------- OS + packages
info "updating the OS and installing packages"
sudo apt-get update -y
sudo apt-get upgrade -y
# libicu-dev pulls in the ICU runtime library .NET needs on this release.
sudo apt-get install -y git curl ca-certificates mariadb-server libicu-dev

info "adding mrr to hardware groups"
for g in gpio i2c dialout $([ "$SENSE_HAT" = yes ] && echo spi); do
    if getent group "$g" >/dev/null; then sudo usermod -aG "$g" mrr; else warn "group '$g' does not exist - skipped"; fi
done

# ---------------------------------------------------------------- SPI (Sense HAT only)
if [ "$SENSE_HAT" = yes ]; then
    info "enabling SPI for the Sense HAT"
    [ -f "$BOOT_CFG" ] || die "$BOOT_CFG not found"
    if grep -qE '^dtparam=spi=on' "$BOOT_CFG"; then
        echo "SPI already enabled"
    elif grep -qE '^#\s*dtparam=spi=on' "$BOOT_CFG"; then
        sudo sed -i -E 's/^#\s*(dtparam=spi=on)/\1/' "$BOOT_CFG"
    else
        echo 'dtparam=spi=on' | sudo tee -a "$BOOT_CFG" >/dev/null
    fi
    sudo systemctl unmask mrr-spi.service 2>/dev/null || true
else
    echo "No Sense HAT: leaving SPI alone (pass --sense-hat if this Pi has one)."
fi

# ---------------------------------------------------------------- TFT screen (--tft-screen only)
if [ "$TFT_SCREEN" = yes ]; then
    info "configuring the 3.5\" SPI screen"
    [ -f "$BOOT_CFG" ] || die "$BOOT_CFG not found"
    if grep -qE '^dtoverlay=piscreen' "$BOOT_CFG"; then
        echo "screen overlay already present: $(grep -E '^dtoverlay=piscreen' "$BOOT_CFG")"
    else
        [ -f "$BOOT_CFG.bak" ] || sudo cp "$BOOT_CFG" "$BOOT_CFG.bak"
        printf '\n# GeeekPi 3.5" SPI TFT (ILI9486 + XPT2046 touch)\n%s\n' "$TFT_OVERLAY" | sudo tee -a "$BOOT_CFG" >/dev/null
        echo "added $TFT_OVERLAY (original saved as $BOOT_CFG.bak)"
    fi
fi

# ---------------------------------------------------------------- .NET 9
info "installing .NET 9 SDK"
if [ -x "$DOTNET_DIR/dotnet" ] && "$DOTNET_DIR/dotnet" --list-sdks | grep -q '^9\.'; then
    echo "already installed: $("$DOTNET_DIR/dotnet" --version)"
else
    tmp=$(mktemp)
    curl -sSL https://dot.net/v1/dotnet-install.sh -o "$tmp"
    bash "$tmp" --channel 9.0
    rm -f "$tmp"
fi
grep -qF 'DOTNET_ROOT=$HOME/.dotnet' ~/.bashrc || echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
grep -qF 'PATH=$PATH:$HOME/.dotnet' ~/.bashrc  || echo 'export PATH=$PATH:$HOME/.dotnet' >> ~/.bashrc
export DOTNET_ROOT="$DOTNET_DIR"
export PATH="$PATH:$DOTNET_DIR"
"$DOTNET_DIR/dotnet" --version

# ---------------------------------------------------------------- MariaDB
info "configuring MariaDB"
sudo systemctl enable --now mariadb

db_count() { sudo mysql -N -e "$1"; }

if [ "$(db_count "SELECT COUNT(*) FROM mysql.user WHERE User='mrr'")" = 0 ]; then
    # Password is set in userMRR.sql and must match ConnectionStrings:Rally in MRR/appsettings.json.
    sudo mysql < "$INSTALL/userMRR.sql"
    echo "created database user mrr"
else
    echo "database user mrr already exists (password unchanged)"
fi

TABLES=$(db_count "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='rally'")
if [ "$TABLES" -gt 0 ] && [ "$RESET_DB" = no ]; then
    echo "rally already has $TABLES tables - NOT reloading (use --reset-db to wipe and reload)"
else
    [ "$TABLES" -eq 0 ] || warn "--reset-db: dropping and reloading rally ($TABLES tables)"
    sudo mysql < "$INSTALL/MRRDatabase.sql"        # creates rally: 37 tables + seed data
    # Deliberately NOT loading install/gameconfig.sql: it starts a specific test game.
    echo "loaded schema and seed data (board library not loaded)"
fi

# Not needed by the game: ConnectionStrings:Rally uses server=127.0.0.1, which MariaDB's default
# bind-address already covers. (It used server=mrobopi, which /etc/hosts maps to 127.0.1.1, so
# the game could not reach its own database without this - changed 2026-10-01.)
if [ "$REMOTE_DB" = yes ]; then
    info "allowing remote database connections"
    if [ -f "$DB_CNF" ]; then
        sudo sed -i -E 's/^([[:space:]]*bind-address[[:space:]]*=)/#\1/' "$DB_CNF"
        sudo systemctl restart mariadb
        warn "database is now reachable from the network with a known default password - use only on the isolated game LAN"
    else
        warn "$DB_CNF not found - edit the MariaDB bind-address by hand"
    fi
fi

# ---------------------------------------------------------------- services (publishes + starts the app)
info "installing the MRR services (this publishes the app; it can take several minutes on a Pi)"
START_ARG=""
# With a Sense HAT, SPI only appears after a reboot, so do not start into a failing preflight.
if [ "$NO_START" = yes ] || [ "$SENSE_HAT" = yes ]; then START_ARG="--no-start"; fi
sudo "$INSTALL/service/install.sh" $START_ARG

if [ "$SENSE_HAT" = yes ]; then
    sudo sed -i -E 's/^MRR_REQUIRE_SPI=.*/MRR_REQUIRE_SPI=yes/' /etc/default/mrr
    grep -q '^MRR_REQUIRE_SPI=' /etc/default/mrr || echo 'MRR_REQUIRE_SPI=yes' | sudo tee -a /etc/default/mrr >/dev/null
else
    # Nothing needs the SPI overlay without a Sense HAT, and on a Pi with an SPI TFT screen the
    # overlay loader could fight the display driver. Masked units in Wants= are skipped.
    sudo sed -i -E 's/^MRR_REQUIRE_SPI=.*/MRR_REQUIRE_SPI=no/' /etc/default/mrr
    grep -q '^MRR_REQUIRE_SPI=' /etc/default/mrr || echo 'MRR_REQUIRE_SPI=no' | sudo tee -a /etc/default/mrr >/dev/null
    # install.sh (above) just wrote the real unit file into /etc/systemd/system, and mask
    # refuses to replace a real file, so remove it first. The source stays in install/service/.
    sudo rm -f /etc/systemd/system/mrr-spi.service
    sudo systemctl mask mrr-spi.service
    sudo systemctl daemon-reload
fi

if [ "$TFT_SCREEN" = yes ]; then
    info "setting the screen to open gmindex.html at login"
    command -v chromium >/dev/null || warn "chromium not found - the kiosk needs the Raspberry Pi OS desktop image"
    install -D -m 0755 "$INSTALL/kiosk/mrr-kiosk.sh" "$HOME/.local/bin/mrr-kiosk.sh"
    install -D -m 0644 "$INSTALL/kiosk/mrr-kiosk.desktop" "$HOME/.config/autostart/mrr-kiosk.desktop"
    echo "installed ~/.local/bin/mrr-kiosk.sh and ~/.config/autostart/mrr-kiosk.desktop"
    # The kiosk starts with the desktop session, so the Pi must log in to the desktop on its own.
    if grep -qE '^autologin-user=mrr' /etc/lightdm/lightdm.conf 2>/dev/null; then
        echo "desktop auto-login already set for mrr"
    elif command -v raspi-config >/dev/null; then
        sudo raspi-config nonint do_boot_behaviour B4   # B4 = desktop, auto-login
        echo "set the Pi to boot to the desktop logged in as mrr"
    else
        warn "raspi-config not found - set desktop auto-login for mrr by hand"
    fi
fi

# ---------------------------------------------------------------- verify
if [ "$NO_START" = no ] && [ "$SENSE_HAT" = no ]; then
    info "waiting for the game host"
    ok=no
    for _ in $(seq 1 30); do
        if curl -sf http://127.0.0.1:5000/api/health >/dev/null; then ok=yes; break; fi
        sleep 3
    done
    if [ "$ok" = yes ]; then echo "game host is up"; else warn "game host did not answer on :5000 - check: mrrctl logs | tail -50"; fi
    curl -sf http://127.0.0.1:5001/api/health >/dev/null && echo "board editor is up" || warn "board editor did not answer on :5001"
fi

cat <<EOF

Done. Next:
  sudo reboot                     # group changes (and SPI / the screen overlay) take effect; confirms it all comes back on its own
  mrrctl status                   # after the reboot
  http://mrobopi:5000/            # player UI     http://mrobopi:5000/gmindex.html   # GM status page
Still manual: network/router (install/NETWORK_SETUP.md), robot IPs, and the database password
in appsettings.json if you change it. Screen notes: PROJECT_STATUS.md section 1.8.
EOF
