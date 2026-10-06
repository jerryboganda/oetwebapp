using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Fleet.Core.Crypto;

namespace Fleet.Manager.Tests.Vault;

/// <summary>RW-138: the AES-256-GCM layout, the AAD binding and the master-key rules of OET-RWP/1 section 8.5.</summary>
public sealed class VaultCipherTests
{
    private static byte[] Key() => RandomNumberGenerator.GetBytes(32);

    private static VaultCipher Cipher(byte[]? key = null, byte[]? previous = null) =>
        new(MasterKeyRing.FromKeys(key ?? Key(), previous));

    private static string Aad() => VaultCipher.Aad("manager-ssh", "host-1", "cred-1");

    [Fact]
    public void Round_trips_and_has_the_documented_layout()
    {
        var key = Key();
        var cipher = Cipher(key);
        var plaintext = Encoding.UTF8.GetBytes("the-owner-private-key");

        var blob = cipher.Encrypt(plaintext, Aad());

        Assert.Equal(0x01, blob[0]);
        Assert.Equal(MasterKeyRing.KeyIdOf(key), BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(1, 4)));
        Assert.Equal(1 + 4 + 12 + plaintext.Length + 16, blob.Length);
        Assert.Equal(plaintext, cipher.Decrypt(blob, Aad()));
        Assert.DoesNotContain("owner-private-key", Encoding.UTF8.GetString(blob));
    }

    [Fact]
    public void Every_encryption_uses_a_fresh_random_nonce()
    {
        var cipher = Cipher();
        var plaintext = Encoding.UTF8.GetBytes("same plaintext");
        var nonces = new HashSet<string>();
        for (var i = 0; i < 64; i++)
        {
            var blob = cipher.Encrypt(plaintext, Aad());
            Assert.True(nonces.Add(Convert.ToHexString(blob.AsSpan(5, 12))), "a nonce was reused");
        }
    }

    [Theory]
    [InlineData("owner-bootstrap", "host-1", "cred-1")]
    [InlineData("manager-ssh", "host-2", "cred-1")]
    [InlineData("manager-ssh", "host-1", "cred-2")]
    public void A_record_cannot_be_moved_to_another_purpose_host_or_row(string purpose, string host, string credential)
    {
        var cipher = Cipher();
        var blob = cipher.Encrypt(Encoding.UTF8.GetBytes("secret"), Aad());
        Assert.Throws<VaultDecryptionException>(() => cipher.Decrypt(blob, VaultCipher.Aad(purpose, host, credential)));
    }

    [Fact]
    public void Tampering_with_any_part_is_detected()
    {
        var cipher = Cipher();
        var blob = cipher.Encrypt(Encoding.UTF8.GetBytes("secret value"), Aad());
        for (var index = 0; index < blob.Length; index++)
        {
            var copy = (byte[])blob.Clone();
            copy[index] ^= 0x01;
            Assert.ThrowsAny<Exception>(() => cipher.Decrypt(copy, Aad()));
        }
    }

    [Fact]
    public void A_different_master_key_cannot_decrypt()
    {
        var blob = Cipher().Encrypt(Encoding.UTF8.GetBytes("secret"), Aad());
        Assert.Throws<VaultDecryptionException>(() => Cipher().Decrypt(blob, Aad()));
    }

    [Fact]
    public void Truncated_or_foreign_records_are_rejected()
    {
        var cipher = Cipher();
        Assert.Throws<VaultDecryptionException>(() => cipher.Decrypt(new byte[10], Aad()));
        var blob = cipher.Encrypt(Encoding.UTF8.GetBytes("x"), Aad());
        blob[0] = 0x02;
        Assert.Throws<VaultDecryptionException>(() => cipher.Decrypt(blob, Aad()));
    }

    [Fact]
    public void Rewrap_moves_a_record_to_the_current_key_with_a_fresh_nonce()
    {
        var oldKey = Key();
        var newKey = Key();
        var oldBlob = Cipher(oldKey).Encrypt(Encoding.UTF8.GetBytes("rotating secret"), Aad());

        var rotated = Cipher(newKey, oldKey);
        Assert.Equal("rotating secret", Encoding.UTF8.GetString(rotated.Decrypt(oldBlob, Aad())));

        var rewrapped = rotated.Rewrap(oldBlob, Aad());
        Assert.Equal(MasterKeyRing.KeyIdOf(newKey), VaultCipher.KeyIdOf(rewrapped));
        Assert.NotEqual(oldBlob.AsSpan(5, 12).ToArray(), rewrapped.AsSpan(5, 12).ToArray());

        // Once the previous key is gone, only the rewrapped record still opens.
        var onlyNew = Cipher(newKey);
        Assert.Equal("rotating secret", Encoding.UTF8.GetString(onlyNew.Decrypt(rewrapped, Aad())));
        Assert.Throws<VaultDecryptionException>(() => onlyNew.Decrypt(oldBlob, Aad()));
    }

    [Fact]
    public void The_fingerprint_hint_is_eight_hex_characters_and_not_the_secret()
    {
        var hint = VaultCipher.FingerprintHint(Encoding.UTF8.GetBytes("public-key-material"));
        Assert.Equal(8, hint.Length);
        Assert.Matches("^[0-9a-f]{8}$", hint);
        Assert.NotEqual(hint, VaultCipher.FingerprintHint(Encoding.UTF8.GetBytes("other")));
    }

    [Fact]
    public void A_secret_buffer_is_zeroed_when_disposed()
    {
        var buffer = SecretBuffer.FromUtf8("private-key-bytes");
        Assert.Equal(17, buffer.Length);
        Assert.Equal("private-key-bytes", Encoding.UTF8.GetString(buffer.AsSpan()));
        buffer.Dispose();
        Assert.True(buffer.IsDisposed);
        Assert.Equal(0, buffer.Length);
        Assert.Throws<ObjectDisposedException>(() => buffer.AsSpan().ToArray());
        buffer.Dispose();
    }
}
