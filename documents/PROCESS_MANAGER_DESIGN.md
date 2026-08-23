# Mega Robo Rally Process Manager Design

> ## ⚠️ SUPERSEDED — 2026-08-22
>
> This is an early draft. The maintained design, matching the units actually installed, is
> **[install/PROCESS_MANAGER.md](../install/PROCESS_MANAGER.md)**, with the implementation in
> [install/service/](../install/service/). That document also covers the two-process layout the
> API decomposition requires (its §10.1).
>
> Kept for history only. Do not update this file; update `install/PROCESS_MANAGER.md`.


## 1. Purpose

Provide reliable lifecycle management for the Mega Robo Rally application on the Raspberry Pi:

- start automatically after the Pi boots;
- restart after a crash or unexpected clean exit;
- allow an operator to pause, resume, stop, start, and restart it;
- preserve logs and make state easy to inspect;
- avoid changing existing HTTP and SignalR contracts.

The application is an ASP.NET Core .NET 9 process. Its startup constructs `GameController`, which connects to the database and robots, so it should be supervised as one process rather than having internal components restarted independently.

## 2. Recommended architecture

Use `systemd` as the operating-system process manager and expose a small `mrrctl` command-line wrapper for operators.

```text
Raspberry Pi boot
    |
    v
systemd: mrr.service
    |
    +-- dotnet MRR.dll
    +-- Restart=always on unexpected or clean exit
    +-- journald captures stdout/stderr
    v
mrrctl -> systemctl / systemd signals
```

This starts before a user logs in, handles dependencies, and provides standard logs and status output. A shell loop or desktop autostart entry does not provide those guarantees.

### 2.1 Process registry

The first release manages one process, `mrr`. Use a registry model so additional applications can be added without changing the control API.

| Name | Command | Working directory | Environment | Restart policy |
|---|---|---|---|---|
| `mrr` | `/usr/bin/dotnet /opt/mrr/MRR.dll` | `/opt/mrr` | `ASPNETCORE_ENVIRONMENT=Production` | Always, five-second delay |

The published application should live in `/opt/mrr`. Keep configuration and secrets separate from the manager script and unit file.

### 2.2 Desired state and operator commands

`systemd` distinguishes an explicit stop job from an unexpected process exit. The manager must preserve that behavior:

| Command | Action | Auto-restart? |
|---|---|---|
| `mrrctl start` | Start the service | Yes |
| `mrrctl stop` | Stop intentionally | No, until `start` or `restart` |
| `mrrctl restart` | Stop, then start | Yes |
| `mrrctl pause` | Send `SIGSTOP` and preserve the process | No new process is started |
| `mrrctl resume` | Send `SIGCONT` | Yes if it later exits |
| `mrrctl status` | Show state, PID, uptime, and exit information | N/A |
| `mrrctl logs` | Follow the journal | N/A |

Pause is a process freeze, not a game-level pause. Requests, SignalR messages, timers, and robot-control work will not progress while paused.

## 3. systemd unit design

Install `/etc/systemd/system/mrr.service`:

```ini
[Unit]
Description=Mega Robo Rally application
Wants=network-online.target mariadb.service
After=network-online.target mariadb.service

[Service]
Type=simple
User=mrr
Group=mrr
WorkingDirectory=/opt/mrr
ExecStart=/usr/bin/dotnet /opt/mrr/MRR.dll
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
Restart=always
RestartSec=5s
TimeoutStopSec=30s
KillSignal=SIGINT
SyslogIdentifier=mrr
NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
```

`Restart=always` covers crashes and unexpected clean exits. `systemctl stop mrr` suppresses the restart because it is an intentional systemd stop operation. `RestartSec` prevents rapid failure loops from consuming CPU and filling logs. `KillSignal=SIGINT` gives ASP.NET Core a normal shutdown opportunity before escalation.

`mariadb.service` provides ordering, not a guarantee that the database is accepting connections. The application must tolerate a database startup failure or the service should later gain a readiness check.

Confirm the .NET path with `command -v dotnet`. A system service does not load the interactive shell's `.bashrc`, so a user-local installation must use its absolute path in `ExecStart`.

For multiple applications, use one unit per process, such as `mrr-<name>.service`, with restart isolation between processes.

## 4. `mrrctl` command design

Install `/usr/local/bin/mrrctl` as a root-owned executable. It should accept only fixed process names and verbs; never pass arbitrary user input directly into a shell command.

```text
mrrctl {start|stop|restart|pause|resume|status|logs}
```

Expected mapping:

```text
start   -> systemctl start mrr.service
stop    -> systemctl stop mrr.service
restart -> systemctl restart mrr.service
pause   -> systemctl kill --signal=SIGSTOP mrr.service
resume  -> systemctl kill --signal=SIGCONT mrr.service
status  -> systemctl status --no-pager mrr.service
logs    -> journalctl -u mrr.service -f
```

Return the underlying exit code, reject unknown commands, and restrict use to a dedicated `mrr-operators` group. Use a narrowly scoped sudoers rule for these actions; do not grant unrestricted `sudo` access to `systemctl`.

## 5. Installation procedure

These commands assume Raspberry Pi OS, a checked-out repository, and a service account named `mrr`.

### 5.1 Install prerequisites

```bash
sudo apt-get update
sudo apt-get install -y dotnet-runtime-9.0 mariadb-client
command -v dotnet
dotnet --info
```

If using the `dotnet-install.sh` approach from `install/git.sh`, install the runtime in a system-visible location or use its absolute path in `ExecStart`.

### 5.2 Create the service account and publish

```bash
sudo useradd --system --home /opt/mrr --shell /usr/sbin/nologin mrr
sudo install -d -o mrr -g mrr -m 0750 /opt/mrr
dotnet publish MRR/MRR.csproj --configuration Release --runtime linux-arm64 --self-contained false --output /tmp/mrr-publish
sudo cp -a /tmp/mrr-publish/. /opt/mrr/
sudo chown -R mrr:mrr /opt/mrr
```

Copy required board assets into the deployment layout or update the application's asset lookup as part of packaging. Verify that `/opt/mrr` contains `MRR.dll` and its runtime assets.

### 5.3 Install configuration and the unit

Keep production settings beside the published assembly as `/opt/mrr/appsettings.Production.json` (owned by `mrr`, mode `0640`), where ASP.NET Core loads it automatically for `ASPNETCORE_ENVIRONMENT=Production`. If secrets must live under `/etc/mrr`, add an explicit configuration provider or `--config` implementation first. Confirm `Urls` and the database connection string before enabling the service. Current repository defaults are `http://*:5000` and the `Rally` connection string in `MRR/appsettings.json`.

```bash
sudo install -o mrr -g mrr -m 0640 MRR/appsettings.json /opt/mrr/appsettings.Production.json
sudo install -o root -g root -m 0644 /path/to/mrr.service /etc/systemd/system/mrr.service
sudo systemctl daemon-reload
sudo systemctl enable mrr.service
sudo systemctl start mrr.service
```

The deployment process should replace `/path/to/mrr.service` with the checked-in unit when implementation is added. Do not duplicate the database password in the unit.

### 5.4 Validate the installation

```bash
systemctl is-enabled mrr.service
systemctl is-active mrr.service
systemctl status mrr.service --no-pager
journalctl -u mrr.service -n 100 --no-pager
curl --fail http://127.0.0.1:5000/
```

Reboot testing is required:

```bash
sudo reboot
# After reconnecting:
mrrctl status
```

## 6. Operating procedures

```bash
mrrctl status
mrrctl logs
mrrctl pause
mrrctl resume
mrrctl restart
mrrctl stop
mrrctl start
```

To verify crash recovery during a maintenance window:

```bash
sudo systemctl kill --signal=SIGTERM mrr.service
mrrctl status
```

The service should return to `active (running)` after the configured delay. Verify that `mrrctl stop` leaves it stopped; this is the key distinction between operator intent and automatic recovery.

## 7. Failure handling and observability

- Use `journalctl -u mrr.service` as the primary log source and configure journald retention appropriate for the Pi's storage.
- Document or alert on repeated restart loops. `systemctl status` and `journalctl` expose the evidence; a future health monitor can inspect `NRestarts`.
- Add an HTTP health endpoint such as `/health` only if it reports meaningful application and dependency state. A listening socket alone does not prove that the database or robots are usable.
- Do not use `StartLimitAction=reboot` initially. A bad configuration or unavailable database must not reboot the Pi repeatedly.
- Before stopping or rebooting, use the application's game-state controls as appropriate. Process supervision cannot make an interrupted robot movement transactional.

## 8. Security and recovery requirements

- Run the app as the unprivileged `mrr` user. Add only the device, GPIO, serial, or SPI group permissions required by hardware integration.
- Restrict write access to `/opt/mrr`, `/etc/mrr`, and the manager executable.
- Bind HTTP to the intended LAN interface or protect port 5000 with the firewall; `http://*:5000` exposes every interface by default.
- Keep a known-good published build for rollback. Publish to a staging directory, stop the service, atomically replace the deployment, then start and validate it.
- Back up the database independently of process-manager state.

## 9. Implementation phases

1. Add and test the `mrr.service` unit and fixed-command `mrrctl` wrapper.
2. Add an install/update script under `install/` that detects the .NET path, publishes for `linux-arm64`, installs the unit, and performs validation.
3. Add a health endpoint and optional readiness checks after basic lifecycle behavior is stable.
4. Extend the registry when a second independently supervised application exists.

## 10. Acceptance criteria

- The application starts after a cold Pi boot without an interactive login.
- A crash and an unexpected clean exit cause a restart after the configured delay.
- `pause` freezes the existing process and `resume` continues it without creating a second instance.
- `stop` remains stopped until `start` or `restart` is requested.
- `restart` produces exactly one replacement process.
- Logs identify startup, shutdown, restart, and failure reasons.
- A non-root operator can use approved manager commands but cannot run arbitrary privileged commands.
- The procedure works after a fresh deployment and after reboot.