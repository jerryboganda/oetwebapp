using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddListeningExtractionDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Details",
                table: "AuditEvents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "ListeningExtractionDrafts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProposedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProposedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsStub = table.Column<bool>(type: "boolean", nullable: false),
                    StubReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Summary = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ProposedQuestionsJson = table.Column<string>(type: "text", nullable: false),
                    RawAiResponseJson = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningExtractionDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningExtractionDrafts_ContentPapers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "ContentPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningExtractionDrafts_PaperId_Status",
                table: "ListeningExtractionDrafts",
                columns: new[] { "PaperId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningExtractionDrafts_ProposedAt",
                table: "ListeningExtractionDrafts",
                column: "ProposedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListeningExtractionDrafts");

            migrationBuilder.AlterColumn<string>(
                name: "Details",
                table: "AuditEvents",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
