using OetLearner.Api.Services.Ai;

namespace OetLearner.Api.Tests;

/// <summary>
/// W2 of the AI cost/reliability remediation — proves
/// <see cref="AiIdempotencyKeyBuilder"/> is stable, dimension-sensitive, and
/// normalises casing/whitespace the same way on every call.
/// </summary>
public sealed class AiIdempotencyKeyTests
{
    [Fact]
    public void Build_SameInputsTwice_ProducesIdenticalKey()
    {
        var a = AiIdempotencyKeyBuilder.Build("writing.grade", "writing", "user-1", "sub-1", 3, "hash-1", "pv1", "rb1", "anthropic/claude-sonnet-5");
        var b = AiIdempotencyKeyBuilder.Build("writing.grade", "writing", "user-1", "sub-1", 3, "hash-1", "pv1", "rb1", "anthropic/claude-sonnet-5");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Build_IsCaseAndWhitespaceInsensitive()
    {
        var a = AiIdempotencyKeyBuilder.Build("Writing.Grade", "Writing", "User-1", "Sub-1", 3, "Hash-1", "PV1", "RB1", "Anthropic/Claude-Sonnet-5");
        var b = AiIdempotencyKeyBuilder.Build("  writing.grade  ", "writing", "user-1", "sub-1", 3, "hash-1", "pv1", "rb1", "anthropic/claude-sonnet-5");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Build_ReturnsLowercaseHexSha256()
    {
        var key = AiIdempotencyKeyBuilder.Build("writing.grade", "writing", "user-1", null, null, null, null, null, null);

        Assert.Equal(64, key.Length);
        Assert.Matches("^[0-9a-f]{64}$", key);
    }

    [Theory]
    [InlineData("resourceId")]
    [InlineData("resourceVersion")]
    [InlineData("requestHash")]
    [InlineData("promptVersion")]
    [InlineData("rulebookVersion")]
    [InlineData("modelRoute")]
    [InlineData("userId")]
    public void Build_ChangingAnyDimension_ChangesTheKey(string dimensionToVary)
    {
        var baseline = AiIdempotencyKeyBuilder.Build(
            feature: "writing.grade",
            module: "writing",
            userId: "user-1",
            resourceId: "sub-1",
            resourceVersion: 1,
            requestHash: "hash-1",
            promptVersion: "pv1",
            rulebookVersion: "rb1",
            modelRoute: "anthropic/claude-sonnet-5");

        var varied = dimensionToVary switch
        {
            "resourceId" => AiIdempotencyKeyBuilder.Build("writing.grade", "writing", "user-1", "sub-2", 1, "hash-1", "pv1", "rb1", "anthropic/claude-sonnet-5"),
            "resourceVersion" => AiIdempotencyKeyBuilder.Build("writing.grade", "writing", "user-1", "sub-1", 2, "hash-1", "pv1", "rb1", "anthropic/claude-sonnet-5"),
            "requestHash" => AiIdempotencyKeyBuilder.Build("writing.grade", "writing", "user-1", "sub-1", 1, "hash-2", "pv1", "rb1", "anthropic/claude-sonnet-5"),
            "promptVersion" => AiIdempotencyKeyBuilder.Build("writing.grade", "writing", "user-1", "sub-1", 1, "hash-1", "pv2", "rb1", "anthropic/claude-sonnet-5"),
            "rulebookVersion" => AiIdempotencyKeyBuilder.Build("writing.grade", "writing", "user-1", "sub-1", 1, "hash-1", "pv1", "rb2", "anthropic/claude-sonnet-5"),
            "modelRoute" => AiIdempotencyKeyBuilder.Build("writing.grade", "writing", "user-1", "sub-1", 1, "hash-1", "pv1", "rb1", "openai/gpt-4o"),
            "userId" => AiIdempotencyKeyBuilder.Build("writing.grade", "writing", "user-2", "sub-1", 1, "hash-1", "pv1", "rb1", "anthropic/claude-sonnet-5"),
            _ => throw new ArgumentOutOfRangeException(nameof(dimensionToVary)),
        };

        Assert.NotEqual(baseline, varied);
    }

    [Fact]
    public void Build_DifferentFeatureOrModule_ChangesTheKey()
    {
        var byFeature = AiIdempotencyKeyBuilder.Build("writing.grade", "writing", "u", "r", 1, "h", "p", "rb", "m");
        var otherFeature = AiIdempotencyKeyBuilder.Build("speaking.grade", "writing", "u", "r", 1, "h", "p", "rb", "m");
        var otherModule = AiIdempotencyKeyBuilder.Build("writing.grade", "speaking", "u", "r", 1, "h", "p", "rb", "m");

        Assert.NotEqual(byFeature, otherFeature);
        Assert.NotEqual(byFeature, otherModule);
    }

    [Fact]
    public void Build_NullOptionalDimensions_DoNotThrow_AndAreStable()
    {
        var a = AiIdempotencyKeyBuilder.Build("writing.grade", "writing", null, null, null, null, null, null, null);
        var b = AiIdempotencyKeyBuilder.Build("writing.grade", "writing", null, null, null, null, null, null, null);

        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_BlankFeature_Throws(string? feature)
    {
        // Null throws ArgumentNullException (a subtype of ArgumentException);
        // empty/whitespace throw plain ArgumentException — both are "blank
        // feature is rejected", so assert the common base type.
        Assert.ThrowsAny<ArgumentException>(() =>
            AiIdempotencyKeyBuilder.Build(feature!, "writing", "u", "r", 1, "h", "p", "rb", "m"));
    }
}
