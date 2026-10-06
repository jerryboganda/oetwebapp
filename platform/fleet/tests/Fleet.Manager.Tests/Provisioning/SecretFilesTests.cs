using System.Text;
using Fleet.Core.Crypto;
using Fleet.Manager.Configuration;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Provisioning;
using Fleet.Manager.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Tests.Provisioning;

/// <summary>Secret files, per-run scratch directories, public key lines and the ssh-keygen wrapper (OET-RWP/1 sections 8.2 and 8.5).</summary>
public sealed class SecretFilesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fleet-secret-tests-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ---- SecretFile ------------------------------------------------------------------------

    [Fact]
    public void A_secret_file_is_read_trimmed_and_anything_unusable_is_null()
    {
        Assert.Equal("value-with-newline", SecretFile.TryRead(Write("a", "  value-with-newline \r\n")));
        Assert.Null(SecretFile.TryRead(Write("empty", string.Empty)));
        Assert.Null(SecretFile.TryRead(Write("blank", " \n\t ")));
        Assert.Null(SecretFile.TryRead(Path.Combine(_root, "missing")));
        Assert.Null(SecretFile.TryRead(_root));
    }

    [Fact]
    public void Bearer_comparison_is_exact_and_refuses_empty_values()
    {
        Assert.True(SecretFile.FixedTimeEquals("ofs1_secret", "ofs1_secret"));
        Assert.False(SecretFile.FixedTimeEquals("ofs1_secret", "ofs1_secreT"));
        Assert.False(SecretFile.FixedTimeEquals("ofs1_secret", "ofs1_secret "));
        Assert.False(SecretFile.FixedTimeEquals("short", "a-much-longer-expected-secret"));
        Assert.False(SecretFile.FixedTimeEquals(null, "x"));
        Assert.False(SecretFile.FixedTimeEquals("x", null));
        Assert.False(SecretFile.FixedTimeEquals(string.Empty, string.Empty));
        Assert.False(SecretFile.FixedTimeEquals(null, null));
    }

    [Fact]
    public void The_api_credential_is_cached_for_five_seconds_so_rotating_the_file_needs_no_restart()
    {
        var time = new ManualTimeProvider();
        var options = new FleetOptions();
        options.Secrets.Directory = _root;
        var provider = new FileApiCredentialProvider(Options.Create(options), time);

        Assert.Null(provider.GetBearer());

        Write(options.Secrets.ApiCredentialFile, "ofs1_first\n");
        Assert.Null(provider.GetBearer());

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("ofs1_first", provider.GetBearer());

        Write(options.Secrets.ApiCredentialFile, "ofs1_second\n");
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal("ofs1_first", provider.GetBearer());
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("ofs1_second", provider.GetBearer());
    }

    // ---- TempSecretFile and RunScratch -----------------------------------------------------

    [Fact]
    public void A_temp_secret_file_holds_exactly_the_secret_and_is_gone_after_dispose()
    {
        using var secret = SecretBuffer.FromUtf8("PRIVATE-KEY-BYTES");
        string path;
        using (var file = TempSecretFile.Create(_root, "key", secret))
        {
            path = file.Path;
            Assert.Equal(Path.Combine(_root, "key"), path);
            Assert.Equal("PRIVATE-KEY-BYTES", File.ReadAllText(path));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            }
        }

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_temp_secret_file_never_overwrites_an_existing_file_and_dispose_is_idempotent()
    {
        using var secret = SecretBuffer.FromUtf8("PRIVATE-KEY-BYTES");
        Write("taken", "already here");

        Assert.Throws<IOException>(() => TempSecretFile.Create(_root, "taken", secret));
        Assert.Equal("already here", File.ReadAllText(Path.Combine(_root, "taken")));

        var file = TempSecretFile.Create(_root, "once", secret);
        file.Dispose();
        file.Dispose();
        Assert.False(File.Exists(file.Path));
    }

    [Fact]
    public void A_scratch_directory_is_private_unique_and_removed_with_everything_in_it()
    {
        string path;
        using (var scratch = RunScratch.Create(_root))
        {
            path = scratch.Path;
            Assert.StartsWith(Path.Combine(_root, "run-"), path);
            Assert.True(Directory.Exists(path));
            File.WriteAllText(scratch.File("known_hosts"), "x");
            Directory.CreateDirectory(Path.Combine(path, "nested"));
            File.WriteAllText(Path.Combine(path, "nested", "deep"), "y");
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(path));
            }

            using var other = RunScratch.Create(_root);
            Assert.NotEqual(path, other.Path);
        }

        Assert.False(Directory.Exists(path));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public void A_scratch_root_with_a_space_in_it_is_refused_because_ansible_options_are_space_separated()
    {
        Assert.Throws<InvalidOperationException>(() => RunScratch.Create(Path.Combine(_root, "has space")));
    }

    // ---- public key lines ------------------------------------------------------------------

    [Fact]
    public void A_public_key_line_is_validated_strictly_and_hinted_by_eight_hex_characters()
    {
        var blob = FakeKeys.Blob(Encoding.UTF8.GetBytes("one"));

        var parsed = PublicKeyLines.TryParse("ssh-ed25519 " + blob + " oet-fleet-manager");
        Assert.NotNull(parsed);
        Assert.Equal("ssh-ed25519", parsed!.Value.Algorithm);
        Assert.Equal(blob, parsed.Value.Base64);
        Assert.NotNull(PublicKeyLines.TryParse("ssh-ed25519 " + blob));
        Assert.NotNull(PublicKeyLines.TryParse("  ssh-ed25519 " + blob + "  "));

        var hint = PublicKeyLines.Hint(blob);
        Assert.Matches("^[0-9a-f]{8}$", hint);
        Assert.NotEqual(hint, PublicKeyLines.Hint(FakeKeys.Blob(Encoding.UTF8.GetBytes("two"))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ssh-dss AAAAB3NzaC1kc3MAAACBAPwQ oet")]
    [InlineData("ssh-ed25519 short")]
    [InlineData("ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\nssh-rsa AAAA evil")]
    [InlineData("command=\"/bin/sh\" ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA comment with spaces")]
    [InlineData("ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA $(id)")]
    public void Anything_that_is_not_a_plain_public_key_line_is_refused(string? line)
    {
        Assert.Null(PublicKeyLines.TryParse(line));
    }

    // ---- ssh-keygen wrapper ----------------------------------------------------------------

    private SshKeygenTool NewTool(RecordingProcessRunner runner)
    {
        var options = new FleetOptions();
        options.Provisioning.ScratchDirectory = _root;
        return new SshKeygenTool(runner, Options.Create(options));
    }

    [Fact]
    public async Task A_manager_key_is_generated_in_scratch_space_read_into_a_buffer_and_leaves_no_file_behind()
    {
        var publicLine = FakeKeys.PublicLine(Encoding.UTF8.GetBytes("generated"));
        var runner = new RecordingProcessRunner
        {
            Handler = spec =>
            {
                var keyPath = spec.Arguments[^1];
                File.WriteAllText(keyPath, "-----BEGIN OPENSSH PRIVATE KEY-----\nFAKE\n-----END OPENSSH PRIVATE KEY-----\n"); // secret-scan:allow (fake PEM framing, no key material)
                File.WriteAllText(keyPath + ".pub", publicLine + "\n");
                return new ProcessResult(0, string.Empty, string.Empty, false);
            },
        };

        var pair = await NewTool(runner).GenerateEd25519Async(CancellationToken.None);
        using var privateKey = pair.PrivateKey;

        Assert.Equal(publicLine, pair.PublicKeyLine);
        Assert.Equal("-----BEGIN OPENSSH PRIVATE KEY-----\nFAKE\n-----END OPENSSH PRIVATE KEY-----\n", Encoding.UTF8.GetString(privateKey.AsSpan())); // secret-scan:allow (fake PEM framing, no key material)
        var spec = Assert.Single(runner.Specs);
        Assert.Equal("ssh-keygen", spec.FileName);
        Assert.Equal(TimeSpan.FromSeconds(30), spec.Timeout);
        Assert.Equal(new[] { "-q", "-t", "ed25519", "-N", string.Empty, "-C", "oet-fleet-manager", "-f" }, spec.Arguments.Take(8).ToArray());
        Assert.StartsWith(_root, spec.Arguments[^1]);
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task A_key_generation_that_fails_or_yields_a_malformed_public_key_is_an_error()
    {
        var failing = new RecordingProcessRunner { Handler = _ => new ProcessResult(1, string.Empty, "boom", false) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewTool(failing).GenerateEd25519Async(CancellationToken.None));

        var malformed = new RecordingProcessRunner
        {
            Handler = spec =>
            {
                File.WriteAllText(spec.Arguments[^1], "PRIVATE");
                File.WriteAllText(spec.Arguments[^1] + ".pub", "not a public key\n");
                return new ProcessResult(0, string.Empty, string.Empty, false);
            },
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewTool(malformed).GenerateEd25519Async(CancellationToken.None));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task The_public_half_of_an_owner_key_is_derived_from_a_temporary_file_and_garbage_gives_null()
    {
        var publicLine = FakeKeys.PublicLine(Encoding.UTF8.GetBytes("owner"));
        string? seen = null;
        var runner = new RecordingProcessRunner
        {
            Handler = spec =>
            {
                seen = File.ReadAllText(spec.Arguments[^1]);
                return new ProcessResult(0, publicLine + "\n", string.Empty, false);
            },
        };
        using var key = SecretBuffer.FromUtf8("OWNER-KEY-TEXT");

        var derived = await NewTool(runner).DerivePublicKeyAsync(key, CancellationToken.None);

        Assert.Equal(publicLine, derived);
        Assert.Equal("OWNER-KEY-TEXT", seen);
        var spec = Assert.Single(runner.Specs);
        Assert.Equal(new[] { "-y", "-f" }, spec.Arguments.Take(2).ToArray());
        Assert.Equal(TimeSpan.FromSeconds(15), spec.Timeout);
        Assert.Empty(Directory.GetFileSystemEntries(_root));

        runner.Handler = _ => new ProcessResult(1, string.Empty, "incorrect passphrase supplied", false);
        Assert.Null(await NewTool(runner).DerivePublicKeyAsync(key, CancellationToken.None));

        runner.Handler = _ => new ProcessResult(0, "this is not a key\n", string.Empty, false);
        Assert.Null(await NewTool(runner).DerivePublicKeyAsync(key, CancellationToken.None));
    }
}
