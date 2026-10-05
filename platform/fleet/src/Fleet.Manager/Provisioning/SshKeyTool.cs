using System.Text.RegularExpressions;
using Fleet.Core.Crypto;
using Fleet.Core.Ssh;
using Fleet.Manager.Configuration;
using Fleet.Manager.Infrastructure;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Provisioning;

/// <summary>A freshly generated manager key. The caller owns <see cref="PrivateKey"/> and must dispose it.</summary>
public sealed record GeneratedKeyPair(SecretBuffer PrivateKey, string PublicKeyLine);

public interface ISshKeyTool
{
    /// <summary>Generates an unencrypted ed25519 key pair in memory-backed scratch space (OET-RWP/1 section 8.2, step S3).</summary>
    Task<GeneratedKeyPair> GenerateEd25519Async(CancellationToken cancellationToken);

    /// <summary>
    /// Derives the public key line of a private key. Null when the key cannot be read non-interactively
    /// (for example a passphrase-protected key, which the manager does not support).
    /// </summary>
    Task<string?> DerivePublicKeyAsync(SecretBuffer privateKey, CancellationToken cancellationToken);
}

public static class PublicKeyLines
{
    private static readonly Regex Pattern = new(
        @"\A(ssh-ed25519|ecdsa-sha2-nistp256|ecdsa-sha2-nistp384|ecdsa-sha2-nistp521|ssh-rsa) ([A-Za-z0-9+/]{16,2048}={0,2})( [A-Za-z0-9@._-]{0,64})?\z",
        RegexOptions.CultureInvariant);

    /// <summary>Validates a public key line and returns its algorithm and base64 blob, or null.</summary>
    public static (string Algorithm, string Base64)? TryParse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var match = Pattern.Match(line.Trim());
        return match.Success ? (match.Groups[1].Value, match.Groups[2].Value) : null;
    }

    /// <summary>First 8 hex characters of SHA-256 over the decoded public key blob (the vault's write-only hint).</summary>
    public static string Hint(string base64Blob) => VaultCipher.FingerprintHint(Convert.FromBase64String(base64Blob));
}

/// <summary>Uses <c>ssh-keygen</c> from the manager image. Key material only ever touches a 0700 tmpfs directory that is zeroed and removed.</summary>
public sealed class SshKeygenTool : ISshKeyTool
{
    private readonly IProcessRunner _runner;
    private readonly IOptions<FleetOptions> _options;

    public SshKeygenTool(IProcessRunner runner, IOptions<FleetOptions> options)
    {
        _runner = runner;
        _options = options;
    }

    public async Task<GeneratedKeyPair> GenerateEd25519Async(CancellationToken cancellationToken)
    {
        var provisioning = _options.Value.Provisioning;
        using var scratch = RunScratch.Create(provisioning.ScratchDirectory);
        var keyPath = scratch.File("key");
        var result = await _runner.RunAsync(
            new ProcessSpec(
                provisioning.SshKeygenExecutable,
                new[] { "-q", "-t", "ed25519", "-N", string.Empty, "-C", "oet-fleet-manager", "-f", keyPath },
                null,
                null,
                TimeSpan.FromSeconds(30)),
            cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException("ssh-keygen failed to generate a manager key.");
        }

        byte[]? privateBytes = null;
        try
        {
            privateBytes = await File.ReadAllBytesAsync(keyPath, cancellationToken);
            var publicLine = (await File.ReadAllTextAsync(keyPath + ".pub", cancellationToken)).Trim();
            if (PublicKeyLines.TryParse(publicLine) is null)
            {
                throw new InvalidOperationException("ssh-keygen produced an unexpected public key.");
            }

            return new GeneratedKeyPair(SecretBuffer.FromBytes(privateBytes), publicLine);
        }
        finally
        {
            if (privateBytes is not null)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(privateBytes);
            }
        }
    }

    public async Task<string?> DerivePublicKeyAsync(SecretBuffer privateKey, CancellationToken cancellationToken)
    {
        var provisioning = _options.Value.Provisioning;
        using var scratch = RunScratch.Create(provisioning.ScratchDirectory);
        using var keyFile = TempSecretFile.Create(scratch.Path, "owner-key", privateKey);
        var result = await _runner.RunAsync(
            new ProcessSpec(
                provisioning.SshKeygenExecutable,
                new[] { "-y", "-f", keyFile.Path },
                null,
                null,
                TimeSpan.FromSeconds(15)),
            cancellationToken);
        if (!result.Success)
        {
            return null;
        }

        var line = result.Stdout.Trim();
        return PublicKeyLines.TryParse(line) is null ? null : line;
    }
}
