namespace Fulltxt.Core.Crypto;

/// <summary>Schützt Geheimnisse (DB-Schlüssel, Cloud-Zugangsdaten) so, dass sie nur im aktuellen
/// Nutzerkonto lesbar sind. Die Umsetzung hängt vom Betriebssystem ab.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);

    byte[] Unprotect(byte[] ciphertext);
}

/// <summary>
/// Einstiegspunkt für den Geheimnisschutz. Unter Windows über DPAPI, unter Linux über einen mit dem
/// Schlüsselbund (Secret Service) gesicherten Hauptschlüssel. Die Daten verlassen nie den Rechner und
/// sind ohne den Login des Nutzers (z.B. bei gestohlener Festplatte) nicht entschlüsselbar.
/// </summary>
public static class SecretProtector
{
    private static ISecretProtector? provider;

    /// <summary>Für Tests austauschbar; standardmäßig der zum Betriebssystem passende Schutz.</summary>
    public static ISecretProtector Provider
    {
        get => provider ??= CreateDefault();
        set => provider = value;
    }

    public static byte[] Protect(byte[] plaintext) => Provider.Protect(plaintext);

    public static byte[] Unprotect(byte[] ciphertext) => Provider.Unprotect(ciphertext);

    public static byte[] ProtectString(string plaintext) =>
        Protect(System.Text.Encoding.UTF8.GetBytes(plaintext));

    public static string UnprotectString(byte[] ciphertext) =>
        System.Text.Encoding.UTF8.GetString(Unprotect(ciphertext));

    private static ISecretProtector CreateDefault()
    {
        if (OperatingSystem.IsWindows()) return new DpapiSecretProtector();
        if (OperatingSystem.IsLinux()) return new KeyringSecretProtector(new SecretToolKeyring());
        throw new PlatformNotSupportedException("FullTXT unterstützt Windows und Linux.");
    }
}
