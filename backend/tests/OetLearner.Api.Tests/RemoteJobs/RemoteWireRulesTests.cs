using OetLearner.Api.Services.RemoteJobs;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>Range parsing (RW-063), character rules (RW-081) and request validation of the job plane. Pure.</summary>
public sealed class RemoteWireRulesTests
{
    // ── Range (section 4.3) ──────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Range_NoHeader_ServesTheWholeObject(string? header)
    {
        var range = RemoteRangeParser.Parse(header, 100);
        Assert.Equal(RemoteRangeKind.Full, range.Kind);
    }

    [Theory]
    [InlineData("bytes=0-9", 0, 9)]
    [InlineData("bytes=10-", 10, 99)]
    [InlineData("bytes=-10", 90, 99)]
    [InlineData("bytes=90-5000", 90, 99)]
    [InlineData("bytes=99-99", 99, 99)]
    [InlineData("BYTES=0-0", 0, 0)]
    [InlineData("bytes=-500", 0, 99)]
    public void Range_SingleRange_IsPartial(string header, long start, long end)
    {
        var range = RemoteRangeParser.Parse(header, 100);

        Assert.Equal(RemoteRangeKind.Partial, range.Kind);
        Assert.Equal(start, range.Start);
        Assert.Equal(end, range.End);
        Assert.Equal(end - start + 1, range.Length);
    }

    [Theory]
    [InlineData("bytes=100-")]
    [InlineData("bytes=100-120")]
    [InlineData("bytes=50-40")]
    [InlineData("bytes=0-1,5-6")]
    [InlineData("items=0-1")]
    [InlineData("bytes=")]
    [InlineData("bytes=abc")]
    [InlineData("bytes=a-b")]
    [InlineData("bytes=-0")]
    [InlineData("bytes=--5")]
    [InlineData("bytes=-1-2")]
    public void Range_AnythingUnsatisfiable_IsRejected(string header)
    {
        Assert.Equal(RemoteRangeKind.Unsatisfiable, RemoteRangeParser.Parse(header, 100).Kind);
    }

    [Fact]
    public void Range_OnAnEmptyObject_OnlyTheWholeThingIsServable()
    {
        Assert.Equal(RemoteRangeKind.Unsatisfiable, RemoteRangeParser.Parse("bytes=0-0", 0).Kind);
        Assert.Equal(RemoteRangeKind.Unsatisfiable, RemoteRangeParser.Parse("bytes=-5", 0).Kind);
        Assert.Equal(RemoteRangeKind.Full, RemoteRangeParser.Parse(null, 0).Kind);
    }

    // ── Character rules (section 6.1.3) ──────────────────────────────────────

    [Theory]
    [InlineData("plain ascii text")]
    [InlineData("tab\tand newline\nand carriage\rreturn")]
    [InlineData("non-breaking space")]
    [InlineData("replacement � char")]
    [InlineData("emoji 😀 pair")]
    [InlineData("café 中文")]
    public void TextRules_AllowsOrdinaryText(string text)
    {
        Assert.False(RemoteTextRules.HasForbiddenCharacter(text));
    }

    // Code units are passed as numbers (not as string literals) so no control character or lone surrogate
    // ever reaches the test runner's display names or result files.
    [Theory]
    [InlineData(0x0000)]
    [InlineData(0x0007)]
    [InlineData(0x0008)]
    [InlineData(0x000B)]
    [InlineData(0x000C)]
    [InlineData(0x000E)]
    [InlineData(0x001F)]
    [InlineData(0x007F)]
    [InlineData(0x0085)]
    [InlineData(0x009F)]
    [InlineData(0xFFFE)]
    [InlineData(0xFFFF)]
    [InlineData(0xD83D)]
    [InlineData(0xDE00)]
    public void TextRules_RejectsControlsNoncharactersAndUnpairedSurrogates(int codeUnit)
    {
        var text = "before " + (char)codeUnit + " after";
        Assert.True(RemoteTextRules.HasForbiddenCharacter(text));
    }

    [Fact]
    public void TextRules_AcceptsAWellFormedSurrogatePair_ButNotItsHalves()
    {
        var pair = "x " + (char)0xD83D + (char)0xDE00 + " y";
        Assert.False(RemoteTextRules.HasForbiddenCharacter(pair));
        Assert.True(RemoteTextRules.HasForbiddenCharacter("x " + (char)0xD83D));
        Assert.True(RemoteTextRules.HasForbiddenCharacter("x " + (char)0xDE00 + (char)0xD83D));
    }

    // ── Wire validation ──────────────────────────────────────────────────────

    private static RemoteClaimRequestDto ValidClaim() => new()
    {
        ClaimId = Guid.NewGuid(),
        InstanceId = Guid.NewGuid(),
        AppliedRevision = 0,
        Kinds = new List<RemoteKindOfferDto> { RemoteTestData.Offer(RemoteJobKinds.PdfExtract) },
        Capacity = RemoteTestData.Capacity(),
        Agent = new RemoteAgentDto
        {
            Version = "1.0.0",
            ImageDigest = "sha256:" + new string('b', 64),
            Protocol = 1,
            ProtocolsSupported = new List<int> { 1 },
        },
    };

    [Fact]
    public void Claim_AValidRequest_HasNoProblem()
    {
        Assert.Null(RemoteWireValidation.Claim(ValidClaim()));
    }

    [Fact]
    public void Claim_NullAndMissingMembers_AreClientErrorsNotExceptions()
    {
        Assert.NotNull(RemoteWireValidation.Claim(null));

        var noClaim = ValidClaim();
        noClaim.ClaimId = null;
        Assert.NotNull(RemoteWireValidation.Claim(noClaim));

        var emptyInstance = ValidClaim();
        emptyInstance.InstanceId = Guid.Empty;
        Assert.NotNull(RemoteWireValidation.Claim(emptyInstance));

        var noKinds = ValidClaim();
        noKinds.Kinds = new List<RemoteKindOfferDto>();
        Assert.NotNull(RemoteWireValidation.Claim(noKinds));

        var noCapacity = ValidClaim();
        noCapacity.Capacity = null;
        Assert.NotNull(RemoteWireValidation.Claim(noCapacity));

        var badDigest = ValidClaim();
        badDigest.Agent!.ImageDigest = "latest";
        Assert.NotNull(RemoteWireValidation.Claim(badDigest));

        var negativeRevision = ValidClaim();
        negativeRevision.AppliedRevision = -1;
        Assert.NotNull(RemoteWireValidation.Claim(negativeRevision));
    }

    [Fact]
    public void Claim_CapacityNumbersAreBounded()
    {
        var huge = ValidClaim();
        huge.Capacity!.MemBudgetFreeMiB = RemoteWireValidation.MaxNumber + 1;
        Assert.NotNull(RemoteWireValidation.Claim(huge));

        var negative = ValidClaim();
        negative.Capacity!.CpuBudgetFreeMilli = -1;
        Assert.NotNull(RemoteWireValidation.Claim(negative));
    }

    [Fact]
    public void Fail_RequiresAFenceAndACode_AndABoundedPrintableMessage()
    {
        Assert.Null(RemoteWireValidation.Fail(new RemoteFailRequestDto { Fence = 1, Code = "timeout", Message = "took too long (120s)" }));
        Assert.NotNull(RemoteWireValidation.Fail(null));
        Assert.NotNull(RemoteWireValidation.Fail(new RemoteFailRequestDto { Code = "timeout" }));
        Assert.NotNull(RemoteWireValidation.Fail(new RemoteFailRequestDto { Fence = 1 }));
        Assert.NotNull(RemoteWireValidation.Fail(new RemoteFailRequestDto { Fence = 1, Code = "timeout", Message = "line\nbreak" }));
        Assert.NotNull(RemoteWireValidation.Fail(new RemoteFailRequestDto { Fence = 1, Code = "timeout", Message = new string('x', 201) }));
    }

    [Fact]
    public void JobHeartbeat_RequiresAFence_AndAKnownStageShape()
    {
        Assert.Null(RemoteWireValidation.JobHeartbeat(new RemoteJobHeartbeatRequestDto { Fence = 3, Stage = "parsing", Metrics = new Dictionary<string, double> { ["pages"] = 4 } }));
        Assert.NotNull(RemoteWireValidation.JobHeartbeat(new RemoteJobHeartbeatRequestDto()));
        Assert.NotNull(RemoteWireValidation.JobHeartbeat(new RemoteJobHeartbeatRequestDto { Fence = 3, Stage = "Not A Stage" }));
    }

    [Fact]
    public void NodeHeartbeat_ValidatesTheLeaseAuditList()
    {
        RemoteNodeHeartbeatRequestDto Valid() => new()
        {
            InstanceId = Guid.NewGuid(),
            AppliedRevision = 1,
            State = "ready",
            Agent = ValidClaim().Agent,
            Kinds = new List<RemoteKindOfferDto> { RemoteTestData.Offer(RemoteJobKinds.PdfExtract) },
            Capacity = RemoteTestData.Capacity(),
            Leases = new List<RemoteLeaseReportDto>(),
        };

        Assert.Null(RemoteWireValidation.NodeHeartbeat(Valid()));

        var badState = Valid();
        badState.State = "exploding";
        Assert.NotNull(RemoteWireValidation.NodeHeartbeat(badState));

        var badLease = Valid();
        badLease.Leases = new List<RemoteLeaseReportDto> { new() { JobId = "not-a-job", Fence = 1 } };
        Assert.NotNull(RemoteWireValidation.NodeHeartbeat(badLease));

        var noLeases = Valid();
        noLeases.Leases = null;
        Assert.NotNull(RemoteWireValidation.NodeHeartbeat(noLeases));
    }
}
