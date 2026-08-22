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

        return new RobotProbeResult { IP = ip, RawResponse = json };
    }
    catch
    {
        return null; // not a robot or not reachable
    }
}
```

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

After discovery, the GM assigns each found IP to a `RobotBases` row and updates `IPAddress`.
Robots cannot self-identify, so automatic matching is not possible.

The only viable approach is **GM manual assignment** — return the discovered IPs and
let the GM assign each to a `RobotBaseID` via the API:
```
GET /api/robot/discover        → returns list of { ip, robotIdentifier, rawResponse }
GET /api/robot/assignbase/{robotBaseId}/{ip}  → UPDATE RobotBases SET IPAddress='{ip}' WHERE RobotBaseID={id}
```

---

## Result Model

```csharp
public class RobotProbeResult
{
    public string IP          { get; init; } = "";
    public string RawResponse { get; init; } = "";
    // No identity field — VEX AIM robots do not self-identify over WebSocket.
    // Assign RobotBaseID via the /api/robot/assignbase endpoint after discovery.
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
                    Console.WriteLine($"[Discovery] Robot found at {ip}");
                }
            }
            finally { semaphore.Release(); }
        }));

        return [.. found];
    }

    public void AssignBase(int robotBaseId, string ip)
    {
        _dataService.ExecuteSQL(
            $"UPDATE RobotBases SET IPAddress = '{ip}' WHERE RobotBaseID = {robotBaseId}");
        Console.WriteLine($"[Discovery] Assigned base {robotBaseId} → {ip}");
    }

    // ... GetScanTargets() and ProbeAsync() as above
}
```

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

// Assign a discovered IP to a RobotBase record
app.MapGet("/api/robot/assignbase/{robotBaseId:int}/{ip}",
    (int robotBaseId, string ip, RobotDiscoveryService discovery) =>
{
    discovery.AssignBase(robotBaseId, ip);
    return Results.Ok(new { robotBaseId, ip });
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
