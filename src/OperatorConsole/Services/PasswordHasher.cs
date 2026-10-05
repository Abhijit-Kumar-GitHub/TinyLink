using System.Security.Cryptography;

namespace OperatorConsole.Services;

// Format: PBKDF2-SHA256$<iterations>$<base64 salt>$<base64 hash>
public static class PasswordHasher
{
    private const string Prefix = "PBKDF2-SHA256";
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? stored)
    {
        var parts = stored?.Split('$');
        if (parts is not [Prefix, var iterationsText, var saltText, var hashText] ||
            !int.TryParse(iterationsText, out var iterations))
        {
            return false;
        }

        try
        {
            var expected = Convert.FromBase64String(hashText);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(saltText), iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
