using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Recovers model elements that were present in the EF snapshot but not
    /// represented in executable migration <c>Up()</c> operations. The SQL is
    /// idempotent so it can run safely against partially recovered databases.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260505231500_RecoverRecentSnapshotDrift")]
    public partial class RecoverRecentSnapshotDrift : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""AIConfigVersions""
    ADD COLUMN IF NOT EXISTS ""ConfidencePolicyJson"" character varying(2048) NOT NULL DEFAULT '{}';

CREATE TABLE IF NOT EXISTS ""ExpertReviewerPayouts"" (
    ""Id""                    character varying(64)  NOT NULL,
    ""AdminNote""             character varying(512) NULL,
    ""ApprovedAt""            timestamp with time zone NULL,
    ""ApprovedByAdminId""     character varying(64)  NULL,
    ""ApprovedByAdminName""   character varying(128) NULL,
    ""CreatedAt""             timestamp with time zone NOT NULL,
    ""PaidAt""                timestamp with time zone NULL,
    ""PayPeriodEnd""          timestamp with time zone NOT NULL,
    ""PayPeriodStart""        timestamp with time zone NOT NULL,
    ""ReviewCount""           integer NOT NULL,
    ""ReviewRequestIdsJson""  text NOT NULL,
    ""ReviewerId""            character varying(64) NOT NULL,
    ""Status""                character varying(32) NOT NULL,
    ""TotalCompensation""     numeric NOT NULL,
    ""TotalLearnerPrice""     numeric NOT NULL,
    CONSTRAINT ""PK_ExpertReviewerPayouts"" PRIMARY KEY (""Id"")
);

ALTER TABLE ""ExpertReviewerPayouts""
    ADD COLUMN IF NOT EXISTS ""AdminNote"" character varying(512) NULL,
    ADD COLUMN IF NOT EXISTS ""ApprovedAt"" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS ""ApprovedByAdminId"" character varying(64) NULL,
    ADD COLUMN IF NOT EXISTS ""ApprovedByAdminName"" character varying(128) NULL,
    ADD COLUMN IF NOT EXISTS ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
    ADD COLUMN IF NOT EXISTS ""PaidAt"" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS ""PayPeriodEnd"" timestamp with time zone NOT NULL DEFAULT now(),
    ADD COLUMN IF NOT EXISTS ""PayPeriodStart"" timestamp with time zone NOT NULL DEFAULT now(),
    ADD COLUMN IF NOT EXISTS ""ReviewCount"" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS ""ReviewRequestIdsJson"" text NOT NULL DEFAULT '[]',
    ADD COLUMN IF NOT EXISTS ""ReviewerId"" character varying(64) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS ""Status"" character varying(32) NOT NULL DEFAULT 'Pending',
    ADD COLUMN IF NOT EXISTS ""TotalCompensation"" numeric NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS ""TotalLearnerPrice"" numeric NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS ""IX_ExpertReviewerPayouts_ReviewerId""
    ON ""ExpertReviewerPayouts"" (""ReviewerId"");
CREATE INDEX IF NOT EXISTS ""IX_ExpertReviewerPayouts_Status""
    ON ""ExpertReviewerPayouts"" (""Status"");
CREATE INDEX IF NOT EXISTS ""IX_ExpertReviewerPayouts_PayPeriodStart_PayPeriodEnd""
    ON ""ExpertReviewerPayouts"" (""PayPeriodStart"", ""PayPeriodEnd"");

ALTER TABLE ""LearnerRegistrationProfiles""
    ADD COLUMN IF NOT EXISTS ""LandingPath"" character varying(512) NULL,
    ADD COLUMN IF NOT EXISTS ""ReferrerUrl"" character varying(512) NULL,
    ADD COLUMN IF NOT EXISTS ""UtmCampaign"" character varying(256) NULL,
    ADD COLUMN IF NOT EXISTS ""UtmContent"" character varying(128) NULL,
    ADD COLUMN IF NOT EXISTS ""UtmMedium"" character varying(128) NULL,
    ADD COLUMN IF NOT EXISTS ""UtmSource"" character varying(128) NULL,
    ADD COLUMN IF NOT EXISTS ""UtmTerm"" character varying(128) NULL;

ALTER TABLE ""ReviewRequests""
    ADD COLUMN IF NOT EXISTS ""CompensationPaid"" boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS ""ReviewerCompensation"" numeric NOT NULL DEFAULT 0;

ALTER TABLE ""ReviewEscalations""
    ADD COLUMN IF NOT EXISTS ""AttemptId"" character varying(64) NULL,
    ADD COLUMN IF NOT EXISTS ""ConfigId"" character varying(64) NULL;

ALTER TABLE ""Attempts""
    ADD COLUMN IF NOT EXISTS ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
    ADD COLUMN IF NOT EXISTS ""ModelVersionId"" character varying(64) NULL;

ALTER TABLE ""Evaluations""
    ADD COLUMN IF NOT EXISTS ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
    ADD COLUMN IF NOT EXISTS ""ModelVersionId"" character varying(64) NULL;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recovery migration; intentionally non-destructive on rollback.
        }
    }
}