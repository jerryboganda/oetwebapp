using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Recovers 16 tables present in <c>LearnerDbContextModelSnapshot</c> but
    /// missing from any prior migration. Without this, EF Core thinks the
    /// schema is "up to date" yet runtime queries (e.g. <c>/v1/mocks</c>,
    /// <c>/v1/listening/home</c>) fail with <c>42P01: relation X does not exist</c>.
    ///
    /// Uses idempotent <c>CREATE TABLE IF NOT EXISTS</c> + <c>CREATE INDEX
    /// IF NOT EXISTS</c> so it is safe on environments that may have one or
    /// more of these tables already created out-of-band.
    /// </summary>
    public partial class RecoverMissingTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""AdminUsers"" (
    ""Id""           character varying(64)  NOT NULL,
    ""CreatedAt""    timestamp with time zone NOT NULL,
    ""DisplayName""  character varying(256) NOT NULL,
    ""Email""        character varying(256) NOT NULL,
    ""IsActive""     boolean                NOT NULL,
    ""Role""         character varying(64)  NOT NULL,
    CONSTRAINT ""PK_AdminUsers"" PRIMARY KEY (""Id"")
);

CREATE TABLE IF NOT EXISTS ""AdminPermissionGrants"" (
    ""Id""          character varying(64)  NOT NULL,
    ""AdminUserId"" character varying(64)  NOT NULL,
    ""GrantedAt""   timestamp with time zone NOT NULL,
    ""GrantedBy""   character varying(128) NOT NULL,
    ""Permission""  character varying(64)  NOT NULL,
    CONSTRAINT ""PK_AdminPermissionGrants"" PRIMARY KEY (""Id"")
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AdminPermissionGrants_AdminUserId_Permission""
    ON ""AdminPermissionGrants"" (""AdminUserId"", ""Permission"");

CREATE TABLE IF NOT EXISTS ""ContentPublishRequests"" (
    ""Id""                       character varying(64)  NOT NULL,
    ""ContentItemId""             character varying(64)  NOT NULL,
    ""EditorNotes""               character varying(512) NULL,
    ""EditorReviewedAt""          timestamp with time zone NULL,
    ""EditorReviewedBy""          character varying(64)  NULL,
    ""EditorReviewedByName""      character varying(128) NULL,
    ""PublisherApprovedAt""       timestamp with time zone NULL,
    ""PublisherApprovedBy""       character varying(64)  NULL,
    ""PublisherApprovedByName""   character varying(128) NULL,
    ""PublisherNotes""            character varying(512) NULL,
    ""RejectedAt""                timestamp with time zone NULL,
    ""RejectedBy""                character varying(64)  NULL,
    ""RejectedByName""            character varying(128) NULL,
    ""RejectionReason""           character varying(512) NULL,
    ""RejectionStage""            character varying(32)  NULL,
    ""RequestNote""               character varying(512) NULL,
    ""RequestedAt""               timestamp with time zone NOT NULL,
    ""RequestedBy""               character varying(64)  NOT NULL,
    ""RequestedByName""           character varying(128) NOT NULL,
    ""ReviewNote""                character varying(512) NULL,
    ""ReviewedAt""                timestamp with time zone NULL,
    ""ReviewedBy""                character varying(64)  NULL,
    ""ReviewedByName""            character varying(128) NULL,
    ""Stage""                     character varying(32)  NOT NULL,
    ""Status""                    character varying(32)  NOT NULL,
    CONSTRAINT ""PK_ContentPublishRequests"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_ContentPublishRequests_RequestedBy"" ON ""ContentPublishRequests"" (""RequestedBy"");
CREATE INDEX IF NOT EXISTS ""IX_ContentPublishRequests_Stage""        ON ""ContentPublishRequests"" (""Stage"");
CREATE INDEX IF NOT EXISTS ""IX_ContentPublishRequests_ContentItemId_Status"" ON ""ContentPublishRequests"" (""ContentItemId"", ""Status"");

CREATE TABLE IF NOT EXISTS ""ExpertAnnotationTemplates"" (
    ""Id""                 character varying(64)   NOT NULL,
    ""CreatedAt""          timestamp with time zone NOT NULL,
    ""CreatedByExpertId""  character varying(64)   NOT NULL,
    ""CriterionCode""      character varying(64)   NOT NULL,
    ""IsShared""           boolean                 NOT NULL,
    ""Label""              character varying(128)  NOT NULL,
    ""SubtestCode""        character varying(32)   NOT NULL,
    ""TemplateText""       character varying(1500) NOT NULL,
    ""UpdatedAt""          timestamp with time zone NOT NULL,
    ""UsageCount""         integer                 NOT NULL,
    CONSTRAINT ""PK_ExpertAnnotationTemplates"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_ExpertAnnotationTemplates_CreatedByExpertId"" ON ""ExpertAnnotationTemplates"" (""CreatedByExpertId"");

CREATE TABLE IF NOT EXISTS ""FreeTierConfigs"" (
    ""Id""                    character varying(64) NOT NULL,
    ""Enabled""               boolean               NOT NULL,
    ""MaxListeningAttempts""  integer               NOT NULL,
    ""MaxReadingAttempts""    integer               NOT NULL,
    ""MaxSpeakingAttempts""   integer               NOT NULL,
    ""MaxWritingAttempts""    integer               NOT NULL,
    ""ShowUpgradePrompts""    boolean               NOT NULL,
    ""TrialDurationDays""     integer               NOT NULL,
    ""UpdatedAt""             timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_FreeTierConfigs"" PRIMARY KEY (""Id"")
);

CREATE TABLE IF NOT EXISTS ""LearnerCertificates"" (
    ""Id""              character varying(64)   NOT NULL,
    ""CertificateType"" character varying(64)   NOT NULL,
    ""Description""     character varying(512)  NOT NULL,
    ""DownloadUrl""     character varying(512)  NULL,
    ""IssuedAt""        timestamp with time zone NOT NULL,
    ""MetadataJson""    character varying(2048) NOT NULL,
    ""Title""           character varying(256)  NOT NULL,
    ""UserId""          character varying(64)   NOT NULL,
    CONSTRAINT ""PK_LearnerCertificates"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_LearnerCertificates_UserId"" ON ""LearnerCertificates"" (""UserId"");

CREATE TABLE IF NOT EXISTS ""MobilePushTokens"" (
    ""Id""             uuid                    NOT NULL,
    ""AuthAccountId""  character varying(64)   NOT NULL,
    ""CreatedAt""      timestamp with time zone NOT NULL,
    ""IsActive""       boolean                 NOT NULL,
    ""Platform""       character varying(16)   NOT NULL,
    ""Token""          character varying(512)  NOT NULL,
    ""UpdatedAt""      timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_MobilePushTokens"" PRIMARY KEY (""Id"")
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_MobilePushTokens_Token"" ON ""MobilePushTokens"" (""Token"");
CREATE INDEX IF NOT EXISTS ""IX_MobilePushTokens_AuthAccountId_Platform"" ON ""MobilePushTokens"" (""AuthAccountId"", ""Platform"");

CREATE TABLE IF NOT EXISTS ""NotificationTemplates"" (
    ""Id""              character varying(64)   NOT NULL,
    ""BodyTemplate""    text                    NOT NULL,
    ""Category""        character varying(64)   NULL,
    ""Channel""         character varying(32)   NOT NULL,
    ""CreatedAt""       timestamp with time zone NOT NULL,
    ""EventKey""        character varying(128)  NOT NULL,
    ""IsActive""        boolean                 NOT NULL,
    ""SubjectTemplate"" character varying(256)  NOT NULL,
    ""UpdatedAt""       timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_NotificationTemplates"" PRIMARY KEY (""Id"")
);

CREATE TABLE IF NOT EXISTS ""PeerReviewRequests"" (
    ""Id""               character varying(64)  NOT NULL,
    ""AttemptId""        character varying(64)  NOT NULL,
    ""ClaimedAt""        timestamp with time zone NULL,
    ""CompletedAt""      timestamp with time zone NULL,
    ""CreatedAt""        timestamp with time zone NOT NULL,
    ""ReviewerUserId""   character varying(64)  NULL,
    ""Status""           character varying(32)  NOT NULL,
    ""SubmitterUserId""  character varying(64)  NOT NULL,
    ""SubtestCode""      character varying(32)  NOT NULL,
    CONSTRAINT ""PK_PeerReviewRequests"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_PeerReviewRequests_SubmitterUserId"" ON ""PeerReviewRequests"" (""SubmitterUserId"");
CREATE INDEX IF NOT EXISTS ""IX_PeerReviewRequests_Status_CreatedAt""  ON ""PeerReviewRequests"" (""Status"", ""CreatedAt"");

CREATE TABLE IF NOT EXISTS ""PeerReviewFeedbacks"" (
    ""Id""                  character varying(64)   NOT NULL,
    ""Comments""            character varying(2000) NOT NULL,
    ""CreatedAt""           timestamp with time zone NOT NULL,
    ""HelpfulnessRating""   integer                 NOT NULL,
    ""ImprovementNotes""    character varying(1000) NULL,
    ""OverallRating""       integer                 NOT NULL,
    ""PeerReviewRequestId"" character varying(64)   NOT NULL,
    ""ReviewerUserId""      character varying(64)   NOT NULL,
    ""StrengthNotes""       character varying(1000) NULL,
    CONSTRAINT ""PK_PeerReviewFeedbacks"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_PeerReviewFeedbacks_PeerReviewRequestId"" ON ""PeerReviewFeedbacks"" (""PeerReviewRequestId"");

CREATE TABLE IF NOT EXISTS ""PermissionTemplates"" (
    ""Id""           character varying(64)  NOT NULL,
    ""CreatedAt""    timestamp with time zone NOT NULL,
    ""CreatedBy""    character varying(128) NOT NULL,
    ""Description""  character varying(512) NULL,
    ""Name""         character varying(128) NOT NULL,
    ""Permissions""  text                   NOT NULL,
    CONSTRAINT ""PK_PermissionTemplates"" PRIMARY KEY (""Id"")
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PermissionTemplates_Name"" ON ""PermissionTemplates"" (""Name"");

CREATE TABLE IF NOT EXISTS ""ReferralRecords"" (
    ""Id""                       character varying(64) NOT NULL,
    ""ActivatedAt""              timestamp with time zone NULL,
    ""CreatedAt""                timestamp with time zone NOT NULL,
    ""ReferralCode""             character varying(32) NOT NULL,
    ""ReferredDiscountPercent""  numeric               NOT NULL,
    ""ReferredUserId""           character varying(64) NULL,
    ""ReferrerCreditAmount""     numeric               NOT NULL,
    ""ReferrerUserId""           character varying(64) NOT NULL,
    ""RewardedAt""               timestamp with time zone NULL,
    ""Status""                   character varying(32) NOT NULL,
    CONSTRAINT ""PK_ReferralRecords"" PRIMARY KEY (""Id"")
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ReferralRecords_ReferralCode""    ON ""ReferralRecords"" (""ReferralCode"");
CREATE INDEX        IF NOT EXISTS ""IX_ReferralRecords_ReferrerUserId"" ON ""ReferralRecords"" (""ReferrerUserId"");

CREATE TABLE IF NOT EXISTS ""ScheduleExceptions"" (
    ""Id""           character varying(64)  NOT NULL,
    ""CreatedAt""    timestamp with time zone NOT NULL,
    ""Date""         date                   NOT NULL,
    ""EndTime""      character varying(5)   NULL,
    ""IsBlocked""    boolean                NOT NULL,
    ""Reason""       character varying(500) NULL,
    ""ReviewerId""   character varying(64)  NOT NULL,
    ""StartTime""    character varying(5)   NULL,
    CONSTRAINT ""PK_ScheduleExceptions"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_ScheduleExceptions_ReviewerId"" ON ""ScheduleExceptions"" (""ReviewerId"");

CREATE TABLE IF NOT EXISTS ""ScoreGuaranteePledges"" (
    ""Id""                     character varying(64)  NOT NULL,
    ""ActivatedAt""            timestamp with time zone NOT NULL,
    ""ActualScore""            integer                NULL,
    ""BaselineScore""          integer                NOT NULL,
    ""ClaimNote""              character varying(512) NULL,
    ""ClaimSubmittedAt""       timestamp with time zone NULL,
    ""ExpiresAt""              timestamp with time zone NOT NULL,
    ""GuaranteedImprovement""  integer                NOT NULL,
    ""ProofDocumentUrl""       character varying(512) NULL,
    ""ReviewNote""             character varying(512) NULL,
    ""ReviewedAt""             timestamp with time zone NULL,
    ""ReviewedBy""             character varying(64)  NULL,
    ""Status""                 character varying(32)  NOT NULL,
    ""SubscriptionId""         character varying(64)  NOT NULL,
    ""UserId""                 character varying(64)  NOT NULL,
    CONSTRAINT ""PK_ScoreGuaranteePledges"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_ScoreGuaranteePledges_UserId"" ON ""ScoreGuaranteePledges"" (""UserId"");

CREATE TABLE IF NOT EXISTS ""Sponsorships"" (
    ""Id""             uuid                    NOT NULL,
    ""CreatedAt""      timestamp with time zone NOT NULL,
    ""LearnerEmail""   character varying(256)  NOT NULL,
    ""LearnerUserId""  character varying(64)   NULL,
    ""RevokedAt""      timestamp with time zone NULL,
    ""SponsorUserId""  character varying(64)   NOT NULL,
    ""Status""         character varying(16)   NOT NULL,
    CONSTRAINT ""PK_Sponsorships"" PRIMARY KEY (""Id"")
);

CREATE TABLE IF NOT EXISTS ""StudyCommitments"" (
    ""Id""                      character varying(64) NOT NULL,
    ""CreatedAt""               timestamp with time zone NOT NULL,
    ""DailyMinutes""            integer               NOT NULL,
    ""DeactivatedAt""           timestamp with time zone NULL,
    ""FreezeProtections""       integer               NOT NULL,
    ""FreezeProtectionsUsed""   integer               NOT NULL,
    ""IsActive""                boolean               NOT NULL,
    ""UserId""                  character varying(64) NOT NULL,
    CONSTRAINT ""PK_StudyCommitments"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_StudyCommitments_UserId"" ON ""StudyCommitments"" (""UserId"");
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recovery migration; intentionally non-destructive on rollback.
            // Rolling back does NOT drop these tables since they may already
            // contain production data.
        }
    }
}
