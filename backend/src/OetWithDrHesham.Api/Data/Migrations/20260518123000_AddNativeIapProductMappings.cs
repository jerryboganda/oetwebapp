using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Adds native iOS/Android in-app purchase product mappings to existing
    /// billing catalog targets. No store credentials or receipt/token evidence
    /// are persisted by this migration.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260518123000_AddNativeIapProductMappings")]
    public partial class AddNativeIapProductMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NativeIapProductMappings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StoreProductId = table.Column<string>(type: "character varying(192)", maxLength: 192, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NativeIapProductMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NativeIapProductMappings_Platform_IsActive",
                table: "NativeIapProductMappings",
                columns: new[] { "Platform", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_NativeIapProductMappings_Platform_StoreProductId",
                table: "NativeIapProductMappings",
                columns: new[] { "Platform", "StoreProductId" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_NativeIapProductMappings_TargetType_TargetId",
                table: "NativeIapProductMappings",
                columns: new[] { "TargetType", "TargetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NativeIapProductMappings");
        }
    }
}
