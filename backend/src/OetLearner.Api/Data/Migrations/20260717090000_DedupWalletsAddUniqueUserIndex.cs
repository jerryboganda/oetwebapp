using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260717090000_DedupWalletsAddUniqueUserIndex")]
    public partial class DedupWalletsAddUniqueUserIndex : Migration
    {
        // EnsureLearnerProfileAsync used check-then-insert with no unique constraint on
        // Wallets.UserId, so concurrent first-visit requests created duplicate wallet
        // rows per user (observed: 3 rows for a single fresh account in production).
        // Duplicate wallets are a money-integrity hazard: credit grants and debits pick
        // an arbitrary row, so a user's balance can silently split across wallets.
        //
        // This migration (1) merges each user's balance onto their earliest wallet row,
        // (2) deletes the extra rows, and (3) adds the unique index that prevents the
        // race from re-creating them. The application side now tolerates the unique
        // violation (loser detaches and reuses the winner's row).
        //
        // SQL is deliberately portable (correlated subqueries only) because the test
        // suite applies migrations to SQLite while production runs PostgreSQL.

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Fold every user's total balance onto their earliest wallet row.
            migrationBuilder.Sql(
                @"UPDATE ""Wallets"" SET ""CreditBalance"" = (
                      SELECT SUM(w2.""CreditBalance"") FROM ""Wallets"" w2 WHERE w2.""UserId"" = ""Wallets"".""UserId""
                  )
                  WHERE ""Id"" IN (
                      SELECT MIN(""Id"") FROM ""Wallets"" GROUP BY ""UserId"" HAVING COUNT(*) > 1
                  );");

            // 2. Remove the duplicate rows (everything but the earliest per user).
            migrationBuilder.Sql(
                @"DELETE FROM ""Wallets"" WHERE ""Id"" NOT IN (
                      SELECT MIN(""Id"") FROM ""Wallets"" GROUP BY ""UserId""
                  );");

            // 3. Make the race impossible to persist again.
            migrationBuilder.Sql(
                @"CREATE UNIQUE INDEX ""IX_Wallets_UserId_Unique"" ON ""Wallets"" (""UserId"");");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The dedup itself is irreversible (duplicate rows carried no independent
            // meaning); only the index is dropped.
            migrationBuilder.Sql(@"DROP INDEX ""IX_Wallets_UserId_Unique"";");
        }
    }
}
