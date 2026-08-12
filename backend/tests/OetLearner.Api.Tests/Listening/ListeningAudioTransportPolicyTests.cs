using System.Text.Json;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

public sealed class ListeningAudioTransportPolicyTests
{
    [Fact]
    public void Practice_snapshot_can_enable_replay_and_transport_controls()
    {
        var policy = new AssessmentMarkingPolicyDocument(
            ListeningAudioReplayAllowed: true,
            AudioLockMode: "practice");

        var resolved = ListeningAudioTransportPolicy.FromPolicy("practice", policy);

        Assert.True(resolved.CanPause);
        Assert.True(resolved.CanScrub);
        Assert.False(resolved.OnePlayOnly);
        Assert.Equal("practice", resolved.LockMode);
    }

    [Fact]
    public void High_stakes_modes_remain_strict_even_when_policy_is_relaxed()
    {
        var policy = new AssessmentMarkingPolicyDocument(
            ListeningAudioReplayAllowed: true,
            AudioLockMode: "practice");

        var resolved = ListeningAudioTransportPolicy.FromPolicy("exam", policy);

        Assert.Equal(ListeningAudioTransportPolicy.Strict, resolved);
    }

    [Fact]
    public void Missing_or_malformed_snapshot_fails_closed()
    {
        var resolved = ListeningAudioTransportPolicy.FromSnapshot("practice", "not-json");

        Assert.Equal(ListeningAudioTransportPolicy.Strict, resolved);
    }

    [Fact]
    public void Snapshot_replay_false_keeps_practice_audio_one_play_and_locked()
    {
        var policy = new AssessmentMarkingPolicyDocument(
            ListeningAudioReplayAllowed: false,
            AudioLockMode: "practice");
        var snapshot = JsonSerializer.Serialize(
            new { markingPolicy = policy },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var resolved = ListeningAudioTransportPolicy.FromSnapshot("practice", snapshot);

        Assert.False(resolved.CanPause);
        Assert.False(resolved.CanScrub);
        Assert.True(resolved.OnePlayOnly);
    }
}
