using System.Security.Cryptography;

namespace Fulltxt.Core.Crypto;

/// <summary>
/// Verwaltet den SQLCipher-Schlüssel für den lokalen Suchindex. Der Schlüssel wird einmalig
/// zufällig erzeugt, per DPAPI an das Windows-Nutzerkonto gebunden auf der Platte abgelegt und
/// bei jedem Start automatisch wiederhergestellt - keine Passworteingabe nötig.
/// </summary>
public static class IndexKeyStore
{
    private const int KeyLengthBytes = 32; // 256 bit, SQLCipher default

    public static string GetOrCreateKeyHex(string keyFilePath)
    {
        if (File.Exists(keyFilePath))
        {
            var protectedBytes = File.ReadAllBytes(keyFilePath);
            var keyBytes = SecretProtector.Unprotect(protectedBytes);
            return Convert.ToHexString(keyBytes);
        }

        var newKey = RandomNumberGenerator.GetBytes(KeyLengthBytes);
        var protectedNewKey = SecretProtector.Protect(newKey);

        var directory = Path.GetDirectoryName(keyFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(keyFilePath, protectedNewKey);
        return Convert.ToHexString(newKey);
    }
}
