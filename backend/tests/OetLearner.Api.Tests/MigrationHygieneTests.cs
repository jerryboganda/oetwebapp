using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;
using OetLearner.Api.Data.Migrations;

namespace OetLearner.Api.Tests;

public class MigrationHygieneTests
{
    [Theory]
    [InlineData(typeof(AddVoiceDesignBatchTracking), "20260523200000_AddVoiceDesignBatchTracking")]
    [InlineData(typeof(AddListeningTtsJobAndAudioSha), "20260527100000_AddListeningTtsJobAndAudioSha")]
    [InlineData(typeof(AddRecallAudioElevenLabsSettings), "20260608001000_AddRecallAudioElevenLabsSettings")]
    [InlineData(typeof(AddListeningAnswerAiSkipAndRetry), "20261101090000_AddListeningAnswerAiSkipAndRetry")]
    [InlineData(typeof(AddAiControlPlaneCore), "20261102090000_AddAiControlPlaneCore")]
    [InlineData(typeof(ExtendAiUsageRecordProvenance), "20261103090000_ExtendAiUsageRecordProvenance")]
    [InlineData(typeof(AddAiModelPricingAndFeaturePolicy), "20261104090000_AddAiModelPricingAndFeaturePolicy")]
    [InlineData(typeof(AddAiOperationResourceSlot), "20261105090000_AddAiOperationResourceSlot")]
    [InlineData(typeof(AddAiExplanationCache), "20261106090000_AddAiExplanationCache")]
    [InlineData(typeof(RestoreListeningPartBCSourceStems), "20261129000000_RestoreListeningPartBCSourceStems")]
    public void HandWrittenMigrationsExposeEfDiscoveryAttributes(Type migrationType, string expectedMigrationId)
    {
        var migrationAttribute = migrationType.GetCustomAttribute<MigrationAttribute>();
        var dbContextAttribute = migrationType.GetCustomAttribute<DbContextAttribute>();

        Assert.NotNull(migrationAttribute);
        Assert.Equal(expectedMigrationId, migrationAttribute!.Id);
        Assert.NotNull(dbContextAttribute);
        Assert.Equal(typeof(LearnerDbContext), dbContextAttribute!.ContextType);
    }
}
