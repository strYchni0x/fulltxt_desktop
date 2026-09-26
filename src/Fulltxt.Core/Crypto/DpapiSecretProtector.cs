using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Fulltxt.Core.Crypto;

/// <summary>Windows DPAPI, gebunden an das aktuelle Nutzerkonto.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    // Fixer Kontext-String bindet geschützte Blobs an diese App, verhindert Cross-App-Replay.
    private static readonly byte[] Entropy = "Fulltxt.Windows.v1"u8.ToArray();

    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] ciphertext) =>
        ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
}
