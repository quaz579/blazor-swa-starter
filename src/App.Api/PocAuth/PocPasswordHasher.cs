using System.Security.Cryptography;
using System.Text;

namespace App.Api.PocAuth;

public static class PocPasswordHasher
{
    public const string Algorithm = "PBKDF2-SHA512";
    public const int DefaultIterations = 220_000;
    public const int SaltBytes = 16;
    public const int KeyBytes = 32;

    public static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA512, KeyBytes);

    public static (string Salt, string Hash, int Iterations) Hash(string password, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(Derive(password, salt, iterations)), iterations);
    }

    public static bool Verify(string password, string saltBase64, string hashBase64, int iterations)
    {
        if (iterations <= 0)
        {
            return false;
        }

        try
        {
            var expected = Convert.FromBase64String(hashBase64);
            var actual = Derive(password, Convert.FromBase64String(saltBase64), iterations);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
