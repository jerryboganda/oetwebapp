using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Zoom-backed live classes: catalog, sessions, enrollment ledger, attendance,
    /// recordings, waitlist, and webhook inbox.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260608003000_AddLiveClassTables")]
    public partial class AddLiveClassTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""LiveClasses"" (
                    ""Id"" varchar(64) NOT NULL PRIMARY KEY,
                    ""Slug"" varchar(160) NOT NULL,
                    ""Title"" varchar(180) NOT NULL,
                    ""TitleAr"" varchar(180) NULL,
                    ""Description"" varchar(4096) NOT NULL DEFAULT '',
                    ""DescriptionAr"" varchar(4096) NULL,
                    ""Type"" integer NOT NULL DEFAULT 1,
                    ""ProfessionTrack"" varchar(64) NOT NULL DEFAULT 'All',
                    ""Level"" varchar(32) NOT NULL DEFAULT 'All',
                    ""TutorProfileId"" varchar(64) NULL,
                    ""TutorDisplayName"" varchar(128) NULL,
                    ""DefaultDurationMinutes"" integer NOT NULL DEFAULT 60,
                    ""DefaultCapacity"" integer NOT NULL DEFAULT 100,
                    ""CreditCost"" integer NOT NULL DEFAULT 5,
                    ""PriceUsd"" numeric(10,2) NULL,
                    ""IsRecurring"" boolean NOT NULL DEFAULT false,
                    ""RecurrenceJson"" varchar(2048) NOT NULL DEFAULT '{}',
                    ""Status"" integer NOT NULL DEFAULT 0,
                    ""CoverImageUrl"" varchar(512) NULL,
                    ""TagsJson"" varchar(2048) NOT NULL DEFAULT '[]',
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""FK_LiveClasses_PrivateSpeakingTutorProfiles_TutorProfileId""
                        FOREIGN KEY (""TutorProfileId"") REFERENCES ""PrivateSpeakingTutorProfiles"" (""Id"") ON DELETE SET NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LiveClasses_Slug"" ON ""LiveClasses"" (""Slug"");
                CREATE INDEX IF NOT EXISTS ""IX_LiveClasses_Status_ProfessionTrack_Level"" ON ""LiveClasses"" (""Status"", ""ProfessionTrack"", ""Level"");
                CREATE INDEX IF NOT EXISTS ""IX_LiveClasses_TutorProfileId"" ON ""LiveClasses"" (""TutorProfileId"");

                CREATE TABLE IF NOT EXISTS ""LiveClassSessions"" (
                    ""Id"" varchar(64) NOT NULL PRIMARY KEY,
                    ""LiveClassId"" varchar(64) NOT NULL,
                    ""ScheduledStartAt"" timestamp with time zone NOT NULL,
                    ""ScheduledEndAt"" timestamp with time zone NOT NULL,
                    ""Capacity"" integer NOT NULL,
                    ""EnrolledCount"" integer NOT NULL DEFAULT 0,
                    ""Status"" integer NOT NULL DEFAULT 0,
                    ""ZoomMeetingId"" bigint NULL,
                    ""ZoomMeetingNumber"" varchar(64) NULL,
                    ""ZoomJoinUrl"" varchar(512) NULL,
                    ""ZoomStartUrl"" varchar(512) NULL,
                    ""ZoomPasscode"" varchar(64) NULL,
                    ""ZoomError"" varchar(512) NULL,
                    ""ZoomRetryCount"" integer NOT NULL DEFAULT 0,
                    ""ActualStartAt"" timestamp with time zone NULL,
                    ""ActualEndAt"" timestamp with time zone NULL,
                    ""DurationMinutes"" integer NULL,
                    ""RecordingId"" varchar(64) NULL,
                    ""CancellationReason"" varchar(512) NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""FK_LiveClassSessions_LiveClasses_LiveClassId""
                        FOREIGN KEY (""LiveClassId"") REFERENCES ""LiveClasses"" (""Id"") ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS ""IX_LiveClassSessions_LiveClassId_ScheduledStartAt"" ON ""LiveClassSessions"" (""LiveClassId"", ""ScheduledStartAt"");
                CREATE INDEX IF NOT EXISTS ""IX_LiveClassSessions_ScheduledStartAt"" ON ""LiveClassSessions"" (""ScheduledStartAt"");
                CREATE INDEX IF NOT EXISTS ""IX_LiveClassSessions_Status_ScheduledStartAt"" ON ""LiveClassSessions"" (""Status"", ""ScheduledStartAt"");
                CREATE INDEX IF NOT EXISTS ""IX_LiveClassSessions_ZoomMeetingId"" ON ""LiveClassSessions"" (""ZoomMeetingId"");

                CREATE TABLE IF NOT EXISTS ""LiveClassEnrollments"" (
                    ""Id"" varchar(64) NOT NULL PRIMARY KEY,
                    ""ClassSessionId"" varchar(64) NOT NULL,
                    ""UserId"" varchar(64) NOT NULL,
                    ""EnrolledAt"" timestamp with time zone NOT NULL,
                    ""CreditsCharged"" integer NOT NULL DEFAULT 0,
                    ""WalletTransactionId"" uuid NULL,
                    ""RefundWalletTransactionId"" uuid NULL,
                    ""Status"" integer NOT NULL DEFAULT 0,
                    ""CancelledAt"" timestamp with time zone NULL,
                    ""CancellationReason"" varchar(512) NULL,
                    ""IdempotencyKey"" varchar(128) NOT NULL,
                    CONSTRAINT ""FK_LiveClassEnrollments_LiveClassSessions_ClassSessionId""
                        FOREIGN KEY (""ClassSessionId"") REFERENCES ""LiveClassSessions"" (""Id"") ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS ""IX_LiveClassEnrollments_UserId_Status"" ON ""LiveClassEnrollments"" (""UserId"", ""Status"");
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LiveClassEnrollments_ClassSessionId_UserId"" ON ""LiveClassEnrollments"" (""ClassSessionId"", ""UserId"");
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LiveClassEnrollments_IdempotencyKey"" ON ""LiveClassEnrollments"" (""IdempotencyKey"");

                CREATE TABLE IF NOT EXISTS ""LiveClassAttendances"" (
                    ""Id"" varchar(64) NOT NULL PRIMARY KEY,
                    ""ClassSessionId"" varchar(64) NOT NULL,
                    ""UserId"" varchar(64) NOT NULL,
                    ""EnrollmentId"" varchar(64) NULL,
                    ""JoinedAt"" timestamp with time zone NOT NULL,
                    ""LeftAt"" timestamp with time zone NULL,
                    ""DurationSeconds"" integer NOT NULL DEFAULT 0,
                    ""ZoomParticipantUuid"" varchar(128) NULL,
                    ""ReceivedRecordingAccess"" boolean NOT NULL DEFAULT false,
                    CONSTRAINT ""FK_LiveClassAttendances_LiveClassSessions_ClassSessionId""
                        FOREIGN KEY (""ClassSessionId"") REFERENCES ""LiveClassSessions"" (""Id"") ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS ""IX_LiveClassAttendances_ClassSessionId_UserId"" ON ""LiveClassAttendances"" (""ClassSessionId"", ""UserId"");
                CREATE INDEX IF NOT EXISTS ""IX_LiveClassAttendances_ZoomParticipantUuid"" ON ""LiveClassAttendances"" (""ZoomParticipantUuid"");

                CREATE TABLE IF NOT EXISTS ""LiveClassRecordings"" (
                    ""Id"" varchar(64) NOT NULL PRIMARY KEY,
                    ""ClassSessionId"" varchar(64) NOT NULL,
                    ""ZoomRecordingId"" varchar(128) NULL,
                    ""Status"" integer NOT NULL DEFAULT 0,
                    ""S3VideoKey"" varchar(512) NULL,
                    ""S3AudioKey"" varchar(512) NULL,
                    ""S3TranscriptKey"" varchar(512) NULL,
                    ""TranscriptText"" text NULL,
                    ""AiSummary"" text NULL,
                    ""AiSummaryAr"" text NULL,
                    ""ChaptersJson"" text NOT NULL DEFAULT '[]',
                    ""ActionItemsJson"" text NOT NULL DEFAULT '[]',
                    ""DurationSeconds"" integer NOT NULL DEFAULT 0,
                    ""FileSizeBytes"" bigint NOT NULL DEFAULT 0,
                    ""RecordedAt"" timestamp with time zone NOT NULL,
                    ""ProcessedAt"" timestamp with time zone NULL,
                    ""ExpiresAt"" timestamp with time zone NULL,
                    ""FailureReason"" varchar(512) NULL,
                    CONSTRAINT ""FK_LiveClassRecordings_LiveClassSessions_ClassSessionId""
                        FOREIGN KEY (""ClassSessionId"") REFERENCES ""LiveClassSessions"" (""Id"") ON DELETE CASCADE
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LiveClassRecordings_ClassSessionId"" ON ""LiveClassRecordings"" (""ClassSessionId"");
                CREATE INDEX IF NOT EXISTS ""IX_LiveClassRecordings_ZoomRecordingId"" ON ""LiveClassRecordings"" (""ZoomRecordingId"");

                CREATE TABLE IF NOT EXISTS ""LiveClassWaitlistEntries"" (
                    ""Id"" varchar(64) NOT NULL PRIMARY KEY,
                    ""ClassSessionId"" varchar(64) NOT NULL,
                    ""UserId"" varchar(64) NOT NULL,
                    ""Position"" integer NOT NULL,
                    ""JoinedWaitlistAt"" timestamp with time zone NOT NULL,
                    ""NotifiedOfOpening"" boolean NOT NULL DEFAULT false
                );

                CREATE INDEX IF NOT EXISTS ""IX_LiveClassWaitlistEntries_ClassSessionId_Position"" ON ""LiveClassWaitlistEntries"" (""ClassSessionId"", ""Position"");
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LiveClassWaitlistEntries_ClassSessionId_UserId"" ON ""LiveClassWaitlistEntries"" (""ClassSessionId"", ""UserId"");

                CREATE TABLE IF NOT EXISTS ""LiveClassWebhookEvents"" (
                    ""Id"" varchar(64) NOT NULL PRIMARY KEY,
                    ""EventType"" varchar(96) NOT NULL,
                    ""PayloadHash"" varchar(128) NOT NULL,
                    ""RawPayload"" text NOT NULL,
                    ""Status"" integer NOT NULL DEFAULT 0,
                    ""ErrorMessage"" varchar(1024) NULL,
                    ""ReceivedAt"" timestamp with time zone NOT NULL,
                    ""ProcessedAt"" timestamp with time zone NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LiveClassWebhookEvents_PayloadHash"" ON ""LiveClassWebhookEvents"" (""PayloadHash"");
                CREATE INDEX IF NOT EXISTS ""IX_LiveClassWebhookEvents_EventType_ReceivedAt"" ON ""LiveClassWebhookEvents"" (""EventType"", ""ReceivedAt"");

                INSERT INTO ""WalletTopUpTierConfigs""
                    (""Id"", ""Slug"", ""Amount"", ""Credits"", ""Bonus"", ""Label"", ""IsPopular"", ""DisplayOrder"", ""IsActive"", ""Currency"", ""CreatedAt"", ""UpdatedAt"", ""CreatedBy"", ""UpdatedBy"")
                VALUES
                    ('11111111-1111-4111-8111-111111111111', 'live-class-starter-pack', 29, 5, 1, 'Live class starter pack', false, 410, true, 'AUD', NOW(), NOW(), 'system:live-class-migration', 'system:live-class-migration'),
                    ('22222222-2222-4222-8222-222222222222', 'live-class-focused-pack', 69, 15, 1, 'Live class focused pack', true, 420, true, 'AUD', NOW(), NOW(), 'system:live-class-migration', 'system:live-class-migration'),
                    ('33333333-3333-4333-8333-333333333333', 'live-class-intensive-pack', 99, 24, 1, 'Live class intensive pack', false, 430, true, 'AUD', NOW(), NOW(), 'system:live-class-migration', 'system:live-class-migration')
                ON CONFLICT DO NOTHING;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS ""LiveClassWebhookEvents"";
                DROP TABLE IF EXISTS ""LiveClassWaitlistEntries"";
                DROP TABLE IF EXISTS ""LiveClassRecordings"";
                DROP TABLE IF EXISTS ""LiveClassAttendances"";
                DROP TABLE IF EXISTS ""LiveClassEnrollments"";
                DROP TABLE IF EXISTS ""LiveClassSessions"";
                DROP TABLE IF EXISTS ""LiveClasses"";
                DELETE FROM ""WalletTopUpTierConfigs""
                WHERE ""Slug"" IN ('live-class-starter-pack', 'live-class-focused-pack', 'live-class-intensive-pack');
            ");
        }
    }
}