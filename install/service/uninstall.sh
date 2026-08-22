#!/bin/bash
# uninstall.sh - remove the MRR process manager.
# Usage: sudo ./uninstall.sh [--purge]
#   --purge also removes /srv/mrr (deployed app) and /etc/default/mrr.
set -euo pipefail

PURGE=no
[ "${1:-}" = "--purge" ] && PURGE=yes
[ "$(id -u)" -eq 0 ] || { echo "run with sudo: sudo $0" >&2; exit 1; }

info() { echo "==> $*"; }

info "stopping and disabling units"
systemctl disable --now mrr.target mrr-server.service mrr-health.timer mrr-recover.timer 2>/dev/null || true
systemctl stop mrr-spi.service 2>/dev/null || true
systemctl reset-failed 'mrr*' 2>/dev/null || true

info "removing unit files"
rm -f /etc/systemd/system/mrr.target \
      /etc/systemd/system/mrr-server.service \
      /etc/systemd/system/mrr-spi.service \
      /etc/systemd/system/mrr-health.service \
      /etc/systemd/system/mrr-health.timer \
      /etc/systemd/system/mrr-recover.service \
      /etc/systemd/system/mrr-recover.timer
rm -rf /etc/systemd/system/mrr.target.wants

info "removing scripts and sudoers drop-in"
rm -f /usr/local/bin/mrrctl /usr/local/bin/mrr-preflight \
      /usr/local/bin/mrr-health-check /usr/local/bin/mrr-recover
rm -f /etc/sudoers.d/mrr-process-manager
rm -rf /run/mrr

systemctl daemon-reload

if [ "$PURGE" = yes ]; then
    info "purging /srv/mrr and /etc/default/mrr"
    rm -rf /srv/mrr
    rm -f /etc/default/mrr
else
    info "kept /srv/mrr and /etc/default/mrr (use --purge to remove)"
fi

echo "Done. The git repo was not touched - 'dotnet run' still works as before."
