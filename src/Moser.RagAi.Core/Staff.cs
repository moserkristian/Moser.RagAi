using System.Security.Cryptography;
using System.Text;

namespace Moser.RagAi;

public static class PasswordCompare
{
    public static bool Matches(string expected, string provided)
    {
        if (expected.Length == 0 || provided.Length == 0)
            return false;

        var left = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}

public static class ReturnPath
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "/desk";
        if (value[0] != '/')
            return "/desk";
        if (value.StartsWith("//", StringComparison.Ordinal) || value.StartsWith("/\\", StringComparison.Ordinal))
            return "/desk";
        if (value.Contains('\\') || value.Contains('\r') || value.Contains('\n'))
            return "/desk";

        return value;
    }
}
