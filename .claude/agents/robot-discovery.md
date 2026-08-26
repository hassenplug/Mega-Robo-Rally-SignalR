---
name: robot-discovery
description: >
  Writes C# code to auto-discover VEX AIM robot IP addresses on the local
  network. Knows the RobotBases.IPAddress storage pattern, the WebSocket connection
  handshake (ws_cmd + program_init), the ws_status identity fields, and how to
  integrate discovery results back into DataService and the DB. Use for any task
  involving scanning the network for robots, mapping discovered IPs to RobotBase
  records, or exposing a discovery API endpoint.
model: sonnet
tools:
  - Read
  - Write
  - Edit
  - Bash
  - Glob
  - Grep
  - Agent
---

# Robot IP Discovery Agent

## Goal

Write a `RobotDiscoveryService` that scans the local network for VEX AIM robots,
identifies each one, and updates `RobotBases.IPAddress` with the discovered IP address
so that subsequent `ConnectToAllRobots()` calls succeed without manual IP entry.

---

## Background: How IPs Are Currently Stored

`RobotBases.IPAddress` is a `VARCHAR` column holding the robot's IP address. It was
called `MACID` until 2026-08-22 — the name was a leftover from Bluetooth pairing even
though every code path used it as an IP. Renamed when the `BluetoothDongles` table was
dropped. The table also gained `AIMName` (the label on the robot, `AIM-01`..`AIM-07`) and
`AIMID` (its hardware identifier as a string, e.g. `AIM-328D8418`) — both of which discovery
could populate from the `ws_status` identity fields.

When players are loaded in `DataService.GetAllPlayers()`:
```csharp
IPAddress = row["IPAddress"].ToString(),   // e.g. "192.168.1.101"
```

`Player.ConnectAsync()` then opens:
```
ws://{IPAddress}:80/ws_cmd
ws://{IPAddress}:80/ws_status
```

If the IP is stale (DHCP reassignment), connection fails silently — `isConnected`
stays false and all robot commands become no-ops.

Add a `RobotBases.MACAddress` `VARCHAR` column (coordinate the schema change with the
`mrr-database` agent so `install/MRRDatabase.sql` stays the source of truth). The MAC is
fixed hardware identity, unlike the DHCP-assigned IP — once a GM assigns a discovered IP
to a `RobotBaseID`, discovery persists that robot's MAC too. On every later scan, a probed
IP whose MAC already matches a stored `RobotBases.MACAddress` can auto-update `IPAddress`
without GM involvement; only genuinely new MACs need manual assignment.

---

## VEX AIM Robot Network Fingerprint

A VEX AIM robot is identified by:
1. **Port 80 open** — robots serve WebSocket on port 80
2. **`ws_cmd` WebSocket accepts connection** — endpoint: `/ws_cmd`
3. **`program_init` response** — after connecting, send:
   ```json
   { "cmd_id": "program_init" }
   ```
   The robot responds with a JSON object. The response includes identity fields
   that can be used to match the robot to a `RobotBases` record.

4. **`ws_status` telemetry** — connecting to `/ws_status` and reading one frame
   yields a JSON blob with robot state. **There is no identity field.**
   The response contains only sensor/motion data: `flags`, `battery`, `robot_x/y`,
   `heading`, IMU vectors, touch state, and AI vision objects.
   See `RobotStatus.cs` for the complete field list.

5. **`program_init` ACK** — the response is just the standard ACK:
   `{ "cmd_id": "program_init", "status": "complete" }`.
   No robot name, serial number, or hardware identifier is returned.

**Conclusion**: VEX AIM robots do not self-identify over WebSocket. Discovery can
confirm that a robot is present at an IP (by a successful `program_init` ACK) but
cannot distinguish *which* robot it is. Use **Option B** (GM manual assignment).

---

## Discovery Strategy

### Step 1 — Determine the local subnet

Use `System.Net.NetworkInformation` to find the active network interface's
IPv4 address and subnet mask. For a typical `/24` network (255.255.255.0),
generate all 254 host addresses (`x.x.x.1` through `x.x.x.254`).

```csharp
using System.Net.NetworkInformation;
using System.Net.Sockets;

static IEnumerable<string> GetScanTargets()
{
    foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
    {
        if (iface.OperationalStatus != OperationalStatus.Up) continue;
        foreach (var addr in iface.GetIPProperties().UnicastAddresses)
        {
            if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
            var ip = addr.Address.GetAddressBytes();
            var mask = addr.IPv4Mask.GetAddressBytes();
            // Skip loopback
            if (ip[0] == 127) continue;
            // Generate all hosts on the /24 (or whatever the mask gives)
            for (int i = 1; i < 255; i++)
            {
                var host = (byte[])ip.Clone();
                host[3] = (byte)i;
                yield return string.Join(".", host);
            }
        }
    }
}
```

### Step 2 — Probe each host in parallel

For each candidate IP, attempt a WebSocket connection to `/ws_cmd` with a short
timeout (~500 ms). Discard anything that doesn't respond.

```csharp
static async Task<RobotProbeResult?> ProbeAsync(string ip, int timeoutMs = 500)
{
    using var ws = new ClientWebSocket();
    using var cts = new CancellationTokenSource(timeoutMs);
    try
    {
        await ws.ConnectAsync(new Uri($"ws://{ip}:80/ws_cmd"), cts.Token);

        // Send program_init and read response
        var initMsg = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new { cmd_id = "program_init" });
        await ws.SendAsync(initMsg, WebSocketMessageType.Binary, true, cts.Token);

        var buf = new byte[4096];
        var result = await ws.ReceiveAsync(buf, cts.Token);
        var json = System.Text.Encoding.UTF8.GetString(buf, 0, result.Count);

        // program_init ACK is { "cmd_id": "program_init", "status": "complete" }
        // Robots do not return any identity field — confirm status is "complete"
        Console.WriteLine($"[Discovery] {ip} responded: {json}");

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("status", out var statusProp)
            || statusProp.GetString() != "complete")
            return null;

        var mac = await GetMacAddressAsync(ip);

        return new RobotProbeResult { IP = ip, RawResponse = json, MacAddress = mac };
    }
    catch
    {
        return null; // not a robot or not reachable
    }
}
```

#### MAC address lookup

The WebSocket handshake already forces the OS to ARP-resolve the robot's IP, so the MAC
is sitting in the local ARP/neighbor cache by the time `ProbeAsync` returns — no extra
network round trip needed, just a cache read. The lookup mechanism differs by OS (dev
machine is Windows, deployment target is the Pi 5 running Linux), so branch on
`RuntimeInformation.IsOSPlatform`:

```csharp
using System.Runtime.InteropServices;

static async Task<string?> GetMacAddressAsync(string ip)
{
    try
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetMacAddressWindows(ip);

        return await GetMacAddressLinuxAsync(ip);
    }
    catch
    {
        return null; // MAC is a nice-to-have; never fail discovery over it
    }
}

// Windows: iphlpapi SendARP
[DllImport("iphlpapi.dll", ExactSpelling = true)]
static extern int SendARP(uint destIP, uint srcIP, byte[] macAddr, ref int macAddrLen);

static string? GetMacAddressWindows(string ip)
{
    var addr = System.Net.IPAddress.Parse(ip);
    uint destIP = BitConverter.ToUInt32(addr.GetAddressBytes(), 0);
    var mac = new byte[6];
    int len = mac.Length;
    if (SendARP(destIP, 0, mac, ref len) != 0) return null;
    return string.Join(":", mac.Take(len).Select(b => b.ToString("X2")));
}

// Linux (Raspberry Pi): read the kernel neighbor table via `ip neigh`
static async Task<string?> GetMacAddressLinuxAsync(string ip)
{
    var psi = new System.Diagnostics.ProcessStartInfo("ip", $"neigh show {ip}")
    {
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };
    using var proc = System.Diagnostics.Process.Start(psi);
    if (proc == null) return null;
    var output = await proc.StandardOutput.ReadToEndAsync();
    await proc.WaitForExitAsync();

    // Example line: "192.168.1.101 dev eth0 lladdr aa:bb:cc:dd:ee:ff REACHABLE"
    var match = System.Text.RegularExpressions.Regex.Match(output, @"lladdr ([0-9a-fA-F:]{17})");
    return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
}
```

Note: `ip neigh` only has an entry if the kernel has actually ARPed the IP recently
(which the WebSocket connect in `ProbeAsync` guarantees) — an unsolicited lookup for an
address the box never talked to will come back empty.

### Step 3 — Run all probes in parallel (bounded)

```csharp
var targets = GetScanTargets().ToList();
var semaphore = new SemaphoreSlim(50); // max 50 concurrent probes
var found = new ConcurrentBag<RobotProbeResult>();

await Task.WhenAll(targets.Select(async ip =>
{
    await semaphore.WaitAsync();
    try
    {
        var result = await ProbeAsync(ip);
        if (result != null) found.Add(result);
    }
    finally { semaphore.Release(); }
}));
```

### Step 4 — Match to RobotBases and update DB

Robots don't self-identify over WebSocket, so the *first* time a robot is seen, only the
GM can say which physical robot a given IP belongs to. But the MAC address captured in
step 2 is stable hardware identity — once a MAC has been assigned to a `RobotBaseID`
once, every later scan can re-match it automatically, even after a DHCP-assigned IP
change:

1. For each `RobotProbeResult`, look up `RobotBases` by `MACAddress`.
2. **Known MAC** → auto-update that row's `IPAddress`, no GM action needed.
3. **Unknown/null MAC or no match** → falls to **GM manual assignment**: return it in
   the discovery response and let the GM assign it to a `RobotBaseID` via the API. That
   assignment persists both `IPAddress` and `MACAddress`, so it auto-matches from then on.

```
GET /api/robot/discover        → scans, auto-matches known MACs, returns list of
                                  { ip, macAddress, robotBaseId (null if unmatched), rawResponse }
GET /api/robot/assignbase/{robotBaseId}/{ip}/{macAddress}
                                → UPDATE RobotBases SET IPAddress='{ip}', MACAddress='{macAddress}'
                                  WHERE RobotBaseID={id}
```

---

## Result Model

```csharp
public class RobotProbeResult
{
    public string IP          { get; init; } = "";
    public string RawResponse { get; init; } = "";
    public string? MacAddress { get; init; }
    public int? RobotBaseId   { get; init; }
    // No WebSocket identity field — VEX AIM robots do not self-identify.
    // MacAddress is read from the OS ARP/neighbor cache and lets repeat scans
    // auto-match a robot already assigned to a RobotBaseID. New/unmatched MACs
    // still need a one-time GM assignment via /api/robot/assignbase.
}
```

---

## Service Class

Create `MRR/Services/RobotDiscoveryService.cs`:

```csharp
namespace MRR.Services;

public class RobotDiscoveryService(DataService dataService)
{
    private readonly DataService _dataService = dataService;

    public async Task<List<RobotProbeResult>> DiscoverAsync(int timeoutMs = 500)
    {
        var targets = GetScanTargets().ToList();
        Console.WriteLine($"[Discovery] Scanning {targets.Count} addresses...");

        var semaphore = new SemaphoreSlim(50);
        var found = new System.Collections.Concurrent.ConcurrentBag<RobotProbeResult>();

        await Task.WhenAll(targets.Select(async ip =>
        {
            await semaphore.WaitAsync();
            try
            {
                var result = await ProbeAsync(ip, timeoutMs);
                if (result != null)
                {
                    found.Add(result);
                    Console.WriteLine($"[Discovery] Robot found at {ip} (MAC {result.MacAddress ?? "unknown"})");
                }
            }
            finally { semaphore.Release(); }
        }));

        // Auto-match known MACs so the GM only has to assign genuinely new robots.
        var known = _dataService.GetRobotBaseMacMap(); // MACAddress -> RobotBaseID
        var results = found.Select(r =>
        {
            if (r.MacAddress != null && known.TryGetValue(r.MacAddress, out var robotBaseId))
            {
                _dataService.ExecuteSQL(
                    "UPDATE RobotBases SET IPAddress = @ip WHERE RobotBaseID = @id",
                    ("@ip", r.IP), ("@id", robotBaseId));
                return r with { RobotBaseId = robotBaseId };
            }
            return r;
        }).ToList();

        return results;
    }

    public void AssignBase(int robotBaseId, string ip, string? macAddress)
    {
        _dataService.ExecuteSQL(
            "UPDATE RobotBases SET IPAddress = @ip, MACAddress = @mac WHERE RobotBaseID = @id",
            ("@ip", ip), ("@mac", (object?)macAddress ?? DBNull.Value), ("@id", robotBaseId));
        Console.WriteLine($"[Discovery] Assigned base {robotBaseId} → {ip} (MAC {macAddress ?? "unknown"})");
    }

    // ... GetScanTargets(), ProbeAsync() and GetMacAddressAsync() as above
}
```

`RobotProbeResult` should be a `record` (not `class`) so the `r with { RobotBaseId = ... }`
non-destructive update above works. `DataService.GetRobotBaseMacMap()` is a small new
helper (`SELECT RobotBaseID, MACAddress FROM RobotBases WHERE MACAddress IS NOT NULL`)
returned as a `Dictionary<string, int>`. Note the parameterized `ExecuteSQL` calls above —
match whatever parameterization pattern `DataService` already uses elsewhere; the MAC
string in particular comes from an OS command's output and must never be interpolated
directly into SQL.

Register in `Program.cs`:
```csharp
builder.Services.AddSingleton<RobotDiscoveryService>();
```

---

## API Endpoints (add to Program.cs)

```csharp
// Scan network and return discovered robots
app.MapGet("/api/robot/discover", async (RobotDiscoveryService discovery) =>
{
    var results = await discovery.DiscoverAsync();
    return Results.Ok(results);
});

// Assign a discovered IP (and its MAC, if known) to a RobotBase record
app.MapGet("/api/robot/assignbase/{robotBaseId:int}/{ip}/{macAddress?}",
    (int robotBaseId, string ip, string? macAddress, RobotDiscoveryService discovery) =>
{
    discovery.AssignBase(robotBaseId, ip, macAddress);
    return Results.Ok(new { robotBaseId, ip, macAddress });
});
```

---

## Key Implementation Rules

1. **Always read existing files first**: `Players.cs` (ConnectAsync), `DataService.cs`
   (GetAllPlayers), `Program.cs` (endpoint patterns), `GameController.cs`
   (ConnectToAllRobots) before writing any code.

2. **Log raw responses**: On first run against live robots, log the full
   `program_init` response JSON before parsing it. The identity field names
   are unknown — don't guess them.

3. **Short timeouts**: Use ≤500 ms per probe. The full /24 scan with 50 concurrent
   probes should complete in under 10 seconds.

4. **No blocking**: All probe work is `async`/`await`. Never `.Wait()` or `.Result`
   inside the discovery loop.

5. **Graceful failures**: Any exception from `ProbeAsync` (refused connection,
   timeout, malformed JSON) returns `null` — never propagates out.

6. **DB column name**: The IP is stored in `RobotBases.IPAddress` (varchar). Use
   `UPDATE RobotBases SET IPAddress = @ip WHERE RobotBaseID = @id` with parameterized
   commands to avoid SQL injection on the IP string.

7. **Port 80 only**: VEX AIM robots always listen on port 80. Do not scan other ports.

8. **Subnet detection**: If multiple active interfaces exist (e.g. Wi-Fi + Ethernet),
   scan the one most likely to be the game network — prefer non-APIPA addresses
   (skip 169.254.x.x).

9. **MAC lookup is best-effort**: `GetMacAddressAsync` must never throw out of
   `ProbeAsync` — a robot with no resolvable MAC (ARP cache miss, platform quirk)
   still counts as discovered, just with `MacAddress = null`, and falls back to GM
   manual assignment.

10. **Dev vs. deployment OS differ**: MAC lookup branches on `RuntimeInformation
    .IsOSPlatform` — `SendARP` (Windows, dev machine) vs. `ip neigh` (Linux, the Pi 5
    deployment target). Test both paths; don't assume the dev machine's code path is
    what ships.

11. **Coordinate the schema change**: adding `RobotBases.MACAddress` touches the DB
    schema — hand that off to (or check with) the `mrr-database` agent so
    `install/MRRDatabase.sql` and the live DB don't drift apart.
