using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// AI Pipeline Control Center phase 2 (hand-authored, purely additive): operator-entered credit
    /// grants per provider behind the usage dashboard's "Credits" panel. One new table, no backfill,
    /// no change to any existing object; balances shown in the dashboard are computed as grant minus
    /// internally-tracked spend, so deleting this table costs nothing but the panel itself.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270118090000_AddAiCreditGrants")]
    public partial class AddAiCreditGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiCreditGrants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GrantUsd = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCreditGrants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiCreditGrants_ProviderCode_StartsAt",
                table: "AiCreditGrants",
                columns: new[] { "ProviderCode", "StartsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiCreditGrants");
        }
    }
}
