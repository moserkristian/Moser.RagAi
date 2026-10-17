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

public readonly record struct SignInChoice(bool Succeeded, bool Anonymous, string Name);

public static class SignIn
{
    public const string AnonymousName = "Anonymous";

    public static SignInChoice Choose(string? userName, string? password, IEnumerable<(string UserName, string Password)> users)
    {
        if (string.IsNullOrWhiteSpace(userName) && string.IsNullOrWhiteSpace(password))
            return new SignInChoice(true, true, AnonymousName);

        var name = userName?.Trim() ?? "";
        var provided = password ?? "";
        foreach (var user in users)
        {
            if (string.IsNullOrWhiteSpace(user.UserName) || user.Password.Length == 0)
                continue;
            if (!string.Equals(user.UserName, name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!PasswordCompare.Matches(user.Password, provided))
                return new SignInChoice(false, false, "");

            return new SignInChoice(true, false, user.UserName);
        }

        return new SignInChoice(false, false, "");
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
