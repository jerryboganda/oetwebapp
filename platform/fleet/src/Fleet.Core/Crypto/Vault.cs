using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Fleet.Core.Crypto;

/// <summary>The master key is absent, unreadable or has the wrong length. Startup must fail loudly.</summary>
public sealed class VaultConfigurationException : Exception
{
    public VaultConfigurationException(string message)
        : base(message)
    {
    }
}

/// <summary>A vault record could not be decrypted (wrong key, wrong AAD, tampering, unknown key id).</summary>
public sealed class VaultDecryptionException : Exception
{
    public VaultDecryptionException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The 32-byte AES-256 master keys (OET-RWP/1 section 8.5). <c>fleet_master_key</c> is current,
/// <c>fleet_master_key_prev</c> (optional) is the key being rotated away from. There is NO
/// fallback: a missing, unreadable or wrong-length key throws <see cref="VaultConfigurationException"/>,
/// and a key is never generated implicitly.
/// </summary>
public sealed class MasterKeyRing
{
    public const string CurrentFileName = "fleet_master_key";
    public const string PreviousFileName = "fleet_master_key_prev";
    public const int KeyLength = 32;

    private readonly Dictionary<uint, byte[]> _keys;

    private MasterKeyRing(uint currentKeyId, Dictionary<uint, byte[]> keys)
    {
        CurrentKeyId = currentKeyId;
        _keys = keys;
    }

    public uint CurrentKeyId { get; }

    public byte[] CurrentKey => _keys[CurrentKeyId];

    public IReadOnlyCollection<uint> KeyIds => _keys.Keys;

    /// <summary>First four bytes (big-endian) of SHA-256 of the key.</summary>
    public static uint KeyIdOf(ReadOnlySpan<byte> key) => BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(key));

    public static MasterKeyRing FromKeys(byte[] current, byte[]? previous = null)
    {
        EnsureKey(current, "current");
        var keys = new Dictionary<uint, byte[]> { [KeyIdOf(current)] = (byte[])current.Clone() };
        if (previous is not null)
        {
            EnsureKey(previous, "previous");
            keys[KeyIdOf(previous)] = (byte[])previous.Clone();
        }

        return new MasterKeyRing(KeyIdOf(current), keys);
    }

    public static MasterKeyRing LoadFromDirectory(string directory)
    {
        var current = ParseKeyFile(Path.Combine(directory, CurrentFileName));
        byte[]? previous = null;
        var previousPath = Path.Combine(directory, PreviousFileName);
        // An EMPTY previous-key file means "no key being rotated away from" (compose mounts /dev/null when none is set).
        if (File.Exists(previousPath) && new FileInfo(previousPath).Length > 0)
        {
            previous = ParseKeyFile(previousPath);
        }

        return FromKeys(current, previous);
    }

    /// <summary>Accepts exactly 32 raw bytes, or the same key as 64 hex characters or 44 base64 characters (one trailing newline allowed).</summary>
    public static byte[] ParseKeyFile(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultConfigurationException("Master key file '" + path + "' is missing or unreadable.");
        }

        byte[]? key = null;
        if (bytes.Length == KeyLength)
        {
            key = bytes;
        }
        else
        {
            var text = Encoding.ASCII.GetString(bytes).Trim();
            if (text.Length == KeyLength * 2 && text.All(Uri.IsHexDigit))
            {
                key = Convert.FromHexString(text);
            }
            else if (text.Length == 44 && text.EndsWith('='))
            {
                try
                {
                    var decoded = Convert.FromBase64String(text);
                    if (decoded.Length == KeyLength)
                    {
                        key = decoded;
                    }
                }
                catch (FormatException)
                {
                    key = null;
                }
            }
        }

        if (key is null)
        {
            throw new VaultConfigurationException(
                "Master key file '" + path + "' must hold exactly 32 bytes (raw, 64 hex characters or 44 base64 characters).");
        }

        EnsureKey(key, Path.GetFileName(path));
        return key;
    }

    public bool TryGetKey(uint keyId, out byte[] key)
    {
        if (_keys.TryGetValue(keyId, out var found))
        {
            key = found;
            return true;
        }

        key = Array.Empty<byte>();
        return false;
    }

    private static void EnsureKey(byte[] key, string label)
    {
        if (key.Length != KeyLength)
        {
            throw new VaultConfigurationException("Master key '" + label + "' must be exactly 32 bytes.");
        }

        if (key.All(b => b == 0))
        {
            throw new VaultConfigurationException("Master key '" + label + "' is all zero bytes; refusing to use it.");
        }
    }
}

/// <summary>
/// AES-256-GCM record format of OET-RWP/1 section 8.5:
/// <c>0x01 | keyId (4 bytes, big-endian) | nonce (12 random bytes) | ciphertext | tag (16 bytes)</c>
/// with AAD <c>fleet-vault:v1:{purpose}:{hostId}:{credentialId}</c> so a record cannot be moved to
/// another host, purpose or row. Every encryption draws a fresh random 96-bit nonce.
/// </summary>
public sealed class VaultCipher
{
    public const byte FormatVersion = 0x01;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int HeaderSize = 1 + 4 + NonceSize;

    private readonly MasterKeyRing _keys;

    public VaultCipher(MasterKeyRing keys)
    {
        _keys = keys;
    }

    public MasterKeyRing Keys => _keys;

    public static string Aad(string purpose, string hostId, string credentialId) =>
        "fleet-vault:v1:" + purpose + ":" + hostId + ":" + credentialId;

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext, string aad)
    {
        var blob = new byte[HeaderSize + plaintext.Length + TagSize];
        blob[0] = FormatVersion;
        BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(1, 4), _keys.CurrentKeyId);
        var nonce = blob.AsSpan(5, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = blob.AsSpan(HeaderSize, plaintext.Length);
        var tag = blob.AsSpan(HeaderSize + plaintext.Length, TagSize);
        using var aes = new AesGcm(_keys.CurrentKey, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(aad));
        return blob;
    }

    /// <summary>Returns the plaintext. The caller owns it and should zero it (<see cref="CryptographicOperations.ZeroMemory"/>).</summary>
    public byte[] Decrypt(ReadOnlySpan<byte> blob, string aad)
    {
        if (blob.Length < HeaderSize + TagSize || blob[0] != FormatVersion)
        {
            throw new VaultDecryptionException("Unsupported vault record.");
        }

        var keyId = BinaryPrimitives.ReadUInt32BigEndian(blob.Slice(1, 4));
        if (!_keys.TryGetKey(keyId, out var key))
        {
            throw new VaultDecryptionException("No master key is available for key id " + keyId.ToString("x8", CultureInfo.InvariantCulture) + ".");
        }

        var nonce = blob.Slice(5, NonceSize);
        var cipherLength = blob.Length - HeaderSize - TagSize;
        var ciphertext = blob.Slice(HeaderSize, cipherLength);
        var tag = blob.Slice(HeaderSize + cipherLength, TagSize);
        var plaintext = new byte[cipherLength];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(aad));
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new VaultDecryptionException("Vault record failed authentication.");
        }

        return plaintext;
    }

    public static uint KeyIdOf(ReadOnlySpan<byte> blob) =>
        blob.Length >= 5 ? BinaryPrimitives.ReadUInt32BigEndian(blob.Slice(1, 4)) : 0;

    /// <summary>Decrypts under whichever key wrote the record and re-encrypts under the current key with a fresh nonce.</summary>
    public byte[] Rewrap(ReadOnlySpan<byte> blob, string aad)
    {
        var plaintext = Decrypt(blob, aad);
        try
        {
            return Encrypt(plaintext, aad);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>First 8 hex characters of SHA-256 of the given (public or secret) bytes: the write-only fingerprint hint.</summary>
    public static string FingerprintHint(ReadOnlySpan<byte> material) =>
        Convert.ToHexString(SHA256.HashData(material)).ToLowerInvariant()[..8];
}

/// <summary>
/// A secret held in a pinned array that is zeroed on <see cref="Dispose"/> (OET-RWP/1 section 8.5).
/// A managed <see cref="string"/> cannot be zeroed, so secrets are converted into a buffer as early
/// as possible and never logged.
/// </summary>
public sealed class SecretBuffer : IDisposable
{
    private byte[]? _data;

    private SecretBuffer(byte[] data)
    {
        _data = data;
    }

    public static SecretBuffer FromBytes(ReadOnlySpan<byte> source)
    {
        var data = GC.AllocateArray<byte>(source.Length, pinned: true);
        source.CopyTo(data);
        return new SecretBuffer(data);
    }

    public static SecretBuffer FromUtf8(string text)
    {
        var data = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(text), pinned: true);
        Encoding.UTF8.GetBytes(text, data);
        return new SecretBuffer(data);
    }

    public int Length => _data?.Length ?? 0;

    public bool IsDisposed => _data is null;

    public ReadOnlySpan<byte> AsSpan() =>
        _data ?? throw new ObjectDisposedException(nameof(SecretBuffer));

    public void Dispose()
    {
        var data = Interlocked.Exchange(ref _data, null);
        if (data is not null)
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }
}
