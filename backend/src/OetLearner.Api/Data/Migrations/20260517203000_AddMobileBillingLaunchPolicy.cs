using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260517203000_AddMobileBillingLaunchPolicy")]
    public partial class AddMobileBillingLaunchPolicy : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MobileBillingPolicy",
                table: "LaunchReadinessSettings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "hybrid");

            migrationBuilder.AddColumn<string>(
                name: "RevenueCatIosApiKey",
                table: "LaunchReadinessSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevenueCatAndroidApiKey",
                table: "LaunchReadinessSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IosIapProductId",
                table: "LaunchReadinessSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AndroidIapProductId",
                table: "LaunchReadinessSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MobileBillingEvidenceUrl",
                table: "LaunchReadinessSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MobileStoreReviewNotes",
                table: "LaunchReadinessSettings",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MobileBillingPolicy", table: "LaunchReadinessSettings");
            migrationBuilder.DropColumn(name: "RevenueCatIosApiKey", table: "LaunchReadinessSettings");
            migrationBuilder.DropColumn(name: "RevenueCatAndroidApiKey", table: "LaunchReadinessSettings");
            migrationBuilder.DropColumn(name: "IosIapProductId", table: "LaunchReadinessSettings");
            migrationBuilder.DropColumn(name: "AndroidIapProductId", table: "LaunchReadinessSettings");
            migrationBuilder.DropColumn(name: "MobileBillingEvidenceUrl", table: "LaunchReadinessSettings");
            migrationBuilder.DropColumn(name: "MobileStoreReviewNotes", table: "LaunchReadinessSettings");
        }
    }
}
