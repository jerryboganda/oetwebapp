using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingTutorOverrideAndScoringGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------------------------------------------------------------------------------
            // Reading module: new columns on existing tables (brand-new this release, so plain
            // EF AddColumn operations are safe — nothing in any database has them yet).
            // ---------------------------------------------------------------------------------
            migrationBuilder.AddColumn<int>(
                name: "Difficulty",
                table: "ReadingQuestions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DistractorRationaleJson",
                table: "ReadingQuestions",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceSentence",
                table: "ReadingQuestions",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NormalizeHyphenSpacing",
                table: "ReadingPolicies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NormalizeSmartQuotes",
                table: "ReadingPolicies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NormalizeUnitSpacing",
                table: "ReadingPolicies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PartACaseInsensitive",
                table: "ReadingPolicies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OverriddenAt",
                table: "ReadingAttempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverriddenByUserId",
                table: "ReadingAttempts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScoreOverrideRaw",
                table: "ReadingAttempts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScoreOverrideReason",
                table: "ReadingAttempts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScoreOverrideScaled",
                table: "ReadingAttempts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FlaggedForReview",
                table: "ReadingAnswers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MissReason",
                table: "ReadingAnswers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // ---------------------------------------------------------------------------------
            // Reading module: new tables (brand-new this release).
            // ---------------------------------------------------------------------------------
            migrationBuilder.CreateTable(
                name: "ReadingAnswerRevisions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingQuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserAnswerJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingAnswerRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingAssignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssignedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssignedToUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ScopeJson = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingAttemptFeedbacks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AuthorUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FeedbackText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingAttemptFeedbacks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingAnswerRevisions_ReadingAttemptId_ReadingQuestionId",
                table: "ReadingAnswerRevisions",
                columns: new[] { "ReadingAttemptId", "ReadingQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingAssignments_AssignedByUserId",
                table: "ReadingAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingAssignments_AssignedToUserId",
                table: "ReadingAssignments",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingAttemptFeedbacks_ReadingAttemptId",
                table: "ReadingAttemptFeedbacks",
                column: "ReadingAttemptId");

            // ---------------------------------------------------------------------------------
            // Snapshot drift reconciliation (idempotent).
            //
            // The following objects have entities mapped in the EF model but were never captured
            // by a prior EF migration (they were materialised out-of-band, e.g. via the legacy
            // raw-SQL convention used by 20260615100000_AddNotificationRulesTable). The
            // regenerated model snapshot now includes them, so to keep the migration history
            // reproducible we (re)create them here. Every statement is guarded with IF NOT EXISTS
            // / ADD COLUMN IF NOT EXISTS, so it is safe whether or not the target database already
            // contains the object.
            //
            // NOTE: The Speaking RuntimeSettings columns and the NotificationRules table are NOT
            // recreated here — they are already owned by 20260528134500_AddSpeakingFullRuntimeSettings
            // and 20260615100000_AddNotificationRulesTable respectively, which always run before
            // this migration on every database path.
            // ---------------------------------------------------------------------------------
            migrationBuilder.Sql(@"
ALTER TABLE ""VocabularyTerms"" ADD COLUMN IF NOT EXISTS ""ExamFrequencyCount"" integer NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS ""NotificationCampaigns"" (
    ""Id"" uuid NOT NULL,
    ""Name"" character varying(256) NOT NULL,
    ""Subject"" character varying(256) NOT NULL,
    ""Body"" text NOT NULL,
    ""HtmlBody"" text,
    ""Channel"" integer NOT NULL,
    ""Status"" integer NOT NULL,
    ""SegmentJson"" text NOT NULL,
    ""RecipientCount"" integer,
    ""DeliveredCount"" integer NOT NULL,
    ""FailedCount"" integer NOT NULL,
    ""ScheduledAt"" timestamp with time zone,
    ""SentAt"" timestamp with time zone,
    ""VariantLabel"" character varying(32),
    ""ParentCampaignId"" uuid,
    ""CreatedByAdminId"" character varying(64) NOT NULL,
    ""ApprovedByAdminId"" character varying(64),
    ""ApprovedAt"" timestamp with time zone,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_NotificationCampaigns"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_NotificationCampaigns_Status"" ON ""NotificationCampaigns"" (""Status"");

CREATE TABLE IF NOT EXISTS ""NotificationCampaignRecipients"" (
    ""Id"" uuid NOT NULL,
    ""CampaignId"" uuid NOT NULL,
    ""RecipientUserId"" character varying(64) NOT NULL,
    ""RecipientEmail"" character varying(256) NOT NULL,
    ""DeliveryStatus"" integer NOT NULL,
    ""ErrorMessage"" character varying(512),
    ""DeliveredAt"" timestamp with time zone,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_NotificationCampaignRecipients"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_NotificationCampaignRecipients_CampaignId"" ON ""NotificationCampaignRecipients"" (""CampaignId"");
CREATE INDEX IF NOT EXISTS ""IX_NotificationCampaignRecipients_RecipientUserId"" ON ""NotificationCampaignRecipients"" (""RecipientUserId"");

CREATE TABLE IF NOT EXISTS ""SponsorSeatPacks"" (
    ""Id"" uuid NOT NULL,
    ""SponsorId"" character varying(64) NOT NULL,
    ""Name"" character varying(256) NOT NULL,
    ""TotalSeats"" integer NOT NULL,
    ""AssignedSeats"" integer NOT NULL,
    ""UnitPrice"" numeric NOT NULL,
    ""Currency"" character varying(3) NOT NULL,
    ""StripePaymentId"" character varying(256),
    ""Status"" character varying(16) NOT NULL,
    ""PurchasedAt"" timestamp with time zone NOT NULL,
    ""ExpiresAt"" timestamp with time zone,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_SponsorSeatPacks"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_SponsorSeatPacks_SponsorId"" ON ""SponsorSeatPacks"" (""SponsorId"");

CREATE TABLE IF NOT EXISTS ""SponsorSeatAssignments"" (
    ""Id"" uuid NOT NULL,
    ""SeatPackId"" uuid NOT NULL,
    ""LearnerId"" character varying(64) NOT NULL,
    ""LearnerEmail"" character varying(256) NOT NULL,
    ""Status"" character varying(16) NOT NULL,
    ""AssignedAt"" timestamp with time zone NOT NULL,
    ""RevokedAt"" timestamp with time zone,
    CONSTRAINT ""PK_SponsorSeatAssignments"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_SponsorSeatAssignments_LearnerId"" ON ""SponsorSeatAssignments"" (""LearnerId"");
CREATE INDEX IF NOT EXISTS ""IX_SponsorSeatAssignments_SeatPackId"" ON ""SponsorSeatAssignments"" (""SeatPackId"");

CREATE TABLE IF NOT EXISTS ""SponsorBillingEvents"" (
    ""Id"" uuid NOT NULL,
    ""SponsorId"" character varying(64) NOT NULL,
    ""SeatPackId"" uuid,
    ""EventType"" character varying(32) NOT NULL,
    ""Amount"" numeric,
    ""Currency"" character varying(3),
    ""SeatsDelta"" integer,
    ""Description"" character varying(512),
    ""ActorUserId"" character varying(64),
    ""CreatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_SponsorBillingEvents"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_SponsorBillingEvents_SponsorId"" ON ""SponsorBillingEvents"" (""SponsorId"");
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadingAnswerRevisions");

            migrationBuilder.DropTable(
                name: "ReadingAssignments");

            migrationBuilder.DropTable(
                name: "ReadingAttemptFeedbacks");

            migrationBuilder.DropColumn(
                name: "Difficulty",
                table: "ReadingQuestions");

            migrationBuilder.DropColumn(
                name: "DistractorRationaleJson",
                table: "ReadingQuestions");

            migrationBuilder.DropColumn(
                name: "EvidenceSentence",
                table: "ReadingQuestions");

            migrationBuilder.DropColumn(
                name: "NormalizeHyphenSpacing",
                table: "ReadingPolicies");

            migrationBuilder.DropColumn(
                name: "NormalizeSmartQuotes",
                table: "ReadingPolicies");

            migrationBuilder.DropColumn(
                name: "NormalizeUnitSpacing",
                table: "ReadingPolicies");

            migrationBuilder.DropColumn(
                name: "PartACaseInsensitive",
                table: "ReadingPolicies");

            migrationBuilder.DropColumn(
                name: "OverriddenAt",
                table: "ReadingAttempts");

            migrationBuilder.DropColumn(
                name: "OverriddenByUserId",
                table: "ReadingAttempts");

            migrationBuilder.DropColumn(
                name: "ScoreOverrideRaw",
                table: "ReadingAttempts");

            migrationBuilder.DropColumn(
                name: "ScoreOverrideReason",
                table: "ReadingAttempts");

            migrationBuilder.DropColumn(
                name: "ScoreOverrideScaled",
                table: "ReadingAttempts");

            migrationBuilder.DropColumn(
                name: "FlaggedForReview",
                table: "ReadingAnswers");

            migrationBuilder.DropColumn(
                name: "MissReason",
                table: "ReadingAnswers");

            // NOTE: The Up() drift-reconciliation block (SponsorSeatPacks, SponsorSeatAssignments,
            // SponsorBillingEvents, NotificationCampaigns, NotificationCampaignRecipients, and
            // VocabularyTerms.ExamFrequencyCount) is intentionally NOT reversed here. Those objects
            // are not owned by this migration (they were materialised out-of-band and only created
            // with IF NOT EXISTS guards to repair snapshot drift), and they may already hold live
            // sponsor-billing / campaign data. Dropping them on rollback would cause permanent data
            // loss while Up() would only recreate empty tables on re-apply. This mirrors the same
            // ownership asymmetry already applied to the Speaking / NotificationRules objects.
        }
    }
}
