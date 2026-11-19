using System.Security.Cryptography;
using System.Text;

namespace TimeGuard.Core;

/// <summary>Общие параметры и подпись LAN-запроса истории.</summary>
public static class HistoryProtocol
{
    public const int Port = 38765;
    public const string Route = "/timeguard/history";
    public const string MachineHeader = "X-TimeGuard-Machine";
    public const string TimestampHeader = "X-TimeGuard-Timestamp";
    public const string SignatureHeader = "X-TimeGuard-Signature";
    public const int MaxEntries = 500;
    public const long TimestampWindowSeconds = 300;

    public static string Sign(ReadOnlySpan<byte> secret, string machineId, long timestamp)
    {
        string payload = $"{machineId}\n{timestamp}\n{Route}";
        using var hmac = new HMACSHA256(secret.ToArray());
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static bool IsValidSignature(
        ReadOnlySpan<byte> secret,
        string machineId,
        long timestamp,
        string signature,
        long nowUnixSeconds)
    {
        if (timestamp < nowUnixSeconds - TimestampWindowSeconds ||
            timestamp > nowUnixSeconds + TimestampWindowSeconds ||
            string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        try
        {
            byte[] expected = Convert.FromHexString(Sign(secret, machineId, timestamp));
            byte[] supplied = Convert.FromHexString(signature);
            return CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
