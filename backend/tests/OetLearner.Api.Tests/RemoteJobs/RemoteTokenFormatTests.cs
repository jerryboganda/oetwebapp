using System.Text.RegularExpressions;
using OetLearner.Api.Services.RemoteJobs;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// OET-RWP/1 section 2.2 (RW-001, RW-003): token shape, hashing and constant-time verification. Pure: no
/// database, no clock.
/// </summary>
public sealed class RemoteTokenFormatTests
{
    private static readonly Regex Hex16 = new("^[0-9a-f]{16}$", RegexOptions.CultureInvariant);
    private static readonly Regex Hex64 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    [Fact]
    public void Generate_NodeToken_HasTheSpecifiedShape()
    {
        var token = RemoteTokenFormat.Generate(RemoteTokenFormat.NodeKind);

        Assert.StartsWith("orw1_", token.Token, StringComparison.Ordinal);
        var parts = token.Token.Split('_');
        Assert.Equal(3, parts.Length);
        Assert.Matches(Hex16, token.TokenId);
        Assert.Equal(token.TokenId, parts[1]);
        Assert.Equal(RemoteTokenFormat.SecretLength, token.Secret.Length);
        Assert.Equal(token.Secret, parts[2]);
        Assert.True(token.Token.Length <= RemoteTokenFormat.MaxLength);
        Assert.DoesNotContain('.', token.Token);
        Assert.Matches(Hex64, token.SecretHashHex);
        Assert.Equal(RemoteTokenFormat.HashSecretHex(token.Secret), token.SecretHashHex);
    }

    [Fact]
    public void Generate_FleetToken_UsesTheFleetPrefix()
    {
        var token = RemoteTokenFormat.Generate(RemoteTokenFormat.FleetKind);

        Assert.StartsWith("ofs1_", token.Token, StringComparison.Ordinal);
        Assert.True(RemoteTokenFormat.TryParse("Bearer " + token.Token, RemoteTokenFormat.FleetKind, out var id, out var secret));
        Assert.Equal(token.TokenId, id);
        Assert.Equal(token.Secret, secret);
    }

    [Fact]
    public void Generate_NeverProducesAnUnderscoreInTheSecret_SoTheTokenAlwaysHasThreeParts()
    {
        for (var i = 0; i < 400; i++)
        {
            var token = RemoteTokenFormat.Generate(RemoteTokenFormat.NodeKind);
            Assert.DoesNotContain('_', token.Secret);
            Assert.Equal(3, token.Token.Split('_').Length);
            Assert.True(RemoteTokenFormat.TryParse("Bearer " + token.Token, RemoteTokenFormat.NodeKind, out _, out _));
        }
    }

    [Fact]
    public void Generate_ProducesDistinctTokens()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => RemoteTokenFormat.Generate(RemoteTokenFormat.NodeKind).TokenId).ToHashSet();
        Assert.Equal(200, ids.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer ")]
    [InlineData("Basic abc")]
    [InlineData("Bearer orw1_0123456789abcdef")]
    [InlineData("Bearer orw1_0123456789abcdef_short")]
    [InlineData("Bearer orw1_0123456789ABCDEF_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("Bearer orw1_0123456789abcdef_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_extra")]
    [InlineData("Bearer ofs1_0123456789abcdef_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2ln")]
    public void TryParse_RejectsEverythingThatIsNotAWellFormedNodeToken(string? header)
    {
        Assert.False(RemoteTokenFormat.TryParse(header, RemoteTokenFormat.NodeKind, out var id, out var secret));
        Assert.Equal(string.Empty, id);
        Assert.Equal(string.Empty, secret);
    }

    [Fact]
    public void TryParse_RejectsAnOverlongToken()
    {
        var header = "Bearer orw1_0123456789abcdef_" + new string('a', RemoteTokenFormat.SecretLength) + new string('b', 20);
        Assert.False(RemoteTokenFormat.TryParse(header, RemoteTokenFormat.NodeKind, out _, out _));
    }

    [Fact]
    public void TryParse_AcceptsTheBearerSchemeCaseInsensitively()
    {
        var token = RemoteTokenFormat.Generate(RemoteTokenFormat.NodeKind);
        Assert.True(RemoteTokenFormat.TryParse("bearer " + token.Token, RemoteTokenFormat.NodeKind, out var id, out _));
        Assert.Equal(token.TokenId, id);
    }

    [Fact]
    public void LooksLikeRemoteToken_RecognisesBothPrefixesAndNothingElse()
    {
        Assert.True(RemoteTokenFormat.LooksLikeRemoteToken("Bearer orw1_x"));
        Assert.True(RemoteTokenFormat.LooksLikeRemoteToken("Bearer ofs1_x"));
        Assert.False(RemoteTokenFormat.LooksLikeRemoteToken("Bearer eyJhbGciOiJIUzI1NiJ9.e30.c2ln"));
        Assert.False(RemoteTokenFormat.LooksLikeRemoteToken("Basic orw1_x"));
        Assert.False(RemoteTokenFormat.LooksLikeRemoteToken(null));
        Assert.False(RemoteTokenFormat.LooksLikeRemoteToken(string.Empty));
    }

    [Fact]
    public void Verify_AcceptsTheRightSecretAndRejectsAnyOtherOne()
    {
        var token = RemoteTokenFormat.Generate(RemoteTokenFormat.NodeKind);
        var other = RemoteTokenFormat.Generate(RemoteTokenFormat.NodeKind);

        Assert.True(RemoteTokenFormat.Verify(token.Secret, token.SecretHashHex));
        Assert.False(RemoteTokenFormat.Verify(other.Secret, token.SecretHashHex));
        Assert.False(RemoteTokenFormat.Verify(token.Secret + "x", token.SecretHashHex));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-hex")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Verify_NeverMatchesAnUnknownOrMalformedStoredHash(string? stored)
    {
        // An unknown token id compares against a dummy hash; it must not be possible to "match" it.
        Assert.False(RemoteTokenFormat.Verify("any-secret-value-that-is-not-a-real-one", stored));
    }

    [Fact]
    public void HashSecret_IsTheSha256OfTheAsciiBytes()
    {
        // SHA-256("abc") test vector.
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            RemoteTokenFormat.HashSecretHex("abc"));
    }

    [Fact]
    public void SecretComparison_IsConstantTime_NeverAnOrdinaryStringEquality()
    {
        // RW-003: the secret/hash comparison goes through CryptographicOperations.FixedTimeEquals only.
        var root = FindRepoRoot();
        var api = Path.Combine(root, "backend", "src", "OetLearner.Api");
        var format = File.ReadAllText(Path.Combine(api, "Services", "RemoteJobs", "RemoteTokenFormat.cs"));
        var handler = File.ReadAllText(Path.Combine(api, "Security", "RemoteWorkerAuthentication.cs"));

        Assert.Contains("FixedTimeEquals", format, StringComparison.Ordinal);

        var forbidden = new[]
        {
            new Regex(@"\b(SecretHash|storedHashHex|secretHash|computed)\b\s*(==|!=)", RegexOptions.CultureInvariant),
            new Regex(@"\b(SecretHash|storedHashHex)\s*\.\s*Equals\s*\(", RegexOptions.CultureInvariant),
            new Regex(@"string\s*\.\s*Equals\s*\([^;)]*\b(SecretHash|storedHashHex)\b", RegexOptions.CultureInvariant),
        };

        foreach (var pattern in forbidden)
        {
            Assert.DoesNotMatch(pattern, format);
            Assert.DoesNotMatch(pattern, handler);
        }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "backend")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the OET repository root.");
    }
}
