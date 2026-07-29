using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Activates the mandatory controls from the 25 Jul 2026 Course Platform
    /// Security Requirements after their production observation period.
    /// Device-verification OTP has completed successfully in production, so the
    /// previously dark trusted-device and risk enforcement paths are now safe
    /// to make authoritative.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260825090000_EnforceCoursePlatformSecurityProfile")]
    public partial class EnforceCoursePlatformSecurityProfile : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)) return;

            migrationBuilder.Sql(@"
ALTER TABLE ""VideoPlaybackSessions""
ADD COLUMN IF NOT EXISTS ""DeviceId"" character varying(128);

UPDATE ""RuntimeSettings""
SET
    ""SecuritySingleActiveSessionEnabled"" = TRUE,
    ""SecurityRiskMode"" = 'enforce',
    ""SecurityTrustedDeviceRequired"" = TRUE,
    ""SecurityRequireVerifiedEmailForLearners"" = TRUE,
    ""VideoProtectionRevokeOnCaptureDetected"" = TRUE,
    ""VideoProtectionBlockRootedDevices"" = TRUE,
    ""VideoProtectionBlockEmulators"" = TRUE,
    ""BunnyStreamPlaybackTokenTtlSeconds"" = 300,
    ""UpdatedAt"" = NOW();

INSERT INTO ""SecurityEvents"" (
    ""Id"", ""OccurredAt"", ""AuthAccountId"", ""Kind"", ""Severity"", ""DetailsJson"")
SELECT
    gen_random_uuid(),
    NOW(),
    r.""ApplicationUserAccountId"",
    'session.revoked_all',
    'info',
    '{""reason"":""security_profile_activation"",""scope"":""legacy_device_unbound""}'
FROM ""RefreshTokenRecords"" r
WHERE r.""RevokedAt"" IS NULL
  AND r.""ExpiresAt"" > NOW()
  AND r.""DeviceId"" IS NULL
GROUP BY r.""ApplicationUserAccountId"";

UPDATE ""RefreshTokenRecords""
SET ""RevokedAt"" = NOW()
WHERE ""RevokedAt"" IS NULL
  AND ""ExpiresAt"" > NOW()
  AND ""DeviceId"" IS NULL;

UPDATE ""VideoPlaybackSessions""
SET ""RevokedAt"" = NOW()
WHERE ""RevokedAt"" IS NULL;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally irreversible: a rollback must never resurrect
            // revoked bearer sessions or silently weaken mandatory controls.
        }
    }
}
