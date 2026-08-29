using OetLearner.Api.Endpoints;

namespace OetLearner.Api.Tests.Mocks;

public class MockSpeakingAccessPolicyTests
{
    [Fact]
    public void ResultReleasePath_NeverRequiresAiOnly()
    {
        Assert.False(MockSpeakingAccessPolicy.RequiresAiOnly);
    }
}
