using System.Net;
using System.Net.Sockets;

namespace MRR.Admin
{
    /// <summary>
    /// Who may use the Admin API.
    ///
    /// The Admin API runs arbitrary SQL against the live game, and the game host binds
    /// 0.0.0.0:5000 so six phones can reach it. Anything allowed here is therefore allowed
    /// from the game WiFi, so remote access is off unless it is deliberately turned on.
    ///
    /// Policy, in order:
    ///   1. Loopback is always allowed, with no key. That is the operator on the Pi.
    ///   2. Otherwise, remote access must be enabled AND an ApiKey must be configured AND
    ///      the caller must present it. An enabled-but-keyless config is refused rather than
    ///      silently opening the API to the network.
    ///   3. If AllowedNetworks is non-empty, the caller's address must also fall inside one
    ///      of them. Empty means "any address, provided the key is right".
    ///
    /// Configure in appsettings.json:
    ///
    ///   "Admin": {
    ///     "AllowRemote": true,
    ///     "ApiKey": "some-long-random-string",
    ///     "AllowedNetworks": [ "192.168.1.0/24" ]
    ///   }
    ///
    /// Callers send the key as the "X-MRR-Admin-Key" header, or "?adminKey=" for a browser.
    /// </summary>
    public class AdminAccess
    {
        public const string HeaderName = "X-MRR-Admin-Key";
        public const string QueryName  = "adminKey";

        private readonly bool _allowRemote;
        private readonly string _apiKey;
        private readonly List<(IPAddress Network, int Prefix)> _allowed = [];

        public AdminAccess(IConfiguration configuration)
        {
            var section = configuration.GetSection("Admin");
            _allowRemote = section.GetValue("AllowRemote", false);
            _apiKey = section.GetValue<string>("ApiKey") ?? "";

            foreach (var cidr in section.GetSection("AllowedNetworks").Get<string[]>() ?? [])
            {
                if (TryParseCidr(cidr, out var parsed)) _allowed.Add(parsed);
                else Console.WriteLine($"[admin] ignoring malformed Admin:AllowedNetworks entry '{cidr}'");
            }

            if (_allowRemote && _apiKey.Length == 0)
            {
                Console.WriteLine(
                    "[admin] Admin:AllowRemote is true but Admin:ApiKey is empty — remote admin " +
                    "access stays DISABLED. Set a key; without one this would put arbitrary SQL " +
                    "on the game network.");
            }
            else if (_allowRemote)
            {
                var scope = _allowed.Count == 0 ? "any address" : string.Join(", ", _allowed.Select(a => $"{a.Network}/{a.Prefix}"));
                Console.WriteLine($"[admin] remote admin access ENABLED for {scope}, API key required.");
            }
        }

        /// <summary>True when remote use is properly configured (enabled and keyed).</summary>
        public bool RemoteEnabled => _allowRemote && _apiKey.Length > 0;

        public enum Decision { AllowedLocal, AllowedRemote, DeniedRemoteDisabled, DeniedBadKey, DeniedNetwork }

        public Decision Check(HttpContext http)
        {
            var remote = http.Connection.RemoteIpAddress;
            if (IsLoopback(remote)) return Decision.AllowedLocal;

            if (!RemoteEnabled) return Decision.DeniedRemoteDisabled;
            if (!InAllowedNetwork(remote)) return Decision.DeniedNetwork;
            if (!KeyMatches(http)) return Decision.DeniedBadKey;
            return Decision.AllowedRemote;
        }

        public static bool IsLoopback(IPAddress? address)
        {
            if (address is null) return false;
            if (IPAddress.IsLoopback(address)) return true;
            // A loopback request can arrive mapped as ::ffff:127.0.0.1
            return address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4());
        }

        private bool KeyMatches(HttpContext http)
        {
            string? presented = http.Request.Headers[HeaderName];
            if (string.IsNullOrEmpty(presented)) presented = http.Request.Query[QueryName];
            if (string.IsNullOrEmpty(presented)) return false;

            // Fixed-time comparison: a length-sensitive or early-exit compare leaks the key
            // one character at a time to anyone who can time the responses.
            var expected = System.Text.Encoding.UTF8.GetBytes(_apiKey);
            var actual   = System.Text.Encoding.UTF8.GetBytes(presented);
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        private bool InAllowedNetwork(IPAddress? address)
        {
            if (_allowed.Count == 0) return true;      // no list means no network restriction
            if (address is null) return false;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return _allowed.Any(a => InNetwork(address, a.Network, a.Prefix));
        }

        private static bool InNetwork(IPAddress address, IPAddress network, int prefix)
        {
            if (address.AddressFamily != network.AddressFamily) return false;
            var a = address.GetAddressBytes();
            var n = network.GetAddressBytes();
            int fullBytes = prefix / 8, remainingBits = prefix % 8;

            for (int i = 0; i < fullBytes; i++)
                if (a[i] != n[i]) return false;

            if (remainingBits == 0) return true;
            int mask = (byte)~(0xFF >> remainingBits);
            return (a[fullBytes] & mask) == (n[fullBytes] & mask);
        }

        private static bool TryParseCidr(string cidr, out (IPAddress, int) parsed)
        {
            parsed = default;
            var parts = cidr.Split('/');
            if (!IPAddress.TryParse(parts[0], out var network)) return false;

            int prefix = network.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
            if (parts.Length == 2 && !int.TryParse(parts[1], out prefix)) return false;
            if (prefix < 0 || prefix > (network.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32)) return false;

            parsed = (network, prefix);
            return true;
        }

        /// <summary>Message explaining a refusal, without revealing whether a key exists.</summary>
        public string ExplainDenial(Decision decision) => decision switch
        {
            Decision.DeniedRemoteDisabled =>
                "The admin API is restricted to the machine running the game. To use it remotely, set " +
                "Admin:AllowRemote and Admin:ApiKey in appsettings.json and restart. " +
                "Otherwise tunnel: ssh -L 5000:127.0.0.1:5000 <host>",
            Decision.DeniedNetwork =>
                "Your address is not in Admin:AllowedNetworks.",
            _ =>
                $"A valid admin key is required. Send it as the {HeaderName} header or ?{QueryName}=.",
        };
    }
}
