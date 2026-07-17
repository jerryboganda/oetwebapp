using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OetWithDrHesham.Api.Data;

/// <summary>
/// 2026-05-28 — suppress the EF Core 10 `PendingModelChangesWarning` so
/// `dotnet ef database update` can apply migrations even when the
/// snapshot has long-standing drift unrelated to the new migration.
///
/// Without this, EF refuses to apply ANY new migration after the most
/// recent one because it compares the live model to the snapshot in the
/// LATEST migration and treats every uncaptured numeric-precision /
/// nullability divergence as a blocking warning. The drift it complains
/// about (numeric(5,4) → numeric on WritingTutorCalibrations etc.) is
/// already documented in earlier migrations as intentional; suppressing
/// the warning is the EF-team-recommended fix per
/// https://aka.ms/efcore-docs-pending-changes.
///
/// This is a no-op at runtime; the warning only fires when EF compares
/// the model snapshot to the live model at design time.
/// </summary>
public partial class LearnerDbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(b => b.Ignore(RelationalEventId.PendingModelChangesWarning));
    }
}
