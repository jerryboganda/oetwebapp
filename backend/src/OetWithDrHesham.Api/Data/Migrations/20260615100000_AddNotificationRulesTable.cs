using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260615100000_AddNotificationRulesTable")]
    public partial class AddNotificationRulesTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""NotificationRules"" (
    ""Id"" uuid NOT NULL,
    ""EventKey"" character varying(128) NOT NULL,
    ""AudienceRole"" character varying(32),
    ""Channels"" character varying(256) NOT NULL DEFAULT 'InApp,Email,Push',
    ""Priority"" integer NOT NULL DEFAULT 5,
    ""DelaySeconds"" integer,
    ""ExpiryMinutes"" integer,
    ""FallbackChannels"" character varying(256),
    ""RequiredConsentCategory"" character varying(64),
    ""BypassQuietHours"" boolean NOT NULL DEFAULT false,
    ""IsActive"" boolean NOT NULL DEFAULT true,
    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
    ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT ""PK_NotificationRules"" PRIMARY KEY (""Id"")
);

CREATE INDEX IF NOT EXISTS ""IX_NotificationRules_EventKey_IsActive""
    ON ""NotificationRules"" (""EventKey"", ""IsActive"");

CREATE INDEX IF NOT EXISTS ""IX_NotificationRules_AudienceRole_IsActive""
    ON ""NotificationRules"" (""AudienceRole"", ""IsActive"");
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS ""NotificationRules"";
");
        }
    }
}
