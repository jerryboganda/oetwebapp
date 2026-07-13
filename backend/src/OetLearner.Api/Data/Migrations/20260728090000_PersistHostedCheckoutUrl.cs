using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Persists the hosted checkout URL beside the provider session id so an
/// idempotent replay can return locally without another provider request.
/// Additive and nullable for legacy and non-hosted gateway rows.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260728090000_PersistHostedCheckoutUrl")]
public partial class PersistHostedCheckoutUrl : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "HostedCheckoutUrl",
            table: "CheckoutSessions",
            maxLength: 2048,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "HostedCheckoutUrl",
            table: "CheckoutSessions");
    }
}
