using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Tests.Speaking;

public sealed class SpeakingSimulationV11SchemaTests
{
    [Fact]
    public void Rubric_seed_contains_exactly_ten_criteria_and_weights_sum_to_100()
    {
        var criteria = SpeakingSimulationV11Contracts.RubricCriteria.Criteria;

        Assert.Equal(10, criteria.Count);
        Assert.Equal(new[] { 10, 12, 8, 10, 14, 10, 10, 12, 9, 5 }, criteria.Select(x => x.Weight).ToArray());
        Assert.Equal(
            new[]
            {
                "intelligibility_pronunciation",
                "fluency_continuity",
                "grammar_vocabulary",
                "appropriateness_plain_language",
                "relationship_building_empathy",
                "patient_perspective",
                "information_gathering",
                "information_giving_checking",
                "structure_task_management",
                "closure_time_management",
            },
            criteria.Select(x => x.CriterionCode).ToArray());
        Assert.Equal(100, criteria.Sum(x => x.Weight));
        Assert.DoesNotContain(criteria.SelectMany(x => x.EnabledRuleIds), ruleId => string.Equals(ruleId, "rule55", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DbContext_maps_required_indexes_for_v11_simulation_entities()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-simulation-v11-schema-{Guid.NewGuid():N}")
            .Options;

        using var db = new LearnerDbContext(options);

        AssertHasIndex<SpeakingSimulationV11Assessment>(db, nameof(SpeakingSimulationV11Assessment.ExamSessionId), nameof(SpeakingSimulationV11Assessment.SpeakingSessionId));
        AssertHasIndex<SpeakingSimulationV11Assessment>(db, nameof(SpeakingSimulationV11Assessment.RolePlayCardId));
        AssertHasIndex<SpeakingSimulationV11Assessment>(db, nameof(SpeakingSimulationV11Assessment.Status));
        AssertHasIndex<SpeakingSimulationV11Evidence>(db, nameof(SpeakingSimulationV11Evidence.PrimaryCriterionCode));
        AssertHasIndex<SpeakingSimulationV11Evidence>(db, nameof(SpeakingSimulationV11Evidence.GeneratedAt));
        AssertHasIndex<SpeakingSimulationV11TurnMetric>(db, nameof(SpeakingSimulationV11TurnMetric.GeneratedAt));
    }

    private static void AssertHasIndex<TEntity>(LearnerDbContext db, params string[] propertyNames)
        where TEntity : class
    {
        var entityType = db.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);
        Assert.Contains(
            entityType!.GetIndexes(),
            index => index.Properties.Select(x => x.Name).SequenceEqual(propertyNames));
    }
}
