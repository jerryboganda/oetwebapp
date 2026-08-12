using OetLearner.Api.Services.Assessment;

namespace OetLearner.Api.Tests.Assessment;

public sealed class AssessmentReleaseGateTests
{
    [Fact]
    public void Marking_policy_preserves_owner_graph_and_peak_concurrency_evidence()
    {
        var policy = new AssessmentMarkingPolicyDocument(
            ReleaseGate: new AssessmentReleaseGateDocument(
                ScoreGraphLegalStyleApproved: true,
                PeakConcurrentTimedAttempts: 250,
                PeakConcurrencyEvidenceUrl: "https://evidence.example/loads/lr-v1"));

        var parsed = AssessmentMarkingPolicyDocument.Parse(policy.Serialize());

        Assert.NotNull(parsed.ReleaseGate);
        Assert.True(parsed.ReleaseGate!.IsApproved);
        Assert.Equal(250, parsed.ReleaseGate.PeakConcurrentTimedAttempts);
    }

    [Fact]
    public void Missing_owner_release_gate_is_not_approved()
    {
        var parsed = AssessmentMarkingPolicyDocument.Parse(
            new AssessmentMarkingPolicyDocument().Serialize());

        Assert.False(parsed.ReleaseGate?.IsApproved == true);
    }

    [Fact]
    public void Non_https_peak_concurrency_evidence_is_not_approved()
    {
        var gate = new AssessmentReleaseGateDocument(
            ScoreGraphLegalStyleApproved: true,
            PeakConcurrentTimedAttempts: 250,
            PeakConcurrencyEvidenceUrl: "http://example.invalid/load-evidence");

        Assert.False(gate.IsApproved);
    }
}
