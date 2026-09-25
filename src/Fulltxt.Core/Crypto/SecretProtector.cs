using System.Security.Cryptography;

namespace Fulltxt.Core.Crypto;

/// <summary>
/// Schützt Geheimnisse (DB-Schlüssel, Cloud-Zugangsdaten) über Windows DPAPI, gebunden an das
/// aktuelle Nutzerkonto. Die Daten verlassen nie den Rechner und sind ohne den Windows-Login
/// (z.B. bei gestohlener Festplatte) nicht entschlüsselbar.
/// </summary>
public static class SecretProtector
{
    // Fixer Kontext-String bindet geschützte Blobs an diese App, verhindert Cross-App-Replay.
    private static readonly byte[] Entropy = "Fulltxt.Windows.v1"u8.ToArray();

    public static byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public static byte[] Unprotect(byte[] ciphertext) =>
        ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);

    public static byte[] ProtectString(string plaintext) =>
        Protect(System.Text.Encoding.UTF8.GetBytes(plaintext));

    public static string UnprotectString(byte[] ciphertext) =>
        System.Text.Encoding.UTF8.GetString(Unprotect(ciphertext));
}
