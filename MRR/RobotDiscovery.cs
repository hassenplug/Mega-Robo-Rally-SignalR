using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MRR.Services
{
    public record KnownRobotBase(int RobotID, int RobotBaseID, string? IPAddress);

    public class DiscoveredDevice
    {
        public string IPAddress { get; set; } = "";
        public int? MatchedRobotBaseID { get; set; }
        public int? MatchedRobotID { get; set; }
    }

    /// <summary>
    /// Section 9 (install/todo.md) "Search": scans the game LAN for live AIM robots.
    ///
    /// Limitation: the AIM ws_status wire format (see RobotStatus.cs) carries no hardware/MAC
    /// identity field, so a live device found at an IP cannot be matched to a specific
    /// RobotBaseID from its content alone -- only by whether the IP happens to already be the
    /// one on file for that base. Devices found at IPs that don't match any known base are
    /// reported as unmatched; the operator assigns them via the connection screen's per-row
    /// "Update IP" once they've confirmed which physical robot answered.
    /// </summary>
    public static class RobotDiscovery
    {
        private const int WsPort = 80;
        private static readonly TimeSpan TcpProbeTimeout = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan WsProbeTimeout = TimeSpan.FromMilliseconds(800);
        private const int MaxConcurrentProbes = 40;

        public static async Task<List<DiscoveredDevice>> ScanAsync(IEnumerable<KnownRobotBase> knownBases)
        {
            var bases = knownBases.ToList();
            var found = new List<DiscoveredDevice>();

            var (localIp, mask) = GetLocalIPv4WithMask();
            if (localIp == null || mask == null)
                return found;

            var candidates = EnumerateHosts(localIp, mask).Where(ip => !ip.Equals(localIp)).ToList();

            using var gate = new SemaphoreSlim(MaxConcurrentProbes);
            var liveIps = new ConcurrentBag<string>();

            var tasks = candidates.Select(async ip =>
            {
                await gate.WaitAsync();
                try
                {
                    if (await ProbeIsAimRobotAsync(ip))
                        liveIps.Add(ip.ToString());
                }
                finally
                {
                    gate.Release();
                }
            });
            await Task.WhenAll(tasks);

            foreach (var ip in liveIps.Distinct())
            {
                var match = bases.FirstOrDefault(b => b.IPAddress == ip);
                found.Add(new DiscoveredDevice
                {
                    IPAddress = ip,
                    MatchedRobotBaseID = match?.RobotBaseID,
                    MatchedRobotID = match?.RobotID,
                });
            }

            return found;
        }

        private static (IPAddress? ip, IPAddress? mask) GetLocalIPv4WithMask()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(addr.Address)) continue;
                    if (addr.IPv4Mask == null) continue;
                    return (addr.Address, addr.IPv4Mask);
                }
            }
            return (null, null);
        }

        // This probes the GM's own isolated game LAN (install/todo.md Section 5), not an
        // arbitrary network, so the range is deliberately capped at a /24-sized sweep.
        private static IEnumerable<IPAddress> EnumerateHosts(IPAddress ip, IPAddress mask)
        {
            uint ipInt = ToUInt32(ip);
            uint maskInt = ToUInt32(mask);
            uint network = ipInt & maskInt;
            uint broadcast = network | ~maskInt;

            if (broadcast - network > 512)
            {
                network = ipInt & 0xFFFFFF00u;
                broadcast = network | 0x000000FFu;
            }

            for (uint host = network + 1; host < broadcast; host++)
                yield return FromUInt32(host);
        }

        private static uint ToUInt32(IPAddress addr)
        {
            var bytes = addr.GetAddressBytes();
            return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
        }

        private static IPAddress FromUInt32(uint value) => new(new[]
        {
            (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
        });

        private static async Task<bool> ProbeIsAimRobotAsync(IPAddress ip)
        {
            using (var tcp = new TcpClient())
            {
                try
                {
                    var connectTask = tcp.ConnectAsync(ip, WsPort);
                    if (await Task.WhenAny(connectTask, Task.Delay(TcpProbeTimeout)) != connectTask || !tcp.Connected)
                        return false;
                }
                catch
                {
                    return false;
                }
            }

            // TCP port 80 is open -- confirm it's actually an AIM robot's ws_status endpoint
            // (not some other device on the LAN) before reporting it as found.
            using var ws = new ClientWebSocket();
            using var cts = new CancellationTokenSource(WsProbeTimeout);
            try
            {
                await ws.ConnectAsync(new Uri($"ws://{ip}:{WsPort}/ws_status"), cts.Token);
                await ws.SendAsync(new ArraySegment<byte>(new byte[] { 0x01 }), WebSocketMessageType.Binary, true, cts.Token);

                var buffer = new byte[4096];
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    return false;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("robot", out _);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (ws.State == WebSocketState.Open)
                {
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "probe", CancellationToken.None); }
                    catch { /* best effort */ }
                }
            }
        }
    }
}
