using System.Security.Cryptography;
using System.Text;
using Fleet.Core.Crypto;

namespace Fleet.Manager.Tests.Vault;

/// <summary>A missing, unreadable or wrong-length master key is a HARD startup failure: no fallback, no generated key.</summary>
public sealed class MasterKeyRingTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("fleet-keys-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void A_missing_key_file_fails_loudly()
    {
        var error = Assert.Throws<VaultConfigurationException>(() => MasterKeyRing.LoadFromDirectory(_directory));
        Assert.Contains("fleet_master_key", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void A_wrong_length_key_is_refused(int length)
    {
        // Lengths 64 and 44 are only valid as TEXT encodings (hex/base64); random bytes of those lengths are not.
        Write("fleet_master_key", RandomNumberGenerator.GetBytes(length).Select(b => (byte)(b | 0x80)).ToArray());
        Assert.Throws<VaultConfigurationException>(() => MasterKeyRing.LoadFromDirectory(_directory));
    }

    [Fact]
    public void An_all_zero_key_is_refused()
    {
        Write("fleet_master_key", new byte[32]);
        Assert.Throws<VaultConfigurationException>(() => MasterKeyRing.LoadFromDirectory(_directory));
    }

    [Fact]
    public void Raw_hex_and_base64_encodings_of_the_same_key_agree()
    {
        var key = RandomNumberGenerator.GetBytes(32);

        Write("fleet_master_key", key);
        var raw = MasterKeyRing.LoadFromDirectory(_directory);

        Write("fleet_master_key", Encoding.ASCII.GetBytes(Convert.ToHexString(key).ToLowerInvariant() + "\n"));
        var hex = MasterKeyRing.LoadFromDirectory(_directory);

        Write("fleet_master_key", Encoding.ASCII.GetBytes(Convert.ToBase64String(key) + "\r\n"));
        var base64 = MasterKeyRing.LoadFromDirectory(_directory);

        Assert.Equal(raw.CurrentKeyId, hex.CurrentKeyId);
        Assert.Equal(raw.CurrentKeyId, base64.CurrentKeyId);
        Assert.Equal(MasterKeyRing.KeyIdOf(key), raw.CurrentKeyId);
        Assert.Equal(key, raw.CurrentKey);
        Assert.Equal(key, hex.CurrentKey);
        Assert.Equal(key, base64.CurrentKey);
    }

    [Fact]
    public void The_previous_key_is_loaded_when_present_and_ignored_when_empty()
    {
        var current = RandomNumberGenerator.GetBytes(32);
        var previous = RandomNumberGenerator.GetBytes(32);
        Write("fleet_master_key", current);

        Write("fleet_master_key_prev", Array.Empty<byte>());
        Assert.Single(MasterKeyRing.LoadFromDirectory(_directory).KeyIds);

        Write("fleet_master_key_prev", previous);
        var ring = MasterKeyRing.LoadFromDirectory(_directory);
        Assert.Equal(2, ring.KeyIds.Count);
        Assert.True(ring.TryGetKey(MasterKeyRing.KeyIdOf(previous), out var found));
        Assert.Equal(previous, found);
        Assert.Equal(MasterKeyRing.KeyIdOf(current), ring.CurrentKeyId);
    }

    [Fact]
    public void An_unusable_previous_key_also_fails_startup()
    {
        Write("fleet_master_key", RandomNumberGenerator.GetBytes(32));
        Write("fleet_master_key_prev", Encoding.ASCII.GetBytes("not a key"));
        Assert.Throws<VaultConfigurationException>(() => MasterKeyRing.LoadFromDirectory(_directory));
    }

    [Fact]
    public void The_key_id_is_the_first_four_bytes_of_the_sha256_of_the_key()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var hash = SHA256.HashData(key);
        var expected = (uint)((hash[0] << 24) | (hash[1] << 16) | (hash[2] << 8) | hash[3]);
        Assert.Equal(expected, MasterKeyRing.KeyIdOf(key));
    }
}
