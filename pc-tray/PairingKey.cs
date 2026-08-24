using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace A2dpSink;

internal static class PairingKey
{
    private static readonly Lazy<string> Value = new(LoadOrCreate);

    public static string Secret => Value.Value;

    public static string GetKeyFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "C4P",
            "pairing-key.txt");
    }

    private static string LoadOrCreate()
    {
        string path = GetKeyFilePath();
        string? secret = null;

        if (File.Exists(path))
        {
            try
            {
                string existing = File.ReadAllText(path).Trim();

                if (existing.Length == 64 && IsHex(existing))
                {
                    secret = existing;
                }
                else
                {
                    secret = Encoding.ASCII.GetString(
                        ProtectedData.Unprotect(
                            Convert.FromBase64String(existing),
                            null,
                            DataProtectionScope.CurrentUser));
                }
            }
            catch
            {
                secret = null;
            }
        }

        if (string.IsNullOrEmpty(secret) || secret.Length != 64 || !IsHex(secret))
            secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        try
        {
            WriteProtected(path, secret);
        }
        catch
        {
        }

        return secret;
    }

    private static void WriteProtected(string path, string secret)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        byte[] protectedBytes = ProtectedData.Protect(
            Encoding.ASCII.GetBytes(secret),
            null,
            DataProtectionScope.CurrentUser);

        File.WriteAllText(path, Convert.ToBase64String(protectedBytes));
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }

        return true;
    }
}
