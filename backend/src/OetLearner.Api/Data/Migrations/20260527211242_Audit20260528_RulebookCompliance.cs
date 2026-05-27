using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Audit20260528_RulebookCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountFreezeEntitlements",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FreezeRecordId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResetByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResetByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResetReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountFreezeEntitlements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountFreezePolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SelfServiceEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovalMode = table.Column<int>(type: "integer", nullable: false),
                    MinDurationDays = table.Column<int>(type: "integer", nullable: false),
                    MaxDurationDays = table.Column<int>(type: "integer", nullable: false),
                    AllowScheduling = table.Column<bool>(type: "boolean", nullable: false),
                    AccessMode = table.Column<int>(type: "integer", nullable: false),
                    EntitlementPauseMode = table.Column<int>(type: "integer", nullable: false),
                    RequireReason = table.Column<bool>(type: "boolean", nullable: false),
                    RequireInternalNotes = table.Column<bool>(type: "boolean", nullable: false),
                    AllowActivePaid = table.Column<bool>(type: "boolean", nullable: false),
                    AllowGracePeriod = table.Column<bool>(type: "boolean", nullable: false),
                    AllowTrial = table.Column<bool>(type: "boolean", nullable: false),
                    AllowComplimentary = table.Column<bool>(type: "boolean", nullable: false),
                    AllowCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    AllowExpired = table.Column<bool>(type: "boolean", nullable: false),
                    AllowReviewOnly = table.Column<bool>(type: "boolean", nullable: false),
                    AllowPastDue = table.Column<bool>(type: "boolean", nullable: false),
                    AllowSuspended = table.Column<bool>(type: "boolean", nullable: false),
                    PolicyNotes = table.Column<string>(type: "text", nullable: false),
                    EligibilityReasonCodesJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "text", nullable: true),
                    UpdatedByAdminName = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountFreezePolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountFreezeRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedByLearnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ApprovedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ApprovedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RejectedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RejectedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EndedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EndedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    IsSelfService = table.Column<bool>(type: "boolean", nullable: false),
                    EntitlementConsumed = table.Column<bool>(type: "boolean", nullable: false),
                    EntitlementReset = table.Column<bool>(type: "boolean", nullable: false),
                    IsOverride = table.Column<bool>(type: "boolean", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ScheduledStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    InternalNotes = table.Column<string>(type: "text", nullable: true),
                    PolicySnapshotJson = table.Column<string>(type: "text", nullable: false),
                    PolicyVersionSnapshot = table.Column<int>(type: "integer", nullable: false),
                    EligibilitySnapshotJson = table.Column<string>(type: "text", nullable: false),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    EndReason = table.Column<string>(type: "text", nullable: true),
                    CancellationReason = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountFreezeRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Achievements",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IconUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    XPReward = table.Column<int>(type: "integer", nullable: false),
                    CriteriaJson = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Achievements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminUploadSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AdminUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalFilename = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Extension = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DeclaredMimeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeclaredSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ReceivedBytes = table.Column<long>(type: "bigint", nullable: false),
                    TotalParts = table.Column<int>(type: "integer", nullable: false),
                    PartsReceived = table.Column<int>(type: "integer", nullable: false),
                    IntendedRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUploadSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AffiliateAttributions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AffiliateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClickedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttributedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConvertedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FirstPaymentTransactionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateAttributions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AffiliateCommissions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AffiliateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaymentTransactionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AmountAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AccruedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PayoutBatchId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateCommissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Affiliates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "numeric(6,3)", nullable: false),
                    CookieDays = table.Column<int>(type: "integer", nullable: false),
                    PayoutThresholdAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    PayoutCurrency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PayoutMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayoutDetailsEncrypted = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Affiliates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiAssistantThreads",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ModelOverride = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAssistantThreads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiCodebaseChunks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChunkType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SymbolName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StartLine = table.Column<int>(type: "integer", nullable: false),
                    EndLine = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    TokenCount = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TsVectorConfig = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Embedding = table.Column<float[]>(type: "real[]", nullable: true),
                    IndexedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EmbeddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCodebaseChunks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AIConfigVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TaskType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Accuracy = table.Column<double>(type: "double precision", nullable: false),
                    ConfidenceThreshold = table.Column<double>(type: "double precision", nullable: false),
                    RoutingRule = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ExperimentFlag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PromptLabel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ConfidencePolicyJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ChangeNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIConfigVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiCreditLedger",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokensDelta = table.Column<int>(type: "integer", nullable: false),
                    CostDeltaUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReferenceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiredByEntryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCreditLedger", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiFeatureRoutes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiFeatureRoutes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiFeatureToolGrants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ToolCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiFeatureToolGrants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiFileBackups",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MessageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    OriginalContent = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    AutosaveBranch = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiFileBackups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiGlobalPolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    KillSwitchEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    KillSwitchScope = table.Column<int>(type: "integer", nullable: false),
                    KillSwitchReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DisabledFeaturesCsv = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    MonthlyBudgetUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    SoftWarnPct = table.Column<int>(type: "integer", nullable: false),
                    HardKillPct = table.Column<int>(type: "integer", nullable: false),
                    CurrentSpendUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    AllowByokOnScoringFeatures = table.Column<bool>(type: "boolean", nullable: false),
                    AllowByokOnNonScoringFeatures = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultPlatformProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ByokErrorCooldownHours = table.Column<int>(type: "integer", nullable: false),
                    ByokTransientRetryCount = table.Column<int>(type: "integer", nullable: false),
                    AnomalyDetectionEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AnomalyMultiplierX = table.Column<decimal>(type: "numeric", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGlobalPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiProviderAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EncryptedApiKey = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    ApiKeyHint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MonthlyRequestCap = table.Column<int>(type: "integer", nullable: true),
                    RequestsUsedThisMonth = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    ExhaustedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastTestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastTestStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LastTestError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PeriodMonthKey = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviderAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiProviders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Dialect = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    EncryptedApiKey = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    ApiKeyHint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DefaultModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReasoningEffort = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AllowedModelsCsv = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    PricePer1kPromptTokens = table.Column<decimal>(type: "numeric", nullable: false),
                    PricePer1kCompletionTokens = table.Column<decimal>(type: "numeric", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    CircuitBreakerThreshold = table.Column<int>(type: "integer", nullable: false),
                    CircuitBreakerWindowSeconds = table.Column<int>(type: "integer", nullable: false),
                    FailoverPriority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastTestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastTestStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LastTestError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiQuotaCounters",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PeriodKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TokensUsed = table.Column<int>(type: "integer", nullable: false),
                    RequestsCount = table.Column<int>(type: "integer", nullable: false),
                    CostAccumulatedUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    LastUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiQuotaCounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiQuotaPlans",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Period = table.Column<int>(type: "integer", nullable: false),
                    MonthlyTokenCap = table.Column<int>(type: "integer", nullable: false),
                    DailyTokenCap = table.Column<int>(type: "integer", nullable: false),
                    MaxConcurrentRequests = table.Column<int>(type: "integer", nullable: false),
                    RolloverPolicy = table.Column<int>(type: "integer", nullable: false),
                    RolloverCapPct = table.Column<int>(type: "integer", nullable: false),
                    OveragePolicy = table.Column<int>(type: "integer", nullable: false),
                    OverageRatePer1kTokens = table.Column<decimal>(type: "numeric", nullable: true),
                    AutoUpgradeTargetPlanCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DegradeModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AllowedFeaturesCsv = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    AllowedModelsCsv = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiQuotaPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiToolInvocations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AiUsageRecordId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ToolCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TurnIndex = table.Column<int>(type: "integer", nullable: false),
                    ArgsHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiToolInvocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiTools",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    JsonSchemaArgs = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiTools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiUserQuotaOverrides",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MonthlyTokenCapOverride = table.Column<int>(type: "integer", nullable: true),
                    DailyTokenCapOverride = table.Column<int>(type: "integer", nullable: true),
                    ForcePlanCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AiDisabled = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    GrantedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUserQuotaOverrides", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "AnalyticsEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsEvents", x => new { x.OccurredAt, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "ApplicationUserAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProtectedAuthenticatorSecret = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    EmailVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AuthenticatorEnabledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    PreferredCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    PreferredRegion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    FailedSignInCount = table.Column<int>(type: "integer", nullable: false),
                    LockoutUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StripeCustomerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationUserAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Attempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Context = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ElapsedSeconds = table.Column<int>(type: "integer", nullable: false),
                    DraftVersion = table.Column<int>(type: "integer", nullable: false),
                    ParentAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ComparisonGroupId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeviceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastClientSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DraftContent = table.Column<string>(type: "text", nullable: false),
                    Scratchpad = table.Column<string>(type: "text", nullable: false),
                    ChecklistJson = table.Column<string>(type: "text", nullable: false),
                    AnswersJson = table.Column<string>(type: "text", nullable: false),
                    AudioUploadState = table.Column<int>(type: "integer", nullable: false),
                    AudioObjectKey = table.Column<string>(type: "text", nullable: true),
                    AudioMetadataJson = table.Column<string>(type: "text", nullable: false),
                    TranscriptJson = table.Column<string>(type: "text", nullable: false),
                    AnalysisJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModelVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AudioRegenerationBatches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AudioType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TotalItems = table.Column<int>(type: "integer", nullable: false),
                    CompletedItems = table.Column<int>(type: "integer", nullable: false),
                    FailedItems = table.Column<int>(type: "integer", nullable: false),
                    VoiceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelVariant = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Speed = table.Column<double>(type: "double precision", nullable: false),
                    Pitch = table.Column<double>(type: "double precision", nullable: false),
                    Emotion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudioRegenerationBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackgroundJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    AttemptId = table.Column<string>(type: "text", nullable: true),
                    ResourceId = table.Column<string>(type: "text", nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastTransitionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StatusReasonCode = table.Column<string>(type: "text", nullable: false),
                    StatusMessage = table.Column<string>(type: "text", nullable: false),
                    Retryable = table.Column<bool>(type: "boolean", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    RetryAfterMs = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackgroundJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankAccountConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    BankName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AccountHolderName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Iban = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SwiftBic = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AccountNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RoutingOrSortCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    InstructionsMarkdown = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccountConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingAddOns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Interval = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    GrantCredits = table.Column<int>(type: "integer", nullable: false),
                    GrantEntitlementsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CompatiblePlanCodesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ActiveVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LatestVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AppliesToAllPlans = table.Column<bool>(type: "boolean", nullable: false),
                    IsStackable = table.Column<bool>(type: "boolean", nullable: false),
                    QuantityStep = table.Column<int>(type: "integer", nullable: false),
                    MaxQuantity = table.Column<int>(type: "integer", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OriginalPriceGbp = table.Column<decimal>(type: "numeric", nullable: true),
                    AddonKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequiresEligibleParent = table.Column<bool>(type: "boolean", nullable: false),
                    EligibilityFlag = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LettersGranted = table.Column<int>(type: "integer", nullable: false),
                    SessionsGranted = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAddOns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingAddOnVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AddOnId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Interval = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    GrantCredits = table.Column<int>(type: "integer", nullable: false),
                    GrantEntitlementsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CompatiblePlanCodesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    AppliesToAllPlans = table.Column<bool>(type: "boolean", nullable: false),
                    IsStackable = table.Column<bool>(type: "boolean", nullable: false),
                    QuantityStep = table.Column<int>(type: "integer", nullable: false),
                    MaxQuantity = table.Column<int>(type: "integer", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OriginalPriceGbp = table.Column<decimal>(type: "numeric", nullable: true),
                    AddonKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequiresEligibleParent = table.Column<bool>(type: "boolean", nullable: false),
                    EligibilityFlag = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LettersGranted = table.Column<int>(type: "integer", nullable: false),
                    SessionsGranted = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAddOnVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingCouponRedemptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CouponCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CouponId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CouponVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuoteId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CheckoutSessionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCouponRedemptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingCoupons",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    DiscountType = table.Column<int>(type: "integer", nullable: false),
                    DiscountValue = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UsageLimitTotal = table.Column<int>(type: "integer", nullable: true),
                    UsageLimitPerUser = table.Column<int>(type: "integer", nullable: true),
                    MinimumSubtotal = table.Column<decimal>(type: "numeric", nullable: true),
                    ApplicablePlanCodesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ApplicableAddOnCodesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ActiveVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LatestVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsStackable = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    RedemptionCount = table.Column<int>(type: "integer", nullable: false),
                    CouponVariant = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VariantMetadataJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EligibleCountriesJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    NewUsersOnly = table.Column<bool>(type: "boolean", nullable: false),
                    ExistingUsersOnly = table.Column<bool>(type: "boolean", nullable: false),
                    StackableWithReferral = table.Column<bool>(type: "boolean", nullable: false),
                    StackableWithAffiliate = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCoupons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingCouponVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CouponId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    DiscountType = table.Column<int>(type: "integer", nullable: false),
                    DiscountValue = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UsageLimitTotal = table.Column<int>(type: "integer", nullable: true),
                    UsageLimitPerUser = table.Column<int>(type: "integer", nullable: true),
                    MinimumSubtotal = table.Column<decimal>(type: "numeric", nullable: true),
                    ApplicablePlanCodesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ApplicableAddOnCodesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    IsStackable = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCouponVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    QuoteId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PayloadJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingMetricDailies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MetricDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MetricCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    DetailsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingMetricDailies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingNotificationDispatchLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TemplateCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingNotificationDispatchLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingNotificationTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LocaleTag = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BodyTemplate = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    VariablesJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingNotificationTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingPlans",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Interval = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DurationMonths = table.Column<int>(type: "integer", nullable: false),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false),
                    IsRenewable = table.Column<bool>(type: "boolean", nullable: false),
                    TrialDays = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IncludedCredits = table.Column<int>(type: "integer", nullable: false),
                    DiagnosticMockEntitlement = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IncludedSubtestsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EntitlementsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ActiveVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LatestVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActiveSubscribers = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OriginalPriceGbp = table.Column<decimal>(type: "numeric", nullable: true),
                    AccessDurationDays = table.Column<int>(type: "integer", nullable: false),
                    WritingAddonsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SpeakingAddonsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TutorBookDiscountEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Profession = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProductCategory = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DashboardModulesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    BundledWritingAssessments = table.Column<int>(type: "integer", nullable: false),
                    BundledSpeakingSessions = table.Column<int>(type: "integer", nullable: false),
                    BundledAiCredits = table.Column<int>(type: "integer", nullable: false),
                    BundledTutorBook = table.Column<bool>(type: "boolean", nullable: false),
                    BundledBasicEnglish = table.Column<bool>(type: "boolean", nullable: false),
                    IsDraft = table.Column<bool>(type: "boolean", nullable: false),
                    ExtensionAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    RecallUpdatesEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingPlanVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Interval = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DurationMonths = table.Column<int>(type: "integer", nullable: false),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false),
                    IsRenewable = table.Column<bool>(type: "boolean", nullable: false),
                    TrialDays = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IncludedCredits = table.Column<int>(type: "integer", nullable: false),
                    IncludedSubtestsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EntitlementsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OriginalPriceGbp = table.Column<decimal>(type: "numeric", nullable: true),
                    AccessDurationDays = table.Column<int>(type: "integer", nullable: false),
                    WritingAddonsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SpeakingAddonsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TutorBookDiscountEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Profession = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProductCategory = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DashboardModulesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    BundledWritingAssessments = table.Column<int>(type: "integer", nullable: false),
                    BundledSpeakingSessions = table.Column<int>(type: "integer", nullable: false),
                    BundledAiCredits = table.Column<int>(type: "integer", nullable: false),
                    BundledTutorBook = table.Column<bool>(type: "boolean", nullable: false),
                    BundledBasicEnglish = table.Column<bool>(type: "boolean", nullable: false),
                    IsDraft = table.Column<bool>(type: "boolean", nullable: false),
                    ExtensionAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    RecallUpdatesEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPlanVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ProductType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StripeProductId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingQuotes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PlanCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PlanVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AddOnCodesJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    AddOnVersionIdsJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CouponCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CouponVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    SubtotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CheckoutSessionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExperimentAssignmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SnapshotJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingQuotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CancellationIntents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReasonDetail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OfferedCouponCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationIntents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Carts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SessionToken = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecoveryEmailSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Certificates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DataJson = table.Column<string>(type: "text", nullable: false),
                    PdfUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    VerificationCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Certificates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StripeSessionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FulfilledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChurnRiskSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RiskScore = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    RiskBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    FactorsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RecommendedAction = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActionDispatched = table.Column<bool>(type: "boolean", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChurnRiskSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClassFeedbacks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    RecommendToFriend = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassFeedbacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClassMaterials",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LiveClassId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    FileUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Visibility = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassMaterials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CohortMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CohortId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EnrolledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CohortMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cohorts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SponsorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MaxSeats = table.Column<int>(type: "integer", nullable: false),
                    EnrolledCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cohorts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentCohortOverlays",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProgramId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CohortCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CohortTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReleaseScheduleJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentCohortOverlays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentContributors",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Bio = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    VerificationStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubmissionCount = table.Column<int>(type: "integer", nullable: false),
                    ApprovedCount = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentContributors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentGenerationJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TaskTypeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequestedCount = table.Column<int>(type: "integer", nullable: false),
                    GeneratedCount = table.Column<int>(type: "integer", nullable: false),
                    PromptConfigJson = table.Column<string>(type: "text", nullable: false),
                    GeneratedContentIdsJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentGenerationJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentImportBatches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TotalItems = table.Column<int>(type: "integer", nullable: false),
                    ProcessedItems = table.Column<int>(type: "integer", nullable: false),
                    FailedItems = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorLogJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    CriteriaFocusJson = table.Column<string>(type: "text", nullable: false),
                    ScenarioType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ModeSupportJson = table.Column<string>(type: "text", nullable: false),
                    PublishedRevisionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CaseNotes = table.Column<string>(type: "text", nullable: true),
                    DetailJson = table.Column<string>(type: "text", nullable: false),
                    ModelAnswerJson = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DifficultyRating = table.Column<int>(type: "integer", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QaStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QaReviewedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    QaReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PerformanceMetricsJson = table.Column<string>(type: "text", nullable: true),
                    InstructionLanguage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ContentLanguage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ProfessionIdsJson = table.Column<string>(type: "text", nullable: false),
                    PackageEligibilityJson = table.Column<string>(type: "text", nullable: false),
                    CohortRelevance = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceProvenance = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RightsStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FreshnessConfidence = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SupersededById = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DuplicateGroupId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MediaManifestJson = table.Column<string>(type: "text", nullable: false),
                    CanonicalSourcePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ImportBatchId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsPreviewEligible = table.Column<bool>(type: "boolean", nullable: false),
                    IsDiagnosticEligible = table.Column<bool>(type: "boolean", nullable: false),
                    IsMockEligible = table.Column<bool>(type: "boolean", nullable: false),
                    QualityScore = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentLessons",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModuleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LessonType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentLessons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentModules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TrackId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    PrerequisiteModuleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentModules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentPackages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PackageType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    InstructionLanguage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    BillingPlanId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BillingAddOnId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ComparisonFeaturesJson = table.Column<string>(type: "text", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentPapers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AppliesToAllProfessions = table.Column<bool>(type: "boolean", nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PublishedRevisionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CardType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LetterType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    TagsCsv = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SourceProvenance = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExtractedTextJson = table.Column<string>(type: "text", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IntegrityAcknowledgedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IntegrityAcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPapers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentPrograms",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    InstructionLanguage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ProgramType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPrograms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentPublishRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedByName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReviewedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReviewedByName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    EditorReviewedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EditorReviewedByName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EditorReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EditorNotes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PublisherApprovedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PublisherApprovedByName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PublisherApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublisherNotes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RejectedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RejectedByName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RejectionStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPublishRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentReferences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModuleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExternalUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentReferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentRevisions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChangeNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentSubmissions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContributorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    TaskTypeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ContentPayloadJson = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Difficulty = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Tags = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReviewNotes = table.Column<string>(type: "text", nullable: true),
                    PublishedContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentSubmissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentTracks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProgramId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentTracks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationEvaluations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OverallScaled = table.Column<int>(type: "integer", nullable: false),
                    OverallGrade = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    CountryVariant = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    CriteriaJson = table.Column<string>(type: "text", nullable: false),
                    StrengthsJson = table.Column<string>(type: "text", nullable: false),
                    ImprovementsJson = table.Column<string>(type: "text", nullable: false),
                    SuggestedPracticeJson = table.Column<string>(type: "text", nullable: false),
                    AppliedRuleIdsJson = table.Column<string>(type: "text", nullable: false),
                    RulebookVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Advisory = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AiUsageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationEvaluations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationSessionResumeTokens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationSessionResumeTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TaskTypeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Profession = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScenarioJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TurnCount = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    TranscriptJson = table.Column<string>(type: "text", nullable: false),
                    AudioConsentVersion = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    RecordingConsentAcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VendorConsentAcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EvaluationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: true),
                    AsrProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TtsProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AzureSpeechKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    AzureSpeechRegion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AzureLocale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AzureTtsDefaultVoice = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    WhisperBaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    WhisperApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    WhisperModel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeepgramApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    DeepgramModel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeepgramLanguage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    RealtimeSttEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    RealtimeAsrProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RealtimeSttAllowRealProvider = table.Column<bool>(type: "boolean", nullable: true),
                    RealtimeSttRealProviderProductionAuthorized = table.Column<bool>(type: "boolean", nullable: true),
                    RealtimeSttFallbackToBatch = table.Column<bool>(type: "boolean", nullable: true),
                    RealtimeSttProviderConnectTimeoutSeconds = table.Column<int>(type: "integer", nullable: true),
                    RealtimeSttMaxChunkBytes = table.Column<int>(type: "integer", nullable: true),
                    RealtimeSttPartialMinIntervalMs = table.Column<int>(type: "integer", nullable: true),
                    RealtimeSttTurnIdleTimeoutSeconds = table.Column<int>(type: "integer", nullable: true),
                    RealtimeSttMaxConcurrentStreamsPerUser = table.Column<int>(type: "integer", nullable: true),
                    RealtimeSttMaxAudioSecondsPerSession = table.Column<int>(type: "integer", nullable: true),
                    RealtimeSttDailyAudioSecondsPerUser = table.Column<int>(type: "integer", nullable: true),
                    RealtimeSttMonthlyBudgetCapUsd = table.Column<decimal>(type: "numeric", nullable: true),
                    RealtimeSttEstimatedCostUsdPerMinute = table.Column<decimal>(type: "numeric", nullable: true),
                    RealtimeSttProviderSessionTopology = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RealtimeSttRegionId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    RealtimeSttAssumeLearnersAdult = table.Column<bool>(type: "boolean", nullable: true),
                    RealtimeSttAllowManagedLearnerRealProvider = table.Column<bool>(type: "boolean", nullable: true),
                    RealtimeSttConsentVersion = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    RealtimeSttRollbackMode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RealtimeSttAllowedMimeTypesCsv = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ElevenLabsSttApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    ElevenLabsSttBaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ElevenLabsSttModel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ElevenLabsSttLanguage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ElevenLabsSttAudioFormat = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ElevenLabsSttCommitStrategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ElevenLabsSttKeytermsCsv = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ElevenLabsSttEnableProviderLogging = table.Column<bool>(type: "boolean", nullable: true),
                    ElevenLabsSttTokenTtlSeconds = table.Column<int>(type: "integer", nullable: true),
                    ElevenLabsApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    ElevenLabsTtsBaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ElevenLabsDefaultVoiceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ElevenLabsModel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ElevenLabsOutputFormat = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ElevenLabsPronunciationDictionaryId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ElevenLabsPronunciationDictionaryVersionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ElevenLabsStability = table.Column<double>(type: "double precision", nullable: true),
                    ElevenLabsSimilarityBoost = table.Column<double>(type: "double precision", nullable: true),
                    ElevenLabsStyle = table.Column<double>(type: "double precision", nullable: true),
                    ElevenLabsUseSpeakerBoost = table.Column<bool>(type: "boolean", nullable: true),
                    CosyVoiceBaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CosyVoiceApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    CosyVoiceDefaultVoice = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ChatTtsBaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ChatTtsApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    ChatTtsDefaultVoice = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Qwen3ModelVariant = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Qwen3VoiceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Qwen3VoiceInstructions = table.Column<string>(type: "text", nullable: true),
                    Qwen3Speed = table.Column<double>(type: "double precision", nullable: true),
                    Qwen3Pitch = table.Column<double>(type: "double precision", nullable: true),
                    Qwen3Emotion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    GptSoVitsBaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    GptSoVitsApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    GptSoVitsDefaultVoice = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MaxAudioBytes = table.Column<long>(type: "bigint", nullable: true),
                    AudioRetentionDays = table.Column<int>(type: "integer", nullable: true),
                    PrepDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    MaxSessionDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    MaxTurnDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    EnabledTaskTypesCsv = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FreeTierSessionsLimit = table.Column<int>(type: "integer", nullable: true),
                    FreeTierWindowDays = table.Column<int>(type: "integer", nullable: true),
                    ReplyModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EvaluationModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReplyTemperature = table.Column<double>(type: "double precision", nullable: true),
                    EvaluationTemperature = table.Column<double>(type: "double precision", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByUserName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TaskTypeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Scenario = table.Column<string>(type: "text", nullable: false),
                    RoleDescription = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PatientContext = table.Column<string>(type: "text", nullable: true),
                    ExpectedOutcomes = table.Column<string>(type: "text", nullable: true),
                    ObjectivesJson = table.Column<string>(type: "text", nullable: false),
                    ExpectedRedFlagsJson = table.Column<string>(type: "text", nullable: false),
                    KeyVocabularyJson = table.Column<string>(type: "text", nullable: false),
                    PatientVoiceJson = table.Column<string>(type: "text", nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EstimatedDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TtsVoice = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TtsModelVariant = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    OpeningAudioSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationTurnAnnotations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvaluationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TurnNumber = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RuleId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Evidence = table.Column<string>(type: "text", nullable: false),
                    Suggestion = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTurnAnnotations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationTurns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TurnNumber = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    AudioUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    TimestampMs = table.Column<int>(type: "integer", nullable: false),
                    ConfidenceScore = table.Column<double>(type: "double precision", nullable: true),
                    AnalysisJson = table.Column<string>(type: "text", nullable: false),
                    TurnClientId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    ProviderEventId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FinalizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AiFeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AiUsageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTurns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Criteria",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Criteria", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrossSellRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TriggerProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SuggestedProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrossSellRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StripeSubscriptionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StripePriceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BillingProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentPeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CurrentPeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelAtPeriodEnd = table.Column<bool>(type: "boolean", nullable: false),
                    CanceledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PausedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeflectionRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TriggerReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OfferedCouponCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MinTenureDays = table.Column<int>(type: "integer", nullable: false),
                    MaxOffersPerUser = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeflectionRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiagnosticSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiagnosticSubtests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DiagnosticSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticSubtests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DictationDrills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DrillType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AudioAssetUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    TranscriptText = table.Column<string>(type: "text", nullable: false),
                    AcceptableVariantsJson = table.Column<string>(type: "text", nullable: false),
                    Accent = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    ProfessionRelevanceJson = table.Column<string>(type: "text", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DictationDrills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DunningAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InvoiceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    StripeFailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DunningAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DunningCampaigns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastFailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastFailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    StepsCompletedCsv = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RecoveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DunningCampaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Evaluations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ScoreRange = table.Column<string>(type: "text", nullable: false),
                    GradeRange = table.Column<string>(type: "text", nullable: true),
                    ConfidenceBand = table.Column<int>(type: "integer", nullable: false),
                    StrengthsJson = table.Column<string>(type: "jsonb", nullable: false),
                    IssuesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CriterionScoresJson = table.Column<string>(type: "jsonb", nullable: false),
                    FeedbackItemsJson = table.Column<string>(type: "jsonb", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModelExplanationSafe = table.Column<string>(type: "text", nullable: false),
                    LearnerDisclaimer = table.Column<string>(type: "text", nullable: false),
                    StatusReasonCode = table.Column<string>(type: "text", nullable: false),
                    StatusMessage = table.Column<string>(type: "text", nullable: false),
                    Retryable = table.Column<bool>(type: "boolean", nullable: false),
                    RetryAfterMs = table.Column<int>(type: "integer", nullable: true),
                    LastTransitionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModelVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Evaluations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExamBookings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BookingReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExternalUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TestCenter = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamBookings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExamFamilies",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScoringModel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SubtestConfigJson = table.Column<string>(type: "text", nullable: false),
                    CriteriaConfigJson = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamFamilies", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "ExamTypes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SubtestDefinitionsJson = table.Column<string>(type: "text", nullable: false),
                    ScoringSystemJson = table.Column<string>(type: "text", nullable: false),
                    TimingsJson = table.Column<string>(type: "text", nullable: false),
                    ProfessionIdsJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamTypes", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FromCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ToCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertAnnotationTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByExpertId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CriterionCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TemplateText = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    UsageCount = table.Column<int>(type: "integer", nullable: false),
                    IsShared = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertAnnotationTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertAvailabilities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DaysJson = table.Column<string>(type: "text", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertAvailabilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertCalibrationCases",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BenchmarkLabel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CaseArtifactsJson = table.Column<string>(type: "text", nullable: false),
                    ReferenceRubricJson = table.Column<string>(type: "text", nullable: false),
                    ReferenceNotesJson = table.Column<string>(type: "text", nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    BenchmarkScore = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertCalibrationCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertCalibrationNotes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    CaseId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertCalibrationNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertCalibrationResults",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CalibrationCaseId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmittedRubricJson = table.Column<string>(type: "text", nullable: false),
                    ReviewerScore = table.Column<int>(type: "integer", nullable: false),
                    AlignmentScore = table.Column<double>(type: "double precision", nullable: false),
                    DisagreementSummary = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDraft = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertCalibrationResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertCompensationRates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RateMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertCompensationRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertEarnings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AmountMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EarnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaidOutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PayoutId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertEarnings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertMessageReplies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AuthorName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertMessageReplies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertMessageThreads",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LinkedReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LinkedCalibrationCaseId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LinkedLearnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertMessageThreads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertMetricSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WindowStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WindowEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedReviews = table.Column<int>(type: "integer", nullable: false),
                    DraftReviews = table.Column<int>(type: "integer", nullable: false),
                    AvgTurnaroundHours = table.Column<double>(type: "double precision", nullable: false),
                    SlaHitRate = table.Column<double>(type: "double precision", nullable: false),
                    CalibrationScore = table.Column<double>(type: "double precision", nullable: false),
                    ReworkRate = table.Column<double>(type: "double precision", nullable: false),
                    CompletionDataJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertMetricSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertOnboardingProgresses",
                columns: table => new
                {
                    ExpertUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfileJson = table.Column<string>(type: "text", nullable: false),
                    QualificationsJson = table.Column<string>(type: "text", nullable: false),
                    RatesJson = table.Column<string>(type: "text", nullable: false),
                    CompletedStepsJson = table.Column<string>(type: "text", nullable: false),
                    IsComplete = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertOnboardingProgresses", x => x.ExpertUserId);
                });

            migrationBuilder.CreateTable(
                name: "ExpertPayouts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalAmountMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApprovedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertPayouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertReviewAmends",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BeforeSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    AfterSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    AmendNumber = table.Column<int>(type: "integer", nullable: false),
                    AmendedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertReviewAmends", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertReviewAssignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssignedReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AssignedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClaimState = table.Column<int>(type: "integer", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReassignedFrom = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertReviewAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertReviewDrafts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RubricEntriesJson = table.Column<string>(type: "text", nullable: false),
                    CriterionCommentsJson = table.Column<string>(type: "text", nullable: false),
                    AnchoredCommentsJson = table.Column<string>(type: "text", nullable: false),
                    TimestampCommentsJson = table.Column<string>(type: "text", nullable: false),
                    FinalCommentDraft = table.Column<string>(type: "text", nullable: false),
                    ScratchpadJson = table.Column<string>(type: "text", nullable: false),
                    ChecklistItemsJson = table.Column<string>(type: "text", nullable: false),
                    DraftSavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AutosaveErrorState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertReviewDrafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertReviewerPayouts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayPeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayPeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewCount = table.Column<int>(type: "integer", nullable: false),
                    TotalCompensation = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalLearnerPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AdminNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ApprovedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ApprovedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewRequestIdsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertReviewerPayouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertSlaSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SlaDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WasMet = table.Column<bool>(type: "boolean", nullable: false),
                    TurnaroundHours = table.Column<double>(type: "double precision", nullable: true),
                    SlaState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertSlaSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeatureFlags",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FlagType = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    RolloutPercentage = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Owner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureFlags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ForumCategories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ForumReplies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AuthorRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    IsExpertVerified = table.Column<bool>(type: "boolean", nullable: false),
                    LikeCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EditedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumReplies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ForumThreads",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CategoryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AuthorRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    ReplyCount = table.Column<int>(type: "integer", nullable: false),
                    ViewCount = table.Column<int>(type: "integer", nullable: false),
                    LikeCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumThreads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FoundationResources",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentBody = table.Column<string>(type: "text", nullable: true),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PrerequisiteResourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoundationResources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FreePreviewAssets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PreviewType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConversionCtaText = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TargetPackageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreePreviewAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FreeTierConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    MaxWritingAttempts = table.Column<int>(type: "integer", nullable: false),
                    MaxSpeakingAttempts = table.Column<int>(type: "integer", nullable: false),
                    MaxReadingAttempts = table.Column<int>(type: "integer", nullable: false),
                    MaxListeningAttempts = table.Column<int>(type: "integer", nullable: false),
                    MaxSpeakingMockSets = table.Column<int>(type: "integer", nullable: false),
                    TrialDurationDays = table.Column<int>(type: "integer", nullable: false),
                    ShowUpgradePrompts = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreeTierConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GatewayRoutingConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ProductType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GatewayName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayRoutingConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Goals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetExamDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OverallGoal = table.Column<string>(type: "text", nullable: true),
                    TargetWritingScore = table.Column<int>(type: "integer", nullable: true),
                    TargetSpeakingScore = table.Column<int>(type: "integer", nullable: true),
                    TargetReadingScore = table.Column<int>(type: "integer", nullable: true),
                    TargetListeningScore = table.Column<int>(type: "integer", nullable: true),
                    PreviousAttempts = table.Column<int>(type: "integer", nullable: false),
                    WeakSubtestsJson = table.Column<string>(type: "text", nullable: false),
                    StudyHoursPerWeek = table.Column<int>(type: "integer", nullable: false),
                    TargetCountry = table.Column<string>(type: "text", nullable: true),
                    TargetOrganization = table.Column<string>(type: "text", nullable: true),
                    DraftStateJson = table.Column<string>(type: "text", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Goals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GrammarLessons",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ContentHtml = table.Column<string>(type: "text", nullable: false),
                    ExercisesJson = table.Column<string>(type: "text", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    PrerequisiteLessonId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrammarLessons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Scope = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InterlocutorTrainingModules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    ContentMarkdown = table.Column<string>(type: "text", nullable: false),
                    MediaAssetIdsJson = table.Column<string>(type: "text", nullable: false),
                    RequiredForCalibration = table.Column<bool>(type: "boolean", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterlocutorTrainingModules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    PlanVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AddOnVersionIdsJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CouponVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    QuoteId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CheckoutSessionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                });

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

            migrationBuilder.CreateTable(
                name: "LeaderboardEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Period = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    XP = table.Column<long>(type: "bigint", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    OptedIn = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerAccentProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Accent = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AccuracyPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    QuestionsAttempted = table.Column<int>(type: "integer", nullable: false),
                    QuestionsCorrect = table.Column<int>(type: "integer", nullable: false),
                    MinutesListened = table.Column<int>(type: "integer", nullable: false),
                    SelfConfidenceRating = table.Column<int>(type: "integer", nullable: false),
                    LastPracticedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerAccentProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerAchievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AchievementId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UnlockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerAchievements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerBadges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BadgeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EarnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerBadges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerCertificates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CertificateType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DownloadUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    MetadataJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerCertificates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerDictationProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DictationDrillId = table.Column<Guid>(type: "uuid", nullable: false),
                    LearnerAnswer = table.Column<string>(type: "text", nullable: true),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextReviewAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerDictationProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerEscalations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmissionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerEscalations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerGrammarProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LessonId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExerciseScore = table.Column<int>(type: "integer", nullable: true),
                    AnswersJson = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerGrammarProgress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerLessonProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoWatched = table.Column<bool>(type: "boolean", nullable: false),
                    BodyRead = table.Column<bool>(type: "boolean", nullable: false),
                    Drill1Completed = table.Column<bool>(type: "boolean", nullable: false),
                    Drill2Completed = table.Column<bool>(type: "boolean", nullable: false),
                    Drill3Completed = table.Column<bool>(type: "boolean", nullable: false),
                    QuizScore = table.Column<int>(type: "integer", nullable: true),
                    QuizAttempts = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerLessonProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerListeningLessonProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoWatched = table.Column<bool>(type: "boolean", nullable: false),
                    BodyRead = table.Column<bool>(type: "boolean", nullable: false),
                    Drill1Completed = table.Column<bool>(type: "boolean", nullable: false),
                    Drill2Completed = table.Column<bool>(type: "boolean", nullable: false),
                    Drill3Completed = table.Column<bool>(type: "boolean", nullable: false),
                    QuizScore = table.Column<int>(type: "integer", nullable: true),
                    QuizAttempts = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerListeningLessonProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerListeningPathways",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalWeeks = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WeeksJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerListeningPathways", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerListeningProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ExamDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HoursPerWeek = table.Column<int>(type: "integer", nullable: false),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EnglishExposureSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ComfortBritish = table.Column<int>(type: "integer", nullable: false),
                    ComfortAustralian = table.Column<int>(type: "integer", nullable: false),
                    ComfortVarious = table.Column<int>(type: "integer", nullable: false),
                    HasTakenBefore = table.Column<bool>(type: "boolean", nullable: false),
                    PreviousScore = table.Column<int>(type: "integer", nullable: true),
                    SelfRatedSpeed = table.Column<int>(type: "integer", nullable: false),
                    SelfRatedNoteTaking = table.Column<int>(type: "integer", nullable: false),
                    SelfRatedSpelling = table.Column<int>(type: "integer", nullable: false),
                    CurrentStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentReadinessScore = table.Column<int>(type: "integer", nullable: true),
                    PredictedScore = table.Column<int>(type: "integer", nullable: true),
                    OnboardingCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AudioCheckPassedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PathwayGeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerListeningProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerListeningSkillScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SkillCode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    CurrentScore = table.Column<decimal>(type: "numeric", nullable: false),
                    DiagnosticScore = table.Column<decimal>(type: "numeric", nullable: false),
                    QuestionsAttempted = table.Column<int>(type: "integer", nullable: false),
                    QuestionsCorrect = table.Column<int>(type: "integer", nullable: false),
                    LastPracticedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerListeningSkillScores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerListeningStrategyProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StrategyId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarkedAsRead = table.Column<bool>(type: "boolean", nullable: false),
                    Favorited = table.Column<bool>(type: "boolean", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerListeningStrategyProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerPronunciationCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PronunciationCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Easiness = table.Column<decimal>(type: "numeric", nullable: false),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    Repetitions = table.Column<int>(type: "integer", nullable: false),
                    RetentionScore = table.Column<int>(type: "integer", nullable: false),
                    NextReviewAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerPronunciationCards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerPronunciationDiscriminationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DrillId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetPhoneme = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RoundsTotal = table.Column<int>(type: "integer", nullable: false),
                    RoundsCorrect = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerPronunciationDiscriminationAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerPronunciationProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PhonemeCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AverageScore = table.Column<double>(type: "double precision", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ScoreHistoryJson = table.Column<string>(type: "text", nullable: false),
                    LastPracticedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    Ease = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerPronunciationProgress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerReadingPathways",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalWeeks = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WeeksJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerReadingPathways", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerReadingProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ExamDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HoursPerWeek = table.Column<int>(type: "integer", nullable: false),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HasTakenBefore = table.Column<bool>(type: "boolean", nullable: false),
                    PreviousScore = table.Column<int>(type: "integer", nullable: true),
                    SelfRatedSpeed = table.Column<int>(type: "integer", nullable: false),
                    SelfRatedVocabulary = table.Column<int>(type: "integer", nullable: false),
                    CurrentStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentReadinessScore = table.Column<int>(type: "integer", nullable: true),
                    PredictedScore = table.Column<int>(type: "integer", nullable: true),
                    OnboardingCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PathwayGeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerReadingProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerSkillProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CriterionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentRating = table.Column<double>(type: "double precision", nullable: false),
                    ConfidenceLevel = table.Column<int>(type: "integer", nullable: false),
                    EvidenceCount = table.Column<int>(type: "integer", nullable: false),
                    RecentScoresJson = table.Column<string>(type: "text", nullable: false),
                    LastUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerSkillProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerSkillScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SkillCode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    CurrentScore = table.Column<decimal>(type: "numeric", nullable: false),
                    DiagnosticScore = table.Column<decimal>(type: "numeric", nullable: false),
                    QuestionsAttempted = table.Column<int>(type: "integer", nullable: false),
                    QuestionsCorrect = table.Column<int>(type: "integer", nullable: false),
                    LastPracticedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerSkillScores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerStrategyProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StrategyGuideId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadPercent = table.Column<int>(type: "integer", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    Bookmarked = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BookmarkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerStrategyProgress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerStreaks",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CurrentStreak = table.Column<int>(type: "integer", nullable: false),
                    LongestStreak = table.Column<int>(type: "integer", nullable: false),
                    LastActiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StreakFreezeCount = table.Column<int>(type: "integer", nullable: false),
                    StreakFreezeUsedCount = table.Column<int>(type: "integer", nullable: false),
                    LastFreezeUsedDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerStreaks", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "LearnerVideoProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VideoLessonId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WatchedSeconds = table.Column<int>(type: "integer", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    LastWatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerVideoProgress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerVocabularies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TermId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Mastery = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EaseFactor = table.Column<double>(type: "double precision", nullable: false),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    ReviewCount = table.Column<int>(type: "integer", nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    NextReviewDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Starred = table.Column<bool>(type: "boolean", nullable: false),
                    StarReason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    LastErrorTypeCode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerVocabularies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerVocabularyItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VocabularyWordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Easiness = table.Column<decimal>(type: "numeric", nullable: false),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    Repetitions = table.Column<int>(type: "integer", nullable: false),
                    RetentionScore = table.Column<int>(type: "integer", nullable: false),
                    NextReviewAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerVocabularyItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerWritingLessonProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    BodyRead = table.Column<bool>(type: "boolean", nullable: false),
                    DrillCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    QuizScore = table.Column<int>(type: "integer", nullable: true),
                    QuizAttempts = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerWritingLessonProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerWritingPathways",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalWeeks = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WeeksJson = table.Column<string>(type: "text", nullable: false),
                    WeaknessVectorJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    SubSkillMasteryJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    LastRecalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DiagnosticSubmissionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerWritingPathways", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerWritingProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ExamDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DaysPerWeek = table.Column<int>(type: "integer", nullable: false),
                    MinutesPerDay = table.Column<int>(type: "integer", nullable: false),
                    TargetCountry = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LetterTypeFocusJson = table.Column<string>(type: "text", nullable: false),
                    CurrentStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentReadinessScore = table.Column<int>(type: "integer", nullable: true),
                    PredictedScore = table.Column<int>(type: "integer", nullable: true),
                    LastDiagnosticEvaluationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OnboardingCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PathwayGeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubDiscipline = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    YearsExperience = table.Column<int>(type: "integer", nullable: true),
                    OptInCommunity = table.Column<bool>(type: "boolean", nullable: false),
                    OptInLeaderboard = table.Column<bool>(type: "boolean", nullable: false),
                    OptInDataForTraining = table.Column<bool>(type: "boolean", nullable: false),
                    OptInBuddy = table.Column<bool>(type: "boolean", nullable: false),
                    AccommodationProfileJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    CanonVersionPinned = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerWritingProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerXps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalXp = table.Column<int>(type: "integer", nullable: false),
                    CurrentLevel = table.Column<int>(type: "integer", nullable: false),
                    XpToNextLevel = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerXps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerXPs",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalXP = table.Column<long>(type: "bigint", nullable: false),
                    WeeklyXP = table.Column<long>(type: "bigint", nullable: false),
                    MonthlyXP = table.Column<long>(type: "bigint", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MonthStartDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerXPs", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "ListeningAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeadlineAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false),
                    RawScore = table.Column<int>(type: "integer", nullable: true),
                    ScaledScore = table.Column<int>(type: "integer", nullable: true),
                    MaxRawScore = table.Column<int>(type: "integer", nullable: false),
                    PolicySnapshotJson = table.Column<string>(type: "text", nullable: false),
                    PaperRevisionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ScopeJson = table.Column<string>(type: "text", nullable: true),
                    NavigationStateJson = table.Column<string>(type: "jsonb", nullable: true),
                    WindowStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WindowDurationMs = table.Column<int>(type: "integer", nullable: true),
                    AudioCueTimelineJson = table.Column<string>(type: "jsonb", nullable: true),
                    TechReadinessJson = table.Column<string>(type: "jsonb", nullable: true),
                    AnnotationsJson = table.Column<string>(type: "jsonb", nullable: true),
                    HumanScoreOverridesJson = table.Column<string>(type: "jsonb", nullable: true),
                    LastQuestionVersionMapJson = table.Column<string>(type: "jsonb", nullable: true),
                    RulebookVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningDailyPlanItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    ItemType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FocusSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    FocusAccent = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningDailyPlanItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningLessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SkillCode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    VideoUrl = table.Column<string>(type: "text", nullable: true),
                    BodyMarkdownEn = table.Column<string>(type: "text", nullable: false),
                    BodyMarkdownAr = table.Column<string>(type: "text", nullable: false),
                    DrillQuestionIdsJson = table.Column<string>(type: "text", nullable: false),
                    QuizQuestionIdsJson = table.Column<string>(type: "text", nullable: false),
                    PrerequisiteLessonId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningLessons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningMockTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    QuestionIdsJson = table.Column<string>(type: "text", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningMockTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningParts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PartCode = table.Column<int>(type: "integer", nullable: false),
                    MaxRawScore = table.Column<int>(type: "integer", nullable: false),
                    Instructions = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningParts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningPathwayProgress",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StageCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ScaledScore = table.Column<int>(type: "integer", nullable: true),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UnlockOverrideBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningPathwayProgress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningPolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptsPerPaperPerUser = table.Column<int>(type: "integer", nullable: false),
                    AttemptCooldownMinutes = table.Column<int>(type: "integer", nullable: false),
                    BestScoreDisplay = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ShowPastAttempts = table.Column<bool>(type: "boolean", nullable: false),
                    FullPaperTimerMinutes = table.Column<int>(type: "integer", nullable: false),
                    GracePeriodSeconds = table.Column<int>(type: "integer", nullable: false),
                    OnExpirySubmitPolicy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CountdownWarningsJson = table.Column<string>(type: "text", nullable: false),
                    ExamReplayAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    LearningReplayAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    LearningEvidenceLoopEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ShortAnswerNormalisation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ShortAnswerAcceptSynonyms = table.Column<bool>(type: "boolean", nullable: false),
                    AiExtractionEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AiExtractionRequireHumanApproval = table.Column<bool>(type: "boolean", nullable: false),
                    AiExtractionMaxRetriesPerPaper = table.Column<int>(type: "integer", nullable: false),
                    ShowExplanationsAfterSubmit = table.Column<bool>(type: "boolean", nullable: false),
                    ShowExplanationsOnlyIfWrong = table.Column<bool>(type: "boolean", nullable: false),
                    ShowCorrectAnswerOnReview = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultExtraTimePct = table.Column<int>(type: "integer", nullable: false),
                    ScreenReaderOptimised = table.Column<bool>(type: "boolean", nullable: false),
                    AutoExpireWorkerEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AutoExpireAfterMinutes = table.Column<int>(type: "integer", nullable: false),
                    AllowResumeAfterExpiry = table.Column<bool>(type: "boolean", nullable: false),
                    RetainAnswerRowsDays = table.Column<int>(type: "integer", nullable: false),
                    RetainAttemptHeadersDays = table.Column<int>(type: "integer", nullable: false),
                    AnonymiseOnAccountDelete = table.Column<bool>(type: "boolean", nullable: false),
                    PreviewWindowMsA1 = table.Column<int>(type: "integer", nullable: true),
                    PreviewWindowMsA2 = table.Column<int>(type: "integer", nullable: true),
                    PreviewWindowMsC1 = table.Column<int>(type: "integer", nullable: true),
                    PreviewWindowMsC2 = table.Column<int>(type: "integer", nullable: true),
                    ReviewWindowMsA1 = table.Column<int>(type: "integer", nullable: true),
                    ReviewWindowMsA2 = table.Column<int>(type: "integer", nullable: true),
                    ReviewWindowMsC1 = table.Column<int>(type: "integer", nullable: true),
                    ReviewWindowMsC2FinalCbt = table.Column<int>(type: "integer", nullable: true),
                    ReviewWindowMsC2FinalPaper = table.Column<int>(type: "integer", nullable: true),
                    BetweenSectionTransitionMs = table.Column<int>(type: "integer", nullable: true),
                    PartBQuestionWindowMs = table.Column<int>(type: "integer", nullable: true),
                    OneWayLocksEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    ConfirmDialogRequired = table.Column<bool>(type: "boolean", nullable: true),
                    UnansweredWarningRequired = table.Column<bool>(type: "boolean", nullable: true),
                    ConfirmTokenTtlMs = table.Column<int>(type: "integer", nullable: true),
                    HighlightingEnabledPartA = table.Column<bool>(type: "boolean", nullable: true),
                    HighlightingEnabledPartBC = table.Column<bool>(type: "boolean", nullable: true),
                    OptionStrikethroughEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    InAppZoomEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    CtrlZoomBlocked = table.Column<bool>(type: "boolean", nullable: true),
                    AnnotationsPersistOnAdvance = table.Column<bool>(type: "boolean", nullable: true),
                    TechReadinessRequired = table.Column<bool>(type: "boolean", nullable: true),
                    TechReadinessTtlMs = table.Column<int>(type: "integer", nullable: true),
                    FinalReviewAllPartsMsPaper = table.Column<int>(type: "integer", nullable: true),
                    RowVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningPracticeNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PracticeSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ListeningQuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NoteMarkdown = table.Column<string>(type: "text", nullable: false),
                    CharacterCount = table.Column<int>(type: "integer", nullable: false),
                    LastSavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningPracticeNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningPracticeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FocusSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    FocusAccent = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    QuestionIdsJson = table.Column<string>(type: "text", nullable: false),
                    AudioAssetIdsJson = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    TotalQuestions = table.Column<int>(type: "integer", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningPracticeSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningQuestionAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningQuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PracticeSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AudioAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectedOption = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LearnerAnswer = table.Column<string>(type: "text", nullable: true),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    IsUnknown = table.Column<bool>(type: "boolean", nullable: false),
                    IsSpellingCorrectMeaningWrong = table.Column<bool>(type: "boolean", nullable: false),
                    IsMeaningCorrectSpellingWrong = table.Column<bool>(type: "boolean", nullable: false),
                    ReplaysUsed = table.Column<int>(type: "integer", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "integer", nullable: false),
                    MarkedForReview = table.Column<bool>(type: "boolean", nullable: false),
                    NoteText = table.Column<string>(type: "text", nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InReviewQueue = table.Column<bool>(type: "boolean", nullable: false),
                    NextReviewAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewIntervalIndex = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveCorrect = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningQuestionAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningStrategies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApplicablePartsJson = table.Column<string>(type: "text", nullable: false),
                    EstimatedReadMinutes = table.Column<int>(type: "integer", nullable: false),
                    BodyMarkdownEn = table.Column<string>(type: "text", nullable: false),
                    BodyMarkdownAr = table.Column<string>(type: "text", nullable: false),
                    VideoUrl = table.Column<string>(type: "text", nullable: true),
                    AudioUrl = table.Column<string>(type: "text", nullable: true),
                    LinkedDrillId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnlockStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningStrategies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningTtsJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExtractId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RetryAfter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BatchId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    VoiceOverride = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ModelVariantOverride = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    InstructionsOverride = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SpeedOverride = table.Column<double>(type: "double precision", nullable: true),
                    PitchOverride = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningTtsJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningUserPolicyOverrides",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExtraTimeEntitlementPct = table.Column<int>(type: "integer", nullable: false),
                    BlockAttempts = table.Column<bool>(type: "boolean", nullable: false),
                    AccessibilityModeEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    GrantedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningUserPolicyOverrides", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "LiveClassWaitlistEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    JoinedWaitlistAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NotifiedOfOpening = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveClassWaitlistEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LiveClassWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawPayload = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveClassWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManualPaymentRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuoteId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AmountAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProofUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ProofHashHex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AdminNotes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    AccessGrantedSubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualPaymentRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketingAssets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AssetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PackageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalFilename = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    StoragePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ThumbnailPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CaptionPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TranscriptPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MediaKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    UploadedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MobilePushTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobilePushTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MockBundles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MockType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AppliesToAllProfessions = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    TagsCsv = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QualityStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReleasePolicy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TopicTagsCsv = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SkillTagsCsv = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    WatermarkEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RandomiseQuestions = table.Column<bool>(type: "boolean", nullable: false),
                    SourceProvenance = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockBundles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MockEntitlementLedgers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AddOnId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MockAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockEntitlementLedgers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MockItemAnalysisSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockBundleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentPaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    TotalAttempts = table.Column<int>(type: "integer", nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<double>(type: "double precision", nullable: false),
                    DiscriminationIndex = table.Column<double>(type: "double precision", nullable: true),
                    DistractorJson = table.Column<string>(type: "text", nullable: false),
                    Flag = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetiredReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RetiredByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockItemAnalysisSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MockReports",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PayloadSchemaVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NativeIapProductMappings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StoreProductId = table.Column<string>(type: "character varying(192)", maxLength: 192, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NativeIapProductMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsGranted = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationConsents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveryAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NotificationEventId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MessageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ResponsePayloadJson = table.Column<string>(type: "text", nullable: false),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveryAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipientAuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipientRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Body = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    ActionUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    VersionOrDateBucket = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DedupeKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FanoutAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationInboxItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NotificationEventId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Body = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    ActionUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    ChannelsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationInboxItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPolicyOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AudienceRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InAppEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    EmailEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    PushEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    EmailMode = table.Column<int>(type: "integer", nullable: true),
                    MaxDeliveriesPerHour = table.Column<int>(type: "integer", nullable: true),
                    MaxDeliveriesPerDay = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPolicyOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GlobalInAppEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    GlobalEmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    GlobalPushEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    QuietHoursEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    QuietHoursStartMinutes = table.Column<int>(type: "integer", nullable: true),
                    QuietHoursEndMinutes = table.Column<int>(type: "integer", nullable: true),
                    EventOverridesJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationSuppressions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReleasedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReleasedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSuppressions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SubjectTemplate = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BodyTemplate = table.Column<string>(type: "text", nullable: false),
                    TextTemplate = table.Column<string>(type: "text", nullable: true),
                    HtmlTemplate = table.Column<string>(type: "text", nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentTransactionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LearnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Gateway = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GatewayRefundId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RefundType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AdminNote = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    RequestedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReversedWalletCredits = table.Column<bool>(type: "boolean", nullable: false),
                    ReversedEntitlements = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRefunds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PackageContentRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PackageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuleType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageContentRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentDisputes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentTransactionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LearnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Gateway = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GatewayDisputeId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AmountDisputed = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    EntitlementsFrozen = table.Column<bool>(type: "boolean", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FundsWithdrawnAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentDisputes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentMethodUpdateLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethodUpdateLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Gateway = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GatewayTransactionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ProductType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ProductId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    QuoteId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PlanVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AddOnVersionIdsJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CouponVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Gateway = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GatewayEventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProcessingStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VerificationStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PayloadSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ParserVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    GatewayTransactionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRetriedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRetriedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastRetriedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PeerReviewFeedbacks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PeerReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OverallRating = table.Column<int>(type: "integer", nullable: false),
                    Comments = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    StrengthNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ImprovementNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    HelpfulnessRating = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeerReviewFeedbacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PeerReviewRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmitterUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeerReviewRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PermissionTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Permissions = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PredictionSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PredictedScoreLow = table.Column<int>(type: "integer", nullable: false),
                    PredictedScoreHigh = table.Column<int>(type: "integer", nullable: false),
                    PredictedScoreMid = table.Column<int>(type: "integer", nullable: false),
                    ConfidenceLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    TrendJson = table.Column<string>(type: "text", nullable: false),
                    EvaluationCount = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredictionSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PricingExperimentAssignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExperimentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VariantCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Converted = table.Column<bool>(type: "boolean", nullable: false),
                    ConvertedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConvertedAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingExperimentAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PricingExperiments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RolloutPercent = table.Column<int>(type: "integer", nullable: false),
                    VariantsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingExperiments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrivateSpeakingAuditLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BookingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Details = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateSpeakingAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrivateSpeakingConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultSlotDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    BufferMinutesBetweenSlots = table.Column<int>(type: "integer", nullable: false),
                    MinBookingLeadTimeHours = table.Column<int>(type: "integer", nullable: false),
                    MaxBookingAdvanceDays = table.Column<int>(type: "integer", nullable: false),
                    ReservationTimeoutMinutes = table.Column<int>(type: "integer", nullable: false),
                    DefaultPriceMinorUnits = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CancellationWindowHours = table.Column<int>(type: "integer", nullable: false),
                    AllowReschedule = table.Column<bool>(type: "boolean", nullable: false),
                    RescheduleWindowHours = table.Column<int>(type: "integer", nullable: false),
                    ReminderOffsetsHoursJson = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DailyReminderEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DailyReminderHourUtc = table.Column<int>(type: "integer", nullable: false),
                    CancellationPolicyText = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    BookingPolicyText = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateSpeakingConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Professions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Professions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PronunciationAssessments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DrillId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConversationSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AccuracyScore = table.Column<double>(type: "double precision", nullable: false),
                    FluencyScore = table.Column<double>(type: "double precision", nullable: false),
                    CompletenessScore = table.Column<double>(type: "double precision", nullable: false),
                    ProsodyScore = table.Column<double>(type: "double precision", nullable: false),
                    OverallScore = table.Column<double>(type: "double precision", nullable: false),
                    ProjectedSpeakingScaled = table.Column<int>(type: "integer", nullable: false),
                    ProjectedSpeakingGrade = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    WordScoresJson = table.Column<string>(type: "text", nullable: false),
                    ProblematicPhonemesJson = table.Column<string>(type: "text", nullable: false),
                    FluencyMarkersJson = table.Column<string>(type: "text", nullable: false),
                    FindingsJson = table.Column<string>(type: "text", nullable: false),
                    FeedbackJson = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RulebookVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PronunciationAssessments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PronunciationAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DrillId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AudioStorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AudioSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AudioBytes = table.Column<long>(type: "bigint", nullable: true),
                    AudioMimeType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AudioDurationMs = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AssessmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AudioReapAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PronunciationAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PronunciationCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Word = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PronunciationIpa = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BritishIpa = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AustralianIpa = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AmericanIpa = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AudioBritishUrl = table.Column<string>(type: "text", nullable: true),
                    AudioAustralianUrl = table.Column<string>(type: "text", nullable: true),
                    AudioAmericanUrl = table.Column<string>(type: "text", nullable: true),
                    DefinitionEn = table.Column<string>(type: "text", nullable: false),
                    DefinitionAr = table.Column<string>(type: "text", nullable: false),
                    SyllableCount = table.Column<int>(type: "integer", nullable: false),
                    StressPattern = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CommonMispronunciationsJson = table.Column<string>(type: "text", nullable: false),
                    SimilarSoundingTrapsJson = table.Column<string>(type: "text", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    ProfessionRelevanceJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PronunciationCards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PronunciationDrills",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetPhoneme = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Profession = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Focus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    PrimaryRuleId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ExampleWordsJson = table.Column<string>(type: "text", nullable: false),
                    MinimalPairsJson = table.Column<string>(type: "text", nullable: false),
                    SentencesJson = table.Column<string>(type: "text", nullable: false),
                    AudioModelUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AudioModelAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TipsHtml = table.Column<string>(type: "text", nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PronunciationDrills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    P256dh = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Auth = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FailureReasonCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSuccessfulAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFailureAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadinessHistories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Overall = table.Column<decimal>(type: "numeric", nullable: false),
                    Writing = table.Column<decimal>(type: "numeric", nullable: false),
                    Speaking = table.Column<decimal>(type: "numeric", nullable: false),
                    Reading = table.Column<decimal>(type: "numeric", nullable: false),
                    Listening = table.Column<decimal>(type: "numeric", nullable: false),
                    Vocabulary = table.Column<decimal>(type: "numeric", nullable: false),
                    Risk = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Unknown"),
                    TargetDateProbability = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadinessHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadinessSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    OverallReadiness = table.Column<decimal>(type: "numeric", nullable: false),
                    WritingReadiness = table.Column<decimal>(type: "numeric", nullable: false),
                    SpeakingReadiness = table.Column<decimal>(type: "numeric", nullable: false),
                    ReadingReadiness = table.Column<decimal>(type: "numeric", nullable: false),
                    ListeningReadiness = table.Column<decimal>(type: "numeric", nullable: false),
                    VocabularyReadiness = table.Column<decimal>(type: "numeric", nullable: false),
                    OverallRisk = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Unknown"),
                    TargetDateProbability = table.Column<decimal>(type: "numeric", nullable: true),
                    WeakestSubtest = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RecommendedStudyHoursPerWeek = table.Column<int>(type: "integer", nullable: false),
                    ConfidenceLevel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Low"),
                    DataPointCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadinessSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeadlineAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PartBCTimerPausedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PartBCPausedSeconds = table.Column<int>(type: "integer", nullable: false),
                    PartABreakUsed = table.Column<bool>(type: "boolean", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RawScore = table.Column<int>(type: "integer", nullable: true),
                    ScaledScore = table.Column<int>(type: "integer", nullable: true),
                    MaxRawScore = table.Column<int>(type: "integer", nullable: false),
                    PolicySnapshotJson = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    PaperRevisionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    ScopeJson = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    RulebookVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingDailyPlanItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    ItemType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FocusSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingDailyPlanItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingErrorBankEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingQuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PartCode = table.Column<int>(type: "integer", nullable: false),
                    LastWrongAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FirstSeenWrongAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenWrongAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimesWrong = table.Column<int>(type: "integer", nullable: false),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingErrorBankEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingExtractionDrafts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExtractedManifestJson = table.Column<string>(type: "text", nullable: true),
                    RawAiResponseJson = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    IsStub = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResolvedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingExtractionDrafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingLessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SkillCode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    VideoUrl = table.Column<string>(type: "text", nullable: true),
                    BodyMarkdownEn = table.Column<string>(type: "text", nullable: false),
                    BodyMarkdownAr = table.Column<string>(type: "text", nullable: false),
                    DrillQuestionIdsJson = table.Column<string>(type: "text", nullable: false),
                    QuizQuestionIdsJson = table.Column<string>(type: "text", nullable: false),
                    PrerequisiteLessonId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingLessons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingMockTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    QuestionIdsJson = table.Column<string>(type: "text", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingMockTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingPolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptsPerPaperPerUser = table.Column<int>(type: "integer", nullable: false),
                    AttemptCooldownMinutes = table.Column<int>(type: "integer", nullable: false),
                    BestScoreDisplay = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ShowPastAttempts = table.Column<bool>(type: "boolean", nullable: false),
                    AllowAttemptOnArchivedPaper = table.Column<bool>(type: "boolean", nullable: false),
                    PartATimerStrictness = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PartATimerMinutes = table.Column<int>(type: "integer", nullable: false),
                    PartBCTimerMinutes = table.Column<int>(type: "integer", nullable: false),
                    GracePeriodSeconds = table.Column<int>(type: "integer", nullable: false),
                    OnExpirySubmitPolicy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CountdownWarningsJson = table.Column<string>(type: "text", nullable: false),
                    EnabledQuestionTypesJson = table.Column<string>(type: "text", nullable: false),
                    ShortAnswerNormalisation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ShortAnswerAcceptSynonyms = table.Column<bool>(type: "boolean", nullable: false),
                    MatchingAllowPartialCredit = table.Column<bool>(type: "boolean", nullable: false),
                    SentenceCompletionStrictness = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UnknownTypeFallbackPolicy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ShowExplanationsAfterSubmit = table.Column<bool>(type: "boolean", nullable: false),
                    ShowExplanationsOnlyIfWrong = table.Column<bool>(type: "boolean", nullable: false),
                    ShowCorrectAnswerOnReview = table.Column<bool>(type: "boolean", nullable: false),
                    AllowResultDownload = table.Column<bool>(type: "boolean", nullable: false),
                    AllowResultSharing = table.Column<bool>(type: "boolean", nullable: false),
                    AiExtractionEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AiExtractionRequireHumanApproval = table.Column<bool>(type: "boolean", nullable: false),
                    AiExtractionMaxRetriesPerPaper = table.Column<int>(type: "integer", nullable: false),
                    AiExtractionModelOverride = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AiExtractionStrictSchemaMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    QuestionBankEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AssemblyStrategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AllowLearnerRandomisation = table.Column<bool>(type: "boolean", nullable: false),
                    FontScaleUserControl = table.Column<bool>(type: "boolean", nullable: false),
                    HighContrastMode = table.Column<bool>(type: "boolean", nullable: false),
                    ScreenReaderOptimised = table.Column<bool>(type: "boolean", nullable: false),
                    AllowPaperReadingMode = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ExtraTimeApprovalWorkflow = table.Column<bool>(type: "boolean", nullable: false),
                    RequireFreshAuthForSubmit = table.Column<bool>(type: "boolean", nullable: false),
                    AllowMultipleConcurrentAttempts = table.Column<bool>(type: "boolean", nullable: false),
                    AttemptIpPinning = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubmitRateLimitPerMinute = table.Column<int>(type: "integer", nullable: false),
                    AutosaveRateLimitPerMinute = table.Column<int>(type: "integer", nullable: false),
                    PreventMultipleTabs = table.Column<bool>(type: "boolean", nullable: false),
                    RetainAnswerRowsDays = table.Column<int>(type: "integer", nullable: false),
                    RetainAttemptHeadersDays = table.Column<int>(type: "integer", nullable: false),
                    AnonymiseOnAccountDelete = table.Column<bool>(type: "boolean", nullable: false),
                    ShareAnonymousAnalytics = table.Column<bool>(type: "boolean", nullable: false),
                    AllowPausingAttempt = table.Column<bool>(type: "boolean", nullable: false),
                    AutoExpireWorkerEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AutoExpireAfterMinutes = table.Column<int>(type: "integer", nullable: false),
                    AllowResumeAfterExpiry = table.Column<bool>(type: "boolean", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingPracticeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FocusSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    QuestionIdsJson = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    TotalQuestions = table.Column<int>(type: "integer", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingPracticeSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingQuestionAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PracticeSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectedOption = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    IsUnknown = table.Column<bool>(type: "boolean", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "integer", nullable: false),
                    MarkedForReview = table.Column<bool>(type: "boolean", nullable: false),
                    NoteText = table.Column<string>(type: "text", nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InReviewQueue = table.Column<bool>(type: "boolean", nullable: false),
                    NextReviewAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewIntervalIndex = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveCorrect = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingQuestionAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingQuestionDiscussionComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadingQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Upvotes = table.Column<int>(type: "integer", nullable: false),
                    IsFromTutor = table.Column<bool>(type: "boolean", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingQuestionDiscussionComments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingStrategies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApplicablePartsJson = table.Column<string>(type: "text", nullable: false),
                    EstimatedReadMinutes = table.Column<int>(type: "integer", nullable: false),
                    BodyMarkdownEn = table.Column<string>(type: "text", nullable: false),
                    BodyMarkdownAr = table.Column<string>(type: "text", nullable: false),
                    VideoUrl = table.Column<string>(type: "text", nullable: true),
                    LinkedDrillId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelatedStrategyIdsJson = table.Column<string>(type: "text", nullable: false),
                    UnlockStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingStrategies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingStrategyProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StrategyId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarkedAsRead = table.Column<bool>(type: "boolean", nullable: false),
                    Favorited = table.Column<bool>(type: "boolean", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingStrategyProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingUserPolicyOverrides",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExtraTimeEntitlementPct = table.Column<int>(type: "integer", nullable: false),
                    BlockAttempts = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    GrantedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingUserPolicyOverrides", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "RecallBookmarks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VocabularyTermId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedByFeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecallBookmarks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecallSetTags",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShortLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecallSetTags", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "ReferralCodes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TotalReferrals = table.Column<int>(type: "integer", nullable: false),
                    ConvertedReferrals = table.Column<int>(type: "integer", nullable: false),
                    TotalCreditsEarned = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReferralRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReferrerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReferralCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReferredUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReferrerCreditAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    ReferredDiscountPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RewardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Referrals",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReferrerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReferredUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReferredEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreditAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConvertedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreditedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referrals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegionPricings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PriceAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegionPricings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RemediationTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockReportId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WeaknessTag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RouteHref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DayIndex = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemediationTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReviewEscalations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SecondReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TriggerCriterion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AiScore = table.Column<int>(type: "integer", nullable: false),
                    HumanScore = table.Column<int>(type: "integer", nullable: false),
                    Divergence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResolutionNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FinalScore = table.Column<int>(type: "integer", nullable: true),
                    ConfigId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewEscalations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReviewItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CriterionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    QuestionJson = table.Column<string>(type: "text", nullable: false),
                    AnswerJson = table.Column<string>(type: "text", nullable: false),
                    EaseFactor = table.Column<double>(type: "double precision", nullable: false),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    ReviewCount = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveCorrect = table.Column<int>(type: "integer", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Starred = table.Column<bool>(type: "boolean", nullable: false),
                    StarReason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReviewRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    TurnaroundOption = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FocusAreasJson = table.Column<string>(type: "text", nullable: false),
                    LearnerNotes = table.Column<string>(type: "text", nullable: false),
                    PaymentSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PriceSnapshot = table.Column<decimal>(type: "numeric", nullable: false),
                    ReviewerCompensation = table.Column<decimal>(type: "numeric", nullable: false),
                    CompensationPaid = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EligibilitySnapshotJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BrevoApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    BrevoEmailVerificationTemplateId = table.Column<int>(type: "integer", nullable: true),
                    BrevoPasswordResetTemplateId = table.Column<int>(type: "integer", nullable: true),
                    SmtpHost = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SmtpPort = table.Column<int>(type: "integer", nullable: true),
                    SmtpUsername = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SmtpPasswordEncrypted = table.Column<string>(type: "text", nullable: true),
                    SmtpFromAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SmtpFromName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StripeSecretKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    StripePublishableKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StripeWebhookSecretEncrypted = table.Column<string>(type: "text", nullable: true),
                    StripeSuccessUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    StripeCancelUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    StripeTaxAutomaticEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    StripeTaxRegistrationsCsv = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    StripeCustomerPortalConfigurationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    StripeRadarHighRiskCountryAllowReview = table.Column<bool>(type: "boolean", nullable: true),
                    StripeRadarBlockEmailDomainsCsv = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PayPalClientId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PayPalClientSecretEncrypted = table.Column<string>(type: "text", nullable: true),
                    PayPalWebhookIdEncrypted = table.Column<string>(type: "text", nullable: true),
                    PayPalSuccessUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PayPalCancelUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SentryDsn = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SentryEnvironment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SentrySampleRate = table.Column<double>(type: "double precision", nullable: true),
                    BackupS3Url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    BackupAwsAccessKeyId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BackupAwsSecretAccessKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    BackupGpgPassphraseEncrypted = table.Column<string>(type: "text", nullable: true),
                    BackupAlertWebhook = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    GoogleClientId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    GoogleClientSecretEncrypted = table.Column<string>(type: "text", nullable: true),
                    AppleClientId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AppleTeamId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AppleKeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ApplePrivateKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    FacebookAppId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FacebookAppSecretEncrypted = table.Column<string>(type: "text", nullable: true),
                    ApnsKeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ApnsTeamId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ApnsBundleId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ApnsAuthKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    FcmServerKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    FcmProjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    VapidSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    VapidPublicKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    VapidPrivateKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    UploadScannerProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    UploadScannerHost = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UploadScannerPort = table.Column<int>(type: "integer", nullable: true),
                    UploadScannerTimeoutSeconds = table.Column<int>(type: "integer", nullable: true),
                    UploadScannerFailClosedOnError = table.Column<bool>(type: "boolean", nullable: true),
                    ZoomEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    ZoomAccountId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ZoomClientId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ZoomClientSecretEncrypted = table.Column<string>(type: "text", nullable: true),
                    ZoomApiBaseUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomTokenUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomHostUserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ZoomMeetingSdkKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ZoomMeetingSdkSecretEncrypted = table.Column<string>(type: "text", nullable: true),
                    ZoomWebhookSecretTokenEncrypted = table.Column<string>(type: "text", nullable: true),
                    ZoomWebhookRetryToleranceSeconds = table.Column<int>(type: "integer", nullable: true),
                    ZoomAllowSandboxFallback = table.Column<bool>(type: "boolean", nullable: true),
                    LiveClassesAiRecordingProcessingEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    SpeakingWhisperApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    SpeakingWhisperBaseUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SpeakingWhisperModel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByUserName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleExceptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    StartTime = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    EndTime = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleExceptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Scholarships",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GrantedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AccessTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EntitlementsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AdminNotes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scholarships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScoreGuaranteePledges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BaselineScore = table.Column<int>(type: "integer", nullable: false),
                    GuaranteedImprovement = table.Column<int>(type: "integer", nullable: false),
                    ActualScore = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProofDocumentUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ClaimNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ReviewedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimSubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreGuaranteePledges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScoringPolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "text", nullable: false),
                    PolicyJson = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoringPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfileJson = table.Column<string>(type: "text", nullable: false),
                    NotificationsJson = table.Column<string>(type: "text", nullable: false),
                    PrivacyJson = table.Column<string>(type: "text", nullable: false),
                    AccessibilityJson = table.Column<string>(type: "text", nullable: false),
                    AudioJson = table.Column<string>(type: "text", nullable: false),
                    StudyJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignupExamTypeCatalog",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignupExamTypeCatalog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignupProfessionCatalog",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CountryTargetsJson = table.Column<string>(type: "text", nullable: false),
                    ExamTypeIdsJson = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignupProfessionCatalog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignupSessionCatalog",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ExamTypeId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProfessionIdsJson = table.Column<string>(type: "text", nullable: false),
                    PriceLabel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartDate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EndDate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeliveryMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    SeatsRemaining = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignupSessionCatalog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingCalibrationSamples",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SourceAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GoldScoresJson = table.Column<string>(type: "text", nullable: false),
                    CalibrationNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingCalibrationSamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingCalibrationScores",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SampleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScoresJson = table.Column<string>(type: "text", nullable: false),
                    TotalAbsoluteError = table.Column<double>(type: "double precision", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingCalibrationScores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingCardBatchRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    GeneratedCount = table.Column<int>(type: "integer", nullable: false),
                    TopicListJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DifficultyDistributionJson = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedByAdminName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    Error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingCardBatchRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingComplianceConsents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConsentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConsentVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedFromIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingComplianceConsents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingFeedbackComments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TranscriptLineIndex = table.Column<int>(type: "integer", nullable: false),
                    CriterionCode = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingFeedbackComments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingMockSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockSetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Attempt1Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Attempt2Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    OrchestratorState = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BridgeStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReadinessBandSnapshot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CombinedScaledSnapshot = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingMockSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingMockSets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RolePlay1ContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RolePlay2ContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CriteriaFocus = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Tags = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingMockSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingReviewVoiceNotes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    TranscriptText = table.Column<string>(type: "text", nullable: true),
                    WrittenNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RubricJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingReviewVoiceNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SponsorAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OrganizationName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SponsorAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SponsorLearnerLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SponsorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerConsented = table.Column<bool>(type: "boolean", nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsentedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SponsorLearnerLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sponsorships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SponsorUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LearnerEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sponsorships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StrategyGuides",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContentHtml = table.Column<string>(type: "text", nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReadingTimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsPreviewEligible = table.Column<bool>(type: "boolean", nullable: false),
                    ContentLessonId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceProvenance = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RightsStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    FreshnessConfidence = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyGuides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StreakRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    HasActivity = table.Column<bool>(type: "boolean", nullable: false),
                    QuestionsAnsweredToday = table.Column<int>(type: "integer", nullable: false),
                    CurrentStreak = table.Column<int>(type: "integer", nullable: false),
                    LongestStreak = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreakRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyCommitments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DailyMinutes = table.Column<int>(type: "integer", nullable: false),
                    FreezeProtections = table.Column<int>(type: "integer", nullable: false),
                    FreezeProtectionsUsed = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeactivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyCommitments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyGroupMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyGroupMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyGroups",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatorUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaxMembers = table.Column<int>(type: "integer", nullable: false),
                    MemberCount = table.Column<int>(type: "integer", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyPlanItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StudyPlanId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Rationale = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Section = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ItemType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ContentRoute = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LinkedReviewItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PriorityScore = table.Column<int>(type: "integer", nullable: false),
                    WeekIndex = table.Column<int>(type: "integer", nullable: false),
                    ReplacedById = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FeedbackRating = table.Column<int>(type: "integer", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualMinutesSpent = table.Column<int>(type: "integer", nullable: true),
                    SlotKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TagsJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlanItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyPlans",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Checkpoint = table.Column<string>(type: "text", nullable: false),
                    WeakSkillFocus = table.Column<string>(type: "text", nullable: false),
                    RetakeRescueMode = table.Column<string>(type: "text", nullable: true),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DiagnosticAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    WeekNumber = table.Column<int>(type: "integer", nullable: false),
                    TotalWeeks = table.Column<int>(type: "integer", nullable: false),
                    PlanWindowStart = table.Column<DateOnly>(type: "date", nullable: true),
                    PlanWindowEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    TemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MinutesPerDayBudget = table.Column<int>(type: "integer", nullable: false),
                    GenerationInputsHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SubtestWeightsJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsPremiumPersonalized = table.Column<bool>(type: "boolean", nullable: false),
                    EntitlementTierAtGeneration = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyPlanTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MinWeeks = table.Column<int>(type: "integer", nullable: false),
                    MaxWeeks = table.Column<int>(type: "integer", nullable: false),
                    TargetBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    FocusTagsJson = table.Column<string>(type: "jsonb", nullable: false),
                    DefaultMinutesPerDay = table.Column<int>(type: "integer", nullable: false),
                    TemplateBodyJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlanTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyPlanTemplateTiers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TierCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlanTemplateTiers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ItemCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ItemType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AddOnVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    QuoteId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CheckoutSessionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    NextRenewalAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PriceAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Interval = table.Column<string>(type: "text", nullable: false),
                    PausedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GracePeriodUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WritingAssessmentsRemaining = table.Column<int>(type: "integer", nullable: false),
                    SpeakingSessionsRemaining = table.Column<int>(type: "integer", nullable: false),
                    AiCreditsRemaining = table.Column<int>(type: "integer", nullable: false),
                    TutorBookUnlocked = table.Column<bool>(type: "boolean", nullable: false),
                    BasicEnglishUnlocked = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subtests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SupportsProfessionSpecificContent = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subtests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskTypes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: false),
                    CriteriaIdsJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TaxType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(6,3)", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ZeroRateForB2BReverseCharge = table.Column<bool>(type: "boolean", nullable: false),
                    IsTaxInclusiveDisplay = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeacherClasses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherClasses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestimonialAssets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Profession = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TestDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OverallGrade = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    SubtestGradesJson = table.Column<string>(type: "text", nullable: true),
                    TestimonialText = table.Column<string>(type: "text", nullable: true),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConsentStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DisplayApproved = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestimonialAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutorBookAudioScripts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Chapter = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AudioUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    TranscriptUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorBookAudioScripts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutorBookUpdates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "text", nullable: false),
                    Audience = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorBookUpdates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutoringAvailabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpertUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    EndTime = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutoringAvailabilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutoringSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestFocus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RoomUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LearnerNotes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ExpertNotes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    PaymentSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LearnerRating = table.Column<int>(type: "integer", nullable: true),
                    LearnerFeedback = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutoringSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UploadSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UploadUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageForecastSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WindowDays = table.Column<int>(type: "integer", nullable: false),
                    ForecastCalls = table.Column<int>(type: "integer", nullable: false),
                    ForecastCredits = table.Column<int>(type: "integer", nullable: false),
                    ForecastCostUsd = table.Column<decimal>(type: "numeric(12,4)", nullable: false),
                    Ema30DailyCalls = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    PerFeatureJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    SuggestedTopUpCredits = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageForecastSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAiPreferences",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    AllowPlatformFallback = table.Column<bool>(type: "boolean", nullable: false),
                    PerFeatureOverridesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAiPreferences", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "UserNotes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedByFeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VideoLessons",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    VideoUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InstructorName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ChaptersJson = table.Column<string>(type: "text", nullable: false),
                    ResourcesJson = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoLessons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VocabularyLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    WordIdsJson = table.Column<string>(type: "text", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabularyLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VocabularyQuizResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TermsQuizzed = table.Column<int>(type: "integer", nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    Format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResultsJson = table.Column<string>(type: "text", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabularyQuizResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VocabularyTerms",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Term = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Definition = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ExampleSentence = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ContextNotes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IpaPronunciation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AudioUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AudioSlowUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AudioSentenceUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AmericanSpelling = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AudioMediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AudioProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AudioVoice = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AudioModelVariant = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AudioGeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AudioBatchId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SynonymsJson = table.Column<string>(type: "text", nullable: false),
                    CollocationsJson = table.Column<string>(type: "text", nullable: false),
                    RelatedTermsJson = table.Column<string>(type: "text", nullable: false),
                    CommonMistakesJson = table.Column<string>(type: "text", nullable: false),
                    SimilarSoundingJson = table.Column<string>(type: "text", nullable: false),
                    RecallSetCodesJson = table.Column<string>(type: "text", nullable: false),
                    SourceProvenance = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabularyTerms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VocabularyWords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Word = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PartOfSpeech = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DefinitionEn = table.Column<string>(type: "text", nullable: false),
                    DefinitionAr = table.Column<string>(type: "text", nullable: false),
                    PronunciationIpa = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AudioUrl = table.Column<string>(type: "text", nullable: true),
                    ExampleEn = table.Column<string>(type: "text", nullable: false),
                    ExampleAr = table.Column<string>(type: "text", nullable: false),
                    HealthcareContext = table.Column<string>(type: "text", nullable: false),
                    ProfessionRelevanceJson = table.Column<string>(type: "text", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabularyWords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreditBalance = table.Column<int>(type: "integer", nullable: false),
                    LedgerSummaryJson = table.Column<string>(type: "text", nullable: false),
                    LastUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WalletTopUpTierConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Credits = table.Column<int>(type: "integer", nullable: false),
                    Bonus = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IsPopular = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTopUpTierConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    BalanceAfter = table.Column<int>(type: "integer", nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ReferenceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingBuddyCheckIns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PairId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    UserAReportJson = table.Column<string>(type: "jsonb", nullable: true),
                    UserBReportJson = table.Column<string>(type: "jsonb", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingBuddyCheckIns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingBuddyMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PairId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingBuddyMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingBuddyPairs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserAId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserBId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MatchedAtBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingBuddyPairs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingCalibrationLetters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    LetterContent = table.Column<string>(type: "text", nullable: false),
                    AuthorTier = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DrAhmedGradeJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AddedById = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingCalibrationLetters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingCalibrationResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationLetterId = table.Column<Guid>(type: "uuid", nullable: false),
                    AiGradeJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    AbsErrorRaw = table.Column<int>(type: "integer", nullable: false),
                    BandMatch = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingCalibrationResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingCalibrationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalLetters = table.Column<int>(type: "integer", nullable: false),
                    Within2PointsCount = table.Column<int>(type: "integer", nullable: false),
                    MeanAbsError = table.Column<double>(type: "double precision", nullable: false),
                    BandAgreementCount = table.Column<int>(type: "integer", nullable: false),
                    NotesMarkdown = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingCalibrationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingCanonRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AppliesToLetterTypesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    AppliesToProfessionsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    Severity = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    RuleText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CorrectExamplesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    IncorrectExamplesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    DetectionType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DetectionConfigJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingCanonRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingCanonViolations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Severity = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Snippet = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LineNumber = table.Column<int>(type: "integer", nullable: true),
                    CharStart = table.Column<int>(type: "integer", nullable: true),
                    CharEnd = table.Column<int>(type: "integer", nullable: true),
                    SuggestedFix = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Disputed = table.Column<bool>(type: "boolean", nullable: false),
                    DisputeResolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingCanonViolations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingCaseNoteDrillAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DrillId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponsesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    TotalCount = table.Column<int>(type: "integer", nullable: false),
                    ScorePercent = table.Column<double>(type: "double precision", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "integer", nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingCaseNoteDrillAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingCaseNoteDrills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LetterType = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CaseNotesMarkdown = table.Column<string>(type: "text", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingCaseNoteDrills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingCaseNoteDrillSentences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DrillId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    SentenceText = table.Column<string>(type: "text", nullable: false),
                    RelevanceLabel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Rationale = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingCaseNoteDrillSentences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingCoachSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SuggestionsGenerated = table.Column<int>(type: "integer", nullable: false),
                    SuggestionsAccepted = table.Column<int>(type: "integer", nullable: false),
                    SuggestionsDismissed = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingCoachSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingCoachSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SuggestionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OriginalText = table.Column<string>(type: "text", nullable: false),
                    SuggestedText = table.Column<string>(type: "text", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    EndOffset = table.Column<int>(type: "integer", nullable: false),
                    Resolution = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingCoachSuggestions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingCommonMistakes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExampleWrong = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ExampleRight = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CanonRuleId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    RelatedSubSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingCommonMistakes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingDailyPlanItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    ItemType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FocusSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    FocusCriterion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ActionHref = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SkippedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingDailyPlanItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingDiagnosticSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadingPhaseEndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingDiagnosticSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingDraftsV2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "integer", nullable: false),
                    LastSavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingDraftsV2", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingDrillAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DrillId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseText = table.Column<string>(type: "text", nullable: true),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    FeedbackText = table.Column<string>(type: "text", nullable: true),
                    TimeSpentSeconds = table.Column<int>(type: "integer", nullable: true),
                    EaseFactor = table.Column<double>(type: "double precision", nullable: false),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    Repetitions = table.Column<int>(type: "integer", nullable: false),
                    NextDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingDrillAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingDrills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DrillType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetSubSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    TargetCanonRuleId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AppliesToProfessionsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    AppliesToLetterTypesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    PromptMarkdown = table.Column<string>(type: "text", nullable: false),
                    ExpectedAnswer = table.Column<string>(type: "text", nullable: true),
                    AlternativesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    GradingMethod = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GradingConfigJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingDrills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingExemplarAnnotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExemplarId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    CharStart = table.Column<int>(type: "integer", nullable: true),
                    CharEnd = table.Column<int>(type: "integer", nullable: true),
                    AnnotationType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuleId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingExemplarAnnotations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingExemplarEmbeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExemplarId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Dimensions = table.Column<int>(type: "integer", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "text", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingExemplarEmbeddings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingExemplars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    LetterType = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LetterContent = table.Column<string>(type: "text", nullable: false),
                    AnnotationsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    TargetBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AuthorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingExemplars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingGrades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    C1Purpose = table.Column<short>(type: "smallint", nullable: false),
                    C2Content = table.Column<short>(type: "smallint", nullable: false),
                    C3Conciseness = table.Column<short>(type: "smallint", nullable: false),
                    C4Genre = table.Column<short>(type: "smallint", nullable: false),
                    C5Organisation = table.Column<short>(type: "smallint", nullable: false),
                    C6Language = table.Column<short>(type: "smallint", nullable: false),
                    RawTotal = table.Column<short>(type: "smallint", nullable: false),
                    EstimatedBand = table.Column<int>(type: "integer", nullable: false),
                    BandLabel = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PerCriterionFeedbackJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    TopThreePrioritiesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    ConfidenceFlag = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ModelUsed = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CanonVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AppealedByGradeId = table.Column<Guid>(type: "uuid", nullable: true),
                    TutorReviewId = table.Column<Guid>(type: "uuid", nullable: true),
                    GradedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingGrades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingLearnerMistakeStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MistakeId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "integer", nullable: false),
                    LastOccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FirstOccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingLearnerMistakeStats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingLessonCompletionsV2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    QuizScore = table.Column<int>(type: "integer", nullable: true),
                    QuizAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingLessonCompletionsV2", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingLessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SkillCode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    BodyMarkdownEn = table.Column<string>(type: "text", nullable: false),
                    DrillPrompt = table.Column<string>(type: "text", nullable: false),
                    QuizJson = table.Column<string>(type: "text", nullable: false),
                    PrerequisiteLessonId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingLessons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingLessonsV2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    OrderInCourse = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "text", nullable: false),
                    VideoUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    QuizQuestionsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingLessonsV2", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingMocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingMocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingMockSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadingPhaseEndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingMockSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingOcrJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ConfidenceScore = table.Column<double>(type: "double precision", nullable: true),
                    ExtractedText = table.Column<string>(type: "text", nullable: true),
                    ImageUrlsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingOcrJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingOptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AiGradingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AiCoachEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    KillSwitchReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FreeTierLimit = table.Column<int>(type: "integer", nullable: false),
                    FreeTierWindowDays = table.Column<int>(type: "integer", nullable: false),
                    FreeTierEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PreferredGradingProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PreferredCoachProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PreferredDraftProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingOptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingPathwayItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PathwayId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Phase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FocusSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    FocusCriterion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ItemKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentRefId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    WeekNumber = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingPathwayItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingReadinessScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    MockAverageBand = table.Column<decimal>(type: "numeric", nullable: true),
                    TrajectorySlope = table.Column<decimal>(type: "numeric", nullable: true),
                    CanonCleanRate = table.Column<decimal>(type: "numeric", nullable: true),
                    TimeMgmtScore = table.Column<int>(type: "integer", nullable: true),
                    TypeConsistency = table.Column<int>(type: "integer", nullable: true),
                    PredictedBandLabel = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingReadinessScores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingRuleViolations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvaluationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LetterType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuleId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Quote = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingRuleViolations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingScenarioEmbeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Dimensions = table.Column<int>(type: "integer", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "text", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingScenarioEmbeddings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingScenarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LetterType = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubDiscipline = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TopicsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    CaseNotesMarkdown = table.Column<string>(type: "text", nullable: false),
                    CaseNotesStructuredJson = table.Column<string>(type: "jsonb", nullable: true),
                    EstimatedReadingMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsDiagnostic = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    PreviousVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApprovedById = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingScenarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingScenarioStructuredSentences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    SentenceText = table.Column<string>(type: "text", nullable: false),
                    RelevanceLabel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingScenarioStructuredSentences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingScoreAppeals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalGradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    NewGradeId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DeltaRawPoints = table.Column<int>(type: "integer", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingScoreAppeals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingShowcasePosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AnonymizedLetterContent = table.Column<string>(type: "text", nullable: false),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LetterType = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ApprovedById = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingShowcasePosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LetterContent = table.Column<string>(type: "text", nullable: false),
                    LetterContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsRevision = table.Column<bool>(type: "boolean", nullable: false),
                    OriginalSubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GradingTier = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InputSource = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingSubmissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingTutorCalibrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AgreementCoefficient = table.Column<decimal>(type: "numeric", nullable: false),
                    SamplesReviewed = table.Column<int>(type: "integer", nullable: false),
                    LastCalibratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingTutorCalibrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingTutorReviewAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingTutorReviewAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingTutorReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FreeTextFeedback = table.Column<string>(type: "text", nullable: false),
                    PerCriterionCommentsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    ScoreOverrideJson = table.Column<string>(type: "jsonb", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingTutorReviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiAssistantMessages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: true),
                    ToolCallsJson = table.Column<string>(type: "text", nullable: true),
                    ToolCallId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ToolName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TokenCount = table.Column<int>(type: "integer", nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AiUsageRecordId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAssistantMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiAssistantMessages_AiAssistantThreads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "AiAssistantThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdminPermissionGrants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AdminUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Permission = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GrantedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminPermissionGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminPermissionGrants_ApplicationUserAccounts_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiUsageRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailoverTrace = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    KeySource = table.Column<int>(type: "integer", nullable: false),
                    RulebookVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PromptTemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SystemPromptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserPromptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PromptTokens = table.Column<int>(type: "integer", nullable: false),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: false),
                    CostEstimateUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    PolicyTrace = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PeriodMonthKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PeriodDayKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageRecords", x => new { x.CreatedAt, x.Id });
                    table.ForeignKey(
                        name: "FK_AiUsageRecords_ApplicationUserAccounts_AuthAccountId",
                        column: x => x.AuthAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorAuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActorName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => new { x.OccurredAt, x.Id });
                    table.ForeignKey(
                        name: "FK_AuditEvents_ApplicationUserAccounts_ActorAuthAccountId",
                        column: x => x.ActorAuthAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EmailOtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationUserAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailOtpChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailOtpChallenges_ApplicationUserAccounts_ApplicationUserA~",
                        column: x => x.ApplicationUserAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExpertUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SpecialtiesJson = table.Column<string>(type: "text", nullable: false),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpertUsers_ApplicationUserAccounts_AuthAccountId",
                        column: x => x.AuthAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ExternalIdentityLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationUserAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSignedInAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIdentityLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalIdentityLinks_ApplicationUserAccounts_ApplicationUs~",
                        column: x => x.ApplicationUserAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MfaRecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationUserAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaRecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfaRecoveryCodes_ApplicationUserAccounts_ApplicationUserAcc~",
                        column: x => x.ApplicationUserAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokenRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationUserAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeviceInfo = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokenRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokenRecords_ApplicationUserAccounts_ApplicationUser~",
                        column: x => x.ApplicationUserAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserAiCredentials",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProviderCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EncryptedKey = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    KeyHint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ModelAllowlistCsv = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CooldownUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAiCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAiCredentials_ApplicationUserAccounts_AuthAccountId",
                        column: x => x.AuthAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrentPlanId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActiveProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    OnboardingCurrentStep = table.Column<int>(type: "integer", nullable: false),
                    OnboardingStepCount = table.Column<int>(type: "integer", nullable: false),
                    OnboardingCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    OnboardingStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OnboardingCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AccountStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentStreak = table.Column<int>(type: "integer", nullable: false),
                    LongestStreak = table.Column<int>(type: "integer", nullable: false),
                    LastPracticeDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TotalPracticeMinutes = table.Column<int>(type: "integer", nullable: false),
                    TotalPracticeSessions = table.Column<int>(type: "integer", nullable: false),
                    WeeklyActivityJson = table.Column<string>(type: "text", nullable: true),
                    ActiveExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_ApplicationUserAccounts_AuthAccountId",
                        column: x => x.AuthAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BillingPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    StripePriceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Interval = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    IntervalCount = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingPrices_BillingProducts_BillingProductId",
                        column: x => x.BillingProductId,
                        principalTable: "BillingProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppliedPromoCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    DiscountPercent = table.Column<decimal>(type: "numeric", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppliedPromoCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppliedPromoCodes_Carts_CartId",
                        column: x => x.CartId,
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RolePlayCards",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScenarioTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Setting = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CandidateRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InterlocutorRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PatientName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    PatientAge = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Background = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Task1 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Task2 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Task3 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Task4 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Task5 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AllowedNotes = table.Column<bool>(type: "boolean", nullable: false),
                    PrepTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    RolePlayTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    PatientEmotion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CommunicationGoal = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClinicalTopic = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CriteriaFocusJson = table.Column<string>(type: "text", nullable: false),
                    Disclaimer = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsLiveTutorEligible = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePlayCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RolePlayCards_ContentItems_ContentItemId",
                        column: x => x.ContentItemId,
                        principalTable: "ContentItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingDrillItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DrillKind = table.Column<int>(type: "integer", nullable: false),
                    TargetCriteriaJson = table.Column<string>(type: "text", nullable: false),
                    RecommendedAfterSessionScoreBelow = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingDrillItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingDrillItems_ContentItems_ContentItemId",
                        column: x => x.ContentItemId,
                        principalTable: "ContentItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ListeningExtractionDrafts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProposedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProposedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsStub = table.Column<bool>(type: "boolean", nullable: false),
                    StubReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Summary = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ProposedQuestionsJson = table.Column<string>(type: "text", nullable: false),
                    RawAiResponseJson = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningExtractionDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningExtractionDrafts_ContentPapers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "ContentPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingParts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PartCode = table.Column<int>(type: "integer", nullable: false),
                    TimeLimitMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaxRawScore = table.Column<int>(type: "integer", nullable: false),
                    Instructions = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingParts_ContentPapers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "ContentPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InterlocutorTrainingProgress",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModuleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    QuizScore = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterlocutorTrainingProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterlocutorTrainingProgress_InterlocutorTrainingModules_Mo~",
                        column: x => x.ModuleId,
                        principalTable: "InterlocutorTrainingModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ListeningAttemptNotes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningExtractId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TranscriptMs = table.Column<int>(type: "integer", nullable: true),
                    Text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningAttemptNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningAttemptNotes_ListeningAttempts_ListeningAttemptId",
                        column: x => x.ListeningAttemptId,
                        principalTable: "ListeningAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ListeningExpertFeedbacks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OverallFeedbackMarkdown = table.Column<string>(type: "text", nullable: false),
                    PerQuestionFeedbackJson = table.Column<string>(type: "text", nullable: true),
                    RecommendedAreasJson = table.Column<string>(type: "text", nullable: true),
                    RawScoreOverride = table.Column<int>(type: "integer", nullable: true),
                    ScoreOverrideReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningExpertFeedbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningExpertFeedbacks_ListeningAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "ListeningAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ListeningExtracts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningPartId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SpeakersJson = table.Column<string>(type: "text", nullable: false),
                    AudioStartMs = table.Column<int>(type: "integer", nullable: true),
                    AudioEndMs = table.Column<int>(type: "integer", nullable: true),
                    ReplayInLearningOnly = table.Column<bool>(type: "boolean", nullable: false),
                    TranscriptSegmentsJson = table.Column<string>(type: "text", nullable: false),
                    TopicCsv = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DifficultyRating = table.Column<int>(type: "integer", nullable: true),
                    AudioContentSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TtsVoice = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TtsModelVariant = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningExtracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningExtracts_ListeningParts_ListeningPartId",
                        column: x => x.ListeningPartId,
                        principalTable: "ListeningParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContentPaperAssets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Part = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPaperAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentPaperAssets_ContentPapers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "ContentPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentPaperAssets_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResultTemplateAssets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResultTemplateAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResultTemplateAssets_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RulebookVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Profession = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AuthoritySource = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true),
                    ReferencePdfAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RulebookVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RulebookVersions_MediaAssets_ReferencePdfAssetId",
                        column: x => x.ReferencePdfAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingSharedResources",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UploadedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingSharedResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingSharedResources_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WritingAttemptAssets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssetKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    ExtractionState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExtractedText = table.Column<string>(type: "text", nullable: false),
                    ExtractionProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExtractionReasonCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExtractionMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExtractedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingAttemptAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WritingAttemptAssets_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WritingAttemptAssets_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MockAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockBundleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MockType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Profession = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewSelection = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StrictTimer = table.Column<bool>(type: "boolean", nullable: false),
                    ReservedReviewCredits = table.Column<int>(type: "integer", nullable: false),
                    DeliveryMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Strictness = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RandomisationSeed = table.Column<long>(type: "bigint", nullable: true),
                    ConfigJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReportId = table.Column<string>(type: "text", nullable: true),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockAttempts_MockBundles_MockBundleId",
                        column: x => x.MockBundleId,
                        principalTable: "MockBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MockBundleSections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockBundleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SectionOrder = table.Column<int>(type: "integer", nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentPaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TimeLimitMinutes = table.Column<int>(type: "integer", nullable: false),
                    ReviewEligible = table.Column<bool>(type: "boolean", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    ModelAnswerReleasePolicy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockBundleSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockBundleSections_ContentPapers_ContentPaperId",
                        column: x => x.ContentPaperId,
                        principalTable: "ContentPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MockBundleSections_MockBundles_MockBundleId",
                        column: x => x.MockBundleId,
                        principalTable: "MockBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewVoiceNotes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UploadedByReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    TranscriptText = table.Column<string>(type: "text", nullable: false),
                    WrittenNotes = table.Column<string>(type: "text", nullable: false),
                    RubricJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewVoiceNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewVoiceNotes_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewVoiceNotes_ReviewRequests_ReviewRequestId",
                        column: x => x.ReviewRequestId,
                        principalTable: "ReviewRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeacherClassMembers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TeacherClassId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherClassMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherClassMembers_TeacherClasses_TeacherClassId",
                        column: x => x.TeacherClassId,
                        principalTable: "TeacherClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrivateSpeakingTutorProfiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Bio = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PriceOverrideMinorUnits = table.Column<int>(type: "integer", nullable: true),
                    SlotDurationOverrideMinutes = table.Column<int>(type: "integer", nullable: true),
                    SpecialtiesJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TotalSessions = table.Column<int>(type: "integer", nullable: false),
                    AverageRating = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateSpeakingTutorProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrivateSpeakingTutorProfiles_ExpertUsers_ExpertUserId",
                        column: x => x.ExpertUserId,
                        principalTable: "ExpertUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearnerRegistrationProfiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApplicationUserAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExamTypeId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CountryTarget = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MobileNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AgreeToTerms = table.Column<bool>(type: "boolean", nullable: false),
                    AgreeToPrivacy = table.Column<bool>(type: "boolean", nullable: false),
                    MarketingOptIn = table.Column<bool>(type: "boolean", nullable: false),
                    UtmSource = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UtmMedium = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UtmCampaign = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UtmTerm = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UtmContent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReferrerUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LandingPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerRegistrationProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearnerRegistrationProfiles_ApplicationUserAccounts_Applica~",
                        column: x => x.ApplicationUserAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LearnerRegistrationProfiles_Users_LearnerUserId",
                        column: x => x.LearnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tutors",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayNameAr = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Bio = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    BioAr = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SpecialtiesJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    LanguagesJson = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    HourlyRateUsd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ZoomUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tutors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tutors_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CartItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingPriceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CartItems_BillingPrices_BillingPriceId",
                        column: x => x.BillingPriceId,
                        principalTable: "BillingPrices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CartItems_BillingProducts_BillingProductId",
                        column: x => x.BillingProductId,
                        principalTable: "BillingProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CartItems_Carts_CartId",
                        column: x => x.CartId,
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InterlocutorScripts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RolePlayCardId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OpeningResponse = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Prompt1 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Prompt2 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Prompt3 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    HiddenInformation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ResistanceLevel = table.Column<int>(type: "integer", nullable: false),
                    ClosingCue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EmotionalState = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProfessionRoleNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LayLanguageTriggersJson = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterlocutorScripts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterlocutorScripts_RolePlayCards_RolePlayCardId",
                        column: x => x.RolePlayCardId,
                        principalTable: "RolePlayCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RolePlayCardId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockSetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MockSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    InterlocutorActorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LiveRoomId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    WarmupStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WarmupEndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PrepStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RolePlayStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ElapsedSeconds = table.Column<int>(type: "integer", nullable: false),
                    ConsentVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RulebookVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ConsentAcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PaperDestroyedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecommendedDrillIdsJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingSessions_RolePlayCards_RolePlayCardId",
                        column: x => x.RolePlayCardId,
                        principalTable: "RolePlayCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingDrillAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DrillItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    AudioRecordingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TranscriptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AiFeedbackJson = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingDrillAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingDrillAttempts_SpeakingDrillItems_DrillItemId",
                        column: x => x.DrillItemId,
                        principalTable: "SpeakingDrillItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingTexts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingPartId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BodyHtml = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false),
                    TopicTag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingTexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingTexts_ReadingParts_ReadingPartId",
                        column: x => x.ReadingPartId,
                        principalTable: "ReadingParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ListeningQuestions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningPartId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningExtractId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    QuestionNumber = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    QuestionType = table.Column<int>(type: "integer", nullable: false),
                    Stem = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CorrectAnswerJson = table.Column<string>(type: "text", nullable: false),
                    AcceptedSynonymsJson = table.Column<string>(type: "text", nullable: true),
                    CaseSensitive = table.Column<bool>(type: "boolean", nullable: false),
                    ExplanationMarkdown = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    SkillTag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubSkillTagsCsv = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Accent = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    TranscriptEvidenceText = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TranscriptEvidenceStartMs = table.Column<int>(type: "integer", nullable: true),
                    TranscriptEvidenceEndMs = table.Column<int>(type: "integer", nullable: true),
                    SpeakerAttitude = table.Column<int>(type: "integer", nullable: true),
                    DifficultyLevel = table.Column<int>(type: "integer", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningQuestions_ListeningExtracts_ListeningExtractId",
                        column: x => x.ListeningExtractId,
                        principalTable: "ListeningExtracts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ListeningQuestions_ListeningParts_ListeningPartId",
                        column: x => x.ListeningPartId,
                        principalTable: "ListeningParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RulebookRuleRows",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RulebookVersionId = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    SectionCode = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    AppliesToJson = table.Column<string>(type: "text", nullable: false),
                    TurnStage = table.Column<string>(type: "text", nullable: true),
                    ExemplarPhrasesJson = table.Column<string>(type: "text", nullable: true),
                    ForbiddenPatternsJson = table.Column<string>(type: "text", nullable: true),
                    CheckId = table.Column<string>(type: "text", nullable: true),
                    ParamsJson = table.Column<string>(type: "text", nullable: true),
                    ExamplesJson = table.Column<string>(type: "text", nullable: true),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RulebookRuleRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RulebookRuleRows_RulebookVersions_RulebookVersionId",
                        column: x => x.RulebookVersionId,
                        principalTable: "RulebookVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RulebookSectionRows",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RulebookVersionId = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RulebookSectionRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RulebookSectionRows_RulebookVersions_RulebookVersionId",
                        column: x => x.RulebookVersionId,
                        principalTable: "RulebookVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MockBookings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockBundleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ScheduledStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimezoneIana = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssignedTutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AssignedInterlocutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RescheduleCount = table.Column<int>(type: "integer", nullable: false),
                    ConsentToRecording = table.Column<bool>(type: "boolean", nullable: false),
                    DeliveryMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LiveRoomState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LiveRoomTransitionVersion = table.Column<int>(type: "integer", nullable: false),
                    ZoomMeetingId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ZoomJoinUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomStartUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomMeetingPassword = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LearnerNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordingManifestJson = table.Column<string>(type: "text", nullable: true),
                    RecordingDurationMs = table.Column<long>(type: "bigint", nullable: true),
                    RecordingFinalizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockBookings_MockAttempts_MockAttemptId",
                        column: x => x.MockAttemptId,
                        principalTable: "MockAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MockBookings_MockBundles_MockBundleId",
                        column: x => x.MockBundleId,
                        principalTable: "MockBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MockContentReviews",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockBundleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MockAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReportedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReviewType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockContentReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockContentReviews_MockAttempts_MockAttemptId",
                        column: x => x.MockAttemptId,
                        principalTable: "MockAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MockContentReviews_MockBundles_MockBundleId",
                        column: x => x.MockBundleId,
                        principalTable: "MockBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MockReviewReservations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WalletId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ReservedCredits = table.Column<int>(type: "integer", nullable: false),
                    ConsumedCredits = table.Column<int>(type: "integer", nullable: false),
                    ReleasedCredits = table.Column<int>(type: "integer", nullable: false),
                    Selection = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DebitTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReleaseTransactionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockReviewReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockReviewReservations_MockAttempts_MockAttemptId",
                        column: x => x.MockAttemptId,
                        principalTable: "MockAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MockSectionAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockBundleSectionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ContentPaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LaunchRoute = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContentAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RawScore = table.Column<int>(type: "integer", nullable: true),
                    RawScoreMax = table.Column<int>(type: "integer", nullable: true),
                    ScaledScore = table.Column<int>(type: "integer", nullable: true),
                    Grade = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    FeedbackJson = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeadlineAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockSectionAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockSectionAttempts_MockAttempts_MockAttemptId",
                        column: x => x.MockAttemptId,
                        principalTable: "MockAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MockSectionAttempts_MockBundleSections_MockBundleSectionId",
                        column: x => x.MockBundleSectionId,
                        principalTable: "MockBundleSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LiveClasses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    Description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    DescriptionAr = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ProfessionTrack = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TutorProfileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TutorDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DefaultDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    DefaultCapacity = table.Column<int>(type: "integer", nullable: false),
                    CreditCost = table.Column<int>(type: "integer", nullable: false),
                    PriceUsd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false),
                    RecurrenceJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CoverImageUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TagsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveClasses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveClasses_PrivateSpeakingTutorProfiles_TutorProfileId",
                        column: x => x.TutorProfileId,
                        principalTable: "PrivateSpeakingTutorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PrivateSpeakingAvailabilityOverrides",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TutorProfileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    OverrideType = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    EndTime = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateSpeakingAvailabilityOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrivateSpeakingAvailabilityOverrides_PrivateSpeakingTutorPr~",
                        column: x => x.TutorProfileId,
                        principalTable: "PrivateSpeakingTutorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrivateSpeakingAvailabilityRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TutorProfileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    EndTime = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateSpeakingAvailabilityRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrivateSpeakingAvailabilityRules_PrivateSpeakingTutorProfil~",
                        column: x => x.TutorProfileId,
                        principalTable: "PrivateSpeakingTutorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrivateSpeakingBookings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TutorProfileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SessionStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    TutorTimezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerTimezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PriceMinorUnits = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    StripeCheckoutSessionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StripePaymentIntentId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PaymentStatus = table.Column<int>(type: "integer", nullable: false),
                    PaymentConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ZoomMeetingId = table.Column<long>(type: "bigint", nullable: true),
                    ZoomJoinUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomStartUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomMeetingPassword = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ZoomStatus = table.Column<int>(type: "integer", nullable: false),
                    ZoomError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomRetryCount = table.Column<int>(type: "integer", nullable: false),
                    ReservationExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LearnerNotes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TutorNotes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LearnerRating = table.Column<int>(type: "integer", nullable: true),
                    LearnerFeedback = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CancelledBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RescheduledFromBookingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RemindersSentJson = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DailyReminderSent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateSpeakingBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrivateSpeakingBookings_PrivateSpeakingTutorProfiles_TutorP~",
                        column: x => x.TutorProfileId,
                        principalTable: "PrivateSpeakingTutorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TutorAvailabilities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorAvailabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TutorAvailabilities_Tutors_TutorId",
                        column: x => x.TutorId,
                        principalTable: "Tutors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingAiAssessments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SpeakingSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TranscriptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModelId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    PromptTemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Intelligibility = table.Column<int>(type: "integer", nullable: false),
                    Fluency = table.Column<int>(type: "integer", nullable: false),
                    Appropriateness = table.Column<int>(type: "integer", nullable: false),
                    GrammarExpression = table.Column<int>(type: "integer", nullable: false),
                    RelationshipBuilding = table.Column<int>(type: "integer", nullable: false),
                    PatientPerspective = table.Column<int>(type: "integer", nullable: false),
                    Structure = table.Column<int>(type: "integer", nullable: false),
                    InformationGathering = table.Column<int>(type: "integer", nullable: false),
                    InformationGiving = table.Column<int>(type: "integer", nullable: false),
                    EstimatedScaledScore = table.Column<int>(type: "integer", nullable: false),
                    ReadinessBand = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PerCriterionRationalesJson = table.Column<string>(type: "text", nullable: false),
                    OverallSummary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ConfidenceBand = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RulebookFindingsJson = table.Column<string>(type: "text", nullable: false),
                    IsAdvisory = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingAiAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingAiAssessments_SpeakingSessions_SpeakingSessionId",
                        column: x => x.SpeakingSessionId,
                        principalTable: "SpeakingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingLiveRooms",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SpeakingSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RoomName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LearnerIdentity = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    TutorIdentity = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    LiveKitRoomSid = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    ScheduledStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActualStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualEndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    EgressId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    EgressOutputUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MaxDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    RecordingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RecordingConsentVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WebhookEventsJson = table.Column<string>(type: "text", nullable: false),
                    BookingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingLiveRooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingLiveRooms_SpeakingSessions_SpeakingSessionId",
                        column: x => x.SpeakingSessionId,
                        principalTable: "SpeakingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingRecordings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SpeakingSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    ConsentVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    RetentionExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EgressTrackId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsWarmup = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingRecordings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingRecordings_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpeakingRecordings_SpeakingSessions_SpeakingSessionId",
                        column: x => x.SpeakingSessionId,
                        principalTable: "SpeakingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingTimestampedComments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SpeakingSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorRole = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TranscriptSegmentIndex = table.Column<int>(type: "integer", nullable: false),
                    StartMs = table.Column<int>(type: "integer", nullable: false),
                    EndMs = table.Column<int>(type: "integer", nullable: false),
                    CriterionCode = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    LinkedRulebookEntryCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LinkedDrillId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingTimestampedComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingTimestampedComments_SpeakingSessions_SpeakingSessio~",
                        column: x => x.SpeakingSessionId,
                        principalTable: "SpeakingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingTranscripts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SpeakingSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    SegmentsJson = table.Column<string>(type: "text", nullable: false),
                    IsLatest = table.Column<bool>(type: "boolean", nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false),
                    MeanConfidence = table.Column<double>(type: "double precision", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingTranscripts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingTranscripts_SpeakingSessions_SpeakingSessionId",
                        column: x => x.SpeakingSessionId,
                        principalTable: "SpeakingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingTutorAssessments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SpeakingSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Intelligibility = table.Column<int>(type: "integer", nullable: false),
                    Fluency = table.Column<int>(type: "integer", nullable: false),
                    Appropriateness = table.Column<int>(type: "integer", nullable: false),
                    GrammarExpression = table.Column<int>(type: "integer", nullable: false),
                    RelationshipBuilding = table.Column<int>(type: "integer", nullable: false),
                    PatientPerspective = table.Column<int>(type: "integer", nullable: false),
                    Structure = table.Column<int>(type: "integer", nullable: false),
                    InformationGathering = table.Column<int>(type: "integer", nullable: false),
                    InformationGiving = table.Column<int>(type: "integer", nullable: false),
                    EstimatedScaledScore = table.Column<int>(type: "integer", nullable: false),
                    ReadinessBand = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OverallFeedbackMarkdown = table.Column<string>(type: "text", nullable: false),
                    StrengthsJson = table.Column<string>(type: "text", nullable: false),
                    ImprovementsJson = table.Column<string>(type: "text", nullable: false),
                    RecommendedDrillsJson = table.Column<string>(type: "text", nullable: false),
                    RecommendedRulebookEntries = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsFinal = table.Column<bool>(type: "boolean", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MarkingDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    CalibrationDeltaJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingTutorAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingTutorAssessments_SpeakingSessions_SpeakingSessionId",
                        column: x => x.SpeakingSessionId,
                        principalTable: "SpeakingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingQuestions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingPartId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingTextId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    QuestionType = table.Column<int>(type: "integer", nullable: false),
                    Stem = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    OptionsJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    ParagraphIndex = table.Column<int>(type: "integer", nullable: true),
                    CorrectAnswerJson = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AcceptedSynonymsJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    CaseSensitive = table.Column<bool>(type: "boolean", nullable: false),
                    ExplanationMarkdown = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    SkillTag = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    OptionDistractorsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ReviewState = table.Column<int>(type: "integer", nullable: false),
                    LatestReviewNote = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingQuestions_ReadingParts_ReadingPartId",
                        column: x => x.ReadingPartId,
                        principalTable: "ReadingParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReadingQuestions_ReadingTexts_ReadingTextId",
                        column: x => x.ReadingTextId,
                        principalTable: "ReadingTexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ListeningAnswers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningQuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserAnswerJson = table.Column<string>(type: "text", nullable: false),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: true),
                    PointsEarned = table.Column<int>(type: "integer", nullable: false),
                    SelectedDistractorCategory = table.Column<int>(type: "integer", nullable: true),
                    MissReason = table.Column<int>(type: "integer", nullable: true),
                    QuestionVersionSnapshot = table.Column<int>(type: "integer", nullable: true),
                    OptionVersionSnapshot = table.Column<int>(type: "integer", nullable: true),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningAnswers_ListeningAttempts_ListeningAttemptId",
                        column: x => x.ListeningAttemptId,
                        principalTable: "ListeningAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ListeningAnswers_ListeningQuestions_ListeningQuestionId",
                        column: x => x.ListeningQuestionId,
                        principalTable: "ListeningQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ListeningQuestionOptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningQuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OptionKey = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    DistractorCategory = table.Column<int>(type: "integer", nullable: true),
                    WhyWrongMarkdown = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningQuestionOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningQuestionOptions_ListeningQuestions_ListeningQuesti~",
                        column: x => x.ListeningQuestionId,
                        principalTable: "ListeningQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MockLiveRoomTransitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BookingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FromState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ToState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ClientTransitionId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    TransitionVersion = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockLiveRoomTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockLiveRoomTransitions_MockBookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "MockBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MockProctoringEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockSectionAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Kind = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockProctoringEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockProctoringEvents_MockAttempts_MockAttemptId",
                        column: x => x.MockAttemptId,
                        principalTable: "MockAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MockProctoringEvents_MockSectionAttempts_MockSectionAttempt~",
                        column: x => x.MockSectionAttemptId,
                        principalTable: "MockSectionAttempts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LiveClassSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LiveClassId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScheduledStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ScheduledEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    EnrolledCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ZoomMeetingId = table.Column<long>(type: "bigint", nullable: true),
                    ZoomMeetingNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ZoomJoinUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomStartUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomPasscode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ZoomError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomRetryCount = table.Column<int>(type: "integer", nullable: false),
                    ActualStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    RecordingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveClassSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveClassSessions_LiveClasses_LiveClassId",
                        column: x => x.LiveClassId,
                        principalTable: "LiveClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingLiveRoomTokens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LiveRoomId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Identity = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Capabilities = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingLiveRoomTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingLiveRoomTokens_SpeakingLiveRooms_LiveRoomId",
                        column: x => x.LiveRoomId,
                        principalTable: "SpeakingLiveRooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingAnswers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingQuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserAnswerJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: true),
                    PointsEarned = table.Column<int>(type: "integer", nullable: false),
                    SelectedDistractorCategory = table.Column<int>(type: "integer", nullable: true),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ElapsedMs = table.Column<int>(type: "integer", nullable: true),
                    TotalElapsedMs = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingAnswers_ReadingAttempts_ReadingAttemptId",
                        column: x => x.ReadingAttemptId,
                        principalTable: "ReadingAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReadingAnswers_ReadingQuestions_ReadingQuestionId",
                        column: x => x.ReadingQuestionId,
                        principalTable: "ReadingQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReadingQuestionReviewLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingQuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FromState = table.Column<int>(type: "integer", nullable: false),
                    ToState = table.Column<int>(type: "integer", nullable: false),
                    ReviewerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TransitionedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingQuestionReviewLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingQuestionReviewLogs_ReadingQuestions_ReadingQuestionId",
                        column: x => x.ReadingQuestionId,
                        principalTable: "ReadingQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LiveClassAttendances",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EnrollmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeftAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    ZoomParticipantUuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReceivedRecordingAccess = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveClassAttendances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveClassAttendances_LiveClassSessions_ClassSessionId",
                        column: x => x.ClassSessionId,
                        principalTable: "LiveClassSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LiveClassEnrollments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EnrolledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreditsCharged = table.Column<int>(type: "integer", nullable: false),
                    WalletTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefundWalletTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveClassEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveClassEnrollments_LiveClassSessions_ClassSessionId",
                        column: x => x.ClassSessionId,
                        principalTable: "LiveClassSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LiveClassRecordings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ZoomRecordingId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    S3VideoKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    S3AudioKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    S3TranscriptKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TranscriptText = table.Column<string>(type: "text", nullable: true),
                    AiSummary = table.Column<string>(type: "text", nullable: true),
                    AiSummaryAr = table.Column<string>(type: "text", nullable: true),
                    ChaptersJson = table.Column<string>(type: "text", nullable: false),
                    ActionItemsJson = table.Column<string>(type: "text", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveClassRecordings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveClassRecordings_LiveClassSessions_ClassSessionId",
                        column: x => x.ClassSessionId,
                        principalTable: "LiveClassSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClassRecordingEmbeddings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassRecordingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    ChunkText = table.Column<string>(type: "text", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "text", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    EndTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassRecordingEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassRecordingEmbeddings_LiveClassRecordings_ClassRecording~",
                        column: x => x.ClassRecordingId,
                        principalTable: "LiveClassRecordings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountFreezeEntitlements_UserId",
                table: "AccountFreezeEntitlements",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountFreezePolicies_Version",
                table: "AccountFreezePolicies",
                column: "Version");

            migrationBuilder.CreateIndex(
                name: "IX_AccountFreezeRecords_Status_EndedAt",
                table: "AccountFreezeRecords",
                columns: new[] { "Status", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountFreezeRecords_Status_ScheduledStartAt",
                table: "AccountFreezeRecords",
                columns: new[] { "Status", "ScheduledStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountFreezeRecords_UserId",
                table: "AccountFreezeRecords",
                column: "UserId",
                unique: true,
                filter: "\"IsCurrent\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_AccountFreezeRecords_UserId_Status",
                table: "AccountFreezeRecords",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_Status_SortOrder",
                table: "Achievements",
                columns: new[] { "Status", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminPermissionGrants_AdminUserId_Permission",
                table: "AdminPermissionGrants",
                columns: new[] { "AdminUserId", "Permission" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdminUploadSessions_AdminUserId_ExpiresAt",
                table: "AdminUploadSessions",
                columns: new[] { "AdminUserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminUploadSessions_State_ExpiresAt",
                table: "AdminUploadSessions",
                columns: new[] { "State", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAttributions_AffiliateId_ConvertedAt",
                table: "AffiliateAttributions",
                columns: new[] { "AffiliateId", "ConvertedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAttributions_UserId_AffiliateId",
                table: "AffiliateAttributions",
                columns: new[] { "UserId", "AffiliateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommissions_AffiliateId_Status",
                table: "AffiliateCommissions",
                columns: new[] { "AffiliateId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommissions_PaymentTransactionId",
                table: "AffiliateCommissions",
                column: "PaymentTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Affiliates_Code",
                table: "Affiliates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Affiliates_Status",
                table: "Affiliates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantMessages_ThreadId_CreatedAt",
                table: "AiAssistantMessages",
                columns: new[] { "ThreadId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantThreads_UserId_Role",
                table: "AiAssistantThreads",
                columns: new[] { "UserId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantThreads_UserId_UpdatedAt",
                table: "AiAssistantThreads",
                columns: new[] { "UserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiCodebaseChunks_FilePath",
                table: "AiCodebaseChunks",
                column: "FilePath");

            migrationBuilder.CreateIndex(
                name: "IX_AiCodebaseChunks_Language",
                table: "AiCodebaseChunks",
                column: "Language");

            migrationBuilder.CreateIndex(
                name: "IX_AIConfigVersions_TaskType_Status",
                table: "AIConfigVersions",
                columns: new[] { "TaskType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AiCreditLedger_ExpiresAt",
                table: "AiCreditLedger",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiCreditLedger_UserId_CreatedAt",
                table: "AiCreditLedger",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_AiCreditLedger_Expiration_ReferenceId",
                table: "AiCreditLedger",
                columns: new[] { "Source", "ReferenceId" },
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL AND \"Source\" = 5");

            migrationBuilder.CreateIndex(
                name: "UX_AiCreditLedger_PlanRenewal_ReferenceId",
                table: "AiCreditLedger",
                column: "ReferenceId",
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL AND \"Source\" = 0");

            migrationBuilder.CreateIndex(
                name: "UX_AiCreditLedger_Purchase_ReferenceId",
                table: "AiCreditLedger",
                columns: new[] { "UserId", "ReferenceId", "Source" },
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL AND \"Source\" = 2");

            migrationBuilder.CreateIndex(
                name: "UX_AiCreditLedger_RefundAdjustment_ReferenceId",
                table: "AiCreditLedger",
                columns: new[] { "Source", "UserId", "ReferenceId" },
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL AND \"Source\" = 3 AND (\"ReferenceId\" LIKE 'addon-refund:%' OR \"ReferenceId\" LIKE 'plan-refund:%')");

            migrationBuilder.CreateIndex(
                name: "UX_AiCreditLedger_UsageDebit_ReferenceId",
                table: "AiCreditLedger",
                columns: new[] { "ReferenceId", "Source" },
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL AND \"Source\" = 4");

            migrationBuilder.CreateIndex(
                name: "IX_AiFeatureRoutes_FeatureCode",
                table: "AiFeatureRoutes",
                column: "FeatureCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiFeatureToolGrants_FeatureCode",
                table: "AiFeatureToolGrants",
                column: "FeatureCode");

            migrationBuilder.CreateIndex(
                name: "IX_AiFeatureToolGrants_FeatureCode_ToolCode",
                table: "AiFeatureToolGrants",
                columns: new[] { "FeatureCode", "ToolCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiFileBackups_FilePath_CreatedAt",
                table: "AiFileBackups",
                columns: new[] { "FilePath", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiFileBackups_ThreadId_CreatedAt",
                table: "AiFileBackups",
                columns: new[] { "ThreadId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderAccounts_ProviderId_IsActive",
                table: "AiProviderAccounts",
                columns: new[] { "ProviderId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderAccounts_ProviderId_Priority",
                table: "AiProviderAccounts",
                columns: new[] { "ProviderId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_AiProviders_Code",
                table: "AiProviders",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiQuotaCounters_UserId_PeriodKey",
                table: "AiQuotaCounters",
                columns: new[] { "UserId", "PeriodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiQuotaPlans_Code",
                table: "AiQuotaPlans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiToolInvocations_AiUsageRecordId_TurnIndex",
                table: "AiToolInvocations",
                columns: new[] { "AiUsageRecordId", "TurnIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_AiToolInvocations_FeatureCode_CreatedAt",
                table: "AiToolInvocations",
                columns: new[] { "FeatureCode", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiToolInvocations_ToolCode_CreatedAt",
                table: "AiToolInvocations",
                columns: new[] { "ToolCode", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiTools_Code",
                table: "AiTools",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_AccountId_CreatedAt",
                table: "AiUsageRecords",
                columns: new[] { "AccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_AuthAccountId",
                table: "AiUsageRecords",
                column: "AuthAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_CreatedAt",
                table: "AiUsageRecords",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_FeatureCode_CreatedAt",
                table: "AiUsageRecords",
                columns: new[] { "FeatureCode", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_ProviderId_CreatedAt",
                table: "AiUsageRecords",
                columns: new[] { "ProviderId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_UserId_CreatedAt",
                table: "AiUsageRecords",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_EventName_OccurredAt",
                table: "AnalyticsEvents",
                columns: new[] { "EventName", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_UserId_EventName_OccurredAt",
                table: "AnalyticsEvents",
                columns: new[] { "UserId", "EventName", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUserAccounts_DeletedAt",
                table: "ApplicationUserAccounts",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUserAccounts_LastLoginAt",
                table: "ApplicationUserAccounts",
                column: "LastLoginAt");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUserAccounts_NormalizedEmail",
                table: "ApplicationUserAccounts",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppliedPromoCodes_CartId",
                table: "AppliedPromoCodes",
                column: "CartId");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_ContentId",
                table: "Attempts",
                column: "ContentId");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_UserId_SubtestCode_State",
                table: "Attempts",
                columns: new[] { "UserId", "SubtestCode", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorAuthAccountId",
                table: "AuditEvents",
                column: "ActorAuthAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorId",
                table: "AuditEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredAt",
                table: "AuditEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ResourceType_ResourceId",
                table: "AuditEvents",
                columns: new[] { "ResourceType", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJobs_State_AvailableAt",
                table: "BackgroundJobs",
                columns: new[] { "State", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccountConfigs_Region_Currency_IsActive",
                table: "BankAccountConfigs",
                columns: new[] { "Region", "Currency", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingAddOns_Code",
                table: "BillingAddOns",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingAddOns_Status_DisplayOrder",
                table: "BillingAddOns",
                columns: new[] { "Status", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingAddOnVersions_AddOnId_VersionNumber",
                table: "BillingAddOnVersions",
                columns: new[] { "AddOnId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingAddOnVersions_Code",
                table: "BillingAddOnVersions",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_BillingCouponRedemptions_CouponCode_UserId_RedeemedAt",
                table: "BillingCouponRedemptions",
                columns: new[] { "CouponCode", "UserId", "RedeemedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCouponRedemptions_CouponId_UserId_RedeemedAt",
                table: "BillingCouponRedemptions",
                columns: new[] { "CouponId", "UserId", "RedeemedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCouponRedemptions_CouponVersionId",
                table: "BillingCouponRedemptions",
                column: "CouponVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingCoupons_Code",
                table: "BillingCoupons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingCoupons_Status_EndsAt",
                table: "BillingCoupons",
                columns: new[] { "Status", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCouponVersions_Code",
                table: "BillingCouponVersions",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_BillingCouponVersions_CouponId_VersionNumber",
                table: "BillingCouponVersions",
                columns: new[] { "CouponId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingEvents_EntityType_EntityId_OccurredAt",
                table: "BillingEvents",
                columns: new[] { "EntityType", "EntityId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingEvents_UserId_OccurredAt",
                table: "BillingEvents",
                columns: new[] { "UserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingMetricDailies_MetricCode_MetricDate",
                table: "BillingMetricDailies",
                columns: new[] { "MetricCode", "MetricDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingMetricDailies_MetricDate_MetricCode_Region",
                table: "BillingMetricDailies",
                columns: new[] { "MetricDate", "MetricCode", "Region" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingNotificationDispatchLogs_UserId_EventCode_EventId_Te~",
                table: "BillingNotificationDispatchLogs",
                columns: new[] { "UserId", "EventCode", "EventId", "TemplateCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingNotificationDispatchLogs_UserId_SentAt",
                table: "BillingNotificationDispatchLogs",
                columns: new[] { "UserId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingNotificationTemplates_Code_Channel_LocaleTag",
                table: "BillingNotificationTemplates",
                columns: new[] { "Code", "Channel", "LocaleTag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingPlans_Code",
                table: "BillingPlans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingPlans_Status_DisplayOrder",
                table: "BillingPlans",
                columns: new[] { "Status", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPlanVersions_Code",
                table: "BillingPlanVersions",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_BillingPlanVersions_PlanId_VersionNumber",
                table: "BillingPlanVersions",
                columns: new[] { "PlanId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingPrices_BillingProductId",
                table: "BillingPrices",
                column: "BillingProductId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingPrices_StripePriceId",
                table: "BillingPrices",
                column: "StripePriceId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingProducts_Code",
                table: "BillingProducts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingQuotes_CouponVersionId",
                table: "BillingQuotes",
                column: "CouponVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingQuotes_PlanVersionId",
                table: "BillingQuotes",
                column: "PlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingQuotes_Status_ExpiresAt",
                table: "BillingQuotes",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingQuotes_UserId_CreatedAt",
                table: "BillingQuotes",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationIntents_SubscriptionId_Status",
                table: "CancellationIntents",
                columns: new[] { "SubscriptionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_BillingPriceId",
                table: "CartItems",
                column: "BillingPriceId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_BillingProductId",
                table: "CartItems",
                column: "BillingProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_CartId",
                table: "CartItems",
                column: "CartId");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_SessionToken",
                table: "Carts",
                column: "SessionToken");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_Status",
                table: "Carts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_UserId",
                table: "Carts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_UserId",
                table: "Certificates",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_VerificationCode",
                table: "Certificates",
                column: "VerificationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_IdempotencyKey",
                table: "CheckoutSessions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_StripeSessionId",
                table: "CheckoutSessions",
                column: "StripeSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_UserId",
                table: "CheckoutSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChurnRiskSnapshots_RiskBand_RiskScore",
                table: "ChurnRiskSnapshots",
                columns: new[] { "RiskBand", "RiskScore" });

            migrationBuilder.CreateIndex(
                name: "IX_ChurnRiskSnapshots_SnapshotDate_RiskBand",
                table: "ChurnRiskSnapshots",
                columns: new[] { "SnapshotDate", "RiskBand" });

            migrationBuilder.CreateIndex(
                name: "IX_ChurnRiskSnapshots_UserId_SnapshotDate",
                table: "ChurnRiskSnapshots",
                columns: new[] { "UserId", "SnapshotDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassFeedbacks_ClassSessionId",
                table: "ClassFeedbacks",
                column: "ClassSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassFeedbacks_ClassSessionId_UserId",
                table: "ClassFeedbacks",
                columns: new[] { "ClassSessionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassFeedbacks_UserId",
                table: "ClassFeedbacks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassMaterials_ClassSessionId",
                table: "ClassMaterials",
                column: "ClassSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassMaterials_LiveClassId",
                table: "ClassMaterials",
                column: "LiveClassId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassMaterials_LiveClassId_ClassSessionId",
                table: "ClassMaterials",
                columns: new[] { "LiveClassId", "ClassSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassRecordingEmbeddings_ClassRecordingId",
                table: "ClassRecordingEmbeddings",
                column: "ClassRecordingId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassRecordingEmbeddings_ClassRecordingId_ChunkIndex",
                table: "ClassRecordingEmbeddings",
                columns: new[] { "ClassRecordingId", "ChunkIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CohortMembers_CohortId_LearnerId",
                table: "CohortMembers",
                columns: new[] { "CohortId", "LearnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentCohortOverlays_ProgramId_CohortCode",
                table: "ContentCohortOverlays",
                columns: new[] { "ProgramId", "CohortCode" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentGenerationJobs_RequestedBy",
                table: "ContentGenerationJobs",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ContentGenerationJobs_State_CreatedAt",
                table: "ContentGenerationJobs",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentImportBatches_CreatedBy",
                table: "ContentImportBatches",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ContentImportBatches_Status_CreatedAt",
                table: "ContentImportBatches",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_DuplicateGroupId",
                table: "ContentItems",
                column: "DuplicateGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_ImportBatchId",
                table: "ContentItems",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_InstructionLanguage",
                table: "ContentItems",
                column: "InstructionLanguage");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_IsPreviewEligible_Status",
                table: "ContentItems",
                columns: new[] { "IsPreviewEligible", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_SourceProvenance",
                table: "ContentItems",
                column: "SourceProvenance");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_SubtestCode_Status",
                table: "ContentItems",
                columns: new[] { "SubtestCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentLessons_ModuleId_DisplayOrder",
                table: "ContentLessons",
                columns: new[] { "ModuleId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentModules_TrackId_DisplayOrder",
                table: "ContentModules",
                columns: new[] { "TrackId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPackages_Code",
                table: "ContentPackages",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentPackages_Status_DisplayOrder",
                table: "ContentPackages",
                columns: new[] { "Status", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPaperAssets_MediaAssetId",
                table: "ContentPaperAssets",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPaperAssets_PaperId_Role",
                table: "ContentPaperAssets",
                columns: new[] { "PaperId", "Role" });

            migrationBuilder.CreateIndex(
                name: "UX_PaperAsset_Primary_Per_RolePart",
                table: "ContentPaperAssets",
                columns: new[] { "PaperId", "Role", "Part", "IsPrimary" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentPapers_CardType",
                table: "ContentPapers",
                column: "CardType");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPapers_LetterType",
                table: "ContentPapers",
                column: "LetterType");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPapers_ProfessionId_SubtestCode",
                table: "ContentPapers",
                columns: new[] { "ProfessionId", "SubtestCode" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPapers_Slug",
                table: "ContentPapers",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentPapers_SubtestCode_Status",
                table: "ContentPapers",
                columns: new[] { "SubtestCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPrograms_Code",
                table: "ContentPrograms",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentPrograms_ProgramType_InstructionLanguage",
                table: "ContentPrograms",
                columns: new[] { "ProgramType", "InstructionLanguage" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPrograms_Status_DisplayOrder",
                table: "ContentPrograms",
                columns: new[] { "Status", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPublishRequests_ContentItemId_Status",
                table: "ContentPublishRequests",
                columns: new[] { "ContentItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPublishRequests_RequestedBy",
                table: "ContentPublishRequests",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPublishRequests_Stage",
                table: "ContentPublishRequests",
                column: "Stage");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReferences_ModuleId_DisplayOrder",
                table: "ContentReferences",
                columns: new[] { "ModuleId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentRevisions_ContentItemId_RevisionNumber",
                table: "ContentRevisions",
                columns: new[] { "ContentItemId", "RevisionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentSubmissions_ContributorId_Status",
                table: "ContentSubmissions",
                columns: new[] { "ContributorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentTracks_ProgramId_DisplayOrder",
                table: "ContentTracks",
                columns: new[] { "ProgramId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationEvaluations_SessionId",
                table: "ConversationEvaluations",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationEvaluations_UserId_CreatedAt",
                table: "ConversationEvaluations",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSessionResumeTokens_ExpiresAt",
                table: "ConversationSessionResumeTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSessionResumeTokens_TokenHash",
                table: "ConversationSessionResumeTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSessionResumeTokens_UserId_SessionId",
                table: "ConversationSessionResumeTokens",
                columns: new[] { "UserId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSessions_TemplateId",
                table: "ConversationSessions",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSessions_UserId_CreatedAt",
                table: "ConversationSessions",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSessions_UserId_State",
                table: "ConversationSessions",
                columns: new[] { "UserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTemplates_Status_Difficulty",
                table: "ConversationTemplates",
                columns: new[] { "Status", "Difficulty" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTemplates_Status_TaskTypeCode_ProfessionId",
                table: "ConversationTemplates",
                columns: new[] { "Status", "TaskTypeCode", "ProfessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurnAnnotations_EvaluationId",
                table: "ConversationTurnAnnotations",
                column: "EvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurnAnnotations_SessionId_TurnNumber",
                table: "ConversationTurnAnnotations",
                columns: new[] { "SessionId", "TurnNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurns_SessionId_ProviderEventId",
                table: "ConversationTurns",
                columns: new[] { "SessionId", "ProviderEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurns_SessionId_TurnClientId",
                table: "ConversationTurns",
                columns: new[] { "SessionId", "TurnClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurns_SessionId_TurnNumber",
                table: "ConversationTurns",
                columns: new[] { "SessionId", "TurnNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_CrossSellRules_TriggerProductCode",
                table: "CrossSellRules",
                column: "TriggerProductCode");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerSubscriptions_StripeSubscriptionId",
                table: "CustomerSubscriptions",
                column: "StripeSubscriptionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerSubscriptions_UserId",
                table: "CustomerSubscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeflectionRules_TriggerReason_IsActive",
                table: "DeflectionRules",
                columns: new[] { "TriggerReason", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticSessions_UserId_State",
                table: "DiagnosticSessions",
                columns: new[] { "UserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_DictationDrills_DrillType_Accent_IsPublished",
                table: "DictationDrills",
                columns: new[] { "DrillType", "Accent", "IsPublished" });

            migrationBuilder.CreateIndex(
                name: "IX_DunningAttempts_InvoiceId_AttemptNumber",
                table: "DunningAttempts",
                columns: new[] { "InvoiceId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DunningAttempts_Outcome_ScheduledAt",
                table: "DunningAttempts",
                columns: new[] { "Outcome", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DunningAttempts_SubscriptionId_InvoiceId",
                table: "DunningAttempts",
                columns: new[] { "SubscriptionId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_DunningCampaigns_Status_NextAttemptAt",
                table: "DunningCampaigns",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DunningCampaigns_SubscriptionId_Status",
                table: "DunningCampaigns",
                columns: new[] { "SubscriptionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOtpChallenges_ApplicationUserAccountId_Purpose_Expires~",
                table: "EmailOtpChallenges",
                columns: new[] { "ApplicationUserAccountId", "Purpose", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOtpChallenges_ExpiresAt",
                table: "EmailOtpChallenges",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_AttemptId_State",
                table: "Evaluations",
                columns: new[] { "AttemptId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_State_LastTransitionAt",
                table: "Evaluations",
                columns: new[] { "State", "LastTransitionAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamBookings_UserId_ExamDate",
                table: "ExamBookings",
                columns: new[] { "UserId", "ExamDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamFamilies_IsActive_SortOrder",
                table: "ExamFamilies",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamTypes_Status_SortOrder",
                table: "ExamTypes",
                columns: new[] { "Status", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_EffectiveFrom",
                table: "ExchangeRates",
                column: "EffectiveFrom");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_FromCurrency_ToCurrency_EffectiveFrom",
                table: "ExchangeRates",
                columns: new[] { "FromCurrency", "ToCurrency", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpertAnnotationTemplates_CreatedByExpertId",
                table: "ExpertAnnotationTemplates",
                column: "CreatedByExpertId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertAvailabilities_ReviewerId",
                table: "ExpertAvailabilities",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertCalibrationResults_CalibrationCaseId_ReviewerId",
                table: "ExpertCalibrationResults",
                columns: new[] { "CalibrationCaseId", "ReviewerId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpertCompensationRates_ExpertId",
                table: "ExpertCompensationRates",
                column: "ExpertId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertEarnings_ExpertId",
                table: "ExpertEarnings",
                column: "ExpertId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertEarnings_ReviewRequestId",
                table: "ExpertEarnings",
                column: "ReviewRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertMessageReplies_ThreadId",
                table: "ExpertMessageReplies",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertMessageThreads_ExpertId",
                table: "ExpertMessageThreads",
                column: "ExpertId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertMetricSnapshots_ReviewerId_WindowStart",
                table: "ExpertMetricSnapshots",
                columns: new[] { "ReviewerId", "WindowStart" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpertPayouts_ExpertId",
                table: "ExpertPayouts",
                column: "ExpertId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertReviewAmends_ReviewRequestId",
                table: "ExpertReviewAmends",
                column: "ReviewRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertReviewAssignments_AssignedReviewerId",
                table: "ExpertReviewAssignments",
                column: "AssignedReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertReviewAssignments_ReviewRequestId_ClaimState",
                table: "ExpertReviewAssignments",
                columns: new[] { "ReviewRequestId", "ClaimState" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpertReviewDrafts_ReviewRequestId_ReviewerId",
                table: "ExpertReviewDrafts",
                columns: new[] { "ReviewRequestId", "ReviewerId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpertReviewerPayouts_PayPeriodStart_PayPeriodEnd",
                table: "ExpertReviewerPayouts",
                columns: new[] { "PayPeriodStart", "PayPeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpertReviewerPayouts_ReviewerId",
                table: "ExpertReviewerPayouts",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertReviewerPayouts_Status",
                table: "ExpertReviewerPayouts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertSlaSnapshots_ReviewRequestId",
                table: "ExpertSlaSnapshots",
                column: "ReviewRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertUsers_AuthAccountId",
                table: "ExpertUsers",
                column: "AuthAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentityLinks_ApplicationUserAccountId_Provider",
                table: "ExternalIdentityLinks",
                columns: new[] { "ApplicationUserAccountId", "Provider" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentityLinks_Provider_ProviderSubject",
                table: "ExternalIdentityLinks",
                columns: new[] { "Provider", "ProviderSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlags_Key",
                table: "FeatureFlags",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForumReplies_ThreadId_CreatedAt",
                table: "ForumReplies",
                columns: new[] { "ThreadId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ForumThreads_CategoryId_LastActivityAt",
                table: "ForumThreads",
                columns: new[] { "CategoryId", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FoundationResources_ResourceType_Status",
                table: "FoundationResources",
                columns: new[] { "ResourceType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FreePreviewAssets_Status_DisplayOrder",
                table: "FreePreviewAssets",
                columns: new[] { "Status", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayRoutingConfigs_Region_Currency_ProductType_GatewayNa~",
                table: "GatewayRoutingConfigs",
                columns: new[] { "Region", "Currency", "ProductType", "GatewayName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GatewayRoutingConfigs_Region_Currency_ProductType_Priority",
                table: "GatewayRoutingConfigs",
                columns: new[] { "Region", "Currency", "ProductType", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_Goals_UserId",
                table: "Goals",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_GrammarLessons_ExamTypeCode_Category_Status",
                table: "GrammarLessons",
                columns: new[] { "ExamTypeCode", "Category", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_Scope_Key",
                table: "IdempotencyRecords",
                columns: new[] { "Scope", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InterlocutorScripts_RolePlayCardId",
                table: "InterlocutorScripts",
                column: "RolePlayCardId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InterlocutorTrainingModules_Stage_Status",
                table: "InterlocutorTrainingModules",
                columns: new[] { "Stage", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InterlocutorTrainingProgress_ModuleId",
                table: "InterlocutorTrainingProgress",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_InterlocutorTrainingProgress_TutorId_ModuleId",
                table: "InterlocutorTrainingProgress",
                columns: new[] { "TutorId", "ModuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CheckoutSessionId",
                table: "Invoices",
                column: "CheckoutSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PlanVersionId",
                table: "Invoices",
                column: "PlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_QuoteId",
                table: "Invoices",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_UserId_IssuedAt",
                table: "Invoices",
                columns: new[] { "UserId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_UserId_Number",
                table: "Invoices",
                columns: new[] { "UserId", "Number" },
                unique: true,
                filter: "\"Number\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardEntries_ExamTypeCode_Period_PeriodStart_Rank",
                table: "LeaderboardEntries",
                columns: new[] { "ExamTypeCode", "Period", "PeriodStart", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardEntries_UserId_Period",
                table: "LeaderboardEntries",
                columns: new[] { "UserId", "Period" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerAccentProgresses_UserId_Accent",
                table: "LearnerAccentProgresses",
                columns: new[] { "UserId", "Accent" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerAchievements_UserId",
                table: "LearnerAchievements",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerAchievements_UserId_AchievementId",
                table: "LearnerAchievements",
                columns: new[] { "UserId", "AchievementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerBadges_UserId_BadgeCode",
                table: "LearnerBadges",
                columns: new[] { "UserId", "BadgeCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerCertificates_UserId",
                table: "LearnerCertificates",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerDictationProgresses_UserId_DictationDrillId",
                table: "LearnerDictationProgresses",
                columns: new[] { "UserId", "DictationDrillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerDictationProgresses_UserId_NextReviewAt",
                table: "LearnerDictationProgresses",
                columns: new[] { "UserId", "NextReviewAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerEscalations_SubmissionId",
                table: "LearnerEscalations",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerEscalations_UserId_Status",
                table: "LearnerEscalations",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerGrammarProgress_UserId_LessonId",
                table: "LearnerGrammarProgress",
                columns: new[] { "UserId", "LessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerLessonProgresses_UserId_LessonId",
                table: "LearnerLessonProgresses",
                columns: new[] { "UserId", "LessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerListeningLessonProgresses_UserId_LessonId",
                table: "LearnerListeningLessonProgresses",
                columns: new[] { "UserId", "LessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerListeningProfiles_UserId",
                table: "LearnerListeningProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerListeningSkillScores_UserId_SkillCode",
                table: "LearnerListeningSkillScores",
                columns: new[] { "UserId", "SkillCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerListeningStrategyProgresses_UserId_StrategyId",
                table: "LearnerListeningStrategyProgresses",
                columns: new[] { "UserId", "StrategyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPronunciationCards_UserId_NextReviewAt",
                table: "LearnerPronunciationCards",
                columns: new[] { "UserId", "NextReviewAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPronunciationCards_UserId_PronunciationCardId",
                table: "LearnerPronunciationCards",
                columns: new[] { "UserId", "PronunciationCardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPronunciationDiscriminationAttempts_UserId_CreatedAt",
                table: "LearnerPronunciationDiscriminationAttempts",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPronunciationProgress_UserId_AverageScore",
                table: "LearnerPronunciationProgress",
                columns: new[] { "UserId", "AverageScore" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPronunciationProgress_UserId_PhonemeCode",
                table: "LearnerPronunciationProgress",
                columns: new[] { "UserId", "PhonemeCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerReadingProfiles_UserId",
                table: "LearnerReadingProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerRegistrationProfiles_ApplicationUserAccountId",
                table: "LearnerRegistrationProfiles",
                column: "ApplicationUserAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerRegistrationProfiles_LearnerUserId",
                table: "LearnerRegistrationProfiles",
                column: "LearnerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerSkillProfiles_UserId_ExamTypeCode_SubtestCode",
                table: "LearnerSkillProfiles",
                columns: new[] { "UserId", "ExamTypeCode", "SubtestCode" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerSkillScores_UserId_SkillCode",
                table: "LearnerSkillScores",
                columns: new[] { "UserId", "SkillCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerStrategyProgress_UserId_StrategyGuideId",
                table: "LearnerStrategyProgress",
                columns: new[] { "UserId", "StrategyGuideId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerVideoProgress_UserId_VideoLessonId",
                table: "LearnerVideoProgress",
                columns: new[] { "UserId", "VideoLessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerVocabularies_UserId_NextReviewDate",
                table: "LearnerVocabularies",
                columns: new[] { "UserId", "NextReviewDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerVocabularies_UserId_Starred",
                table: "LearnerVocabularies",
                columns: new[] { "UserId", "Starred" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerVocabularies_UserId_TermId",
                table: "LearnerVocabularies",
                columns: new[] { "UserId", "TermId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerVocabularyItems_UserId_NextReviewAt",
                table: "LearnerVocabularyItems",
                columns: new[] { "UserId", "NextReviewAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerVocabularyItems_UserId_VocabularyWordId",
                table: "LearnerVocabularyItems",
                columns: new[] { "UserId", "VocabularyWordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerWritingLessonProgresses_UserId_LessonId",
                table: "LearnerWritingLessonProgresses",
                columns: new[] { "UserId", "LessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerWritingPathways_UserId",
                table: "LearnerWritingPathways",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerWritingProfiles_UserId",
                table: "LearnerWritingProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerXps_UserId",
                table: "LearnerXps",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListeningAnswers_ListeningQuestionId",
                table: "ListeningAnswers",
                column: "ListeningQuestionId");

            migrationBuilder.CreateIndex(
                name: "UX_ListeningAnswer_Attempt_Question",
                table: "ListeningAnswers",
                columns: new[] { "ListeningAttemptId", "ListeningQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListeningAttemptNotes_ListeningAttemptId",
                table: "ListeningAttemptNotes",
                column: "ListeningAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningAttempts_PaperId_StartedAt",
                table: "ListeningAttempts",
                columns: new[] { "PaperId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningAttempts_UserId_Status",
                table: "ListeningAttempts",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningDailyPlanItems_UserId_PlanDate",
                table: "ListeningDailyPlanItems",
                columns: new[] { "UserId", "PlanDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningExpertFeedbacks_AttemptId",
                table: "ListeningExpertFeedbacks",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningExtractionDrafts_PaperId_Status",
                table: "ListeningExtractionDrafts",
                columns: new[] { "PaperId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningExtractionDrafts_ProposedAt",
                table: "ListeningExtractionDrafts",
                column: "ProposedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningExtracts_ListeningPartId_DisplayOrder",
                table: "ListeningExtracts",
                columns: new[] { "ListeningPartId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningLessons_SkillCode_OrderIndex",
                table: "ListeningLessons",
                columns: new[] { "SkillCode", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningLessons_Slug",
                table: "ListeningLessons",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ListeningPart_Paper_PartCode",
                table: "ListeningParts",
                columns: new[] { "PaperId", "PartCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ListeningPathwayProgress_User_Stage",
                table: "ListeningPathwayProgress",
                columns: new[] { "UserId", "StageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListeningPracticeNotes_UserId_PracticeSessionId_ListeningQu~",
                table: "ListeningPracticeNotes",
                columns: new[] { "UserId", "PracticeSessionId", "ListeningQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningPracticeSessions_UserId_StartedAt",
                table: "ListeningPracticeSessions",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningQuestionAttempts_UserId_AttemptedAt",
                table: "ListeningQuestionAttempts",
                columns: new[] { "UserId", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningQuestionAttempts_UserId_InReviewQueue_NextReviewAt",
                table: "ListeningQuestionAttempts",
                columns: new[] { "UserId", "InReviewQueue", "NextReviewAt" });

            migrationBuilder.CreateIndex(
                name: "UX_ListeningQuestionOption_Question_Key",
                table: "ListeningQuestionOptions",
                columns: new[] { "ListeningQuestionId", "OptionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListeningQuestions_ListeningExtractId",
                table: "ListeningQuestions",
                column: "ListeningExtractId");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningQuestions_ListeningPartId_DisplayOrder",
                table: "ListeningQuestions",
                columns: new[] { "ListeningPartId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "UX_ListeningQuestion_Paper_Number",
                table: "ListeningQuestions",
                columns: new[] { "PaperId", "QuestionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListeningStrategies_Slug",
                table: "ListeningStrategies",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListeningTtsJobs_ExtractId",
                table: "ListeningTtsJobs",
                column: "ExtractId");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningTtsJobs_Status_CreatedAt",
                table: "ListeningTtsJobs",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassAttendances_ClassSessionId_UserId",
                table: "LiveClassAttendances",
                columns: new[] { "ClassSessionId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassAttendances_ZoomParticipantUuid",
                table: "LiveClassAttendances",
                column: "ZoomParticipantUuid");

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassEnrollments_ClassSessionId_UserId",
                table: "LiveClassEnrollments",
                columns: new[] { "ClassSessionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassEnrollments_IdempotencyKey",
                table: "LiveClassEnrollments",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassEnrollments_UserId_Status",
                table: "LiveClassEnrollments",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveClasses_Slug",
                table: "LiveClasses",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveClasses_Status_ProfessionTrack_Level",
                table: "LiveClasses",
                columns: new[] { "Status", "ProfessionTrack", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveClasses_TutorProfileId",
                table: "LiveClasses",
                column: "TutorProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassRecordings_ClassSessionId",
                table: "LiveClassRecordings",
                column: "ClassSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassRecordings_ZoomRecordingId",
                table: "LiveClassRecordings",
                column: "ZoomRecordingId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassSessions_LiveClassId_ScheduledStartAt",
                table: "LiveClassSessions",
                columns: new[] { "LiveClassId", "ScheduledStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassSessions_ScheduledStartAt",
                table: "LiveClassSessions",
                column: "ScheduledStartAt");

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassSessions_Status_ScheduledStartAt",
                table: "LiveClassSessions",
                columns: new[] { "Status", "ScheduledStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassSessions_ZoomMeetingId",
                table: "LiveClassSessions",
                column: "ZoomMeetingId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassWaitlistEntries_ClassSessionId_Position",
                table: "LiveClassWaitlistEntries",
                columns: new[] { "ClassSessionId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassWaitlistEntries_ClassSessionId_UserId",
                table: "LiveClassWaitlistEntries",
                columns: new[] { "ClassSessionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassWebhookEvents_EventType_ReceivedAt",
                table: "LiveClassWebhookEvents",
                columns: new[] { "EventType", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassWebhookEvents_PayloadHash",
                table: "LiveClassWebhookEvents",
                column: "PayloadHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManualPaymentRequests_ProofHashHex",
                table: "ManualPaymentRequests",
                column: "ProofHashHex");

            migrationBuilder.CreateIndex(
                name: "IX_ManualPaymentRequests_Status_SubmittedAt",
                table: "ManualPaymentRequests",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ManualPaymentRequests_UserId_Status",
                table: "ManualPaymentRequests",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketingAssets_AssetType_Status",
                table: "MarketingAssets",
                columns: new[] { "AssetType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_Status",
                table: "MediaAssets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MfaRecoveryCodes_ApplicationUserAccountId",
                table: "MfaRecoveryCodes",
                column: "ApplicationUserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MobilePushTokens_AuthAccountId_Platform",
                table: "MobilePushTokens",
                columns: new[] { "AuthAccountId", "Platform" });

            migrationBuilder.CreateIndex(
                name: "IX_MobilePushTokens_Token",
                table: "MobilePushTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockAttempts_MockBundleId",
                table: "MockAttempts",
                column: "MockBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_MockAttempts_UserId_State",
                table: "MockAttempts",
                columns: new[] { "UserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_MockBookings_AssignedTutorId_ScheduledStartAt",
                table: "MockBookings",
                columns: new[] { "AssignedTutorId", "ScheduledStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MockBookings_MockAttemptId",
                table: "MockBookings",
                column: "MockAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_MockBookings_MockBundleId",
                table: "MockBookings",
                column: "MockBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_MockBookings_Status_ScheduledStartAt",
                table: "MockBookings",
                columns: new[] { "Status", "ScheduledStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MockBookings_UserId_ScheduledStartAt",
                table: "MockBookings",
                columns: new[] { "UserId", "ScheduledStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MockBundles_ProfessionId_MockType",
                table: "MockBundles",
                columns: new[] { "ProfessionId", "MockType" });

            migrationBuilder.CreateIndex(
                name: "IX_MockBundles_Slug",
                table: "MockBundles",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockBundles_Status_MockType",
                table: "MockBundles",
                columns: new[] { "Status", "MockType" });

            migrationBuilder.CreateIndex(
                name: "IX_MockBundleSections_ContentPaperId",
                table: "MockBundleSections",
                column: "ContentPaperId");

            migrationBuilder.CreateIndex(
                name: "IX_MockBundleSections_MockBundleId_SectionOrder",
                table: "MockBundleSections",
                columns: new[] { "MockBundleId", "SectionOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockBundleSections_MockBundleId_SubtestCode",
                table: "MockBundleSections",
                columns: new[] { "MockBundleId", "SubtestCode" });

            migrationBuilder.CreateIndex(
                name: "IX_MockContentReviews_MockAttemptId",
                table: "MockContentReviews",
                column: "MockAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_MockContentReviews_MockBundleId",
                table: "MockContentReviews",
                column: "MockBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_MockContentReviews_ReportedByUserId_CreatedAt",
                table: "MockContentReviews",
                columns: new[] { "ReportedByUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MockContentReviews_Status_Severity",
                table: "MockContentReviews",
                columns: new[] { "Status", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_MockEntitlementLedgers_AddOnId",
                table: "MockEntitlementLedgers",
                column: "AddOnId");

            migrationBuilder.CreateIndex(
                name: "IX_MockEntitlementLedgers_UserId_ConsumedAt",
                table: "MockEntitlementLedgers",
                columns: new[] { "UserId", "ConsumedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MockEntitlementLedgers_UserId_MockType",
                table: "MockEntitlementLedgers",
                columns: new[] { "UserId", "MockType" });

            migrationBuilder.CreateIndex(
                name: "IX_MockItemAnalysisSnapshots_ContentPaperId",
                table: "MockItemAnalysisSnapshots",
                column: "ContentPaperId");

            migrationBuilder.CreateIndex(
                name: "IX_MockItemAnalysisSnapshots_MockBundleId",
                table: "MockItemAnalysisSnapshots",
                column: "MockBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_MockItemAnalysisSnapshots_MockBundleId_SubtestCode",
                table: "MockItemAnalysisSnapshots",
                columns: new[] { "MockBundleId", "SubtestCode" });

            migrationBuilder.CreateIndex(
                name: "UX_MockItemAnalysis_Bundle_Item",
                table: "MockItemAnalysisSnapshots",
                columns: new[] { "MockBundleId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockLiveRoomTransitions_BookingId_ClientTransitionId",
                table: "MockLiveRoomTransitions",
                columns: new[] { "BookingId", "ClientTransitionId" },
                unique: true,
                filter: "\"ClientTransitionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MockLiveRoomTransitions_BookingId_OccurredAt",
                table: "MockLiveRoomTransitions",
                columns: new[] { "BookingId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MockLiveRoomTransitions_BookingId_TransitionVersion",
                table: "MockLiveRoomTransitions",
                columns: new[] { "BookingId", "TransitionVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockProctoringEvents_Kind",
                table: "MockProctoringEvents",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_MockProctoringEvents_MockAttemptId_OccurredAt",
                table: "MockProctoringEvents",
                columns: new[] { "MockAttemptId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MockProctoringEvents_MockSectionAttemptId",
                table: "MockProctoringEvents",
                column: "MockSectionAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_MockReviewReservations_MockAttemptId",
                table: "MockReviewReservations",
                column: "MockAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockReviewReservations_UserId_State",
                table: "MockReviewReservations",
                columns: new[] { "UserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_MockSectionAttempts_ContentAttemptId",
                table: "MockSectionAttempts",
                column: "ContentAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_MockSectionAttempts_MockAttemptId_MockBundleSectionId",
                table: "MockSectionAttempts",
                columns: new[] { "MockAttemptId", "MockBundleSectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockSectionAttempts_MockAttemptId_SubtestCode",
                table: "MockSectionAttempts",
                columns: new[] { "MockAttemptId", "SubtestCode" });

            migrationBuilder.CreateIndex(
                name: "IX_MockSectionAttempts_MockBundleSectionId",
                table: "MockSectionAttempts",
                column: "MockBundleSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_NativeIapProductMappings_Platform_IsActive",
                table: "NativeIapProductMappings",
                columns: new[] { "Platform", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_NativeIapProductMappings_Platform_StoreProductId",
                table: "NativeIapProductMappings",
                columns: new[] { "Platform", "StoreProductId" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_NativeIapProductMappings_TargetType_TargetId",
                table: "NativeIapProductMappings",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationConsents_AuthAccountId_Channel_Category",
                table: "NotificationConsents",
                columns: new[] { "AuthAccountId", "Channel", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationConsents_Channel_IsGranted_UpdatedAt",
                table: "NotificationConsents",
                columns: new[] { "Channel", "IsGranted", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryAttempts_AuthAccountId_Channel_Status_A~",
                table: "NotificationDeliveryAttempts",
                columns: new[] { "AuthAccountId", "Channel", "Status", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryAttempts_NotificationEventId_Channel_At~",
                table: "NotificationDeliveryAttempts",
                columns: new[] { "NotificationEventId", "Channel", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryAttempts_Status_AttemptedAt",
                table: "NotificationDeliveryAttempts",
                columns: new[] { "Status", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEvents_DedupeKey",
                table: "NotificationEvents",
                column: "DedupeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEvents_RecipientAuthAccountId_CreatedAt",
                table: "NotificationEvents",
                columns: new[] { "RecipientAuthAccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEvents_State_CreatedAt",
                table: "NotificationEvents",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationInboxItems_AuthAccountId_IsRead_CreatedAt",
                table: "NotificationInboxItems",
                columns: new[] { "AuthAccountId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationInboxItems_NotificationEventId",
                table: "NotificationInboxItems",
                column: "NotificationEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPolicyOverrides_AudienceRole_EventKey",
                table: "NotificationPolicyOverrides",
                columns: new[] { "AudienceRole", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_AuthAccountId",
                table: "NotificationPreferences",
                column: "AuthAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSuppressions_AuthAccountId_Channel_EventKey_IsA~",
                table: "NotificationSuppressions",
                columns: new[] { "AuthAccountId", "Channel", "EventKey", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSuppressions_Channel_IsActive_ExpiresAt",
                table: "NotificationSuppressions",
                columns: new[] { "Channel", "IsActive", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_Gateway_GatewayRefundId",
                table: "OrderRefunds",
                columns: new[] { "Gateway", "GatewayRefundId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_IdempotencyKey",
                table: "OrderRefunds",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_PaymentTransactionId",
                table: "OrderRefunds",
                column: "PaymentTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageContentRules_PackageId_RuleType",
                table: "PackageContentRules",
                columns: new[] { "PackageId", "RuleType" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentDisputes_Gateway_GatewayDisputeId",
                table: "PaymentDisputes",
                columns: new[] { "Gateway", "GatewayDisputeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentDisputes_PaymentTransactionId",
                table: "PaymentDisputes",
                column: "PaymentTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethodUpdateLinks_UserId_ExpiresAt",
                table: "PaymentMethodUpdateLinks",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_CouponVersionId",
                table: "PaymentTransactions",
                column: "CouponVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_GatewayTransactionId",
                table: "PaymentTransactions",
                column: "GatewayTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_LearnerUserId",
                table: "PaymentTransactions",
                column: "LearnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_LearnerUserId_CreatedAt",
                table: "PaymentTransactions",
                columns: new[] { "LearnerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_PlanVersionId",
                table: "PaymentTransactions",
                column: "PlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_QuoteId",
                table: "PaymentTransactions",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_Status_CreatedAt",
                table: "PaymentTransactions",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_GatewayEventId",
                table: "PaymentWebhookEvents",
                column: "GatewayEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_ProcessingStatus_ReceivedAt",
                table: "PaymentWebhookEvents",
                columns: new[] { "ProcessingStatus", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_VerificationStatus_ProcessingStatus",
                table: "PaymentWebhookEvents",
                columns: new[] { "VerificationStatus", "ProcessingStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_PeerReviewFeedbacks_PeerReviewRequestId",
                table: "PeerReviewFeedbacks",
                column: "PeerReviewRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PeerReviewRequests_Status_CreatedAt",
                table: "PeerReviewRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PeerReviewRequests_SubmitterUserId",
                table: "PeerReviewRequests",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PermissionTemplates_Name",
                table: "PermissionTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PredictionSnapshots_UserId_ExamTypeCode_SubtestCode_Compute~",
                table: "PredictionSnapshots",
                columns: new[] { "UserId", "ExamTypeCode", "SubtestCode", "ComputedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PricingExperimentAssignments_ExperimentId_UserId",
                table: "PricingExperimentAssignments",
                columns: new[] { "ExperimentId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PricingExperimentAssignments_ExperimentId_VariantCode",
                table: "PricingExperimentAssignments",
                columns: new[] { "ExperimentId", "VariantCode" });

            migrationBuilder.CreateIndex(
                name: "IX_PricingExperiments_Status",
                table: "PricingExperiments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PricingExperiments_TargetType_TargetId",
                table: "PricingExperiments",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingAuditLogs_ActorId_CreatedAt",
                table: "PrivateSpeakingAuditLogs",
                columns: new[] { "ActorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingAuditLogs_BookingId",
                table: "PrivateSpeakingAuditLogs",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingAuditLogs_CreatedAt",
                table: "PrivateSpeakingAuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingAvailabilityOverrides_TutorProfileId_Date",
                table: "PrivateSpeakingAvailabilityOverrides",
                columns: new[] { "TutorProfileId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingAvailabilityRules_TutorProfileId_DayOfWeek",
                table: "PrivateSpeakingAvailabilityRules",
                columns: new[] { "TutorProfileId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingBookings_IdempotencyKey",
                table: "PrivateSpeakingBookings",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingBookings_LearnerUserId_SessionStartUtc",
                table: "PrivateSpeakingBookings",
                columns: new[] { "LearnerUserId", "SessionStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingBookings_LearnerUserId_Status",
                table: "PrivateSpeakingBookings",
                columns: new[] { "LearnerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingBookings_Status",
                table: "PrivateSpeakingBookings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingBookings_StripeCheckoutSessionId",
                table: "PrivateSpeakingBookings",
                column: "StripeCheckoutSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingBookings_TutorProfileId_SessionStartUtc",
                table: "PrivateSpeakingBookings",
                columns: new[] { "TutorProfileId", "SessionStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingTutorProfiles_ExpertUserId",
                table: "PrivateSpeakingTutorProfiles",
                column: "ExpertUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationAssessments_DrillId",
                table: "PronunciationAssessments",
                column: "DrillId");

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationAssessments_UserId_CreatedAt",
                table: "PronunciationAssessments",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationAttempts_DrillId_CreatedAt",
                table: "PronunciationAttempts",
                columns: new[] { "DrillId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationAttempts_Status",
                table: "PronunciationAttempts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationAttempts_UserId_CreatedAt",
                table: "PronunciationAttempts",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationAttempts_UserId_DrillId_CreatedAt",
                table: "PronunciationAttempts",
                columns: new[] { "UserId", "DrillId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationCards_Word",
                table: "PronunciationCards",
                column: "Word",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_AuthAccountId_IsActive",
                table: "PushSubscriptions",
                columns: new[] { "AuthAccountId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_Endpoint",
                table: "PushSubscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessHistories_RecordedAt",
                table: "ReadinessHistories",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessHistories_UserId_WeekStartDate",
                table: "ReadinessHistories",
                columns: new[] { "UserId", "WeekStartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessSnapshots_ExpiresAt",
                table: "ReadinessSnapshots",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessSnapshots_UserId",
                table: "ReadinessSnapshots",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessSnapshots_UserId_ComputedAt",
                table: "ReadinessSnapshots",
                columns: new[] { "UserId", "ComputedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingAnswer_AttemptId",
                table: "ReadingAnswers",
                column: "ReadingAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingAnswers_ReadingQuestionId",
                table: "ReadingAnswers",
                column: "ReadingQuestionId");

            migrationBuilder.CreateIndex(
                name: "UX_ReadingAnswer_Attempt_Question",
                table: "ReadingAnswers",
                columns: new[] { "ReadingAttemptId", "ReadingQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingAttempt_User_Paper_Mode_Status",
                table: "ReadingAttempts",
                columns: new[] { "UserId", "PaperId", "Mode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingAttempts_PaperId_StartedAt",
                table: "ReadingAttempts",
                columns: new[] { "PaperId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingAttempts_UserId_PaperId",
                table: "ReadingAttempts",
                columns: new[] { "UserId", "PaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingAttempts_UserId_Status",
                table: "ReadingAttempts",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_ReadingAttempt_UserPaperExam_InProgress",
                table: "ReadingAttempts",
                columns: new[] { "UserId", "PaperId", "Mode", "Status" },
                unique: true,
                filter: "\"Mode\" = 0 AND \"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingDailyPlanItems_UserId_PlanDate",
                table: "ReadingDailyPlanItems",
                columns: new[] { "UserId", "PlanDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingErrorBankEntries_UserId_IsResolved",
                table: "ReadingErrorBankEntries",
                columns: new[] { "UserId", "IsResolved" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingErrorBankEntries_UserId_LastSeenWrongAt",
                table: "ReadingErrorBankEntries",
                columns: new[] { "UserId", "LastSeenWrongAt" });

            migrationBuilder.CreateIndex(
                name: "UX_ReadingErrorBankEntry_User_Question",
                table: "ReadingErrorBankEntries",
                columns: new[] { "ReadingQuestionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingExtractionDrafts_CreatedAt",
                table: "ReadingExtractionDrafts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingExtractionDrafts_PaperId_Status",
                table: "ReadingExtractionDrafts",
                columns: new[] { "PaperId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_ReadingPart_Paper_PartCode",
                table: "ReadingParts",
                columns: new[] { "PaperId", "PartCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingQuestionAttempts_UserId_AttemptedAt",
                table: "ReadingQuestionAttempts",
                columns: new[] { "UserId", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingQuestionAttempts_UserId_InReviewQueue_NextReviewAt",
                table: "ReadingQuestionAttempts",
                columns: new[] { "UserId", "InReviewQueue", "NextReviewAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingQuestionDiscussionComments_ReadingQuestionId",
                table: "ReadingQuestionDiscussionComments",
                column: "ReadingQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingQuestionReviewLogs_ReadingQuestionId_TransitionedAt",
                table: "ReadingQuestionReviewLogs",
                columns: new[] { "ReadingQuestionId", "TransitionedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingQuestions_ReadingPartId_DisplayOrder",
                table: "ReadingQuestions",
                columns: new[] { "ReadingPartId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingQuestions_ReadingTextId",
                table: "ReadingQuestions",
                column: "ReadingTextId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingStrategyProgresses_UserId_StrategyId",
                table: "ReadingStrategyProgresses",
                columns: new[] { "UserId", "StrategyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingTexts_ReadingPartId_DisplayOrder",
                table: "ReadingTexts",
                columns: new[] { "ReadingPartId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RecallBookmarks_UserId_CreatedAt",
                table: "RecallBookmarks",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecallBookmarks_UserId_VocabularyTermId",
                table: "RecallBookmarks",
                columns: new[] { "UserId", "VocabularyTermId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecallSetTags_Code",
                table: "RecallSetTags",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecallSetTags_IsActive_SortOrder",
                table: "RecallSetTags",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCodes_Code",
                table: "ReferralCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCodes_UserId",
                table: "ReferralCodes",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralRecords_ReferralCode",
                table: "ReferralRecords",
                column: "ReferralCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralRecords_ReferrerUserId",
                table: "ReferralRecords",
                column: "ReferrerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReferrerUserId",
                table: "Referrals",
                column: "ReferrerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokenRecord_Active",
                table: "RefreshTokenRecords",
                column: "ApplicationUserAccountId",
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokenRecords_ApplicationUserAccountId_ExpiresAt",
                table: "RefreshTokenRecords",
                columns: new[] { "ApplicationUserAccountId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokenRecords_ApplicationUserAccountId_TokenHash",
                table: "RefreshTokenRecords",
                columns: new[] { "ApplicationUserAccountId", "TokenHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegionPricings_Region_IsActive",
                table: "RegionPricings",
                columns: new[] { "Region", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RegionPricings_TargetType_TargetId_Region",
                table: "RegionPricings",
                columns: new[] { "TargetType", "TargetId", "Region" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemediationTasks_MockReportId",
                table: "RemediationTasks",
                column: "MockReportId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationTasks_UserId_Status",
                table: "RemediationTasks",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ResultTemplateAssets_IsActive_SortOrder",
                table: "ResultTemplateAssets",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ResultTemplateAssets_MediaAssetId",
                table: "ResultTemplateAssets",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ResultTemplateAssets_ProfessionId_IsActive",
                table: "ResultTemplateAssets",
                columns: new[] { "ProfessionId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ResultTemplateAssets_TemplateKey",
                table: "ResultTemplateAssets",
                column: "TemplateKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewEscalations_ReviewRequestId_Status",
                table: "ReviewEscalations",
                columns: new[] { "ReviewRequestId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewEscalations_SecondReviewerId",
                table: "ReviewEscalations",
                column: "SecondReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewItems_UserId_DueDate_Status",
                table: "ReviewItems",
                columns: new[] { "UserId", "DueDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewItems_UserId_ExamTypeCode_Status",
                table: "ReviewItems",
                columns: new[] { "UserId", "ExamTypeCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRequests_AttemptId_State",
                table: "ReviewRequests",
                columns: new[] { "AttemptId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRequests_State_CreatedAt",
                table: "ReviewRequests",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewVoiceNotes_MediaAssetId",
                table: "ReviewVoiceNotes",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewVoiceNotes_ReviewRequestId_CreatedAt",
                table: "ReviewVoiceNotes",
                columns: new[] { "ReviewRequestId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RolePlayCards_ContentItemId",
                table: "RolePlayCards",
                column: "ContentItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePlayCards_ProfessionId_Status",
                table: "RolePlayCards",
                columns: new[] { "ProfessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RulebookRuleRows_RulebookVersionId_Code",
                table: "RulebookRuleRows",
                columns: new[] { "RulebookVersionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RulebookRuleRows_RulebookVersionId_SectionCode",
                table: "RulebookRuleRows",
                columns: new[] { "RulebookVersionId", "SectionCode" });

            migrationBuilder.CreateIndex(
                name: "IX_RulebookSectionRows_RulebookVersionId_Code",
                table: "RulebookSectionRows",
                columns: new[] { "RulebookVersionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RulebookVersions_Kind_Profession_Status",
                table: "RulebookVersions",
                columns: new[] { "Kind", "Profession", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RulebookVersions_ReferencePdfAssetId",
                table: "RulebookVersions",
                column: "ReferencePdfAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleExceptions_ReviewerId",
                table: "ScheduleExceptions",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_Scholarships_Status_ExpiresAt",
                table: "Scholarships",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Scholarships_UserId_Status",
                table: "Scholarships",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScoreGuaranteePledges_UserId",
                table: "ScoreGuaranteePledges",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoringPolicies_IsActive",
                table: "ScoringPolicies",
                column: "IsActive",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_Settings_UserId",
                table: "Settings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SignupExamTypeCatalog_IsActive_SortOrder",
                table: "SignupExamTypeCatalog",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SignupProfessionCatalog_IsActive_SortOrder",
                table: "SignupProfessionCatalog",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SignupSessionCatalog_IsActive_SortOrder",
                table: "SignupSessionCatalog",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingAiAssessments_SpeakingSessionId",
                table: "SpeakingAiAssessments",
                column: "SpeakingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingCalibrationSamples_Status_PublishedAt",
                table: "SpeakingCalibrationSamples",
                columns: new[] { "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingCalibrationScores_SampleId_TutorId",
                table: "SpeakingCalibrationScores",
                columns: new[] { "SampleId", "TutorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingCalibrationScores_TutorId",
                table: "SpeakingCalibrationScores",
                column: "TutorId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingCardBatchRequests_IdempotencyKey",
                table: "SpeakingCardBatchRequests",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingCardBatchRequests_Status_CreatedAt",
                table: "SpeakingCardBatchRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingComplianceConsents_ConsentType_ConsentVersion",
                table: "SpeakingComplianceConsents",
                columns: new[] { "ConsentType", "ConsentVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingComplianceConsents_UserId_ConsentType",
                table: "SpeakingComplianceConsents",
                columns: new[] { "UserId", "ConsentType" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingDrillAttempts_DrillItemId",
                table: "SpeakingDrillAttempts",
                column: "DrillItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingDrillAttempts_UserId_DrillItemId",
                table: "SpeakingDrillAttempts",
                columns: new[] { "UserId", "DrillItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingDrillItems_ContentItemId",
                table: "SpeakingDrillItems",
                column: "ContentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingDrillItems_DrillKind",
                table: "SpeakingDrillItems",
                column: "DrillKind");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingFeedbackComments_AttemptId_TranscriptLineIndex",
                table: "SpeakingFeedbackComments",
                columns: new[] { "AttemptId", "TranscriptLineIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingLiveRooms_RoomName",
                table: "SpeakingLiveRooms",
                column: "RoomName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingLiveRooms_SpeakingSessionId",
                table: "SpeakingLiveRooms",
                column: "SpeakingSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingLiveRoomTokens_LiveRoomId",
                table: "SpeakingLiveRoomTokens",
                column: "LiveRoomId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingMockSessions_MockSetId",
                table: "SpeakingMockSessions",
                column: "MockSetId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingMockSessions_UserId_StartedAt",
                table: "SpeakingMockSessions",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingMockSets_Status_SortOrder",
                table: "SpeakingMockSets",
                columns: new[] { "Status", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingRecordings_MediaAssetId",
                table: "SpeakingRecordings",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingRecordings_Sha256",
                table: "SpeakingRecordings",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingRecordings_SpeakingSessionId",
                table: "SpeakingRecordings",
                column: "SpeakingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingReviewVoiceNotes_ReviewRequestId_CreatedAt",
                table: "SpeakingReviewVoiceNotes",
                columns: new[] { "ReviewRequestId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingSessions_MockSessionId",
                table: "SpeakingSessions",
                column: "MockSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingSessions_RolePlayCardId_State",
                table: "SpeakingSessions",
                columns: new[] { "RolePlayCardId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingSessions_UserId_State",
                table: "SpeakingSessions",
                columns: new[] { "UserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingSharedResources_Kind_Status",
                table: "SpeakingSharedResources",
                columns: new[] { "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingSharedResources_MediaAssetId",
                table: "SpeakingSharedResources",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingSharedResources_ProfessionId_Kind",
                table: "SpeakingSharedResources",
                columns: new[] { "ProfessionId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingTimestampedComments_SpeakingSessionId_StartMs",
                table: "SpeakingTimestampedComments",
                columns: new[] { "SpeakingSessionId", "StartMs" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingTranscripts_SpeakingSessionId_IsLatest",
                table: "SpeakingTranscripts",
                columns: new[] { "SpeakingSessionId", "IsLatest" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingTutorAssessments_SpeakingSessionId_IsFinal",
                table: "SpeakingTutorAssessments",
                columns: new[] { "SpeakingSessionId", "IsFinal" });

            migrationBuilder.CreateIndex(
                name: "IX_SponsorLearnerLinks_SponsorId_LearnerId",
                table: "SponsorLearnerLinks",
                columns: new[] { "SponsorId", "LearnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrategyGuides_ContentLessonId",
                table: "StrategyGuides",
                column: "ContentLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyGuides_ExamTypeCode_Category_Status",
                table: "StrategyGuides",
                columns: new[] { "ExamTypeCode", "Category", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StrategyGuides_Slug",
                table: "StrategyGuides",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StreakRecords_UserId_Date",
                table: "StreakRecords",
                columns: new[] { "UserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudyCommitments_UserId",
                table: "StudyCommitments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroupMembers_GroupId_UserId",
                table: "StudyGroupMembers",
                columns: new[] { "GroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroupMembers_UserId",
                table: "StudyGroupMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanItems_LinkedReviewItemId",
                table: "StudyPlanItems",
                column: "LinkedReviewItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanItems_ReplacedById",
                table: "StudyPlanItems",
                column: "ReplacedById");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanItems_StudyPlanId_Section_Status",
                table: "StudyPlanItems",
                columns: new[] { "StudyPlanId", "Section", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanItems_StudyPlanId_WeekIndex",
                table: "StudyPlanItems",
                columns: new[] { "StudyPlanId", "WeekIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlans_TemplateId",
                table: "StudyPlans",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlans_UserId",
                table: "StudyPlans",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlans_UserId_IsActive",
                table: "StudyPlans",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTemplates_IsActive_ExamTypeCode",
                table: "StudyPlanTemplates",
                columns: new[] { "IsActive", "ExamTypeCode" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTemplates_IsActive_MinWeeks_MaxWeeks",
                table: "StudyPlanTemplates",
                columns: new[] { "IsActive", "MinWeeks", "MaxWeeks" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTemplates_ProfessionId",
                table: "StudyPlanTemplates",
                column: "ProfessionId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTemplates_Slug",
                table: "StudyPlanTemplates",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTemplateTiers_TemplateId_TierCode",
                table: "StudyPlanTemplateTiers",
                columns: new[] { "TemplateId", "TierCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionItems_AddOnVersionId",
                table: "SubscriptionItems",
                column: "AddOnVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionItems_ItemCode_SubscriptionId",
                table: "SubscriptionItems",
                columns: new[] { "ItemCode", "SubscriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionItems_SubscriptionId_Status",
                table: "SubscriptionItems",
                columns: new[] { "SubscriptionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PlanVersionId",
                table: "Subscriptions",
                column: "PlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_UserId",
                table: "Subscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTypes_ExamTypeCode_SubtestCode_Status",
                table: "TaskTypes",
                columns: new[] { "ExamTypeCode", "SubtestCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRules_Country_EffectiveFrom",
                table: "TaxRules",
                columns: new[] { "Country", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRules_Region_IsActive",
                table: "TaxRules",
                columns: new[] { "Region", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherClasses_OwnerUserId",
                table: "TeacherClasses",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "UX_TeacherClassMember_Class_User",
                table: "TeacherClassMembers",
                columns: new[] { "TeacherClassId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestimonialAssets_DisplayApproved_DisplayOrder",
                table: "TestimonialAssets",
                columns: new[] { "DisplayApproved", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorAvailabilities_TutorId",
                table: "TutorAvailabilities",
                column: "TutorId");

            migrationBuilder.CreateIndex(
                name: "IX_TutorAvailabilities_TutorId_DayOfWeek",
                table: "TutorAvailabilities",
                columns: new[] { "TutorId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorBookUpdates_Audience",
                table: "TutorBookUpdates",
                column: "Audience");

            migrationBuilder.CreateIndex(
                name: "IX_TutorBookUpdates_PublishedAt",
                table: "TutorBookUpdates",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TutoringAvailabilities_ExpertUserId",
                table: "TutoringAvailabilities",
                column: "ExpertUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TutoringSessions_ExpertUserId_ScheduledAt",
                table: "TutoringSessions",
                columns: new[] { "ExpertUserId", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutoringSessions_LearnerUserId_ScheduledAt",
                table: "TutoringSessions",
                columns: new[] { "LearnerUserId", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tutors_IsActive",
                table: "Tutors",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Tutors_UserId",
                table: "Tutors",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadSessions_AttemptId",
                table: "UploadSessions",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageForecastSnapshots_FeatureCode_SnapshotDate",
                table: "UsageForecastSnapshots",
                columns: new[] { "FeatureCode", "SnapshotDate" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageForecastSnapshots_UserId_SnapshotDate",
                table: "UsageForecastSnapshots",
                columns: new[] { "UserId", "SnapshotDate" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAiCredentials_AuthAccountId",
                table: "UserAiCredentials",
                column: "AuthAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAiCredentials_UserId_ProviderCode",
                table: "UserAiCredentials",
                columns: new[] { "UserId", "ProviderCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotes_UserId_CreatedAt",
                table: "UserNotes",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_AccountStatus",
                table: "Users",
                column: "AccountStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Users_AuthAccountId",
                table: "Users",
                column: "AuthAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Users_LastActiveAt",
                table: "Users",
                column: "LastActiveAt");

            migrationBuilder.CreateIndex(
                name: "IX_VideoLessons_ExamTypeCode_Category_Status",
                table: "VideoLessons",
                columns: new[] { "ExamTypeCode", "Category", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VocabularyQuizResults_UserId_CompletedAt",
                table: "VocabularyQuizResults",
                columns: new[] { "UserId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VocabularyTerms_ExamTypeCode_Status_Category",
                table: "VocabularyTerms",
                columns: new[] { "ExamTypeCode", "Status", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_VocabularyTerms_ProfessionId_Category_Status",
                table: "VocabularyTerms",
                columns: new[] { "ProfessionId", "Category", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VocabularyTerms_Term_ExamTypeCode_ProfessionId",
                table: "VocabularyTerms",
                columns: new[] { "Term", "ExamTypeCode", "ProfessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_UserId",
                table: "Wallets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTopUpTierConfigs_Amount_Currency",
                table: "WalletTopUpTierConfigs",
                columns: new[] { "Amount", "Currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTopUpTierConfigs_IsActive_DisplayOrder",
                table: "WalletTopUpTierConfigs",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTopUpTierConfigs_Slug",
                table: "WalletTopUpTierConfigs",
                column: "Slug",
                unique: true,
                filter: "\"Slug\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId",
                table: "WalletTransactions",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId_CreatedAt",
                table: "WalletTransactions",
                columns: new[] { "WalletId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId_IdempotencyKey",
                table: "WalletTransactions",
                columns: new[] { "WalletId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId_TransactionType_ReferenceType_R~",
                table: "WalletTransactions",
                columns: new[] { "WalletId", "TransactionType", "ReferenceType", "ReferenceId" },
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL AND ((\"TransactionType\" = 'top_up' AND \"ReferenceType\" = 'payment') OR (\"TransactionType\" = 'plan_grant' AND \"ReferenceType\" = 'subscription') OR (\"TransactionType\" = 'credit_purchase' AND \"ReferenceType\" = 'addon'))");

            migrationBuilder.CreateIndex(
                name: "IX_WritingAttemptAssets_AttemptId_PageNumber",
                table: "WritingAttemptAssets",
                columns: new[] { "AttemptId", "PageNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingAttemptAssets_MediaAssetId",
                table: "WritingAttemptAssets",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingAttemptAssets_UserId_CreatedAt",
                table: "WritingAttemptAssets",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyCheckIns_PairId_WeekStartDate",
                table: "WritingBuddyCheckIns",
                columns: new[] { "PairId", "WeekStartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyMessage_Pair_SentAt_Desc",
                table: "WritingBuddyMessages",
                columns: new[] { "PairId", "SentAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyMessages_FromUserId_SentAt",
                table: "WritingBuddyMessages",
                columns: new[] { "FromUserId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyPairs_Profession_Status_MatchedAtBand",
                table: "WritingBuddyPairs",
                columns: new[] { "Profession", "Status", "MatchedAtBand" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyPairs_UserAId",
                table: "WritingBuddyPairs",
                column: "UserAId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyPairs_UserAId_UserBId",
                table: "WritingBuddyPairs",
                columns: new[] { "UserAId", "UserBId" },
                unique: true,
                filter: "\"Status\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyPairs_UserBId",
                table: "WritingBuddyPairs",
                column: "UserBId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationLetters_AuthorTier",
                table: "WritingCalibrationLetters",
                column: "AuthorTier");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationLetters_ScenarioId",
                table: "WritingCalibrationLetters",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationResults_CalibrationLetterId",
                table: "WritingCalibrationResults",
                column: "CalibrationLetterId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationResults_RunId_AbsErrorRaw",
                table: "WritingCalibrationResults",
                columns: new[] { "RunId", "AbsErrorRaw" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationRuns_ModelVersion",
                table: "WritingCalibrationRuns",
                column: "ModelVersion");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationRuns_RunDate",
                table: "WritingCalibrationRuns",
                column: "RunDate");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCanonRules_Category_Active",
                table: "WritingCanonRules",
                columns: new[] { "Category", "Active" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingCanonRules_DetectionType",
                table: "WritingCanonRules",
                column: "DetectionType");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCanonViolations_RuleId",
                table: "WritingCanonViolations",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCanonViolations_SubmissionId",
                table: "WritingCanonViolations",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCanonViolations_SubmissionId_RuleId",
                table: "WritingCanonViolations",
                columns: new[] { "SubmissionId", "RuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingCaseNoteDrillAttempts_UserId_DrillId_AttemptedAt",
                table: "WritingCaseNoteDrillAttempts",
                columns: new[] { "UserId", "DrillId", "AttemptedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WritingCaseNoteDrills_Profession_LetterType_Status",
                table: "WritingCaseNoteDrills",
                columns: new[] { "Profession", "LetterType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingCaseNoteDrillSentences_DrillId_Ordinal",
                table: "WritingCaseNoteDrillSentences",
                columns: new[] { "DrillId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingCoachSessions_AttemptId",
                table: "WritingCoachSessions",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCoachSuggestions_AttemptId_Resolution",
                table: "WritingCoachSuggestions",
                columns: new[] { "AttemptId", "Resolution" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingCommonMistakes_CanonRuleId",
                table: "WritingCommonMistakes",
                column: "CanonRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCommonMistakes_Category",
                table: "WritingCommonMistakes",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCommonMistakes_RelatedSubSkill",
                table: "WritingCommonMistakes",
                column: "RelatedSubSkill");

            migrationBuilder.CreateIndex(
                name: "IX_WritingDailyPlanItems_UserId_PlanDate",
                table: "WritingDailyPlanItems",
                columns: new[] { "UserId", "PlanDate" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingDailyPlanItems_UserId_Status",
                table: "WritingDailyPlanItems",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingDiagnosticSessions_ExpiresAt",
                table: "WritingDiagnosticSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_WritingDiagnosticSessions_SubmissionId",
                table: "WritingDiagnosticSessions",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingDiagnosticSessions_UserId_Id",
                table: "WritingDiagnosticSessions",
                columns: new[] { "UserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingDraftsV2_UserId_LastSavedAt",
                table: "WritingDraftsV2",
                columns: new[] { "UserId", "LastSavedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WritingDraftsV2_UserId_ScenarioId_Mode",
                table: "WritingDraftsV2",
                columns: new[] { "UserId", "ScenarioId", "Mode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingDrillAttempts_User_Drill_Time",
                table: "WritingDrillAttempts",
                columns: new[] { "UserId", "DrillId", "AttemptedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WritingDrillAttempts_UserId_NextDueAt",
                table: "WritingDrillAttempts",
                columns: new[] { "UserId", "NextDueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingDrills_DrillType_Status",
                table: "WritingDrills",
                columns: new[] { "DrillType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingDrills_TargetCanonRuleId",
                table: "WritingDrills",
                column: "TargetCanonRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingDrills_TargetSubSkill",
                table: "WritingDrills",
                column: "TargetSubSkill");

            migrationBuilder.CreateIndex(
                name: "IX_WritingExemplarAnnotations_ExemplarId_Ordinal",
                table: "WritingExemplarAnnotations",
                columns: new[] { "ExemplarId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingExemplarEmbeddings_ExemplarId",
                table: "WritingExemplarEmbeddings",
                column: "ExemplarId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingExemplars_Profession_LetterType",
                table: "WritingExemplars",
                columns: new[] { "Profession", "LetterType" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingExemplars_ScenarioId",
                table: "WritingExemplars",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingExemplars_Status",
                table: "WritingExemplars",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WritingGrades_AppealedByGradeId",
                table: "WritingGrades",
                column: "AppealedByGradeId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingGrades_SubmissionId",
                table: "WritingGrades",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingGrades_TutorReviewId",
                table: "WritingGrades",
                column: "TutorReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingLearnerMistakeStats_UserId_LastOccurredAt",
                table: "WritingLearnerMistakeStats",
                columns: new[] { "UserId", "LastOccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WritingLearnerMistakeStats_UserId_MistakeId",
                table: "WritingLearnerMistakeStats",
                columns: new[] { "UserId", "MistakeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingLessonCompletionsV2_UserId_LessonId",
                table: "WritingLessonCompletionsV2",
                columns: new[] { "UserId", "LessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingLessons_SkillCode_OrderIndex",
                table: "WritingLessons",
                columns: new[] { "SkillCode", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingLessons_Slug",
                table: "WritingLessons",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingLessonsV2_Status",
                table: "WritingLessonsV2",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WritingLessonsV2_SubSkill_OrderInCourse",
                table: "WritingLessonsV2",
                columns: new[] { "SubSkill", "OrderInCourse" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingMocks_ScenarioId",
                table: "WritingMocks",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingMocks_Status",
                table: "WritingMocks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WritingMockSessions_MockId",
                table: "WritingMockSessions",
                column: "MockId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingMockSessions_Status",
                table: "WritingMockSessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WritingMockSessions_SubmissionId",
                table: "WritingMockSessions",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingMockSessions_UserId_StartedAt",
                table: "WritingMockSessions",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingOcrJobs_Status",
                table: "WritingOcrJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WritingOcrJobs_SubmissionId",
                table: "WritingOcrJobs",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingOcrJobs_UserId_CreatedAt",
                table: "WritingOcrJobs",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WritingPathwayItems_PathwayId_OrderIndex",
                table: "WritingPathwayItems",
                columns: new[] { "PathwayId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingPathwayItems_PathwayId_Status",
                table: "WritingPathwayItems",
                columns: new[] { "PathwayId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingReadinessScores_ComputedAt",
                table: "WritingReadinessScores",
                column: "ComputedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WritingReadinessScores_UserId_Date",
                table: "WritingReadinessScores",
                columns: new[] { "UserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingRuleViolations_AttemptId",
                table: "WritingRuleViolations",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingRuleViolations_Profession_GeneratedAt",
                table: "WritingRuleViolations",
                columns: new[] { "Profession", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingRuleViolations_RuleId_GeneratedAt",
                table: "WritingRuleViolations",
                columns: new[] { "RuleId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingScenarioEmbeddings_ScenarioId",
                table: "WritingScenarioEmbeddings",
                column: "ScenarioId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingScenarios_IsDiagnostic",
                table: "WritingScenarios",
                column: "IsDiagnostic");

            migrationBuilder.CreateIndex(
                name: "IX_WritingScenarios_Profession_LetterType",
                table: "WritingScenarios",
                columns: new[] { "Profession", "LetterType" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingScenarios_Status",
                table: "WritingScenarios",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WritingScenarioStructuredSentences_ScenarioId_Ordinal",
                table: "WritingScenarioStructuredSentences",
                columns: new[] { "ScenarioId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingScoreAppeals_OriginalGradeId",
                table: "WritingScoreAppeals",
                column: "OriginalGradeId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingScoreAppeals_SubmissionId",
                table: "WritingScoreAppeals",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingScoreAppeals_UserId_Status",
                table: "WritingScoreAppeals",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingShowcasePosts_Profession_LetterType_Status",
                table: "WritingShowcasePosts",
                columns: new[] { "Profession", "LetterType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingShowcasePosts_Status_PublishedAt",
                table: "WritingShowcasePosts",
                columns: new[] { "Status", "PublishedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WritingShowcasePosts_SubmissionId",
                table: "WritingShowcasePosts",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingSubmissions_LetterContentHash",
                table: "WritingSubmissions",
                column: "LetterContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_WritingSubmissions_OriginalSubmissionId",
                table: "WritingSubmissions",
                column: "OriginalSubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingSubmissions_Status_Pending",
                table: "WritingSubmissions",
                column: "Status",
                filter: "\"Status\" IN ('queued','grading')");

            migrationBuilder.CreateIndex(
                name: "IX_WritingSubmissions_User_CreatedAt",
                table: "WritingSubmissions",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WritingSubmissions_UserId_ScenarioId",
                table: "WritingSubmissions",
                columns: new[] { "UserId", "ScenarioId" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingTutorCalibrations_TutorId",
                table: "WritingTutorCalibrations",
                column: "TutorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingTutorReviewAssignments_DueAt",
                table: "WritingTutorReviewAssignments",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_WritingTutorReviewAssignments_SubmissionId",
                table: "WritingTutorReviewAssignments",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingTutorReviewAssignments_TutorId_Status",
                table: "WritingTutorReviewAssignments",
                columns: new[] { "TutorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingTutorReviews_SubmissionId",
                table: "WritingTutorReviews",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingTutorReviews_TutorId_Status",
                table: "WritingTutorReviews",
                columns: new[] { "TutorId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountFreezeEntitlements");

            migrationBuilder.DropTable(
                name: "AccountFreezePolicies");

            migrationBuilder.DropTable(
                name: "AccountFreezeRecords");

            migrationBuilder.DropTable(
                name: "Achievements");

            migrationBuilder.DropTable(
                name: "AdminPermissionGrants");

            migrationBuilder.DropTable(
                name: "AdminUploadSessions");

            migrationBuilder.DropTable(
                name: "AdminUsers");

            migrationBuilder.DropTable(
                name: "AffiliateAttributions");

            migrationBuilder.DropTable(
                name: "AffiliateCommissions");

            migrationBuilder.DropTable(
                name: "Affiliates");

            migrationBuilder.DropTable(
                name: "AiAssistantMessages");

            migrationBuilder.DropTable(
                name: "AiCodebaseChunks");

            migrationBuilder.DropTable(
                name: "AIConfigVersions");

            migrationBuilder.DropTable(
                name: "AiCreditLedger");

            migrationBuilder.DropTable(
                name: "AiFeatureRoutes");

            migrationBuilder.DropTable(
                name: "AiFeatureToolGrants");

            migrationBuilder.DropTable(
                name: "AiFileBackups");

            migrationBuilder.DropTable(
                name: "AiGlobalPolicies");

            migrationBuilder.DropTable(
                name: "AiProviderAccounts");

            migrationBuilder.DropTable(
                name: "AiProviders");

            migrationBuilder.DropTable(
                name: "AiQuotaCounters");

            migrationBuilder.DropTable(
                name: "AiQuotaPlans");

            migrationBuilder.DropTable(
                name: "AiToolInvocations");

            migrationBuilder.DropTable(
                name: "AiTools");

            migrationBuilder.DropTable(
                name: "AiUsageRecords");

            migrationBuilder.DropTable(
                name: "AiUserQuotaOverrides");

            migrationBuilder.DropTable(
                name: "AnalyticsEvents");

            migrationBuilder.DropTable(
                name: "AppliedPromoCodes");

            migrationBuilder.DropTable(
                name: "AudioRegenerationBatches");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "BackgroundJobs");

            migrationBuilder.DropTable(
                name: "BankAccountConfigs");

            migrationBuilder.DropTable(
                name: "BillingAddOns");

            migrationBuilder.DropTable(
                name: "BillingAddOnVersions");

            migrationBuilder.DropTable(
                name: "BillingCouponRedemptions");

            migrationBuilder.DropTable(
                name: "BillingCoupons");

            migrationBuilder.DropTable(
                name: "BillingCouponVersions");

            migrationBuilder.DropTable(
                name: "BillingEvents");

            migrationBuilder.DropTable(
                name: "BillingMetricDailies");

            migrationBuilder.DropTable(
                name: "BillingNotificationDispatchLogs");

            migrationBuilder.DropTable(
                name: "BillingNotificationTemplates");

            migrationBuilder.DropTable(
                name: "BillingPlans");

            migrationBuilder.DropTable(
                name: "BillingPlanVersions");

            migrationBuilder.DropTable(
                name: "BillingQuotes");

            migrationBuilder.DropTable(
                name: "CancellationIntents");

            migrationBuilder.DropTable(
                name: "CartItems");

            migrationBuilder.DropTable(
                name: "Certificates");

            migrationBuilder.DropTable(
                name: "CheckoutSessions");

            migrationBuilder.DropTable(
                name: "ChurnRiskSnapshots");

            migrationBuilder.DropTable(
                name: "ClassFeedbacks");

            migrationBuilder.DropTable(
                name: "ClassMaterials");

            migrationBuilder.DropTable(
                name: "ClassRecordingEmbeddings");

            migrationBuilder.DropTable(
                name: "CohortMembers");

            migrationBuilder.DropTable(
                name: "Cohorts");

            migrationBuilder.DropTable(
                name: "ContentCohortOverlays");

            migrationBuilder.DropTable(
                name: "ContentContributors");

            migrationBuilder.DropTable(
                name: "ContentGenerationJobs");

            migrationBuilder.DropTable(
                name: "ContentImportBatches");

            migrationBuilder.DropTable(
                name: "ContentLessons");

            migrationBuilder.DropTable(
                name: "ContentModules");

            migrationBuilder.DropTable(
                name: "ContentPackages");

            migrationBuilder.DropTable(
                name: "ContentPaperAssets");

            migrationBuilder.DropTable(
                name: "ContentPrograms");

            migrationBuilder.DropTable(
                name: "ContentPublishRequests");

            migrationBuilder.DropTable(
                name: "ContentReferences");

            migrationBuilder.DropTable(
                name: "ContentRevisions");

            migrationBuilder.DropTable(
                name: "ContentSubmissions");

            migrationBuilder.DropTable(
                name: "ContentTracks");

            migrationBuilder.DropTable(
                name: "ConversationEvaluations");

            migrationBuilder.DropTable(
                name: "ConversationSessionResumeTokens");

            migrationBuilder.DropTable(
                name: "ConversationSessions");

            migrationBuilder.DropTable(
                name: "ConversationSettings");

            migrationBuilder.DropTable(
                name: "ConversationTemplates");

            migrationBuilder.DropTable(
                name: "ConversationTurnAnnotations");

            migrationBuilder.DropTable(
                name: "ConversationTurns");

            migrationBuilder.DropTable(
                name: "Criteria");

            migrationBuilder.DropTable(
                name: "CrossSellRules");

            migrationBuilder.DropTable(
                name: "CustomerSubscriptions");

            migrationBuilder.DropTable(
                name: "DeflectionRules");

            migrationBuilder.DropTable(
                name: "DiagnosticSessions");

            migrationBuilder.DropTable(
                name: "DiagnosticSubtests");

            migrationBuilder.DropTable(
                name: "DictationDrills");

            migrationBuilder.DropTable(
                name: "DunningAttempts");

            migrationBuilder.DropTable(
                name: "DunningCampaigns");

            migrationBuilder.DropTable(
                name: "EmailOtpChallenges");

            migrationBuilder.DropTable(
                name: "Evaluations");

            migrationBuilder.DropTable(
                name: "ExamBookings");

            migrationBuilder.DropTable(
                name: "ExamFamilies");

            migrationBuilder.DropTable(
                name: "ExamTypes");

            migrationBuilder.DropTable(
                name: "ExchangeRates");

            migrationBuilder.DropTable(
                name: "ExpertAnnotationTemplates");

            migrationBuilder.DropTable(
                name: "ExpertAvailabilities");

            migrationBuilder.DropTable(
                name: "ExpertCalibrationCases");

            migrationBuilder.DropTable(
                name: "ExpertCalibrationNotes");

            migrationBuilder.DropTable(
                name: "ExpertCalibrationResults");

            migrationBuilder.DropTable(
                name: "ExpertCompensationRates");

            migrationBuilder.DropTable(
                name: "ExpertEarnings");

            migrationBuilder.DropTable(
                name: "ExpertMessageReplies");

            migrationBuilder.DropTable(
                name: "ExpertMessageThreads");

            migrationBuilder.DropTable(
                name: "ExpertMetricSnapshots");

            migrationBuilder.DropTable(
                name: "ExpertOnboardingProgresses");

            migrationBuilder.DropTable(
                name: "ExpertPayouts");

            migrationBuilder.DropTable(
                name: "ExpertReviewAmends");

            migrationBuilder.DropTable(
                name: "ExpertReviewAssignments");

            migrationBuilder.DropTable(
                name: "ExpertReviewDrafts");

            migrationBuilder.DropTable(
                name: "ExpertReviewerPayouts");

            migrationBuilder.DropTable(
                name: "ExpertSlaSnapshots");

            migrationBuilder.DropTable(
                name: "ExternalIdentityLinks");

            migrationBuilder.DropTable(
                name: "FeatureFlags");

            migrationBuilder.DropTable(
                name: "ForumCategories");

            migrationBuilder.DropTable(
                name: "ForumReplies");

            migrationBuilder.DropTable(
                name: "ForumThreads");

            migrationBuilder.DropTable(
                name: "FoundationResources");

            migrationBuilder.DropTable(
                name: "FreePreviewAssets");

            migrationBuilder.DropTable(
                name: "FreeTierConfigs");

            migrationBuilder.DropTable(
                name: "GatewayRoutingConfigs");

            migrationBuilder.DropTable(
                name: "Goals");

            migrationBuilder.DropTable(
                name: "GrammarLessons");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "InterlocutorScripts");

            migrationBuilder.DropTable(
                name: "InterlocutorTrainingProgress");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "LaunchReadinessSettings");

            migrationBuilder.DropTable(
                name: "LeaderboardEntries");

            migrationBuilder.DropTable(
                name: "LearnerAccentProgresses");

            migrationBuilder.DropTable(
                name: "LearnerAchievements");

            migrationBuilder.DropTable(
                name: "LearnerBadges");

            migrationBuilder.DropTable(
                name: "LearnerCertificates");

            migrationBuilder.DropTable(
                name: "LearnerDictationProgresses");

            migrationBuilder.DropTable(
                name: "LearnerEscalations");

            migrationBuilder.DropTable(
                name: "LearnerGrammarProgress");

            migrationBuilder.DropTable(
                name: "LearnerLessonProgresses");

            migrationBuilder.DropTable(
                name: "LearnerListeningLessonProgresses");

            migrationBuilder.DropTable(
                name: "LearnerListeningPathways");

            migrationBuilder.DropTable(
                name: "LearnerListeningProfiles");

            migrationBuilder.DropTable(
                name: "LearnerListeningSkillScores");

            migrationBuilder.DropTable(
                name: "LearnerListeningStrategyProgresses");

            migrationBuilder.DropTable(
                name: "LearnerPronunciationCards");

            migrationBuilder.DropTable(
                name: "LearnerPronunciationDiscriminationAttempts");

            migrationBuilder.DropTable(
                name: "LearnerPronunciationProgress");

            migrationBuilder.DropTable(
                name: "LearnerReadingPathways");

            migrationBuilder.DropTable(
                name: "LearnerReadingProfiles");

            migrationBuilder.DropTable(
                name: "LearnerRegistrationProfiles");

            migrationBuilder.DropTable(
                name: "LearnerSkillProfiles");

            migrationBuilder.DropTable(
                name: "LearnerSkillScores");

            migrationBuilder.DropTable(
                name: "LearnerStrategyProgress");

            migrationBuilder.DropTable(
                name: "LearnerStreaks");

            migrationBuilder.DropTable(
                name: "LearnerVideoProgress");

            migrationBuilder.DropTable(
                name: "LearnerVocabularies");

            migrationBuilder.DropTable(
                name: "LearnerVocabularyItems");

            migrationBuilder.DropTable(
                name: "LearnerWritingLessonProgresses");

            migrationBuilder.DropTable(
                name: "LearnerWritingPathways");

            migrationBuilder.DropTable(
                name: "LearnerWritingProfiles");

            migrationBuilder.DropTable(
                name: "LearnerXps");

            migrationBuilder.DropTable(
                name: "LearnerXPs");

            migrationBuilder.DropTable(
                name: "ListeningAnswers");

            migrationBuilder.DropTable(
                name: "ListeningAttemptNotes");

            migrationBuilder.DropTable(
                name: "ListeningDailyPlanItems");

            migrationBuilder.DropTable(
                name: "ListeningExpertFeedbacks");

            migrationBuilder.DropTable(
                name: "ListeningExtractionDrafts");

            migrationBuilder.DropTable(
                name: "ListeningLessons");

            migrationBuilder.DropTable(
                name: "ListeningMockTemplates");

            migrationBuilder.DropTable(
                name: "ListeningPathwayProgress");

            migrationBuilder.DropTable(
                name: "ListeningPolicies");

            migrationBuilder.DropTable(
                name: "ListeningPracticeNotes");

            migrationBuilder.DropTable(
                name: "ListeningPracticeSessions");

            migrationBuilder.DropTable(
                name: "ListeningQuestionAttempts");

            migrationBuilder.DropTable(
                name: "ListeningQuestionOptions");

            migrationBuilder.DropTable(
                name: "ListeningStrategies");

            migrationBuilder.DropTable(
                name: "ListeningTtsJobs");

            migrationBuilder.DropTable(
                name: "ListeningUserPolicyOverrides");

            migrationBuilder.DropTable(
                name: "LiveClassAttendances");

            migrationBuilder.DropTable(
                name: "LiveClassEnrollments");

            migrationBuilder.DropTable(
                name: "LiveClassWaitlistEntries");

            migrationBuilder.DropTable(
                name: "LiveClassWebhookEvents");

            migrationBuilder.DropTable(
                name: "ManualPaymentRequests");

            migrationBuilder.DropTable(
                name: "MarketingAssets");

            migrationBuilder.DropTable(
                name: "MfaRecoveryCodes");

            migrationBuilder.DropTable(
                name: "MobilePushTokens");

            migrationBuilder.DropTable(
                name: "MockContentReviews");

            migrationBuilder.DropTable(
                name: "MockEntitlementLedgers");

            migrationBuilder.DropTable(
                name: "MockItemAnalysisSnapshots");

            migrationBuilder.DropTable(
                name: "MockLiveRoomTransitions");

            migrationBuilder.DropTable(
                name: "MockProctoringEvents");

            migrationBuilder.DropTable(
                name: "MockReports");

            migrationBuilder.DropTable(
                name: "MockReviewReservations");

            migrationBuilder.DropTable(
                name: "NativeIapProductMappings");

            migrationBuilder.DropTable(
                name: "NotificationConsents");

            migrationBuilder.DropTable(
                name: "NotificationDeliveryAttempts");

            migrationBuilder.DropTable(
                name: "NotificationEvents");

            migrationBuilder.DropTable(
                name: "NotificationInboxItems");

            migrationBuilder.DropTable(
                name: "NotificationPolicyOverrides");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "NotificationSuppressions");

            migrationBuilder.DropTable(
                name: "NotificationTemplates");

            migrationBuilder.DropTable(
                name: "OrderRefunds");

            migrationBuilder.DropTable(
                name: "PackageContentRules");

            migrationBuilder.DropTable(
                name: "PaymentDisputes");

            migrationBuilder.DropTable(
                name: "PaymentMethodUpdateLinks");

            migrationBuilder.DropTable(
                name: "PaymentTransactions");

            migrationBuilder.DropTable(
                name: "PaymentWebhookEvents");

            migrationBuilder.DropTable(
                name: "PeerReviewFeedbacks");

            migrationBuilder.DropTable(
                name: "PeerReviewRequests");

            migrationBuilder.DropTable(
                name: "PermissionTemplates");

            migrationBuilder.DropTable(
                name: "PredictionSnapshots");

            migrationBuilder.DropTable(
                name: "PricingExperimentAssignments");

            migrationBuilder.DropTable(
                name: "PricingExperiments");

            migrationBuilder.DropTable(
                name: "PrivateSpeakingAuditLogs");

            migrationBuilder.DropTable(
                name: "PrivateSpeakingAvailabilityOverrides");

            migrationBuilder.DropTable(
                name: "PrivateSpeakingAvailabilityRules");

            migrationBuilder.DropTable(
                name: "PrivateSpeakingBookings");

            migrationBuilder.DropTable(
                name: "PrivateSpeakingConfigs");

            migrationBuilder.DropTable(
                name: "Professions");

            migrationBuilder.DropTable(
                name: "PronunciationAssessments");

            migrationBuilder.DropTable(
                name: "PronunciationAttempts");

            migrationBuilder.DropTable(
                name: "PronunciationCards");

            migrationBuilder.DropTable(
                name: "PronunciationDrills");

            migrationBuilder.DropTable(
                name: "PushSubscriptions");

            migrationBuilder.DropTable(
                name: "ReadinessHistories");

            migrationBuilder.DropTable(
                name: "ReadinessSnapshots");

            migrationBuilder.DropTable(
                name: "ReadingAnswers");

            migrationBuilder.DropTable(
                name: "ReadingDailyPlanItems");

            migrationBuilder.DropTable(
                name: "ReadingErrorBankEntries");

            migrationBuilder.DropTable(
                name: "ReadingExtractionDrafts");

            migrationBuilder.DropTable(
                name: "ReadingLessons");

            migrationBuilder.DropTable(
                name: "ReadingMockTemplates");

            migrationBuilder.DropTable(
                name: "ReadingPolicies");

            migrationBuilder.DropTable(
                name: "ReadingPracticeSessions");

            migrationBuilder.DropTable(
                name: "ReadingQuestionAttempts");

            migrationBuilder.DropTable(
                name: "ReadingQuestionDiscussionComments");

            migrationBuilder.DropTable(
                name: "ReadingQuestionReviewLogs");

            migrationBuilder.DropTable(
                name: "ReadingStrategies");

            migrationBuilder.DropTable(
                name: "ReadingStrategyProgresses");

            migrationBuilder.DropTable(
                name: "ReadingUserPolicyOverrides");

            migrationBuilder.DropTable(
                name: "RecallBookmarks");

            migrationBuilder.DropTable(
                name: "RecallSetTags");

            migrationBuilder.DropTable(
                name: "ReferralCodes");

            migrationBuilder.DropTable(
                name: "ReferralRecords");

            migrationBuilder.DropTable(
                name: "Referrals");

            migrationBuilder.DropTable(
                name: "RefreshTokenRecords");

            migrationBuilder.DropTable(
                name: "RegionPricings");

            migrationBuilder.DropTable(
                name: "RemediationTasks");

            migrationBuilder.DropTable(
                name: "ResultTemplateAssets");

            migrationBuilder.DropTable(
                name: "ReviewEscalations");

            migrationBuilder.DropTable(
                name: "ReviewItems");

            migrationBuilder.DropTable(
                name: "ReviewVoiceNotes");

            migrationBuilder.DropTable(
                name: "RulebookRuleRows");

            migrationBuilder.DropTable(
                name: "RulebookSectionRows");

            migrationBuilder.DropTable(
                name: "RuntimeSettings");

            migrationBuilder.DropTable(
                name: "ScheduleExceptions");

            migrationBuilder.DropTable(
                name: "Scholarships");

            migrationBuilder.DropTable(
                name: "ScoreGuaranteePledges");

            migrationBuilder.DropTable(
                name: "ScoringPolicies");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "SignupExamTypeCatalog");

            migrationBuilder.DropTable(
                name: "SignupProfessionCatalog");

            migrationBuilder.DropTable(
                name: "SignupSessionCatalog");

            migrationBuilder.DropTable(
                name: "SpeakingAiAssessments");

            migrationBuilder.DropTable(
                name: "SpeakingCalibrationSamples");

            migrationBuilder.DropTable(
                name: "SpeakingCalibrationScores");

            migrationBuilder.DropTable(
                name: "SpeakingCardBatchRequests");

            migrationBuilder.DropTable(
                name: "SpeakingComplianceConsents");

            migrationBuilder.DropTable(
                name: "SpeakingDrillAttempts");

            migrationBuilder.DropTable(
                name: "SpeakingFeedbackComments");

            migrationBuilder.DropTable(
                name: "SpeakingLiveRoomTokens");

            migrationBuilder.DropTable(
                name: "SpeakingMockSessions");

            migrationBuilder.DropTable(
                name: "SpeakingMockSets");

            migrationBuilder.DropTable(
                name: "SpeakingRecordings");

            migrationBuilder.DropTable(
                name: "SpeakingReviewVoiceNotes");

            migrationBuilder.DropTable(
                name: "SpeakingSharedResources");

            migrationBuilder.DropTable(
                name: "SpeakingTimestampedComments");

            migrationBuilder.DropTable(
                name: "SpeakingTranscripts");

            migrationBuilder.DropTable(
                name: "SpeakingTutorAssessments");

            migrationBuilder.DropTable(
                name: "SponsorAccounts");

            migrationBuilder.DropTable(
                name: "SponsorLearnerLinks");

            migrationBuilder.DropTable(
                name: "Sponsorships");

            migrationBuilder.DropTable(
                name: "StrategyGuides");

            migrationBuilder.DropTable(
                name: "StreakRecords");

            migrationBuilder.DropTable(
                name: "StudyCommitments");

            migrationBuilder.DropTable(
                name: "StudyGroupMembers");

            migrationBuilder.DropTable(
                name: "StudyGroups");

            migrationBuilder.DropTable(
                name: "StudyPlanItems");

            migrationBuilder.DropTable(
                name: "StudyPlans");

            migrationBuilder.DropTable(
                name: "StudyPlanTemplates");

            migrationBuilder.DropTable(
                name: "StudyPlanTemplateTiers");

            migrationBuilder.DropTable(
                name: "SubscriptionItems");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "Subtests");

            migrationBuilder.DropTable(
                name: "TaskTypes");

            migrationBuilder.DropTable(
                name: "TaxRules");

            migrationBuilder.DropTable(
                name: "TeacherClassMembers");

            migrationBuilder.DropTable(
                name: "TestimonialAssets");

            migrationBuilder.DropTable(
                name: "TutorAvailabilities");

            migrationBuilder.DropTable(
                name: "TutorBookAudioScripts");

            migrationBuilder.DropTable(
                name: "TutorBookUpdates");

            migrationBuilder.DropTable(
                name: "TutoringAvailabilities");

            migrationBuilder.DropTable(
                name: "TutoringSessions");

            migrationBuilder.DropTable(
                name: "UploadSessions");

            migrationBuilder.DropTable(
                name: "UsageForecastSnapshots");

            migrationBuilder.DropTable(
                name: "UserAiCredentials");

            migrationBuilder.DropTable(
                name: "UserAiPreferences");

            migrationBuilder.DropTable(
                name: "UserNotes");

            migrationBuilder.DropTable(
                name: "VideoLessons");

            migrationBuilder.DropTable(
                name: "VocabularyLists");

            migrationBuilder.DropTable(
                name: "VocabularyQuizResults");

            migrationBuilder.DropTable(
                name: "VocabularyTerms");

            migrationBuilder.DropTable(
                name: "VocabularyWords");

            migrationBuilder.DropTable(
                name: "Wallets");

            migrationBuilder.DropTable(
                name: "WalletTopUpTierConfigs");

            migrationBuilder.DropTable(
                name: "WalletTransactions");

            migrationBuilder.DropTable(
                name: "WritingAttemptAssets");

            migrationBuilder.DropTable(
                name: "WritingBuddyCheckIns");

            migrationBuilder.DropTable(
                name: "WritingBuddyMessages");

            migrationBuilder.DropTable(
                name: "WritingBuddyPairs");

            migrationBuilder.DropTable(
                name: "WritingCalibrationLetters");

            migrationBuilder.DropTable(
                name: "WritingCalibrationResults");

            migrationBuilder.DropTable(
                name: "WritingCalibrationRuns");

            migrationBuilder.DropTable(
                name: "WritingCanonRules");

            migrationBuilder.DropTable(
                name: "WritingCanonViolations");

            migrationBuilder.DropTable(
                name: "WritingCaseNoteDrillAttempts");

            migrationBuilder.DropTable(
                name: "WritingCaseNoteDrills");

            migrationBuilder.DropTable(
                name: "WritingCaseNoteDrillSentences");

            migrationBuilder.DropTable(
                name: "WritingCoachSessions");

            migrationBuilder.DropTable(
                name: "WritingCoachSuggestions");

            migrationBuilder.DropTable(
                name: "WritingCommonMistakes");

            migrationBuilder.DropTable(
                name: "WritingDailyPlanItems");

            migrationBuilder.DropTable(
                name: "WritingDiagnosticSessions");

            migrationBuilder.DropTable(
                name: "WritingDraftsV2");

            migrationBuilder.DropTable(
                name: "WritingDrillAttempts");

            migrationBuilder.DropTable(
                name: "WritingDrills");

            migrationBuilder.DropTable(
                name: "WritingExemplarAnnotations");

            migrationBuilder.DropTable(
                name: "WritingExemplarEmbeddings");

            migrationBuilder.DropTable(
                name: "WritingExemplars");

            migrationBuilder.DropTable(
                name: "WritingGrades");

            migrationBuilder.DropTable(
                name: "WritingLearnerMistakeStats");

            migrationBuilder.DropTable(
                name: "WritingLessonCompletionsV2");

            migrationBuilder.DropTable(
                name: "WritingLessons");

            migrationBuilder.DropTable(
                name: "WritingLessonsV2");

            migrationBuilder.DropTable(
                name: "WritingMocks");

            migrationBuilder.DropTable(
                name: "WritingMockSessions");

            migrationBuilder.DropTable(
                name: "WritingOcrJobs");

            migrationBuilder.DropTable(
                name: "WritingOptions");

            migrationBuilder.DropTable(
                name: "WritingPathwayItems");

            migrationBuilder.DropTable(
                name: "WritingReadinessScores");

            migrationBuilder.DropTable(
                name: "WritingRuleViolations");

            migrationBuilder.DropTable(
                name: "WritingScenarioEmbeddings");

            migrationBuilder.DropTable(
                name: "WritingScenarios");

            migrationBuilder.DropTable(
                name: "WritingScenarioStructuredSentences");

            migrationBuilder.DropTable(
                name: "WritingScoreAppeals");

            migrationBuilder.DropTable(
                name: "WritingShowcasePosts");

            migrationBuilder.DropTable(
                name: "WritingSubmissions");

            migrationBuilder.DropTable(
                name: "WritingTutorCalibrations");

            migrationBuilder.DropTable(
                name: "WritingTutorReviewAssignments");

            migrationBuilder.DropTable(
                name: "WritingTutorReviews");

            migrationBuilder.DropTable(
                name: "AiAssistantThreads");

            migrationBuilder.DropTable(
                name: "BillingPrices");

            migrationBuilder.DropTable(
                name: "Carts");

            migrationBuilder.DropTable(
                name: "LiveClassRecordings");

            migrationBuilder.DropTable(
                name: "InterlocutorTrainingModules");

            migrationBuilder.DropTable(
                name: "ListeningAttempts");

            migrationBuilder.DropTable(
                name: "ListeningQuestions");

            migrationBuilder.DropTable(
                name: "MockBookings");

            migrationBuilder.DropTable(
                name: "MockSectionAttempts");

            migrationBuilder.DropTable(
                name: "ReadingAttempts");

            migrationBuilder.DropTable(
                name: "ReadingQuestions");

            migrationBuilder.DropTable(
                name: "ReviewRequests");

            migrationBuilder.DropTable(
                name: "RulebookVersions");

            migrationBuilder.DropTable(
                name: "SpeakingDrillItems");

            migrationBuilder.DropTable(
                name: "SpeakingLiveRooms");

            migrationBuilder.DropTable(
                name: "TeacherClasses");

            migrationBuilder.DropTable(
                name: "Tutors");

            migrationBuilder.DropTable(
                name: "Attempts");

            migrationBuilder.DropTable(
                name: "BillingProducts");

            migrationBuilder.DropTable(
                name: "LiveClassSessions");

            migrationBuilder.DropTable(
                name: "ListeningExtracts");

            migrationBuilder.DropTable(
                name: "MockAttempts");

            migrationBuilder.DropTable(
                name: "MockBundleSections");

            migrationBuilder.DropTable(
                name: "ReadingTexts");

            migrationBuilder.DropTable(
                name: "MediaAssets");

            migrationBuilder.DropTable(
                name: "SpeakingSessions");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "LiveClasses");

            migrationBuilder.DropTable(
                name: "ListeningParts");

            migrationBuilder.DropTable(
                name: "MockBundles");

            migrationBuilder.DropTable(
                name: "ReadingParts");

            migrationBuilder.DropTable(
                name: "RolePlayCards");

            migrationBuilder.DropTable(
                name: "PrivateSpeakingTutorProfiles");

            migrationBuilder.DropTable(
                name: "ContentPapers");

            migrationBuilder.DropTable(
                name: "ContentItems");

            migrationBuilder.DropTable(
                name: "ExpertUsers");

            migrationBuilder.DropTable(
                name: "ApplicationUserAccounts");
        }
    }
}
