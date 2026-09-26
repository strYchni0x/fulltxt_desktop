using System.Security.Cryptography;
using Fulltxt.Core.Cloud;
using Fulltxt.Core.Crypto;

namespace Fulltxt.Core.Tests;

public sealed class SecretProtectorTests
{
    private sealed class MemoryKeyring : IKeyringBackend
    {
        public string? Secret { get; set; }
        public int Stores { get; private set; }

        public string? Lookup() => Secret;

        public void Store(string secretHex)
        {
            Secret = secretHex;
            Stores++;
        }
    }

    [Fact]
    public void Keyring_RoundTrip_AndMasterKeyCreatedOnce()
    {
        var keyring = new MemoryKeyring();
        var protector = new KeyringSecretProtector(keyring);

        var blob = protector.Protect("geheim"u8.ToArray());
        var blob2 = protector.Protect("geheim"u8.ToArray());

        Assert.Equal("geheim"u8.ToArray(), protector.Unprotect(blob));
        Assert.NotEqual(blob, blob2); // zufällige Nonce
        Assert.Equal(1, keyring.Stores);
    }

    [Fact]
    public void Keyring_NewInstance_ReusesStoredMasterKey()
    {
        var keyring = new MemoryKeyring();
        var blob = new KeyringSecretProtector(keyring).Protect("abc"u8.ToArray());

        var plain = new KeyringSecretProtector(keyring).Unprotect(blob);

        Assert.Equal("abc"u8.ToArray(), plain);
        Assert.Equal(1, keyring.Stores);
    }

    [Fact]
    public void Keyring_WrongMasterKey_Throws()
    {
        var blob = new KeyringSecretProtector(new MemoryKeyring()).Protect("abc"u8.ToArray());
        var other = new KeyringSecretProtector(new MemoryKeyring());

        Assert.ThrowsAny<CryptographicException>(() => other.Unprotect(blob));
    }

    [Fact]
    public void Keyring_TamperedBlob_Throws()
    {
        var protector = new KeyringSecretProtector(new MemoryKeyring());
        var blob = protector.Protect("abc"u8.ToArray());
        blob[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(blob));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect([1, 2, 3]));
    }

    [Fact]
    public void IndexKeyStore_WorksWithKeyringProtector()
    {
        var previous = SecretProtector.Provider;
        var path = Path.Combine(Path.GetTempPath(), $"fulltxt-key-{Guid.NewGuid():N}");
        try
        {
            SecretProtector.Provider = new KeyringSecretProtector(new MemoryKeyring());
            var first = IndexKeyStore.GetOrCreateKeyHex(path);
            var second = IndexKeyStore.GetOrCreateKeyHex(path);

            Assert.Equal(64, first.Length);
            Assert.Equal(first, second);
            Assert.DoesNotContain(first, Convert.ToHexString(File.ReadAllBytes(path)));
        }
        finally
        {
            SecretProtector.Provider = previous;
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("cloud.example.com", "https://cloud.example.com")]
    [InlineData("https://cloud.example.com/", "https://cloud.example.com")]
    [InlineData("ftp://x", null)]
    [InlineData("  ", null)]
    public void NormalizeServerUrl(string input, string? expected) =>
        Assert.Equal(expected, CloudAccountInput.NormalizeServerUrl(input));

    [Theory]
    [InlineData("http://cloud.example.com", true)]
    [InlineData("https://cloud.example.com", false)]
    [InlineData("http://192.168.1.5", false)]
    [InlineData("http://8.8.8.8", true)]
    [InlineData("http://nas.local", false)]
    [InlineData("http://localhost:8080", false)]
    public void IsInsecureRemote(string url, bool expected) =>
        Assert.Equal(expected, CloudAccountInput.IsInsecureRemote(new Uri(url)));
}
