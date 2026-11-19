using System.Security.Cryptography;
using System.Text;

namespace TimeGuard.Core;

/// <summary>
/// Хранение и проверка мастер-пароля через PBKDF2 (SHA-256).
/// Хэш и соль безопасно кладутся в конфиг; сам пароль на диск не попадает.
/// </summary>
public static class MasterPassword
{
    private const int SaltLen = 16;
    private const int HashLen = 32;
    private const int Iterations = 200_000;

    public static (string HashBase64, string SaltBase64) Create(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltLen);
        byte[] hash = Derive(password, salt);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string hashBase64, string saltBase64)
    {
        if (string.IsNullOrEmpty(password) ||
            string.IsNullOrEmpty(hashBase64) ||
            string.IsNullOrEmpty(saltBase64))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(saltBase64);
            expected = Convert.FromBase64String(hashBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actual = Derive(password, salt);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashLen);
}
