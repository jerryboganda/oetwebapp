using OetLearner.Api.Domain;
using OetLearner.Api.Services.Mocks.Results;

namespace OetLearner.Api.Tests.Mocks;

public class MockFullGradeRetiredTests
{
    [Fact]
    public void MockReportAggregation_DoesNotReferenceRetiredFeature()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "OetLearner.Api", "Services", "Mocks", "Results", "MockReportAggregationService.cs");
        path = Path.GetFullPath(path);
        Assert.True(File.Exists(path), path);
        var src = File.ReadAllText(path);
        Assert.DoesNotContain(AiFeatureCodes.MockFullGrade, src, StringComparison.Ordinal);
        Assert.DoesNotContain("mock.full_grade", src, StringComparison.Ordinal);
    }

    [Fact]
    public void RetiredFeatureCode_IsNotInvokedByAggregationType()
    {
        var methods = typeof(MockReportAggregationService).GetMethods();
        foreach (var method in methods)
        {
            Assert.DoesNotContain("MockFullGrade", method.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
