using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Fulltxt.Core.Crypto;

/// <summary>Zugriff auf einen einzelnen, im Schlüsselbund des Nutzers abgelegten Hauptschlüssel.</summary>
public interface IKeyringBackend
{
    /// <summary>Liefert den gespeicherten Hauptschlüssel (Hex) oder null, wenn noch keiner existiert.</summary>
    string? Lookup();

    void Store(string secretHex);
}

public sealed class KeyringUnavailableException(string message) : Exception(message);

/// <summary>
/// Linux-Gegenstück zu DPAPI: Die Blobs werden mit AES-256-GCM verschlüsselt. Der dafür nötige zufällige
/// Hauptschlüssel liegt nur im Schlüsselbund des Nutzers (GNOME Keyring, KDE Wallet, KeePassXC …) und wird
/// mit dem Login entsperrt - keine Passworteingabe in FullTXT nötig.
/// </summary>
public sealed class KeyringSecretProtector(IKeyringBackend keyring) : ISecretProtector
{
    private const byte FormatVersion = 1;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private static readonly byte[] Context = "Fulltxt.Linux.v1"u8.ToArray();

    private byte[]? masterKey;

    public byte[] Protect(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        using (var aes = new AesGcm(MasterKey(), TagLength))
        {
            aes.Encrypt(nonce, plaintext, cipher, tag, Context);
        }

        var blob = new byte[1 + NonceLength + TagLength + cipher.Length];
        blob[0] = FormatVersion;
        nonce.CopyTo(blob, 1);
        tag.CopyTo(blob, 1 + NonceLength);
        cipher.CopyTo(blob, 1 + NonceLength + TagLength);
        return blob;
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        if (ciphertext.Length < 1 + NonceLength + TagLength || ciphertext[0] != FormatVersion)
        {
            throw new CryptographicException("Unbekanntes Format der geschützten Daten.");
        }

        var nonce = ciphertext.AsSpan(1, NonceLength);
        var tag = ciphertext.AsSpan(1 + NonceLength, TagLength);
        var cipher = ciphertext.AsSpan(1 + NonceLength + TagLength);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(MasterKey(), TagLength);
        aes.Decrypt(nonce, cipher, tag, plain, Context);
        return plain;
    }

    private byte[] MasterKey()
    {
        if (masterKey is not null) return masterKey;

        var hex = keyring.Lookup();
        if (hex is null)
        {
            hex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            keyring.Store(hex);
        }
        return masterKey = Convert.FromHexString(hex);
    }
}

/// <summary>Schlüsselbund-Zugriff über das Kommandozeilenwerkzeug <c>secret-tool</c> (Paket libsecret-tools).
/// Das Geheimnis wird über stdin übergeben und taucht nie in der Prozessliste auf.</summary>
public sealed class SecretToolKeyring : IKeyringBackend
{
    private static readonly string[] Attributes = ["service", "fulltxt", "account", "index-master"];

    public string? Lookup()
    {
        var (exitCode, output, error) = Run(["lookup", .. Attributes], stdin: null);
        var secret = output.Trim();
        if (exitCode == 0 && secret.Length > 0) return secret;
        if (error.Trim().Length > 0) throw Unavailable(error);
        return null; // nichts gefunden, der Schlüsselbund ist aber erreichbar
    }

    public void Store(string secretHex)
    {
        var (exitCode, _, error) = Run(["store", "--label=FullTXT Index-Schlüssel", .. Attributes], secretHex);
        if (exitCode != 0) throw Unavailable(error);
    }

    private static KeyringUnavailableException Unavailable(string detail) => new(
        "Der Schlüsselbund des Systems ist nicht erreichbar. FullTXT braucht einen laufenden Secret-Service " +
        "(z.B. GNOME Keyring, KDE Wallet oder KeePassXC) und das Programm secret-tool " +
        "(Debian/Ubuntu: sudo apt install libsecret-tools gnome-keyring). Details: " + detail.Trim());

    private static (int ExitCode, string Output, string Error) Run(string[] arguments, string? stdin)
    {
        var info = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(info)!;
            if (stdin is not null) process.StandardInput.Write(stdin);
            process.StandardInput.Close();
            var errorTask = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output, errorTask.Result);
        }
        catch (Win32Exception)
        {
            throw Unavailable("secret-tool wurde nicht gefunden.");
        }
    }
}
