using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Renames RuntimeSettings.FcmServerKeyEncrypted to FcmServiceAccountJsonEncrypted.
    /// Google retired FCM's legacy static server-key send API in 2024 — sending now
    /// requires an OAuth2 token minted from a Firebase service-account JSON key, so the
    /// stored secret is a service-account JSON blob, not a server key. No other row data
    /// is affected. Hand-written idempotent SQL (house style).
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260718090000_RenameFcmServerKeyToServiceAccountJson")]
    public partial class RenameFcmServerKeyToServiceAccountJson : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'RuntimeSettings' AND column_name = 'FcmServerKeyEncrypted'
    ) AND NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'RuntimeSettings' AND column_name = 'FcmServiceAccountJsonEncrypted'
    ) THEN
        ALTER TABLE ""RuntimeSettings"" RENAME COLUMN ""FcmServerKeyEncrypted"" TO ""FcmServiceAccountJsonEncrypted"";
    END IF;
END $$;

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""FcmServiceAccountJsonEncrypted"" text;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'RuntimeSettings' AND column_name = 'FcmServiceAccountJsonEncrypted'
    ) AND NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'RuntimeSettings' AND column_name = 'FcmServerKeyEncrypted'
    ) THEN
        ALTER TABLE ""RuntimeSettings"" RENAME COLUMN ""FcmServiceAccountJsonEncrypted"" TO ""FcmServerKeyEncrypted"";
    END IF;
END $$;
");
        }
    }
}
