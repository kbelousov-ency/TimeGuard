using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TimeGuard.Core;

public sealed class DiscoveryBeacon
{
    public string Kind { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Port { get; set; }
    public long Timestamp { get; set; }
    public string Signature { get; set; } = string.Empty;
}

public static class DiscoveryProtocol
{
    public const int Port = 38767;
    public const string ParentKind = "TimeGuard.Parent";
    public const string ClientKind = "TimeGuard.Client";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize(DiscoveryBeacon beacon) => JsonSerializer.Serialize(beacon, Json);
    public static DiscoveryBeacon? Deserialize(string text)
    {
        try { return JsonSerializer.Deserialize<DiscoveryBeacon>(text, Json); }
        catch (JsonException) { return null; }
    }

    public static string Sign(ReadOnlySpan<byte> secret, DiscoveryBeacon beacon)
    {
        string payload = $"{beacon.Kind}\n{beacon.MachineId}\n{beacon.Name}\n{beacon.Port}\n{beacon.Timestamp}";
        using var hmac = new HMACSHA256(secret.ToArray());
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static bool IsValid(ReadOnlySpan<byte> secret, DiscoveryBeacon beacon, long now)
    {
        if (string.IsNullOrWhiteSpace(beacon.Kind) || string.IsNullOrWhiteSpace(beacon.MachineId) ||
            Math.Abs(now - beacon.Timestamp) > 30 || string.IsNullOrWhiteSpace(beacon.Signature)) return false;
        try
        {
            byte[] expected = Convert.FromHexString(Sign(secret, beacon));
            byte[] actual = Convert.FromHexString(beacon.Signature);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException) { return false; }
    }
}
