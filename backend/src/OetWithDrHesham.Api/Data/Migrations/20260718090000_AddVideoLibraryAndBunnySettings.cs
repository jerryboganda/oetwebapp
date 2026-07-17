using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Video Library (Bunny Stream) — new feature schema:
    ///
    ///   • LibraryVideos + VideoCategories/-Items + VideoCaptionTracks +
    ///     VideoAttachments — admin-authored catalog backed by Bunny Stream.
    ///   • LearnerVideoLibraryProgress + LearnerVideoBookmarks — per-user state
    ///     (UserId-named so UserHardDeleteService auto-purges them).
    ///   • VideoPlaybackSessions + VideoAttestationChallenges — attested
    ///     native-client playback (sessions are never issued to browsers).
    ///   • VideoPlaybackEvents — playback telemetry.
    ///   • RuntimeSettings Bunny Stream + video attestation override columns
    ///     (kept in sync with RuntimeSettingsSchemaSelfHeal).
    ///   • Retires the legacy video_lessons feature flags (the /v1/lessons
    ///     endpoints now answer 410). VideoLessons/LearnerVideoProgress tables
    ///     are deliberately KEPT.
    ///
    /// <para>
    /// Hand-written Postgres-only migration, following the established pattern
    /// (see 20260715090000_AddSpeakingPricingEntitlementFields.cs and
    /// 20260608000000_AddOet2026CatalogFlags.cs): the 27k-line EF model
    /// snapshot is deliberately not hand-edited. SQLite/InMemory test runs
    /// bypass migrations via EnsureCreatedAsync() and pick the schema up from
    /// the entity model directly.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260718090000_AddVideoLibraryAndBunnySettings")]
    public partial class AddVideoLibraryAndBunnySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""LibraryVideos"" (
    ""Id"" character varying(64) NOT NULL,
    ""Title"" character varying(256) NOT NULL,
    ""Description"" character varying(2048),
    ""TagsCsv"" character varying(512),
    ""Difficulty"" character varying(16),
    ""SubtestCode"" character varying(32),
    ""ExamTypeCode"" character varying(16),
    ""BunnyVideoId"" character varying(64),
    ""BunnyLibraryId"" character varying(32),
    ""EncodeStatus"" integer NOT NULL DEFAULT 0,
    ""EncodeProgress"" integer NOT NULL DEFAULT 0,
    ""EncodeError"" character varying(512),
    ""DurationSeconds"" integer NOT NULL DEFAULT 0,
    ""Width"" integer,
    ""Height"" integer,
    ""BunnyThumbnailUrl"" character varying(512),
    ""CustomThumbnailMediaAssetId"" character varying(64),
    ""AccessTier"" character varying(16) NOT NULL DEFAULT 'premium',
    ""ProfessionIdsJson"" jsonb NOT NULL DEFAULT '[]',
    ""IsFeatured"" boolean NOT NULL DEFAULT FALSE,
    ""SortOrder"" integer NOT NULL DEFAULT 0,
    ""ViewCount"" bigint NOT NULL DEFAULT 0,
    ""ChaptersJson"" jsonb NOT NULL DEFAULT '[]',
    ""Status"" integer NOT NULL DEFAULT 0,
    ""PublishAt"" timestamp with time zone,
    ""PublishedAt"" timestamp with time zone,
    ""ArchivedAt"" timestamp with time zone,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    ""CreatedByAdminId"" character varying(64),
    ""UpdatedByAdminId"" character varying(64),
    CONSTRAINT ""PK_LibraryVideos"" PRIMARY KEY (""Id""),
    CONSTRAINT ""FK_LibraryVideos_MediaAssets_CustomThumbnailMediaAssetId""
        FOREIGN KEY (""CustomThumbnailMediaAssetId"") REFERENCES ""MediaAssets"" (""Id"") ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS ""IX_LibraryVideos_Status_PublishAt"" ON ""LibraryVideos"" (""Status"", ""PublishAt"");
CREATE INDEX IF NOT EXISTS ""IX_LibraryVideos_BunnyVideoId"" ON ""LibraryVideos"" (""BunnyVideoId"");
CREATE INDEX IF NOT EXISTS ""IX_LibraryVideos_IsFeatured_SortOrder"" ON ""LibraryVideos"" (""IsFeatured"", ""SortOrder"");
CREATE INDEX IF NOT EXISTS ""IX_LibraryVideos_CustomThumbnailMediaAssetId"" ON ""LibraryVideos"" (""CustomThumbnailMediaAssetId"");

CREATE TABLE IF NOT EXISTS ""VideoCategories"" (
    ""Id"" character varying(64) NOT NULL,
    ""Title"" character varying(128) NOT NULL,
    ""Slug"" character varying(128) NOT NULL,
    ""Description"" character varying(512),
    ""DisplayOrder"" integer NOT NULL DEFAULT 0,
    ""Status"" integer NOT NULL DEFAULT 4,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_VideoCategories"" PRIMARY KEY (""Id"")
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_VideoCategories_Slug"" ON ""VideoCategories"" (""Slug"");
CREATE INDEX IF NOT EXISTS ""IX_VideoCategories_Status_DisplayOrder"" ON ""VideoCategories"" (""Status"", ""DisplayOrder"");

CREATE TABLE IF NOT EXISTS ""VideoCategoryItems"" (
    ""Id"" uuid NOT NULL,
    ""CategoryId"" character varying(64) NOT NULL,
    ""VideoId"" character varying(64) NOT NULL,
    ""SortOrder"" integer NOT NULL DEFAULT 0,
    CONSTRAINT ""PK_VideoCategoryItems"" PRIMARY KEY (""Id""),
    CONSTRAINT ""FK_VideoCategoryItems_VideoCategories_CategoryId""
        FOREIGN KEY (""CategoryId"") REFERENCES ""VideoCategories"" (""Id"") ON DELETE CASCADE,
    CONSTRAINT ""FK_VideoCategoryItems_LibraryVideos_VideoId""
        FOREIGN KEY (""VideoId"") REFERENCES ""LibraryVideos"" (""Id"") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_VideoCategoryItems_CategoryId_VideoId"" ON ""VideoCategoryItems"" (""CategoryId"", ""VideoId"");
CREATE INDEX IF NOT EXISTS ""IX_VideoCategoryItems_VideoId"" ON ""VideoCategoryItems"" (""VideoId"");

CREATE TABLE IF NOT EXISTS ""VideoCaptionTracks"" (
    ""Id"" uuid NOT NULL,
    ""VideoId"" character varying(64) NOT NULL,
    ""LanguageCode"" character varying(16) NOT NULL,
    ""Label"" character varying(64) NOT NULL,
    ""MediaAssetId"" character varying(64) NOT NULL,
    ""SyncedToBunnyAt"" timestamp with time zone,
    ""SortOrder"" integer NOT NULL DEFAULT 0,
    CONSTRAINT ""PK_VideoCaptionTracks"" PRIMARY KEY (""Id""),
    CONSTRAINT ""FK_VideoCaptionTracks_LibraryVideos_VideoId""
        FOREIGN KEY (""VideoId"") REFERENCES ""LibraryVideos"" (""Id"") ON DELETE CASCADE,
    CONSTRAINT ""FK_VideoCaptionTracks_MediaAssets_MediaAssetId""
        FOREIGN KEY (""MediaAssetId"") REFERENCES ""MediaAssets"" (""Id"") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_VideoCaptionTracks_VideoId_LanguageCode"" ON ""VideoCaptionTracks"" (""VideoId"", ""LanguageCode"");
CREATE INDEX IF NOT EXISTS ""IX_VideoCaptionTracks_MediaAssetId"" ON ""VideoCaptionTracks"" (""MediaAssetId"");

CREATE TABLE IF NOT EXISTS ""VideoAttachments"" (
    ""Id"" uuid NOT NULL,
    ""VideoId"" character varying(64) NOT NULL,
    ""Title"" character varying(128) NOT NULL,
    ""MediaAssetId"" character varying(64) NOT NULL,
    ""SortOrder"" integer NOT NULL DEFAULT 0,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_VideoAttachments"" PRIMARY KEY (""Id""),
    CONSTRAINT ""FK_VideoAttachments_LibraryVideos_VideoId""
        FOREIGN KEY (""VideoId"") REFERENCES ""LibraryVideos"" (""Id"") ON DELETE CASCADE,
    CONSTRAINT ""FK_VideoAttachments_MediaAssets_MediaAssetId""
        FOREIGN KEY (""MediaAssetId"") REFERENCES ""MediaAssets"" (""Id"") ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS ""IX_VideoAttachments_VideoId_SortOrder"" ON ""VideoAttachments"" (""VideoId"", ""SortOrder"");
CREATE INDEX IF NOT EXISTS ""IX_VideoAttachments_MediaAssetId"" ON ""VideoAttachments"" (""MediaAssetId"");

CREATE TABLE IF NOT EXISTS ""LearnerVideoLibraryProgress"" (
    ""Id"" uuid NOT NULL,
    ""UserId"" character varying(64) NOT NULL,
    ""VideoId"" character varying(64) NOT NULL,
    ""PositionSeconds"" integer NOT NULL DEFAULT 0,
    ""WatchedSeconds"" integer NOT NULL DEFAULT 0,
    ""Completed"" boolean NOT NULL DEFAULT FALSE,
    ""CompletedAt"" timestamp with time zone,
    ""LastWatchedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_LearnerVideoLibraryProgress"" PRIMARY KEY (""Id"")
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LearnerVideoLibraryProgress_UserId_VideoId"" ON ""LearnerVideoLibraryProgress"" (""UserId"", ""VideoId"");
CREATE INDEX IF NOT EXISTS ""IX_LearnerVideoLibraryProgress_UserId_LastWatchedAt"" ON ""LearnerVideoLibraryProgress"" (""UserId"", ""LastWatchedAt"");

CREATE TABLE IF NOT EXISTS ""LearnerVideoBookmarks"" (
    ""Id"" uuid NOT NULL,
    ""UserId"" character varying(64) NOT NULL,
    ""VideoId"" character varying(64) NOT NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_LearnerVideoBookmarks"" PRIMARY KEY (""Id"")
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LearnerVideoBookmarks_UserId_VideoId"" ON ""LearnerVideoBookmarks"" (""UserId"", ""VideoId"");

CREATE TABLE IF NOT EXISTS ""VideoPlaybackSessions"" (
    ""Id"" character varying(64) NOT NULL,
    ""UserId"" character varying(64) NOT NULL,
    ""VideoId"" character varying(64) NOT NULL,
    ""Platform"" character varying(32) NOT NULL,
    ""AttestationKeyId"" character varying(32) NOT NULL,
    ""IpAddress"" character varying(64),
    ""UserAgent"" character varying(256),
    ""IssuedAt"" timestamp with time zone NOT NULL,
    ""ExpiresAt"" timestamp with time zone NOT NULL,
    ""RevokedAt"" timestamp with time zone,
    CONSTRAINT ""PK_VideoPlaybackSessions"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_VideoPlaybackSessions_UserId_IssuedAt"" ON ""VideoPlaybackSessions"" (""UserId"", ""IssuedAt"");
CREATE INDEX IF NOT EXISTS ""IX_VideoPlaybackSessions_VideoId_IssuedAt"" ON ""VideoPlaybackSessions"" (""VideoId"", ""IssuedAt"");

CREATE TABLE IF NOT EXISTS ""VideoAttestationChallenges"" (
    ""Id"" character varying(64) NOT NULL,
    ""UserId"" character varying(64) NOT NULL,
    ""IssuedAt"" timestamp with time zone NOT NULL,
    ""ExpiresAt"" timestamp with time zone NOT NULL,
    ""ConsumedAt"" timestamp with time zone,
    ""Platform"" character varying(32),
    CONSTRAINT ""PK_VideoAttestationChallenges"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_VideoAttestationChallenges_UserId_IssuedAt"" ON ""VideoAttestationChallenges"" (""UserId"", ""IssuedAt"");

CREATE TABLE IF NOT EXISTS ""VideoPlaybackEvents"" (
    ""Id"" uuid NOT NULL,
    ""UserId"" character varying(64) NOT NULL,
    ""VideoId"" character varying(64) NOT NULL,
    ""SessionId"" character varying(64),
    ""EventType"" character varying(32) NOT NULL,
    ""PositionSeconds"" integer NOT NULL DEFAULT 0,
    ""OccurredAt"" timestamp with time zone NOT NULL,
    ""PayloadJson"" jsonb NOT NULL DEFAULT '{}',
    CONSTRAINT ""PK_VideoPlaybackEvents"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_VideoPlaybackEvents_VideoId_OccurredAt"" ON ""VideoPlaybackEvents"" (""VideoId"", ""OccurredAt"");
CREATE INDEX IF NOT EXISTS ""IX_VideoPlaybackEvents_UserId_OccurredAt"" ON ""VideoPlaybackEvents"" (""UserId"", ""OccurredAt"");

-- RuntimeSettings overrides (keep in sync with RuntimeSettingsSchemaSelfHeal)
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamLibraryId"" character varying(32);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamApiKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamCdnHostname"" character varying(256);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamTokenAuthKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamWebhookSecretEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamCollectionId"" character varying(64);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamPlaybackTokenTtlSeconds"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamEnabled"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""VideoAttestationKeysEncrypted"" text;

-- Legacy video_lessons feature flags: the /v1/lessons endpoints now answer
-- 410 unconditionally, so the DB flag rows are dead configuration. The new
-- video_library flag needs NO seed row — absence resolves to ENABLED
-- (VideoLibraryLearnerService.IsEnabledAsync mirrors the old default-true).
DELETE FROM ""FeatureFlags"" WHERE ""Key"" IN ('video_lessons', 'video-lessons');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS ""VideoPlaybackEvents"";
DROP TABLE IF EXISTS ""VideoAttestationChallenges"";
DROP TABLE IF EXISTS ""VideoPlaybackSessions"";
DROP TABLE IF EXISTS ""LearnerVideoBookmarks"";
DROP TABLE IF EXISTS ""LearnerVideoLibraryProgress"";
DROP TABLE IF EXISTS ""VideoAttachments"";
DROP TABLE IF EXISTS ""VideoCaptionTracks"";
DROP TABLE IF EXISTS ""VideoCategoryItems"";
DROP TABLE IF EXISTS ""VideoCategories"";
DROP TABLE IF EXISTS ""LibraryVideos"";

ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""VideoAttestationKeysEncrypted"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""BunnyStreamEnabled"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""BunnyStreamPlaybackTokenTtlSeconds"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""BunnyStreamCollectionId"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""BunnyStreamWebhookSecretEncrypted"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""BunnyStreamTokenAuthKeyEncrypted"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""BunnyStreamCdnHostname"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""BunnyStreamApiKeyEncrypted"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""BunnyStreamLibraryId"";
");
        }
    }
}
