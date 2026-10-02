using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using OetLearner.Api.Data;

namespace OetLearner.Api.Tests.Platform;

/// <summary>TEMPORARY verification probe (WAI-06): the hand-edited
/// WritingDraftsV2 snapshot block must match the EF model. Removed once a CI
/// run proves it; speaking-ci's migrations-check owns this on PRs.</summary>
public sealed class WritingDraftSnapshotProbeTests
{
    [Fact]
    public void WritingDraftsV2Snapshot_MatchesTheModel()
    {
        using var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseNpgsql("Host=localhost;Database=snapshot_probe", npgsql => npgsql.UseVector())
            .Options);
        IModel snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        if (snapshot is IMutableModel mutable) snapshot = mutable.FinalizeModel();
        snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot);
        var differences = db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshot.GetRelationalModel(), db.GetService<IDesignTimeModel>().Model.GetRelationalModel());
        var described = differences
            .Select(op => $"{op.GetType().Name} {(op as ITableMigrationOperation)?.Table ?? (op as CreateTableOperation)?.Name}.{(op as ColumnOperation)?.Name}")
            .ToList();
        Assert.True(described.All(d => !d.Contains("WritingDraftsV2", StringComparison.Ordinal)),
            string.Join("\n", described));
    }
}
