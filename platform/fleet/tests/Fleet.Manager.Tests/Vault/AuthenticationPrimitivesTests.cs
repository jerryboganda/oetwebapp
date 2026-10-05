using System.Text;
using Fleet.Core.Audit;
using Fleet.Core.Crypto;

namespace Fleet.Manager.Tests.Vault;

/// <summary>TOTP (RFC 6238 vectors, replay guard), PBKDF2 owner passwords, base32 and the log scrubber.</summary>
public sealed class AuthenticationPrimitivesTests
{
    // RFC 6238 appendix B, SHA-1 secret "12345678901234567890"; the 6-digit code is the last six digits of the 8-digit value.
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Totp_matches_the_rfc_6238_vectors(long unixSeconds, string expected)
    {
        Assert.Equal(expected, Totp.ComputeCode(RfcSecret, unixSeconds / 30));
    }

    [Fact]
    public void A_code_is_accepted_once_and_only_in_a_plus_minus_one_step_window()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_010);
        var step = Totp.StepAt(now);

        Assert.True(Totp.TryVerify(RfcSecret, Totp.ComputeCode(RfcSecret, step), now, 0, out var accepted));
        Assert.Equal(step, accepted);

        // Drift: the code of the previous and the next step also verify.
        Assert.True(Totp.TryVerify(RfcSecret, Totp.ComputeCode(RfcSecret, step - 1), now, 0, out var previous));
        Assert.Equal(step - 1, previous);
        Assert.True(Totp.TryVerify(RfcSecret, Totp.ComputeCode(RfcSecret, step + 1), now, 0, out var next));
        Assert.Equal(step + 1, next);

        // Two steps away does not.
        Assert.False(Totp.TryVerify(RfcSecret, Totp.ComputeCode(RfcSecret, step + 2), now, 0, out _));
        Assert.False(Totp.TryVerify(RfcSecret, Totp.ComputeCode(RfcSecret, step - 2), now, 0, out _));
    }

    [Fact]
    public void The_replay_guard_rejects_a_step_that_was_already_used()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_010);
        var step = Totp.StepAt(now);
        var code = Totp.ComputeCode(RfcSecret, step);

        Assert.True(Totp.TryVerify(RfcSecret, code, now, step - 1, out _));
        Assert.False(Totp.TryVerify(RfcSecret, code, now, step, out _), "the same code must not verify twice");
        Assert.False(Totp.TryVerify(RfcSecret, code, now, step + 5, out _));

        // The next step's code still works after the current one was consumed.
        Assert.True(Totp.TryVerify(RfcSecret, Totp.ComputeCode(RfcSecret, step + 1), now, step, out var accepted));
        Assert.Equal(step + 1, accepted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData(" 12345")]
    public void Malformed_codes_never_verify(string? code)
    {
        Assert.False(Totp.TryVerify(RfcSecret, code, DateTimeOffset.UtcNow, 0, out _));
    }

    [Fact]
    public void Base32_round_trips_and_the_provisioning_uri_carries_the_secret()
    {
        var bytes = new byte[] { 0, 1, 2, 3, 250, 251, 252, 253, 254, 255, 7 };
        Assert.Equal(bytes, Base32.Decode(Base32.Encode(bytes)));
        // The well-known authenticator test secret: "Hello!" followed by DE AD BE EF.
        var hello = Encoding.ASCII.GetBytes("Hello!").Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }).ToArray();
        Assert.Equal("JBSWY3DPEHPK3PXP", Base32.Encode(hello));
        Assert.Throws<FormatException>(() => Base32.Decode("not base32!"));

        var secret = Totp.GenerateSecretBase32();
        Assert.Equal(32, secret.Length);
        var uri = Totp.ProvisioningUri("OET Fleet Manager", "owner", secret);
        Assert.StartsWith("otpauth://totp/OET%20Fleet%20Manager:owner?secret=" + secret, uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }

    [Fact]
    public void Passwords_use_pbkdf2_sha512_with_at_least_220000_iterations()
    {
        var stored = PasswordHasher.Hash("a long enough password");
        var parts = stored.Split('$');
        Assert.Equal("pbkdf2-sha512", parts[0]);
        Assert.True(int.Parse(parts[1]) >= 220_000);

        Assert.True(PasswordHasher.Verify("a long enough password", stored));
        Assert.False(PasswordHasher.Verify("a long enough passworD", stored));
        Assert.False(PasswordHasher.Verify(string.Empty, stored));
        Assert.NotEqual(stored, PasswordHasher.Hash("a long enough password"));
    }

    [Fact]
    public void Weak_iteration_counts_are_refused_when_hashing_and_when_verifying()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordHasher.Hash("a long enough password", 1000));

        // A stored hash that was downgraded to a low iteration count never verifies, even with the right password.
        var stored = PasswordHasher.Hash("a long enough password");
        var parts = stored.Split('$');
        var downgraded = string.Join('$', parts[0], "1000", parts[2], parts[3]);
        Assert.False(PasswordHasher.Verify("a long enough password", downgraded));
        Assert.False(PasswordHasher.Verify("a long enough password", "garbage"));
        Assert.False(PasswordHasher.Verify("a long enough password", null));
    }

    [Theory]
    [InlineData("short", false)]
    [InlineData("aaaaaaaaaaaaaaaaaaaa", false)]
    [InlineData("correct horse battery staple", true)]
    public void Owner_passwords_must_be_long_and_not_repetitive(string password, bool acceptable)
    {
        Assert.Equal(acceptable, PasswordHasher.ValidateStrength(password) is null);
    }

    [Fact]
    public void The_scrubber_redacts_credential_shapes_and_untrusted_text_is_neutralised()
    {
        var nodeToken = "orw1_" + new string('a', 16) + "_" + new string('B', 43);
        var fleetToken = "ofs1_" + new string('1', 16) + "_" + new string('C', 43);
        var text = string.Join(
            " ",
            "token=" + nodeToken,
            "fleet=" + fleetToken,
            "gh=ghp_" + new string('x', 30),
            "jwt=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijklmnop",
            "db=postgres://user:secret@host/db",
            "-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXktdjE\n-----END OPENSSH PRIVATE KEY-----");

        var scrubbed = LogScrubber.Scrub(text, 2000);

        Assert.DoesNotContain(nodeToken, scrubbed);
        Assert.DoesNotContain(fleetToken, scrubbed);
        Assert.DoesNotContain("ghp_", scrubbed);
        Assert.DoesNotContain("eyJhbGci", scrubbed);
        Assert.DoesNotContain("user:secret", scrubbed);
        Assert.DoesNotContain("BEGIN OPENSSH", scrubbed);
        Assert.Contains(LogScrubber.Redacted, scrubbed);

        var hostile = "\u001b[31mred\u001b[0m <script>alert(1)</script>\u0007" + new string('z', 800);
        var safe = LogScrubber.SanitizeUntrusted(hostile, 100);
        Assert.DoesNotContain("\u001b", safe);
        Assert.DoesNotContain("\u0007", safe);
        Assert.DoesNotContain("<script>", safe);
        Assert.Contains("&lt;script&gt;", safe);
        Assert.True(safe.Length < 200);
        Assert.Equal(string.Empty, LogScrubber.SanitizeUntrusted(null));
    }
}
