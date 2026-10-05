using System.Security.Cryptography;
using System.Text;
using Fleet.Core.Crypto;
using Fleet.Manager.Provisioning;

namespace Fleet.Manager.Tests.Infrastructure;

/// <summary>Well-formed (but fake) ssh-ed25519 keys: valid wire format so the production parsers accept them.</summary>
public static class FakeKeys
{
    /// <summary>Base64 of the OpenSSH public key blob: string "ssh-ed25519" + string(32 key bytes).</summary>
    public static string Blob(byte[] material)
    {
        var key = SHA256.HashData(material);
        using var stream = new MemoryStream();
        WriteString(stream, Encoding.ASCII.GetBytes("ssh-ed25519"));
        WriteString(stream, key);
        return Convert.ToBase64String(stream.ToArray());
    }

    public static string HostKeyBlob(string seed) => Blob(Encoding.UTF8.GetBytes("host-key:" + seed));

    public static string PublicLine(byte[] privateMaterial) => "ssh-ed25519 " + Blob(privateMaterial) + " oet-fleet-manager";

    public static string PublicLineFor(SecretBuffer privateKey) => PublicLine(privateKey.AsSpan().ToArray());

    public static string OwnerKeyText(string marker) =>
        "-----BEGIN OPENSSH PRIVATE KEY-----\n" + marker + "\n-----END OPENSSH PRIVATE KEY-----\n"; // secret-scan:allow (fake PEM framing, no key material)

    private static void WriteString(Stream stream, byte[] value)
    {
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, (uint)value.Length);
        stream.Write(length);
        stream.Write(value);
    }
}

/// <summary>ssh-keygen replaced by a deterministic function: the public half is a hash of the private bytes.</summary>
public sealed class FakeSshKeyTool : ISshKeyTool
{
    private int _generated;

    public int GenerateCalls => _generated;

    public Task<GeneratedKeyPair> GenerateEd25519Async(CancellationToken cancellationToken)
    {
        var number = Interlocked.Increment(ref _generated);
        var material = Encoding.UTF8.GetBytes("FAKE-MANAGER-PRIVATE-KEY-" + number);
        return Task.FromResult(new GeneratedKeyPair(SecretBuffer.FromBytes(material), FakeKeys.PublicLine(material)));
    }

    /// <summary>Runs inside <see cref="DerivePublicKeyAsync"/>: a test's way to change the world between two steps of one service call (a concurrent writer).</summary>
    public Func<Task>? OnDerive { get; set; }

    public async Task<string?> DerivePublicKeyAsync(SecretBuffer privateKey, CancellationToken cancellationToken)
    {
        if (OnDerive is { } hook)
        {
            await hook();
        }

        var text = Encoding.UTF8.GetString(privateKey.AsSpan());
        if (text.Contains("PASSPHRASE-PROTECTED", StringComparison.Ordinal))
        {
            return null;
        }

        return FakeKeys.PublicLine(privateKey.AsSpan().ToArray());
    }

    /// <summary>The passphrase the fake accepts for a key whose text carries <c>PASSPHRASE-PROTECTED</c>.</summary>
    public const string CorrectPassphrase = "correct passphrase 1";

    /// <summary>Every passphrase this fake was asked to try (so a test can prove it was never kept anywhere else).</summary>
    public List<string> TriedPassphrases { get; } = new();

    public Task<SecretBuffer?> RemovePassphraseAsync(SecretBuffer privateKey, string passphrase, CancellationToken cancellationToken)
    {
        TriedPassphrases.Add(passphrase);
        if (!string.Equals(passphrase, CorrectPassphrase, StringComparison.Ordinal))
        {
            return Task.FromResult<SecretBuffer?>(null);
        }

        var text = Encoding.UTF8.GetString(privateKey.AsSpan());
        return Task.FromResult<SecretBuffer?>(SecretBuffer.FromUtf8(text.Replace("PASSPHRASE-PROTECTED", "UNPROTECTED", StringComparison.Ordinal)));
    }
}
