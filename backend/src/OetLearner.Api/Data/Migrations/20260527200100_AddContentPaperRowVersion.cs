using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Adds RowVersion (int, default 0) to ContentPapers for optimistic concurrency.
/// Counterpart of 20260527200000_AddListeningAttemptRowVersion which did the same
/// for the ListeningAttempts table.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260527200100_AddContentPaperRowVersion")]
public partial class AddContentPaperRowVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // No-op: RowVersion column already added by 20260525123133_AddBillingV2Schema
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No-op: column owned by 20260525123133_AddBillingV2Schema
    }
}
