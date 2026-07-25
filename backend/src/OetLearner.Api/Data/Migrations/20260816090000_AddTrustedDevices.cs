using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Creates <c>TrustedDevices</c> (Course Platform Security Requirements
    /// §3.2), adds <c>RefreshTokenRecords.DeviceId</c>, and the
    /// <c>SecurityTrustedDeviceRequired</c> /
    /// <c>SecurityDeviceChangeWindowDays</c> / <c>SecurityDeviceChangeMaxPerWindow</c>
    /// RuntimeSettings toggles. <c>SecurityTrustedDeviceRequired</c> defaults
    /// FALSE at the database level (see RuntimeSettingsProvider resolver) —
    /// deliberately NOT enforced yet because the frontend device-verification
    /// challenge UI has not shipped; flipping this on before that UI exists
    /// would strand any learner signing in from a new device with no way to
    /// complete the email-OTP challenge the backend would issue.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260816090000_AddTrustedDevices")]
    public partial class AddTrustedDevices : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""TrustedDevices"" (
    ""Id"" uuid NOT NULL,
    ""ApplicationUserAccountId"" character varying(64) NOT NULL,
    ""DeviceId"" character varying(128) NOT NULL,
    ""DeviceName"" character varying(256),
    ""Platform"" character varying(32),
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""TrustedAt"" timestamp with time zone NOT NULL,
    ""LastSeenAt"" timestamp with time zone,
    ""RevokedAt"" timestamp with time zone,
    ""TrustGrantedVia"" character varying(32) NOT NULL DEFAULT 'bootstrap',
    CONSTRAINT ""PK_TrustedDevices"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_TrustedDevices_ApplicationUserAccountId"" ON ""TrustedDevices"" (""ApplicationUserAccountId"");
");

            migrationBuilder.AddColumn<string>(
                name: "DeviceId",
                table: "RefreshTokenRecords",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SecurityTrustedDeviceRequired",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecurityDeviceChangeWindowDays",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecurityDeviceChangeMaxPerWindow",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SecurityDeviceChangeMaxPerWindow", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SecurityDeviceChangeWindowDays", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SecurityTrustedDeviceRequired", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "DeviceId", table: "RefreshTokenRecords");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""TrustedDevices"";");
        }
    }
}
