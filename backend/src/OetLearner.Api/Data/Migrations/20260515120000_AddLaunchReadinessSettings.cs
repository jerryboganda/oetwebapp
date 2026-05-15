using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLaunchReadinessSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RealtimeSttAllowedMimeTypesCsv",
                table: "ConversationSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RealtimeSttRealProviderProductionAuthorized",
                table: "ConversationSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LaunchReadinessSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MobileMinSupportedVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MobileLatestVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MobileForceUpdate = table.Column<bool>(type: "boolean", nullable: false),
                    IosAppStoreUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AndroidPlayStoreUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IosBundleId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AppleTeamId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AppleAssociatedDomainStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AppleUniversalLinksStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IosSigningProfileReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IosIapStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IosPushStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AndroidPackageName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AndroidSha256Fingerprints = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    AndroidSigningKeyReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AndroidAssetLinksStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AndroidIapStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AndroidPushStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DesktopMinSupportedVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DesktopLatestVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DesktopForceUpdate = table.Column<bool>(type: "boolean", nullable: false),
                    DesktopUpdateFeedUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DesktopUpdateChannel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    WindowsSigningStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MacSigningStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LinuxSigningStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeviceValidationEvidenceUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DeviceValidationNotes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RealtimeLegalApprovalStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RealtimePrivacyApprovalStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RealtimeProtectedSmokeStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RealtimeEvidenceUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RealtimeSpendCapApproved = table.Column<bool>(type: "boolean", nullable: false),
                    RealtimeTopologyApproved = table.Column<bool>(type: "boolean", nullable: false),
                    ReleaseOwnerApprovalStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LaunchNotes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LaunchReadinessSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LaunchReadinessSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttAllowedMimeTypesCsv",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttRealProviderProductionAuthorized",
                table: "ConversationSettings");
        }
    }
}
