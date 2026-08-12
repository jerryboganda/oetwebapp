using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

[DbContext(typeof(LearnerDbContext))]
[Migration("20260904090000_AddCustomerSupportCases")]
public partial class AddCustomerSupportCases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CustomerSupportCases",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ExternalTicketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CandidateUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                OpenedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                OpenedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerSupportCases", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "UX_CustomerSupportCases_Ticket_Candidate",
            table: "CustomerSupportCases",
            columns: new[] { "ExternalTicketId", "CandidateUserId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CustomerSupportCases_Candidate_Status_Expiry",
            table: "CustomerSupportCases",
            columns: new[] { "CandidateUserId", "Status", "ExpiresAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CustomerSupportCases");
    }
}
