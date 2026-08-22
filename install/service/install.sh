#!/bin/bash
# install.sh - install the MRR process manager (systemd units + mrrctl).
# Usage: sudo ./install.sh [--no-start]
#
# Idempotent: safe to re-run after editing a unit file. /etc/default/mrr is never
# overwritten once it exists.
set -euo pipefail

SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUN_USER=mrr                     # must match User= in mrr-server.service
APP_ROOT=/srv/mrr
UNIT_DIR=/etc/systemd/system
BIN_DIR=/usr/local/bin
START=yes

[ "${1:-}" = "--no-start" ] && START=no
[ "$(id -u)" -eq 0 ] || { echo "run with sudo: sudo $0" >&2; exit 1; }
id "$RUN_USER" >/dev/null 2>&1 || { echo "user '$RUN_USER' does not exist" >&2; exit 1; }

info() { echo "==> $*"; }

info "installing helper scripts to $BIN_DIR"
for f in mrrctl mrr-preflight mrr-health-check mrr-recover; do
    install -m 0755 -o root -g root "$SRC/$f" "$BIN_DIR/$f"
done

info "installing systemd units to $UNIT_DIR"
for f in mrr.target mrr-server.service mrr-spi.service \
         mrr-health.service mrr-health.timer \
         mrr-recover.service mrr-recover.timer; do
    install -m 0644 -o root -g root "$SRC/$f" "$UNIT_DIR/$f"
done

if [ -f /etc/default/mrr ]; then
    info "/etc/default/mrr exists - leaving it alone (reference: $SRC/mrr.env)"
else
    info "seeding /etc/default/mrr"
    install -m 0644 -o root -g root "$SRC/mrr.env" /etc/default/mrr
fi

info "creating $APP_ROOT (owned by $RUN_USER)"
mkdir -p "$APP_ROOT/app/images/align"
chown -R "$RUN_USER":"$RUN_USER" "$APP_ROOT"

info "installing sudoers drop-in so '$RUN_USER' can drive systemctl without a password"
tmp=$(mktemp)
cat > "$tmp" <<EOF
# Installed by Mega-Robo-Rally install/service/install.sh
# Lets '$RUN_USER' control only the mrr* units - nothing else.
Cmnd_Alias MRR_UNITS = /usr/bin/systemctl start mrr*, \\
                       /usr/bin/systemctl stop mrr*, \\
                       /usr/bin/systemctl restart mrr*, \\
                       /usr/bin/systemctl freeze mrr*, \\
                       /usr/bin/systemctl thaw mrr*, \\
                       /usr/bin/systemctl enable mrr*, \\
                       /usr/bin/systemctl disable mrr*, \\
                       /usr/bin/systemctl reset-failed mrr*, \\
                       /usr/bin/systemctl kill mrr*
$RUN_USER ALL=(root) NOPASSWD: MRR_UNITS
EOF
visudo -cqf "$tmp" || { rm -f "$tmp"; echo "generated sudoers file is invalid - aborting" >&2; exit 1; }
install -m 0440 -o root -g root "$tmp" /etc/sudoers.d/mrr-process-manager
rm -f "$tmp"

info "reloading systemd"
systemctl daemon-reload

info "publishing the app as $RUN_USER"
sudo -H -u "$RUN_USER" "$BIN_DIR/mrrctl" deploy

info "enabling at boot"
systemctl enable mrr.target mrr-server.service mrr-health.timer mrr-recover.timer

if [ "$START" = yes ]; then
    if ss -ltnH 'sport = :5000' 2>/dev/null | grep -q .; then
        echo
        echo "!! TCP 5000 is already in use - a hand-started server is still running."
        echo "!! Stop it, then:  mrrctl start"
    else
        info "starting mrr.target"
        systemctl start mrr.target
        sleep 3
        sudo -H -u "$RUN_USER" "$BIN_DIR/mrrctl" status || true
    fi
fi

cat <<EOF

Installed. Next:
  mrrctl status         # where things stand
  mrrctl logs | tail    # look for "preflight OK"
  sudo reboot           # confirm it comes back on its own

Docs: /home/mrr/Mega-Robo-Rally-SignalR/install/PROCESS_MANAGER.md
EOF
