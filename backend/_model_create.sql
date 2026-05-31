-- NOTE (local dev): pgvector extension intentionally omitted. This PostgreSQL
-- install has no MSVC toolchain to build pgvector, and a fresh dev DB has no
-- writing-exemplar embeddings to search. The two nullable vector(1536) columns
-- (WritingExemplarEmbeddings.Embedding / WritingScenarioEmbeddings.Embedding)
-- are also omitted below. To enable the pgvector similarity feature locally,
-- install pgvector for PostgreSQL 17, run CREATE EXTENSION vector, and add the
-- two columns back. The JSON embedding columns (EmbeddingJson) remain intact.


CREATE TABLE "AccountFreezeEntitlements" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "FreezeRecordId" character varying(64),
    "ConsumedAt" timestamp with time zone,
    "ResetAt" timestamp with time zone,
    "ResetByAdminId" character varying(64),
    "ResetByAdminName" character varying(128),
    "ResetReason" character varying(256),
    CONSTRAINT "PK_AccountFreezeEntitlements" PRIMARY KEY ("Id")
);


CREATE TABLE "AccountFreezePolicies" (
    "Id" character varying(64) NOT NULL,
    "IsEnabled" boolean NOT NULL,
    "SelfServiceEnabled" boolean NOT NULL,
    "ApprovalMode" integer NOT NULL,
    "MinDurationDays" integer NOT NULL,
    "MaxDurationDays" integer NOT NULL,
    "AllowScheduling" boolean NOT NULL,
    "AccessMode" integer NOT NULL,
    "EntitlementPauseMode" integer NOT NULL,
    "RequireReason" boolean NOT NULL,
    "RequireInternalNotes" boolean NOT NULL,
    "AllowActivePaid" boolean NOT NULL,
    "AllowGracePeriod" boolean NOT NULL,
    "AllowTrial" boolean NOT NULL,
    "AllowComplimentary" boolean NOT NULL,
    "AllowCancelled" boolean NOT NULL,
    "AllowExpired" boolean NOT NULL,
    "AllowReviewOnly" boolean NOT NULL,
    "AllowPastDue" boolean NOT NULL,
    "AllowSuspended" boolean NOT NULL,
    "PolicyNotes" text NOT NULL,
    "EligibilityReasonCodesJson" text NOT NULL,
    "UpdatedByAdminId" text,
    "UpdatedByAdminName" text,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "Version" integer NOT NULL,
    CONSTRAINT "PK_AccountFreezePolicies" PRIMARY KEY ("Id")
);


CREATE TABLE "AccountFreezeRecords" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "RequestedByLearnerId" character varying(64),
    "RequestedByAdminId" character varying(64),
    "RequestedByAdminName" character varying(128),
    "ApprovedByAdminId" character varying(64),
    "ApprovedByAdminName" character varying(128),
    "RejectedByAdminId" character varying(64),
    "RejectedByAdminName" character varying(128),
    "EndedByAdminId" character varying(64),
    "EndedByAdminName" character varying(128),
    "Status" integer NOT NULL,
    "IsCurrent" boolean NOT NULL,
    "IsSelfService" boolean NOT NULL,
    "EntitlementConsumed" boolean NOT NULL,
    "EntitlementReset" boolean NOT NULL,
    "IsOverride" boolean NOT NULL,
    "RequestedAt" timestamp with time zone NOT NULL,
    "ScheduledStartAt" timestamp with time zone,
    "StartedAt" timestamp with time zone,
    "EndedAt" timestamp with time zone,
    "DurationDays" integer NOT NULL,
    "Reason" text NOT NULL,
    "InternalNotes" text,
    "PolicySnapshotJson" text NOT NULL,
    "PolicyVersionSnapshot" integer NOT NULL,
    "EligibilitySnapshotJson" text NOT NULL,
    "RejectionReason" text,
    "EndReason" text,
    "CancellationReason" text,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AccountFreezeRecords" PRIMARY KEY ("Id")
);


CREATE TABLE "Achievements" (
    "Id" character varying(64) NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Label" character varying(128) NOT NULL,
    "Description" character varying(512) NOT NULL,
    "Category" character varying(32) NOT NULL,
    "IconUrl" character varying(256),
    "XPReward" integer NOT NULL,
    "CriteriaJson" text NOT NULL,
    "SortOrder" integer NOT NULL,
    "Status" character varying(16) NOT NULL,
    CONSTRAINT "PK_Achievements" PRIMARY KEY ("Id")
);


CREATE TABLE "AdminUploadSessions" (
    "Id" character varying(64) NOT NULL,
    "AdminUserId" character varying(64) NOT NULL,
    "OriginalFilename" character varying(256) NOT NULL,
    "Extension" character varying(16) NOT NULL,
    "DeclaredMimeType" character varying(64) NOT NULL,
    "DeclaredSizeBytes" bigint NOT NULL,
    "ReceivedBytes" bigint NOT NULL,
    "TotalParts" integer NOT NULL,
    "PartsReceived" integer NOT NULL,
    "IntendedRole" character varying(32) NOT NULL,
    "State" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "Sha256" character varying(64),
    "MediaAssetId" character varying(64),
    CONSTRAINT "PK_AdminUploadSessions" PRIMARY KEY ("Id")
);


CREATE TABLE "AdminUsers" (
    "Id" character varying(64) NOT NULL,
    "DisplayName" character varying(256) NOT NULL,
    "Email" character varying(256) NOT NULL,
    "Role" character varying(64) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AdminUsers" PRIMARY KEY ("Id")
);


CREATE TABLE "AffiliateAttributions" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "AffiliateId" character varying(64) NOT NULL,
    "ClickedAt" timestamp with time zone NOT NULL,
    "AttributedAt" timestamp with time zone NOT NULL,
    "ConvertedAt" timestamp with time zone,
    "FirstPaymentTransactionId" character varying(64),
    CONSTRAINT "PK_AffiliateAttributions" PRIMARY KEY ("Id")
);


CREATE TABLE "AffiliateCommissions" (
    "Id" character varying(64) NOT NULL,
    "AffiliateId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "PaymentTransactionId" character varying(64) NOT NULL,
    "AmountAmount" numeric(12,2) NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "AccruedAt" timestamp with time zone NOT NULL,
    "PaidAt" timestamp with time zone,
    "ReversedAt" timestamp with time zone,
    "PayoutBatchId" character varying(256),
    CONSTRAINT "PK_AffiliateCommissions" PRIMARY KEY ("Id")
);


CREATE TABLE "Affiliates" (
    "Id" character varying(64) NOT NULL,
    "Code" character varying(64) NOT NULL,
    "OwnerName" character varying(128) NOT NULL,
    "ContactEmail" character varying(256) NOT NULL,
    "CommissionPercent" numeric(6,3) NOT NULL,
    "CookieDays" integer NOT NULL,
    "PayoutThresholdAmount" numeric(12,2) NOT NULL,
    "PayoutCurrency" character varying(8) NOT NULL,
    "PayoutMethod" character varying(32) NOT NULL,
    "PayoutDetailsEncrypted" character varying(4096) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Affiliates" PRIMARY KEY ("Id")
);


CREATE TABLE "AiAssistantThreads" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Role" character varying(16) NOT NULL,
    "Title" character varying(256),
    "ModelOverride" character varying(128),
    "IsArchived" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AiAssistantThreads" PRIMARY KEY ("Id")
);


CREATE TABLE "AiCodebaseChunks" (
    "Id" character varying(64) NOT NULL,
    "FilePath" character varying(1024) NOT NULL,
    "Language" character varying(32) NOT NULL,
    "ChunkType" character varying(32) NOT NULL,
    "SymbolName" character varying(256),
    "StartLine" integer NOT NULL,
    "EndLine" integer NOT NULL,
    "Content" text NOT NULL,
    "TokenCount" integer NOT NULL,
    "ContentHash" character varying(64) NOT NULL,
    "TsVectorConfig" character varying(64),
    "Embedding" real[],
    "IndexedAt" timestamp with time zone NOT NULL,
    "EmbeddedAt" timestamp with time zone,
    CONSTRAINT "PK_AiCodebaseChunks" PRIMARY KEY ("Id")
);


CREATE TABLE "AIConfigVersions" (
    "Id" character varying(64) NOT NULL,
    "Model" character varying(128) NOT NULL,
    "Provider" character varying(64) NOT NULL,
    "TaskType" character varying(64) NOT NULL,
    "Status" integer NOT NULL,
    "Accuracy" double precision NOT NULL,
    "ConfidenceThreshold" double precision NOT NULL,
    "RoutingRule" character varying(512),
    "ExperimentFlag" character varying(128),
    "PromptLabel" character varying(128) NOT NULL,
    "ConfidencePolicyJson" character varying(2048) NOT NULL,
    "ChangeNote" character varying(512),
    "CreatedBy" character varying(128) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AIConfigVersions" PRIMARY KEY ("Id")
);


CREATE TABLE "AiCreditLedger" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "TokensDelta" integer NOT NULL,
    "CostDeltaUsd" numeric NOT NULL,
    "Source" integer NOT NULL,
    "Description" character varying(256),
    "ReferenceId" character varying(128),
    "ExpiresAt" timestamp with time zone,
    "ExpiredByEntryId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedByAdminId" character varying(64),
    CONSTRAINT "PK_AiCreditLedger" PRIMARY KEY ("Id")
);


CREATE TABLE "AiFeatureRoutes" (
    "Id" character varying(64) NOT NULL,
    "FeatureCode" character varying(64) NOT NULL,
    "ProviderCode" character varying(64) NOT NULL,
    "Model" character varying(128),
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "UpdatedByAdminId" character varying(64),
    CONSTRAINT "PK_AiFeatureRoutes" PRIMARY KEY ("Id")
);


CREATE TABLE "AiFeatureToolGrants" (
    "Id" character varying(64) NOT NULL,
    "FeatureCode" character varying(64) NOT NULL,
    "ToolCode" character varying(64) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "UpdatedByAdminId" character varying(64),
    CONSTRAINT "PK_AiFeatureToolGrants" PRIMARY KEY ("Id")
);


CREATE TABLE "AiFileBackups" (
    "Id" character varying(64) NOT NULL,
    "ThreadId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "MessageId" character varying(64),
    "FilePath" character varying(1024) NOT NULL,
    "OriginalContent" text NOT NULL,
    "ContentHash" character varying(64) NOT NULL,
    "SizeBytes" bigint NOT NULL,
    "AutosaveBranch" character varying(256),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AiFileBackups" PRIMARY KEY ("Id")
);


CREATE TABLE "AiGlobalPolicies" (
    "Id" character varying(32) NOT NULL,
    "KillSwitchEnabled" boolean NOT NULL,
    "KillSwitchScope" integer NOT NULL,
    "KillSwitchReason" character varying(256),
    "DisabledFeaturesCsv" character varying(1024) NOT NULL,
    "MonthlyBudgetUsd" numeric NOT NULL,
    "SoftWarnPct" integer NOT NULL,
    "HardKillPct" integer NOT NULL,
    "CurrentSpendUsd" numeric NOT NULL,
    "AllowByokOnScoringFeatures" boolean NOT NULL,
    "AllowByokOnNonScoringFeatures" boolean NOT NULL,
    "DefaultPlatformProviderId" character varying(64) NOT NULL,
    "ByokErrorCooldownHours" integer NOT NULL,
    "ByokTransientRetryCount" integer NOT NULL,
    "AnomalyDetectionEnabled" boolean NOT NULL,
    "AnomalyMultiplierX" numeric NOT NULL,
    "RowVersion" integer NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "UpdatedByAdminId" character varying(64),
    CONSTRAINT "PK_AiGlobalPolicies" PRIMARY KEY ("Id")
);


CREATE TABLE "AiProviderAccounts" (
    "Id" character varying(64) NOT NULL,
    "ProviderId" character varying(64) NOT NULL,
    "Label" character varying(128) NOT NULL,
    "EncryptedApiKey" character varying(4096) NOT NULL,
    "ApiKeyHint" character varying(16) NOT NULL,
    "MonthlyRequestCap" integer,
    "RequestsUsedThisMonth" integer NOT NULL,
    "Priority" integer NOT NULL,
    "ExhaustedUntil" timestamp with time zone,
    "IsActive" boolean NOT NULL,
    "LastTestedAt" timestamp with time zone,
    "LastTestStatus" character varying(32),
    "LastTestError" character varying(512),
    "PeriodMonthKey" character varying(8) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "UpdatedByAdminId" character varying(64),
    CONSTRAINT "PK_AiProviderAccounts" PRIMARY KEY ("Id")
);


CREATE TABLE "AiProviders" (
    "Id" character varying(64) NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Dialect" integer NOT NULL,
    "Category" integer NOT NULL,
    "BaseUrl" character varying(512) NOT NULL,
    "EncryptedApiKey" character varying(4096) NOT NULL,
    "ApiKeyHint" character varying(16) NOT NULL,
    "DefaultModel" character varying(128) NOT NULL,
    "ReasoningEffort" character varying(16),
    "AllowedModelsCsv" character varying(4096) NOT NULL,
    "PricePer1kPromptTokens" numeric NOT NULL,
    "PricePer1kCompletionTokens" numeric NOT NULL,
    "RetryCount" integer NOT NULL,
    "CircuitBreakerThreshold" integer NOT NULL,
    "CircuitBreakerWindowSeconds" integer NOT NULL,
    "FailoverPriority" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "LastTestedAt" timestamp with time zone,
    "LastTestStatus" character varying(32),
    "LastTestError" character varying(512),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "UpdatedByAdminId" character varying(64),
    CONSTRAINT "PK_AiProviders" PRIMARY KEY ("Id")
);


CREATE TABLE "AiQuotaCounters" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "PeriodKey" character varying(32) NOT NULL,
    "TokensUsed" integer NOT NULL,
    "RequestsCount" integer NOT NULL,
    "CostAccumulatedUsd" numeric NOT NULL,
    "LastUpdatedAt" timestamp with time zone NOT NULL,
    "RowVersion" integer NOT NULL,
    CONSTRAINT "PK_AiQuotaCounters" PRIMARY KEY ("Id")
);


CREATE TABLE "AiQuotaPlans" (
    "Id" character varying(64) NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Description" character varying(512) NOT NULL,
    "Period" integer NOT NULL,
    "MonthlyTokenCap" integer NOT NULL,
    "DailyTokenCap" integer NOT NULL,
    "MaxConcurrentRequests" integer NOT NULL,
    "RolloverPolicy" integer NOT NULL,
    "RolloverCapPct" integer NOT NULL,
    "OveragePolicy" integer NOT NULL,
    "OverageRatePer1kTokens" numeric,
    "AutoUpgradeTargetPlanCode" character varying(64),
    "DegradeModel" character varying(128),
    "AllowedFeaturesCsv" character varying(1024) NOT NULL,
    "AllowedModelsCsv" character varying(1024) NOT NULL,
    "IsActive" boolean NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AiQuotaPlans" PRIMARY KEY ("Id")
);


CREATE TABLE "AiToolInvocations" (
    "Id" character varying(64) NOT NULL,
    "AiUsageRecordId" character varying(64) NOT NULL,
    "FeatureCode" character varying(64) NOT NULL,
    "ToolCode" character varying(64) NOT NULL,
    "Category" integer NOT NULL,
    "UserId" character varying(64),
    "TurnIndex" integer NOT NULL,
    "ArgsHash" character varying(64) NOT NULL,
    "ResultHash" character varying(64) NOT NULL,
    "Outcome" integer NOT NULL,
    "ErrorCode" character varying(64),
    "ErrorMessage" character varying(512),
    "LatencyMs" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AiToolInvocations" PRIMARY KEY ("Id")
);


CREATE TABLE "AiTools" (
    "Id" character varying(64) NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Description" character varying(512) NOT NULL,
    "Category" integer NOT NULL,
    "JsonSchemaArgs" character varying(8192) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "UpdatedByAdminId" character varying(64),
    CONSTRAINT "PK_AiTools" PRIMARY KEY ("Id")
);


CREATE TABLE "AiUserQuotaOverrides" (
    "UserId" character varying(64) NOT NULL,
    "MonthlyTokenCapOverride" integer,
    "DailyTokenCapOverride" integer,
    "ForcePlanCode" character varying(64),
    "AiDisabled" boolean NOT NULL,
    "Reason" character varying(512),
    "GrantedByAdminId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone,
    CONSTRAINT "PK_AiUserQuotaOverrides" PRIMARY KEY ("UserId")
);


CREATE TABLE "AnalyticsEvents" (
    "Id" character varying(64) NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "EventName" character varying(64) NOT NULL,
    "PayloadJson" jsonb NOT NULL,
    CONSTRAINT "PK_AnalyticsEvents" PRIMARY KEY ("OccurredAt", "Id")
);


CREATE TABLE "ApplicationUserAccounts" (
    "Id" character varying(64) NOT NULL,
    "Email" character varying(256) NOT NULL,
    "NormalizedEmail" character varying(256) NOT NULL,
    "PasswordHash" character varying(512) NOT NULL,
    "Role" character varying(32) NOT NULL,
    "ProtectedAuthenticatorSecret" character varying(1024),
    "EmailVerifiedAt" timestamp with time zone,
    "AuthenticatorEnabledAt" timestamp with time zone,
    "LastLoginAt" timestamp with time zone,
    "DeletedAt" timestamp with time zone,
    "Country" character varying(2),
    "PreferredCurrency" character varying(3),
    "PreferredRegion" character varying(16),
    "FailedSignInCount" integer NOT NULL,
    "LockoutUntil" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "StripeCustomerId" character varying(64),
    CONSTRAINT "PK_ApplicationUserAccounts" PRIMARY KEY ("Id")
);


CREATE TABLE "Attempts" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ContentId" character varying(64) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "Context" character varying(32) NOT NULL,
    "Mode" character varying(32) NOT NULL,
    "State" integer NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "SubmittedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    "ElapsedSeconds" integer NOT NULL,
    "DraftVersion" integer NOT NULL,
    "ParentAttemptId" character varying(64),
    "ComparisonGroupId" character varying(64),
    "DeviceType" character varying(32) NOT NULL,
    "LastClientSyncAt" timestamp with time zone,
    "DraftContent" text NOT NULL,
    "Scratchpad" text NOT NULL,
    "ChecklistJson" text NOT NULL,
    "AnswersJson" text NOT NULL,
    "AudioUploadState" integer NOT NULL,
    "AudioObjectKey" text,
    "AudioMetadataJson" text NOT NULL,
    "TranscriptJson" text NOT NULL,
    "AnalysisJson" jsonb NOT NULL,
    "ExamFamilyCode" character varying(16) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ModelVersionId" character varying(64),
    CONSTRAINT "PK_Attempts" PRIMARY KEY ("Id")
);


CREATE TABLE "AudioRegenerationBatches" (
    "Id" character varying(64) NOT NULL,
    "AudioType" character varying(32) NOT NULL,
    "Scope" character varying(32) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "TotalItems" integer NOT NULL,
    "CompletedItems" integer NOT NULL,
    "FailedItems" integer NOT NULL,
    "VoiceId" character varying(64) NOT NULL,
    "ModelVariant" character varying(32) NOT NULL,
    "ProviderName" character varying(64) NOT NULL,
    "Speed" double precision NOT NULL,
    "Pitch" double precision NOT NULL,
    "Emotion" character varying(256) NOT NULL,
    "Instructions" character varying(1000) NOT NULL,
    "RequestedBy" character varying(128) NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_AudioRegenerationBatches" PRIMARY KEY ("Id")
);


CREATE TABLE "BackgroundJobs" (
    "Id" character varying(64) NOT NULL,
    "Type" integer NOT NULL,
    "State" integer NOT NULL,
    "AttemptId" text,
    "ResourceId" text,
    "PayloadJson" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "AvailableAt" timestamp with time zone NOT NULL,
    "LastTransitionAt" timestamp with time zone NOT NULL,
    "StatusReasonCode" text NOT NULL,
    "StatusMessage" text NOT NULL,
    "Retryable" boolean NOT NULL,
    "RetryCount" integer NOT NULL,
    "RetryAfterMs" integer,
    CONSTRAINT "PK_BackgroundJobs" PRIMARY KEY ("Id")
);


CREATE TABLE "BankAccountConfigs" (
    "Id" character varying(64) NOT NULL,
    "Region" character varying(16) NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "BankName" character varying(128) NOT NULL,
    "AccountHolderName" character varying(128) NOT NULL,
    "Iban" character varying(64),
    "SwiftBic" character varying(64),
    "AccountNumber" character varying(128),
    "RoutingOrSortCode" character varying(128),
    "InstructionsMarkdown" character varying(2048),
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_BankAccountConfigs" PRIMARY KEY ("Id")
);


CREATE TABLE "BillingAddOns" (
    "Id" character varying(64) NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Description" character varying(1024) NOT NULL,
    "Price" numeric NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "Interval" character varying(32) NOT NULL,
    "Status" integer NOT NULL,
    "IsRecurring" boolean NOT NULL,
    "DurationDays" integer NOT NULL,
    "GrantCredits" integer NOT NULL,
    "GrantEntitlementsJson" character varying(2048) NOT NULL,
    "CompatiblePlanCodesJson" character varying(2048) NOT NULL,
    "ActiveVersionId" character varying(64),
    "LatestVersionId" character varying(64),
    "AppliesToAllPlans" boolean NOT NULL,
    "IsStackable" boolean NOT NULL,
    "QuantityStep" integer NOT NULL,
    "MaxQuantity" integer,
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "OriginalPriceGbp" numeric,
    "AddonKind" character varying(32) NOT NULL,
    "RequiresEligibleParent" boolean NOT NULL,
    "EligibilityFlag" character varying(32) NOT NULL,
    "LettersGranted" integer NOT NULL,
    "SessionsGranted" integer NOT NULL,
    CONSTRAINT "PK_BillingAddOns" PRIMARY KEY ("Id")
);


CREATE TABLE "BillingAddOnVersions" (
    "Id" character varying(64) NOT NULL,
    "AddOnId" character varying(64) NOT NULL,
    "VersionNumber" integer NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Description" character varying(1024) NOT NULL,
    "Price" numeric NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "Interval" character varying(32) NOT NULL,
    "Status" integer NOT NULL,
    "IsRecurring" boolean NOT NULL,
    "DurationDays" integer NOT NULL,
    "GrantCredits" integer NOT NULL,
    "GrantEntitlementsJson" character varying(2048) NOT NULL,
    "CompatiblePlanCodesJson" character varying(2048) NOT NULL,
    "AppliesToAllPlans" boolean NOT NULL,
    "IsStackable" boolean NOT NULL,
    "QuantityStep" integer NOT NULL,
    "MaxQuantity" integer,
    "DisplayOrder" integer NOT NULL,
    "CreatedByAdminId" character varying(64),
    "CreatedByAdminName" character varying(128),
    "CreatedAt" timestamp with time zone NOT NULL,
    "OriginalPriceGbp" numeric,
    "AddonKind" character varying(32) NOT NULL,
    "RequiresEligibleParent" boolean NOT NULL,
    "EligibilityFlag" character varying(32) NOT NULL,
    "LettersGranted" integer NOT NULL,
    "SessionsGranted" integer NOT NULL,
    CONSTRAINT "PK_BillingAddOnVersions" PRIMARY KEY ("Id")
);


CREATE TABLE "BillingCouponRedemptions" (
    "Id" character varying(64) NOT NULL,
    "CouponCode" character varying(64) NOT NULL,
    "CouponId" character varying(64),
    "CouponVersionId" character varying(64),
    "UserId" character varying(64) NOT NULL,
    "QuoteId" character varying(64) NOT NULL,
    "CheckoutSessionId" character varying(256),
    "SubscriptionId" character varying(64),
    "DiscountAmount" numeric NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "Status" integer NOT NULL,
    "RedeemedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_BillingCouponRedemptions" PRIMARY KEY ("Id")
);


CREATE TABLE "BillingCoupons" (
    "Id" character varying(64) NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Description" character varying(1024) NOT NULL,
    "DiscountType" integer NOT NULL,
    "DiscountValue" numeric NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "Status" integer NOT NULL,
    "StartsAt" timestamp with time zone,
    "EndsAt" timestamp with time zone,
    "UsageLimitTotal" integer,
    "UsageLimitPerUser" integer,
    "MinimumSubtotal" numeric,
    "ApplicablePlanCodesJson" character varying(2048) NOT NULL,
    "ApplicableAddOnCodesJson" character varying(2048) NOT NULL,
    "ActiveVersionId" character varying(64),
    "LatestVersionId" character varying(64),
    "IsStackable" boolean NOT NULL,
    "Notes" character varying(1024),
    "RedemptionCount" integer NOT NULL,
    "CouponVariant" character varying(32) NOT NULL,
    "VariantMetadataJson" character varying(2048) NOT NULL,
    "EligibleCountriesJson" character varying(1024) NOT NULL,
    "NewUsersOnly" boolean NOT NULL,
    "ExistingUsersOnly" boolean NOT NULL,
    "StackableWithReferral" boolean NOT NULL,
    "StackableWithAffiliate" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_BillingCoupons" PRIMARY KEY ("Id")
);


CREATE TABLE "BillingCouponVersions" (
    "Id" character varying(64) NOT NULL,
    "CouponId" character varying(64) NOT NULL,
    "VersionNumber" integer NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Description" character varying(1024) NOT NULL,
    "DiscountType" integer NOT NULL,
    "DiscountValue" numeric NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "Status" integer NOT NULL,
    "StartsAt" timestamp with time zone,
    "EndsAt" timestamp with time zone,
    "UsageLimitTotal" integer,
    "UsageLimitPerUser" integer,
    "MinimumSubtotal" numeric,
    "ApplicablePlanCodesJson" character varying(2048) NOT NULL,
    "ApplicableAddOnCodesJson" character varying(2048) NOT NULL,
    "IsStackable" boolean NOT NULL,
    "Notes" character varying(1024),
    "CreatedByAdminId" character varying(64),
    "CreatedByAdminName" character varying(128),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_BillingCouponVersions" PRIMARY KEY ("Id")
);


CREATE TABLE "BillingEvents" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64),
    "SubscriptionId" character varying(64),
    "QuoteId" character varying(64),
    "EventType" character varying(64) NOT NULL,
    "EntityType" character varying(64) NOT NULL,
    "EntityId" character varying(256),
    "PayloadJson" character varying(4096) NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_BillingEvents" PRIMARY KEY ("Id")
);


CREATE TABLE "BillingMetricDailies" (
    "Id" character varying(64) NOT NULL,
    "MetricDate" date NOT NULL,
    "MetricCode" character varying(64) NOT NULL,
    "Region" character varying(16) NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "Value" numeric(18,4) NOT NULL,
    "DetailsJson" character varying(2048),
    "ComputedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_BillingMetricDailies" PRIMARY KEY ("Id")
);


CREATE TABLE "BillingNotificationDispatchLogs" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "EventCode" character varying(64) NOT NULL,
    "EventId" character varying(64) NOT NULL,
    "TemplateCode" character varying(64) NOT NULL,
    "Channel" character varying(16) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "FailureReason" character varying(512),
    "CreatedAt" timestamp with time zone NOT NULL,
    "SentAt" timestamp with time zone,
    CONSTRAINT "PK_BillingNotificationDispatchLogs" PRIMARY KEY ("Id")
);


CREATE TABLE "BillingNotificationTemplates" (
    "Id" character varying(64) NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Channel" character varying(16) NOT NULL,
    "LocaleTag" character varying(16) NOT NULL,
    "Subject" character varying(256),
    "BodyTemplate" character varying(8192) NOT NULL,
    "VariablesJson" character varying(1024) NOT NULL,
    "Version" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_BillingNotificationTemplates" PRIMARY KEY ("Id")
);


CREATE TABLE "BillingPlans" (
    "Id" character varying(64) NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Description" character varying(1024) NOT NULL,
    "Price" numeric NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "Interval" character varying(16) NOT NULL,
    "DurationMonths" integer NOT NULL,
    "IsVisible" boolean NOT NULL,
    "IsRenewable" boolean NOT NULL,
    "TrialDays" integer NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "IncludedCredits" integer NOT NULL,
    "DiagnosticMockEntitlement" character varying(32) NOT NULL,
    "IncludedSubtestsJson" character varying(2048) NOT NULL,
    "EntitlementsJson" character varying(2048) NOT NULL,
    "ActiveVersionId" character varying(64),
    "LatestVersionId" character varying(64),
    "ActiveSubscribers" integer NOT NULL,
    "Status" integer NOT NULL,
    "ArchivedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "OriginalPriceGbp" numeric,
    "AccessDurationDays" integer NOT NULL,
    "WritingAddonsEnabled" boolean NOT NULL,
    "SpeakingAddonsEnabled" boolean NOT NULL,
    "TutorBookDiscountEnabled" boolean NOT NULL,
    "Profession" character varying(32) NOT NULL,
    "ProductCategory" character varying(32) NOT NULL,
    "DashboardModulesJson" character varying(2048) NOT NULL,
    "BundledWritingAssessments" integer NOT NULL,
    "BundledSpeakingSessions" integer NOT NULL,
    "BundledAiCredits" integer NOT NULL,
    "BundledTutorBook" boolean NOT NULL,
    "BundledBasicEnglish" boolean NOT NULL,
    "IsDraft" boolean NOT NULL,
    "ExtensionAllowed" boolean NOT NULL,
    "RecallUpdatesEnabled" boolean NOT NULL,
    CONSTRAINT "PK_BillingPlans" PRIMARY KEY ("Id")
);


CREATE TABLE "BillingPlanVersions" (
    "Id" character varying(64) NOT NULL,
    "PlanId" character varying(64) NOT NULL,
    "VersionNumber" integer NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Description" character varying(1024) NOT NULL,
    "Price" numeric NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "Interval" character varying(16) NOT NULL,
    "DurationMonths" integer NOT NULL,
    "IsVisible" boolean NOT NULL,
    "IsRenewable" boolean NOT NULL,
    "TrialDays" integer NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "IncludedCredits" integer NOT NULL,
    "IncludedSubtestsJson" character varying(2048) NOT NULL,
    "EntitlementsJson" character varying(2048) NOT NULL,
    "Status" integer NOT NULL,
    "ArchivedAt" timestamp with time zone,
    "CreatedByAdminId" character varying(64),
    "CreatedByAdminName" character varying(128),
    "CreatedAt" timestamp with time zone NOT NULL,
    "OriginalPriceGbp" numeric,
    "AccessDurationDays" integer NOT NULL,
    "WritingAddonsEnabled" boolean NOT NULL,
    "SpeakingAddonsEnabled" boolean NOT NULL,
    "TutorBookDiscountEnabled" boolean NOT NULL,
    "Profession" character varying(32) NOT NULL,
    "ProductCategory" character varying(32) NOT NULL,
    "DashboardModulesJson" character varying(2048) NOT NULL,
    "BundledWritingAssessments" integer NOT NULL,
    "BundledSpeakingSessions" integer NOT NULL,
    "BundledAiCredits" integer NOT NULL,
    "BundledTutorBook" boolean NOT NULL,
    "BundledBasicEnglish" boolean NOT NULL,
    "IsDraft" boolean NOT NULL,
    "ExtensionAllowed" boolean NOT NULL,
    "RecallUpdatesEnabled" boolean NOT NULL,
    CONSTRAINT "PK_BillingPlanVersions" PRIMARY KEY ("Id")
);


CREATE TABLE "BillingProducts" (
    "Id" uuid NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Name" character varying(256) NOT NULL,
    "Description" character varying(1024),
    "ProductType" character varying(32) NOT NULL,
    "StripeProductId" character varying(64),
    "IsActive" boolean NOT NULL,
    "MetadataJson" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_BillingProducts" PRIMARY KEY ("Id")
);


CREATE TABLE "BillingQuotes" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "SubscriptionId" character varying(64),
    "PlanCode" character varying(64),
    "PlanVersionId" character varying(64),
    "AddOnCodesJson" character varying(1024) NOT NULL,
    "AddOnVersionIdsJson" character varying(1024) NOT NULL,
    "CouponCode" character varying(64),
    "CouponVersionId" character varying(64),
    "Currency" character varying(8) NOT NULL,
    "SubtotalAmount" numeric NOT NULL,
    "DiscountAmount" numeric NOT NULL,
    "TotalAmount" numeric NOT NULL,
    "Status" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "CheckoutSessionId" character varying(256),
    "IdempotencyKey" character varying(64),
    "ExperimentAssignmentId" character varying(64),
    "SnapshotJson" character varying(4096) NOT NULL,
    CONSTRAINT "PK_BillingQuotes" PRIMARY KEY ("Id")
);


CREATE TABLE "CancellationIntents" (
    "Id" character varying(64) NOT NULL,
    "SubscriptionId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Reason" character varying(32) NOT NULL,
    "ReasonDetail" character varying(1024),
    "Status" character varying(32) NOT NULL,
    "OfferedCouponCode" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "ResolvedAt" timestamp with time zone,
    CONSTRAINT "PK_CancellationIntents" PRIMARY KEY ("Id")
);


CREATE TABLE "Carts" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64),
    "SessionToken" character varying(256),
    "Status" character varying(32) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "RecoveryEmailSentAt" timestamp with time zone,
    CONSTRAINT "PK_Carts" PRIMARY KEY ("Id")
);


CREATE TABLE "Certificates" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "UserDisplayName" character varying(128) NOT NULL,
    "Type" character varying(32) NOT NULL,
    "Title" character varying(256) NOT NULL,
    "Description" character varying(512) NOT NULL,
    "DataJson" text NOT NULL,
    "PdfUrl" character varying(512),
    "VerificationCode" character varying(64) NOT NULL,
    "IssuedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Certificates" PRIMARY KEY ("Id")
);


CREATE TABLE "CheckoutSessions" (
    "Id" uuid NOT NULL,
    "CartId" uuid,
    "UserId" character varying(64) NOT NULL,
    "StripeSessionId" character varying(256),
    "IdempotencyKey" character varying(64) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "TotalAmount" numeric NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "MetadataJson" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "FulfilledAt" timestamp with time zone,
    "ExpiresAt" timestamp with time zone,
    CONSTRAINT "PK_CheckoutSessions" PRIMARY KEY ("Id")
);


CREATE TABLE "ChurnRiskSnapshots" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "SnapshotDate" date NOT NULL,
    "RiskScore" numeric(6,4) NOT NULL,
    "RiskBand" character varying(8) NOT NULL,
    "FactorsJson" character varying(2048) NOT NULL,
    "RecommendedAction" character varying(64),
    "ActionDispatched" boolean NOT NULL,
    "ComputedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ChurnRiskSnapshots" PRIMARY KEY ("Id")
);


CREATE TABLE "ClassFeedbacks" (
    "Id" character varying(64) NOT NULL,
    "ClassSessionId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Rating" integer NOT NULL,
    "Comment" character varying(4096),
    "RecommendToFriend" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ClassFeedbacks" PRIMARY KEY ("Id")
);


CREATE TABLE "ClassMaterials" (
    "Id" character varying(64) NOT NULL,
    "LiveClassId" character varying(64) NOT NULL,
    "ClassSessionId" character varying(64),
    "Title" character varying(180) NOT NULL,
    "FileUrl" character varying(1024) NOT NULL,
    "MimeType" character varying(128),
    "Visibility" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ClassMaterials" PRIMARY KEY ("Id")
);


CREATE TABLE "CohortMembers" (
    "Id" uuid NOT NULL,
    "CohortId" character varying(64) NOT NULL,
    "LearnerId" character varying(64) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "EnrolledAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_CohortMembers" PRIMARY KEY ("Id")
);


CREATE TABLE "Cohorts" (
    "Id" character varying(64) NOT NULL,
    "SponsorId" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "StartDate" date,
    "EndDate" date,
    "MaxSeats" integer NOT NULL,
    "EnrolledCount" integer NOT NULL,
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Cohorts" PRIMARY KEY ("Id")
);


CREATE TABLE "ContentCohortOverlays" (
    "Id" character varying(64) NOT NULL,
    "ProgramId" character varying(64) NOT NULL,
    "CohortCode" character varying(64) NOT NULL,
    "CohortTitle" character varying(200) NOT NULL,
    "StartDate" date,
    "EndDate" date,
    "ReleaseScheduleJson" text NOT NULL,
    "Status" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ContentCohortOverlays" PRIMARY KEY ("Id")
);


CREATE TABLE "ContentContributors" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "DisplayName" character varying(128) NOT NULL,
    "Bio" character varying(1024),
    "VerificationStatus" character varying(32) NOT NULL,
    "SubmissionCount" integer NOT NULL,
    "ApprovedCount" integer NOT NULL,
    "Rating" double precision NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ContentContributors" PRIMARY KEY ("Id")
);


CREATE TABLE "ContentGenerationJobs" (
    "Id" character varying(64) NOT NULL,
    "RequestedBy" character varying(64) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "TaskTypeId" character varying(64),
    "ProfessionId" character varying(32),
    "Difficulty" character varying(16) NOT NULL,
    "RequestedCount" integer NOT NULL,
    "GeneratedCount" integer NOT NULL,
    "PromptConfigJson" text NOT NULL,
    "GeneratedContentIdsJson" text NOT NULL,
    "State" character varying(32) NOT NULL,
    "ErrorMessage" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_ContentGenerationJobs" PRIMARY KEY ("Id")
);


CREATE TABLE "ContentImportBatches" (
    "Id" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "TotalItems" integer NOT NULL,
    "ProcessedItems" integer NOT NULL,
    "FailedItems" integer NOT NULL,
    "CreatedBy" character varying(100),
    "ErrorLogJson" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_ContentImportBatches" PRIMARY KEY ("Id")
);


CREATE TABLE "ContentItems" (
    "Id" character varying(64) NOT NULL,
    "ContentType" character varying(32) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "ProfessionId" character varying(32),
    "Title" character varying(200) NOT NULL,
    "Difficulty" character varying(32) NOT NULL,
    "EstimatedDurationMinutes" integer NOT NULL,
    "CriteriaFocusJson" text NOT NULL,
    "ScenarioType" character varying(64),
    "ModeSupportJson" text NOT NULL,
    "PublishedRevisionId" character varying(64) NOT NULL,
    "Status" integer NOT NULL,
    "CaseNotes" text,
    "DetailJson" text NOT NULL,
    "ModelAnswerJson" text NOT NULL,
    "CreatedBy" character varying(100),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "PublishedAt" timestamp with time zone,
    "ArchivedAt" timestamp with time zone,
    "ExamFamilyCode" character varying(16) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "DifficultyRating" integer NOT NULL,
    "SourceType" character varying(32) NOT NULL,
    "QaStatus" character varying(32) NOT NULL,
    "QaReviewedBy" character varying(64),
    "QaReviewedAt" timestamp with time zone,
    "PerformanceMetricsJson" text,
    "InstructionLanguage" character varying(8) NOT NULL,
    "ContentLanguage" character varying(8) NOT NULL,
    "ProfessionIdsJson" text NOT NULL,
    "PackageEligibilityJson" text NOT NULL,
    "CohortRelevance" character varying(64),
    "SourceProvenance" character varying(32) NOT NULL,
    "RightsStatus" character varying(32) NOT NULL,
    "FreshnessConfidence" character varying(32) NOT NULL,
    "SupersededById" character varying(64),
    "DuplicateGroupId" character varying(64),
    "MediaManifestJson" text NOT NULL,
    "CanonicalSourcePath" character varying(512),
    "ImportBatchId" character varying(64),
    "IsPreviewEligible" boolean NOT NULL,
    "IsDiagnosticEligible" boolean NOT NULL,
    "IsMockEligible" boolean NOT NULL,
    "QualityScore" integer NOT NULL,
    CONSTRAINT "PK_ContentItems" PRIMARY KEY ("Id")
);


CREATE TABLE "ContentLessons" (
    "Id" character varying(64) NOT NULL,
    "ModuleId" character varying(64) NOT NULL,
    "ContentItemId" character varying(64),
    "Title" character varying(200) NOT NULL,
    "LessonType" character varying(32) NOT NULL,
    "MediaAssetId" character varying(64),
    "DisplayOrder" integer NOT NULL,
    "Status" integer NOT NULL,
    CONSTRAINT "PK_ContentLessons" PRIMARY KEY ("Id")
);


CREATE TABLE "ContentModules" (
    "Id" character varying(64) NOT NULL,
    "TrackId" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Description" character varying(1024),
    "DisplayOrder" integer NOT NULL,
    "EstimatedDurationMinutes" integer NOT NULL,
    "PrerequisiteModuleId" character varying(64),
    "Status" integer NOT NULL,
    CONSTRAINT "PK_ContentModules" PRIMARY KEY ("Id")
);


CREATE TABLE "ContentPackages" (
    "Id" character varying(64) NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Description" character varying(1024),
    "PackageType" character varying(32) NOT NULL,
    "ProfessionId" character varying(32),
    "InstructionLanguage" character varying(8) NOT NULL,
    "BillingPlanId" character varying(64),
    "BillingAddOnId" character varying(64),
    "Status" integer NOT NULL,
    "ThumbnailUrl" character varying(512),
    "ComparisonFeaturesJson" text NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "ExamFamilyCode" character varying(16) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "PublishedAt" timestamp with time zone,
    CONSTRAINT "PK_ContentPackages" PRIMARY KEY ("Id")
);


CREATE TABLE "ContentPapers" (
    "Id" character varying(64) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Slug" character varying(200) NOT NULL,
    "ProfessionId" character varying(32),
    "AppliesToAllProfessions" boolean NOT NULL,
    "Difficulty" character varying(32) NOT NULL,
    "EstimatedDurationMinutes" integer NOT NULL,
    "Status" integer NOT NULL,
    "PublishedRevisionId" character varying(64),
    "CardType" character varying(64),
    "LetterType" character varying(64),
    "ListeningSequenceJson" text,
    "Priority" integer NOT NULL,
    "TagsCsv" character varying(512) NOT NULL,
    "SourceProvenance" character varying(256),
    "ExtractedTextJson" text NOT NULL,
    "CreatedByAdminId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "PublishedAt" timestamp with time zone,
    "ArchivedAt" timestamp with time zone,
    "IntegrityAcknowledgedByAdminId" character varying(64),
    "IntegrityAcknowledgedAt" timestamp with time zone,
    "RowVersion" integer NOT NULL,
    CONSTRAINT "PK_ContentPapers" PRIMARY KEY ("Id")
);


CREATE TABLE "ContentPrograms" (
    "Id" character varying(64) NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Description" character varying(1024),
    "ProfessionId" character varying(32),
    "InstructionLanguage" character varying(8) NOT NULL,
    "ProgramType" character varying(32) NOT NULL,
    "Status" integer NOT NULL,
    "ThumbnailUrl" character varying(512),
    "DisplayOrder" integer NOT NULL,
    "EstimatedDurationMinutes" integer NOT NULL,
    "ExamFamilyCode" character varying(16) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "CreatedBy" character varying(100),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "PublishedAt" timestamp with time zone,
    "ArchivedAt" timestamp with time zone,
    CONSTRAINT "PK_ContentPrograms" PRIMARY KEY ("Id")
);


CREATE TABLE "ContentPublishRequests" (
    "Id" character varying(64) NOT NULL,
    "ContentItemId" character varying(64) NOT NULL,
    "RequestedBy" character varying(64) NOT NULL,
    "RequestedByName" character varying(128) NOT NULL,
    "ReviewedBy" character varying(64),
    "ReviewedByName" character varying(128),
    "Status" character varying(32) NOT NULL,
    "Stage" character varying(32) NOT NULL,
    "RequestNote" character varying(512),
    "ReviewNote" character varying(512),
    "EditorReviewedBy" character varying(64),
    "EditorReviewedByName" character varying(128),
    "EditorReviewedAt" timestamp with time zone,
    "EditorNotes" character varying(512),
    "PublisherApprovedBy" character varying(64),
    "PublisherApprovedByName" character varying(128),
    "PublisherApprovedAt" timestamp with time zone,
    "PublisherNotes" character varying(512),
    "RejectedBy" character varying(64),
    "RejectedByName" character varying(128),
    "RejectedAt" timestamp with time zone,
    "RejectionReason" character varying(512),
    "RejectionStage" character varying(32),
    "RequestedAt" timestamp with time zone NOT NULL,
    "ReviewedAt" timestamp with time zone,
    CONSTRAINT "PK_ContentPublishRequests" PRIMARY KEY ("Id")
);


CREATE TABLE "ContentReferences" (
    "Id" character varying(64) NOT NULL,
    "ModuleId" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "ReferenceType" character varying(32) NOT NULL,
    "MediaAssetId" character varying(64),
    "ExternalUrl" character varying(1024),
    "DisplayOrder" integer NOT NULL,
    "Status" integer NOT NULL,
    CONSTRAINT "PK_ContentReferences" PRIMARY KEY ("Id")
);


CREATE TABLE "ContentRevisions" (
    "Id" character varying(64) NOT NULL,
    "ContentItemId" character varying(64) NOT NULL,
    "RevisionNumber" integer NOT NULL,
    "State" character varying(32) NOT NULL,
    "ChangeNote" character varying(512),
    "SnapshotJson" text NOT NULL,
    "CreatedBy" character varying(128) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ContentRevisions" PRIMARY KEY ("Id")
);


CREATE TABLE "ContentSubmissions" (
    "Id" character varying(64) NOT NULL,
    "ContributorId" character varying(64) NOT NULL,
    "ExamFamilyCode" character varying(16) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Description" character varying(1024),
    "TaskTypeId" character varying(64),
    "ContentPayloadJson" text NOT NULL,
    "ContentType" character varying(32) NOT NULL,
    "ProfessionId" character varying(32),
    "Difficulty" character varying(32),
    "Tags" character varying(512),
    "Status" character varying(32) NOT NULL,
    "ReviewedBy" character varying(64),
    "ReviewNotes" text,
    "PublishedContentId" character varying(64),
    "SubmittedAt" timestamp with time zone NOT NULL,
    "ApprovedAt" timestamp with time zone,
    "ReviewedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ContentSubmissions" PRIMARY KEY ("Id")
);


CREATE TABLE "ContentTracks" (
    "Id" character varying(64) NOT NULL,
    "ProgramId" character varying(64) NOT NULL,
    "SubtestCode" character varying(32),
    "Title" character varying(200) NOT NULL,
    "Description" character varying(1024),
    "DisplayOrder" integer NOT NULL,
    "Status" integer NOT NULL,
    CONSTRAINT "PK_ContentTracks" PRIMARY KEY ("Id")
);


CREATE TABLE "ConversationEvaluations" (
    "Id" character varying(64) NOT NULL,
    "SessionId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "OverallScaled" integer NOT NULL,
    "OverallGrade" character varying(4) NOT NULL,
    "Passed" boolean NOT NULL,
    "CountryVariant" character varying(4),
    "CriteriaJson" text NOT NULL,
    "StrengthsJson" text NOT NULL,
    "ImprovementsJson" text NOT NULL,
    "SuggestedPracticeJson" text NOT NULL,
    "AppliedRuleIdsJson" text NOT NULL,
    "RulebookVersion" character varying(32) NOT NULL,
    "Advisory" character varying(512),
    "AiUsageId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ConversationEvaluations" PRIMARY KEY ("Id")
);


CREATE TABLE "ConversationSessionResumeTokens" (
    "Id" character varying(64) NOT NULL,
    "SessionId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "TokenHash" character varying(128) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "LastUsedAt" timestamp with time zone,
    "ConsumedAt" timestamp with time zone,
    "RevokedAt" timestamp with time zone,
    CONSTRAINT "PK_ConversationSessionResumeTokens" PRIMARY KEY ("Id")
);


CREATE TABLE "ConversationSessions" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ContentId" character varying(64),
    "TemplateId" character varying(64),
    "ExamTypeCode" character varying(16) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "TaskTypeCode" character varying(64) NOT NULL,
    "Profession" character varying(32) NOT NULL,
    "ScenarioJson" text NOT NULL,
    "State" character varying(32) NOT NULL,
    "TurnCount" integer NOT NULL,
    "DurationSeconds" integer NOT NULL,
    "TranscriptJson" text NOT NULL,
    "AudioConsentVersion" character varying(96),
    "RecordingConsentAcceptedAt" timestamp with time zone,
    "VendorConsentAcceptedAt" timestamp with time zone,
    "EvaluationId" character varying(64),
    "LastErrorCode" character varying(256),
    "CreatedAt" timestamp with time zone NOT NULL,
    "StartedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_ConversationSessions" PRIMARY KEY ("Id")
);


CREATE TABLE "ConversationSettings" (
    "Id" character varying(32) NOT NULL,
    "Enabled" boolean,
    "AsrProvider" character varying(32),
    "TtsProvider" character varying(32),
    "AzureSpeechKeyEncrypted" text,
    "AzureSpeechRegion" character varying(64),
    "AzureLocale" character varying(16),
    "AzureTtsDefaultVoice" character varying(128),
    "WhisperBaseUrl" character varying(256),
    "WhisperApiKeyEncrypted" text,
    "WhisperModel" character varying(64),
    "DeepgramApiKeyEncrypted" text,
    "DeepgramModel" character varying(64),
    "DeepgramLanguage" character varying(16),
    "RealtimeSttEnabled" boolean,
    "RealtimeAsrProvider" character varying(64),
    "RealtimeSttAllowRealProvider" boolean,
    "RealtimeSttRealProviderProductionAuthorized" boolean,
    "RealtimeSttFallbackToBatch" boolean,
    "RealtimeSttProviderConnectTimeoutSeconds" integer,
    "RealtimeSttMaxChunkBytes" integer,
    "RealtimeSttPartialMinIntervalMs" integer,
    "RealtimeSttTurnIdleTimeoutSeconds" integer,
    "RealtimeSttMaxConcurrentStreamsPerUser" integer,
    "RealtimeSttMaxAudioSecondsPerSession" integer,
    "RealtimeSttDailyAudioSecondsPerUser" integer,
    "RealtimeSttMonthlyBudgetCapUsd" numeric,
    "RealtimeSttEstimatedCostUsdPerMinute" numeric,
    "RealtimeSttProviderSessionTopology" character varying(64),
    "RealtimeSttRegionId" character varying(96),
    "RealtimeSttAssumeLearnersAdult" boolean,
    "RealtimeSttAllowManagedLearnerRealProvider" boolean,
    "RealtimeSttConsentVersion" character varying(96),
    "RealtimeSttRollbackMode" character varying(64),
    "RealtimeSttAllowedMimeTypesCsv" character varying(512),
    "ElevenLabsSttApiKeyEncrypted" text,
    "ElevenLabsSttBaseUrl" character varying(256),
    "ElevenLabsSttModel" character varying(64),
    "ElevenLabsSttLanguage" character varying(16),
    "ElevenLabsSttAudioFormat" character varying(64),
    "ElevenLabsSttCommitStrategy" character varying(32),
    "ElevenLabsSttKeytermsCsv" character varying(1024),
    "ElevenLabsSttEnableProviderLogging" boolean,
    "ElevenLabsSttTokenTtlSeconds" integer,
    "ElevenLabsApiKeyEncrypted" text,
    "ElevenLabsTtsBaseUrl" character varying(256),
    "ElevenLabsDefaultVoiceId" character varying(64),
    "ElevenLabsModel" character varying(64),
    "ElevenLabsOutputFormat" character varying(64),
    "ElevenLabsPronunciationDictionaryId" character varying(128),
    "ElevenLabsPronunciationDictionaryVersionId" character varying(128),
    "ElevenLabsStability" double precision,
    "ElevenLabsSimilarityBoost" double precision,
    "ElevenLabsStyle" double precision,
    "ElevenLabsUseSpeakerBoost" boolean,
    "CosyVoiceBaseUrl" character varying(256),
    "CosyVoiceApiKeyEncrypted" text,
    "CosyVoiceDefaultVoice" character varying(64),
    "ChatTtsBaseUrl" character varying(256),
    "ChatTtsApiKeyEncrypted" text,
    "ChatTtsDefaultVoice" character varying(64),
    "Qwen3ModelVariant" character varying(32),
    "Qwen3VoiceId" character varying(64),
    "Qwen3VoiceInstructions" text,
    "Qwen3Speed" double precision,
    "Qwen3Pitch" double precision,
    "Qwen3Emotion" character varying(256),
    "GptSoVitsBaseUrl" character varying(256),
    "GptSoVitsApiKeyEncrypted" text,
    "GptSoVitsDefaultVoice" character varying(64),
    "MaxAudioBytes" bigint,
    "AudioRetentionDays" integer,
    "PrepDurationSeconds" integer,
    "MaxSessionDurationSeconds" integer,
    "MaxTurnDurationSeconds" integer,
    "EnabledTaskTypesCsv" character varying(256),
    "FreeTierSessionsLimit" integer,
    "FreeTierWindowDays" integer,
    "ReplyModel" character varying(128),
    "EvaluationModel" character varying(128),
    "ReplyTemperature" double precision,
    "EvaluationTemperature" double precision,
    "UpdatedByUserId" character varying(64),
    "UpdatedByUserName" character varying(128),
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ConversationSettings" PRIMARY KEY ("Id")
);


CREATE TABLE "ConversationTemplates" (
    "Id" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "ProfessionId" character varying(32),
    "TaskTypeCode" character varying(64) NOT NULL,
    "Scenario" text NOT NULL,
    "RoleDescription" character varying(512),
    "PatientContext" text,
    "ExpectedOutcomes" text,
    "ObjectivesJson" text NOT NULL,
    "ExpectedRedFlagsJson" text NOT NULL,
    "KeyVocabularyJson" text NOT NULL,
    "PatientVoiceJson" text NOT NULL,
    "Difficulty" character varying(16) NOT NULL,
    "EstimatedDurationSeconds" integer NOT NULL,
    "Status" character varying(16) NOT NULL,
    "PublishedAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "CreatedByUserId" character varying(64),
    "UpdatedByUserId" character varying(64),
    "TtsVoice" character varying(64),
    "TtsModelVariant" character varying(32),
    "OpeningAudioSha" character varying(64),
    CONSTRAINT "PK_ConversationTemplates" PRIMARY KEY ("Id")
);


CREATE TABLE "ConversationTurnAnnotations" (
    "Id" character varying(64) NOT NULL,
    "SessionId" character varying(64) NOT NULL,
    "EvaluationId" character varying(64) NOT NULL,
    "TurnNumber" integer NOT NULL,
    "Type" character varying(16) NOT NULL,
    "Category" character varying(64),
    "RuleId" character varying(32),
    "Evidence" text NOT NULL,
    "Suggestion" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ConversationTurnAnnotations" PRIMARY KEY ("Id")
);


CREATE TABLE "ConversationTurns" (
    "Id" uuid NOT NULL,
    "SessionId" character varying(64) NOT NULL,
    "TurnNumber" integer NOT NULL,
    "Role" character varying(16) NOT NULL,
    "Content" text NOT NULL,
    "AudioUrl" character varying(512),
    "DurationMs" integer NOT NULL,
    "TimestampMs" integer NOT NULL,
    "ConfidenceScore" double precision,
    "AnalysisJson" text NOT NULL,
    "TurnClientId" character varying(96),
    "ProviderEventId" character varying(128),
    "ProviderName" character varying(64),
    "FinalizedAt" timestamp with time zone,
    "AiFeatureCode" character varying(64),
    "AiUsageId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ConversationTurns" PRIMARY KEY ("Id")
);


CREATE TABLE "Criteria" (
    "Id" character varying(32) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "Code" character varying(32) NOT NULL,
    "Label" character varying(128) NOT NULL,
    "Description" character varying(512) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "SortOrder" integer NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    CONSTRAINT "PK_Criteria" PRIMARY KEY ("Id")
);


CREATE TABLE "CrossSellRules" (
    "Id" uuid NOT NULL,
    "TriggerProductCode" character varying(64) NOT NULL,
    "SuggestedProductCode" character varying(64) NOT NULL,
    "Priority" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_CrossSellRules" PRIMARY KEY ("Id")
);


CREATE TABLE "CustomerSubscriptions" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "StripeSubscriptionId" character varying(256) NOT NULL,
    "StripePriceId" character varying(256) NOT NULL,
    "BillingProductId" uuid,
    "Status" character varying(32) NOT NULL,
    "CurrentPeriodStart" timestamp with time zone NOT NULL,
    "CurrentPeriodEnd" timestamp with time zone NOT NULL,
    "CancelAtPeriodEnd" boolean NOT NULL,
    "CanceledAt" timestamp with time zone,
    "PausedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_CustomerSubscriptions" PRIMARY KEY ("Id")
);


CREATE TABLE "DeflectionRules" (
    "Id" character varying(64) NOT NULL,
    "TriggerReason" character varying(32) NOT NULL,
    "OfferedCouponCode" character varying(64) NOT NULL,
    "MinTenureDays" integer NOT NULL,
    "MaxOffersPerUser" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_DeflectionRules" PRIMARY KEY ("Id")
);


CREATE TABLE "DiagnosticSessions" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "State" integer NOT NULL,
    "StartedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    "ExpiresAt" timestamp with time zone,
    "ExamFamilyCode" character varying(16) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    CONSTRAINT "PK_DiagnosticSessions" PRIMARY KEY ("Id")
);


CREATE TABLE "DiagnosticSubtests" (
    "Id" character varying(64) NOT NULL,
    "DiagnosticSessionId" character varying(64) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "State" integer NOT NULL,
    "EstimatedDurationMinutes" integer NOT NULL,
    "CompletedAt" timestamp with time zone,
    "AttemptId" text,
    CONSTRAINT "PK_DiagnosticSubtests" PRIMARY KEY ("Id")
);


CREATE TABLE "DictationDrills" (
    "Id" uuid NOT NULL,
    "DrillType" character varying(32) NOT NULL,
    "AudioAssetUrl" character varying(256),
    "DurationSeconds" integer NOT NULL,
    "TranscriptText" text NOT NULL,
    "AcceptableVariantsJson" text NOT NULL,
    "Accent" character varying(16) NOT NULL,
    "Difficulty" integer NOT NULL,
    "ProfessionRelevanceJson" text NOT NULL,
    "IsPublished" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_DictationDrills" PRIMARY KEY ("Id")
);


CREATE TABLE "DunningAttempts" (
    "Id" character varying(64) NOT NULL,
    "SubscriptionId" character varying(64) NOT NULL,
    "InvoiceId" character varying(128) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "AttemptNumber" integer NOT NULL,
    "ScheduledAt" timestamp with time zone NOT NULL,
    "ExecutedAt" timestamp with time zone,
    "Outcome" integer NOT NULL,
    "StripeFailureCode" character varying(64),
    "FailureReason" character varying(512),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_DunningAttempts" PRIMARY KEY ("Id")
);


CREATE TABLE "DunningCampaigns" (
    "Id" character varying(64) NOT NULL,
    "SubscriptionId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "NextAttemptAt" timestamp with time zone NOT NULL,
    "AttemptCount" integer NOT NULL,
    "LastFailureCode" character varying(64),
    "LastFailureReason" character varying(512),
    "StepsCompletedCsv" character varying(512) NOT NULL,
    "RecoveredAt" timestamp with time zone,
    "CancelledAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_DunningCampaigns" PRIMARY KEY ("Id")
);


CREATE TABLE "Evaluations" (
    "Id" character varying(64) NOT NULL,
    "AttemptId" character varying(64) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "State" integer NOT NULL,
    "ScoreRange" text NOT NULL,
    "GradeRange" text,
    "ConfidenceBand" integer NOT NULL,
    "StrengthsJson" jsonb NOT NULL,
    "IssuesJson" jsonb NOT NULL,
    "CriterionScoresJson" jsonb NOT NULL,
    "FeedbackItemsJson" jsonb NOT NULL,
    "GeneratedAt" timestamp with time zone,
    "ModelExplanationSafe" text NOT NULL,
    "LearnerDisclaimer" text NOT NULL,
    "StatusReasonCode" text NOT NULL,
    "StatusMessage" text NOT NULL,
    "Retryable" boolean NOT NULL,
    "RetryAfterMs" integer,
    "LastTransitionAt" timestamp with time zone NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ModelVersionId" character varying(64),
    CONSTRAINT "PK_Evaluations" PRIMARY KEY ("Id")
);


CREATE TABLE "ExamBookings" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "ExamDate" date NOT NULL,
    "BookingReference" character varying(128),
    "ExternalUrl" character varying(512),
    "Status" character varying(32) NOT NULL,
    "TestCenter" character varying(128),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ExamBookings" PRIMARY KEY ("Id")
);


CREATE TABLE "ExamFamilies" (
    "Code" character varying(16) NOT NULL,
    "Label" character varying(64) NOT NULL,
    "ScoringModel" character varying(32) NOT NULL,
    "Description" character varying(256),
    "SubtestConfigJson" text NOT NULL,
    "CriteriaConfigJson" text NOT NULL,
    "SortOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ExamFamilies" PRIMARY KEY ("Code")
);


CREATE TABLE "ExamTypes" (
    "Code" character varying(16) NOT NULL,
    "Label" character varying(128) NOT NULL,
    "Description" character varying(512) NOT NULL,
    "SubtestDefinitionsJson" text NOT NULL,
    "ScoringSystemJson" text NOT NULL,
    "TimingsJson" text NOT NULL,
    "ProfessionIdsJson" text NOT NULL,
    "Status" character varying(16) NOT NULL,
    "SortOrder" integer NOT NULL,
    CONSTRAINT "PK_ExamTypes" PRIMARY KEY ("Code")
);


CREATE TABLE "ExchangeRates" (
    "Id" character varying(64) NOT NULL,
    "FromCurrency" character varying(3) NOT NULL,
    "ToCurrency" character varying(3) NOT NULL,
    "Rate" numeric(18,8) NOT NULL,
    "EffectiveFrom" timestamp with time zone NOT NULL,
    "Source" character varying(32) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ExchangeRates" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertAnnotationTemplates" (
    "Id" character varying(64) NOT NULL,
    "CreatedByExpertId" character varying(64) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "CriterionCode" character varying(64) NOT NULL,
    "Label" character varying(128) NOT NULL,
    "TemplateText" character varying(1500) NOT NULL,
    "UsageCount" integer NOT NULL,
    "IsShared" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ExpertAnnotationTemplates" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertAvailabilities" (
    "Id" character varying(64) NOT NULL,
    "ReviewerId" character varying(64) NOT NULL,
    "Timezone" character varying(64) NOT NULL,
    "DaysJson" text NOT NULL,
    "EffectiveFrom" timestamp with time zone NOT NULL,
    "EffectiveTo" timestamp with time zone,
    CONSTRAINT "PK_ExpertAvailabilities" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertCalibrationCases" (
    "Id" character varying(64) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "ProfessionId" character varying(32) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "BenchmarkLabel" character varying(128) NOT NULL,
    "CaseArtifactsJson" text NOT NULL,
    "ReferenceRubricJson" text NOT NULL,
    "ReferenceNotesJson" text NOT NULL,
    "Difficulty" character varying(32) NOT NULL,
    "Status" integer NOT NULL,
    "BenchmarkScore" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ExpertCalibrationCases" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertCalibrationNotes" (
    "Id" character varying(64) NOT NULL,
    "Type" integer NOT NULL,
    "Message" text NOT NULL,
    "CaseId" character varying(64),
    "ReviewerId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ExpertCalibrationNotes" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertCalibrationResults" (
    "Id" character varying(64) NOT NULL,
    "CalibrationCaseId" character varying(64) NOT NULL,
    "ReviewerId" character varying(64) NOT NULL,
    "SubmittedRubricJson" text NOT NULL,
    "ReviewerScore" integer NOT NULL,
    "AlignmentScore" double precision NOT NULL,
    "DisagreementSummary" text NOT NULL,
    "Notes" text NOT NULL,
    "SubmittedAt" timestamp with time zone NOT NULL,
    "IsDraft" boolean NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_ExpertCalibrationResults" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertCompensationRates" (
    "Id" character varying(64) NOT NULL,
    "ExpertId" character varying(64) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "RateMinorUnits" bigint NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "EffectiveFrom" timestamp with time zone NOT NULL,
    "EffectiveTo" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ExpertCompensationRates" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertEarnings" (
    "Id" character varying(64) NOT NULL,
    "ExpertId" character varying(64) NOT NULL,
    "ReviewRequestId" character varying(64) NOT NULL,
    "AmountMinorUnits" bigint NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "EarnedAt" timestamp with time zone NOT NULL,
    "PaidOutAt" timestamp with time zone,
    "PayoutId" character varying(64),
    CONSTRAINT "PK_ExpertEarnings" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertMessageReplies" (
    "Id" character varying(64) NOT NULL,
    "ThreadId" character varying(64) NOT NULL,
    "AuthorId" character varying(64) NOT NULL,
    "AuthorRole" character varying(32) NOT NULL,
    "AuthorName" character varying(128) NOT NULL,
    "Body" character varying(4000) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ExpertMessageReplies" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertMessageThreads" (
    "Id" character varying(64) NOT NULL,
    "ExpertId" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "LinkedReviewRequestId" character varying(64),
    "LinkedCalibrationCaseId" character varying(64),
    "LinkedLearnerId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ExpertMessageThreads" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertMetricSnapshots" (
    "Id" character varying(64) NOT NULL,
    "ReviewerId" character varying(64) NOT NULL,
    "WindowStart" timestamp with time zone NOT NULL,
    "WindowEnd" timestamp with time zone NOT NULL,
    "CompletedReviews" integer NOT NULL,
    "DraftReviews" integer NOT NULL,
    "AvgTurnaroundHours" double precision NOT NULL,
    "SlaHitRate" double precision NOT NULL,
    "CalibrationScore" double precision NOT NULL,
    "ReworkRate" double precision NOT NULL,
    "CompletionDataJson" text NOT NULL,
    CONSTRAINT "PK_ExpertMetricSnapshots" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertOnboardingProgresses" (
    "ExpertUserId" character varying(64) NOT NULL,
    "ProfileJson" text NOT NULL,
    "QualificationsJson" text NOT NULL,
    "RatesJson" text NOT NULL,
    "CompletedStepsJson" text NOT NULL,
    "IsComplete" boolean NOT NULL,
    "CompletedAt" timestamp with time zone,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ExpertOnboardingProgresses" PRIMARY KEY ("ExpertUserId")
);


CREATE TABLE "ExpertPayouts" (
    "Id" character varying(64) NOT NULL,
    "ExpertId" character varying(64) NOT NULL,
    "TotalAmountMinorUnits" bigint NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "ApprovedByAdminId" character varying(64),
    "ApprovedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ExpertPayouts" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertReviewAmends" (
    "Id" character varying(64) NOT NULL,
    "ReviewRequestId" character varying(64) NOT NULL,
    "ReviewerId" character varying(64) NOT NULL,
    "BeforeSnapshotJson" text NOT NULL,
    "AfterSnapshotJson" text NOT NULL,
    "AmendNumber" integer NOT NULL,
    "AmendedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ExpertReviewAmends" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertReviewAssignments" (
    "Id" character varying(64) NOT NULL,
    "ReviewRequestId" character varying(64) NOT NULL,
    "AssignedReviewerId" character varying(64),
    "AssignedBy" character varying(64),
    "AssignedAt" timestamp with time zone,
    "ClaimState" integer NOT NULL,
    "ReleasedAt" timestamp with time zone,
    "ReassignedFrom" character varying(64),
    "ReasonCode" character varying(128),
    CONSTRAINT "PK_ExpertReviewAssignments" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertReviewDrafts" (
    "Id" character varying(64) NOT NULL,
    "ReviewRequestId" character varying(64) NOT NULL,
    "ReviewerId" character varying(64) NOT NULL,
    "Version" integer NOT NULL,
    "State" character varying(32) NOT NULL,
    "RubricEntriesJson" text NOT NULL,
    "CriterionCommentsJson" text NOT NULL,
    "AnchoredCommentsJson" text NOT NULL,
    "TimestampCommentsJson" text NOT NULL,
    "FinalCommentDraft" text NOT NULL,
    "ScratchpadJson" text NOT NULL,
    "ChecklistItemsJson" text NOT NULL,
    "DraftSavedAt" timestamp with time zone NOT NULL,
    "AutosaveErrorState" character varying(64),
    CONSTRAINT "PK_ExpertReviewDrafts" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertReviewerPayouts" (
    "Id" character varying(64) NOT NULL,
    "ReviewerId" character varying(64) NOT NULL,
    "PayPeriodStart" timestamp with time zone NOT NULL,
    "PayPeriodEnd" timestamp with time zone NOT NULL,
    "ReviewCount" integer NOT NULL,
    "TotalCompensation" numeric NOT NULL,
    "TotalLearnerPrice" numeric NOT NULL,
    "Status" character varying(32) NOT NULL,
    "AdminNote" character varying(512),
    "ApprovedByAdminId" character varying(64),
    "ApprovedByAdminName" character varying(128),
    "ApprovedAt" timestamp with time zone,
    "PaidAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ReviewRequestIdsJson" text NOT NULL,
    CONSTRAINT "PK_ExpertReviewerPayouts" PRIMARY KEY ("Id")
);


CREATE TABLE "ExpertSlaSnapshots" (
    "Id" character varying(64) NOT NULL,
    "ReviewRequestId" character varying(64) NOT NULL,
    "ExpertId" character varying(64) NOT NULL,
    "SlaDueAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "WasMet" boolean NOT NULL,
    "TurnaroundHours" double precision,
    "SlaState" character varying(32) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ExpertSlaSnapshots" PRIMARY KEY ("Id")
);


CREATE TABLE "FeatureFlags" (
    "Id" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Key" character varying(128) NOT NULL,
    "FlagType" integer NOT NULL,
    "Enabled" boolean NOT NULL,
    "RolloutPercentage" integer NOT NULL,
    "Description" character varying(512),
    "Owner" character varying(128),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_FeatureFlags" PRIMARY KEY ("Id")
);


CREATE TABLE "ForumCategories" (
    "Id" character varying(64) NOT NULL,
    "ExamTypeCode" character varying(16),
    "Name" character varying(128) NOT NULL,
    "Description" character varying(512) NOT NULL,
    "SortOrder" integer NOT NULL,
    "Status" character varying(16) NOT NULL,
    CONSTRAINT "PK_ForumCategories" PRIMARY KEY ("Id")
);


CREATE TABLE "ForumReplies" (
    "Id" character varying(64) NOT NULL,
    "ThreadId" character varying(64) NOT NULL,
    "AuthorUserId" character varying(64) NOT NULL,
    "AuthorDisplayName" character varying(128) NOT NULL,
    "AuthorRole" character varying(32) NOT NULL,
    "Body" text NOT NULL,
    "IsExpertVerified" boolean NOT NULL,
    "LikeCount" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "EditedAt" timestamp with time zone,
    CONSTRAINT "PK_ForumReplies" PRIMARY KEY ("Id")
);


CREATE TABLE "ForumThreads" (
    "Id" character varying(64) NOT NULL,
    "CategoryId" character varying(64) NOT NULL,
    "AuthorUserId" character varying(64) NOT NULL,
    "AuthorDisplayName" character varying(128) NOT NULL,
    "AuthorRole" character varying(32) NOT NULL,
    "Title" character varying(256) NOT NULL,
    "Body" text NOT NULL,
    "IsPinned" boolean NOT NULL,
    "IsLocked" boolean NOT NULL,
    "ReplyCount" integer NOT NULL,
    "ViewCount" integer NOT NULL,
    "LikeCount" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "LastActivityAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ForumThreads" PRIMARY KEY ("Id")
);


CREATE TABLE "FoundationResources" (
    "Id" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "ResourceType" character varying(32) NOT NULL,
    "ContentBody" text,
    "MediaAssetId" character varying(64),
    "Difficulty" character varying(16) NOT NULL,
    "PrerequisiteResourceId" character varying(64),
    "DisplayOrder" integer NOT NULL,
    "Status" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_FoundationResources" PRIMARY KEY ("Id")
);


CREATE TABLE "FreePreviewAssets" (
    "Id" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "PreviewType" character varying(32) NOT NULL,
    "ContentItemId" character varying(64),
    "MediaAssetId" character varying(64),
    "ConversionCtaText" character varying(256),
    "TargetPackageId" character varying(64),
    "Status" integer NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_FreePreviewAssets" PRIMARY KEY ("Id")
);


CREATE TABLE "FreeTierConfigs" (
    "Id" character varying(64) NOT NULL,
    "Enabled" boolean NOT NULL,
    "MaxWritingAttempts" integer NOT NULL,
    "MaxSpeakingAttempts" integer NOT NULL,
    "MaxReadingAttempts" integer NOT NULL,
    "MaxListeningAttempts" integer NOT NULL,
    "MaxSpeakingMockSets" integer NOT NULL,
    "TrialDurationDays" integer NOT NULL,
    "ShowUpgradePrompts" boolean NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_FreeTierConfigs" PRIMARY KEY ("Id")
);


CREATE TABLE "GatewayRoutingConfigs" (
    "Id" character varying(64) NOT NULL,
    "Region" character varying(16) NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "ProductType" character varying(32) NOT NULL,
    "GatewayName" character varying(32) NOT NULL,
    "Priority" integer NOT NULL,
    "IsEnabled" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "UpdatedByAdminId" character varying(64),
    CONSTRAINT "PK_GatewayRoutingConfigs" PRIMARY KEY ("Id")
);


CREATE TABLE "Goals" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ProfessionId" character varying(32) NOT NULL,
    "TargetExamDate" date,
    "OverallGoal" text,
    "TargetWritingScore" integer,
    "TargetSpeakingScore" integer,
    "TargetReadingScore" integer,
    "TargetListeningScore" integer,
    "PreviousAttempts" integer NOT NULL,
    "WeakSubtestsJson" text NOT NULL,
    "StudyHoursPerWeek" integer NOT NULL,
    "TargetCountry" text,
    "TargetOrganization" text,
    "DraftStateJson" text NOT NULL,
    "SubmittedAt" timestamp with time zone,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "ExamFamilyCode" character varying(16) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    CONSTRAINT "PK_Goals" PRIMARY KEY ("Id")
);


CREATE TABLE "GrammarLessons" (
    "Id" character varying(64) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "Title" character varying(128) NOT NULL,
    "Description" character varying(512) NOT NULL,
    "Category" character varying(32) NOT NULL,
    "Level" character varying(16) NOT NULL,
    "ContentHtml" text NOT NULL,
    "ExercisesJson" text NOT NULL,
    "EstimatedMinutes" integer NOT NULL,
    "SortOrder" integer NOT NULL,
    "PrerequisiteLessonId" character varying(32),
    "Status" character varying(16) NOT NULL,
    CONSTRAINT "PK_GrammarLessons" PRIMARY KEY ("Id")
);


CREATE TABLE "IdempotencyRecords" (
    "Id" character varying(128) NOT NULL,
    "Scope" character varying(128) NOT NULL,
    "Key" character varying(128) NOT NULL,
    "ResponseJson" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_IdempotencyRecords" PRIMARY KEY ("Id")
);


CREATE TABLE "InterlocutorTrainingModules" (
    "Id" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "OrderIndex" integer NOT NULL,
    "ContentMarkdown" text NOT NULL,
    "MediaAssetIdsJson" text NOT NULL,
    "RequiredForCalibration" boolean NOT NULL,
    "Stage" integer NOT NULL,
    "Status" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "PublishedAt" timestamp with time zone,
    CONSTRAINT "PK_InterlocutorTrainingModules" PRIMARY KEY ("Id")
);


CREATE TABLE "Invoices" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Number" integer,
    "IssuedAt" timestamp with time zone NOT NULL,
    "Amount" numeric NOT NULL,
    "Currency" text NOT NULL,
    "Status" text NOT NULL,
    "Description" text NOT NULL,
    "PlanVersionId" character varying(64),
    "AddOnVersionIdsJson" character varying(1024) NOT NULL,
    "CouponVersionId" character varying(64),
    "QuoteId" character varying(64),
    "CheckoutSessionId" character varying(256),
    CONSTRAINT "PK_Invoices" PRIMARY KEY ("Id")
);


CREATE TABLE "LaunchReadinessSettings" (
    "Id" character varying(32) NOT NULL,
    "MobileMinSupportedVersion" character varying(32) NOT NULL,
    "MobileLatestVersion" character varying(32) NOT NULL,
    "MobileForceUpdate" boolean NOT NULL,
    "IosAppStoreUrl" character varying(512),
    "AndroidPlayStoreUrl" character varying(512),
    "IosBundleId" character varying(128),
    "AppleTeamId" character varying(64),
    "AppleAssociatedDomainStatus" character varying(64),
    "AppleUniversalLinksStatus" character varying(64),
    "IosSigningProfileReference" character varying(512),
    "IosIapStatus" character varying(64),
    "IosPushStatus" character varying(64),
    "AndroidPackageName" character varying(128),
    "AndroidSha256Fingerprints" character varying(2048),
    "AndroidSigningKeyReference" character varying(512),
    "AndroidAssetLinksStatus" character varying(64),
    "AndroidIapStatus" character varying(64),
    "AndroidPushStatus" character varying(64),
    "DesktopMinSupportedVersion" character varying(32) NOT NULL,
    "DesktopLatestVersion" character varying(32) NOT NULL,
    "DesktopForceUpdate" boolean NOT NULL,
    "DesktopUpdateFeedUrl" character varying(512),
    "DesktopUpdateChannel" character varying(64),
    "WindowsSigningStatus" character varying(64),
    "MacSigningStatus" character varying(64),
    "LinuxSigningStatus" character varying(64),
    "DeviceValidationEvidenceUrl" character varying(512),
    "DeviceValidationNotes" character varying(2048),
    "RealtimeLegalApprovalStatus" character varying(64),
    "RealtimePrivacyApprovalStatus" character varying(64),
    "RealtimeProtectedSmokeStatus" character varying(64),
    "RealtimeEvidenceUrl" character varying(512),
    "RealtimeSpendCapApproved" boolean NOT NULL,
    "RealtimeTopologyApproved" boolean NOT NULL,
    "ReleaseOwnerApprovalStatus" character varying(64),
    "LaunchNotes" character varying(2048),
    "UpdatedByAdminId" character varying(64),
    "UpdatedByAdminName" character varying(128),
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LaunchReadinessSettings" PRIMARY KEY ("Id")
);


CREATE TABLE "LeaderboardEntries" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "DisplayName" character varying(128) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "Period" character varying(16) NOT NULL,
    "PeriodStart" date NOT NULL,
    "XP" bigint NOT NULL,
    "Rank" integer NOT NULL,
    "OptedIn" boolean NOT NULL,
    CONSTRAINT "PK_LeaderboardEntries" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerAccentProgresses" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Accent" character varying(16) NOT NULL,
    "AccuracyPercentage" numeric NOT NULL,
    "QuestionsAttempted" integer NOT NULL,
    "QuestionsCorrect" integer NOT NULL,
    "MinutesListened" integer NOT NULL,
    "SelfConfidenceRating" integer NOT NULL,
    "LastPracticedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerAccentProgresses" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerAchievements" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "AchievementId" character varying(64) NOT NULL,
    "UnlockedAt" timestamp with time zone NOT NULL,
    "Notified" boolean NOT NULL,
    CONSTRAINT "PK_LearnerAchievements" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerBadges" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "BadgeCode" character varying(64) NOT NULL,
    "EarnedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerBadges" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerCertificates" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "CertificateType" character varying(64) NOT NULL,
    "Title" character varying(256) NOT NULL,
    "Description" character varying(512) NOT NULL,
    "DownloadUrl" character varying(512),
    "MetadataJson" character varying(2048) NOT NULL,
    "IssuedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerCertificates" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerDictationProgresses" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "DictationDrillId" uuid NOT NULL,
    "LearnerAnswer" text,
    "IsCorrect" boolean NOT NULL,
    "Attempts" integer NOT NULL,
    "NextReviewAt" timestamp with time zone,
    "LastAttemptedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerDictationProgresses" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerEscalations" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "SubmissionId" character varying(64) NOT NULL,
    "Reason" character varying(128) NOT NULL,
    "Details" character varying(2000) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_LearnerEscalations" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerGrammarProgress" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "LessonId" character varying(64) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "ExerciseScore" integer,
    "AnswersJson" text NOT NULL,
    "StartedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_LearnerGrammarProgress" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerLessonProgresses" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "LessonId" uuid NOT NULL,
    "VideoWatched" boolean NOT NULL,
    "BodyRead" boolean NOT NULL,
    "Drill1Completed" boolean NOT NULL,
    "Drill2Completed" boolean NOT NULL,
    "Drill3Completed" boolean NOT NULL,
    "QuizScore" integer,
    "QuizAttempts" integer NOT NULL,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_LearnerLessonProgresses" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerListeningLessonProgresses" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "LessonId" uuid NOT NULL,
    "VideoWatched" boolean NOT NULL,
    "BodyRead" boolean NOT NULL,
    "Drill1Completed" boolean NOT NULL,
    "Drill2Completed" boolean NOT NULL,
    "Drill3Completed" boolean NOT NULL,
    "QuizScore" integer,
    "QuizAttempts" integer NOT NULL,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_LearnerListeningLessonProgresses" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerListeningPathways" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "TotalWeeks" integer NOT NULL,
    "GeneratedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "WeeksJson" text NOT NULL,
    CONSTRAINT "PK_LearnerListeningPathways" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerListeningProfiles" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "TargetBand" character varying(8) NOT NULL,
    "ExamDate" timestamp with time zone,
    "HoursPerWeek" integer NOT NULL,
    "Profession" character varying(64) NOT NULL,
    "EnglishExposureSource" character varying(32) NOT NULL,
    "ComfortBritish" integer NOT NULL,
    "ComfortAustralian" integer NOT NULL,
    "ComfortVarious" integer NOT NULL,
    "HasTakenBefore" boolean NOT NULL,
    "PreviousScore" integer,
    "SelfRatedSpeed" integer NOT NULL,
    "SelfRatedNoteTaking" integer NOT NULL,
    "SelfRatedSpelling" integer NOT NULL,
    "CurrentStage" character varying(32) NOT NULL,
    "CurrentReadinessScore" integer,
    "PredictedScore" integer,
    "OnboardingCompletedAt" timestamp with time zone NOT NULL,
    "AudioCheckPassedAt" timestamp with time zone,
    "PathwayGeneratedAt" timestamp with time zone,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerListeningProfiles" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerListeningSkillScores" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "SkillCode" character varying(4) NOT NULL,
    "CurrentScore" numeric NOT NULL,
    "DiagnosticScore" numeric NOT NULL,
    "QuestionsAttempted" integer NOT NULL,
    "QuestionsCorrect" integer NOT NULL,
    "LastPracticedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerListeningSkillScores" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerListeningStrategyProgresses" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "StrategyId" uuid NOT NULL,
    "MarkedAsRead" boolean NOT NULL,
    "Favorited" boolean NOT NULL,
    "ReadAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerListeningStrategyProgresses" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerPronunciationCards" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "PronunciationCardId" uuid NOT NULL,
    "Source" character varying(32) NOT NULL,
    "Easiness" numeric NOT NULL,
    "IntervalDays" integer NOT NULL,
    "Repetitions" integer NOT NULL,
    "RetentionScore" integer NOT NULL,
    "NextReviewAt" timestamp with time zone NOT NULL,
    "LastReviewedAt" timestamp with time zone,
    "AddedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerPronunciationCards" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerPronunciationDiscriminationAttempts" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "DrillId" character varying(64) NOT NULL,
    "TargetPhoneme" character varying(32) NOT NULL,
    "RoundsTotal" integer NOT NULL,
    "RoundsCorrect" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerPronunciationDiscriminationAttempts" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerPronunciationProgress" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "PhonemeCode" character varying(32) NOT NULL,
    "AverageScore" double precision NOT NULL,
    "AttemptCount" integer NOT NULL,
    "ScoreHistoryJson" text NOT NULL,
    "LastPracticedAt" timestamp with time zone NOT NULL,
    "NextDueAt" timestamp with time zone,
    "IntervalDays" integer NOT NULL,
    "Ease" double precision NOT NULL,
    CONSTRAINT "PK_LearnerPronunciationProgress" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerReadingPathways" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "TotalWeeks" integer NOT NULL,
    "GeneratedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "WeeksJson" text NOT NULL,
    CONSTRAINT "PK_LearnerReadingPathways" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerReadingProfiles" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "TargetBand" character varying(8) NOT NULL,
    "ExamDate" timestamp with time zone,
    "HoursPerWeek" integer NOT NULL,
    "Profession" character varying(64) NOT NULL,
    "HasTakenBefore" boolean NOT NULL,
    "PreviousScore" integer,
    "SelfRatedSpeed" integer NOT NULL,
    "SelfRatedVocabulary" integer NOT NULL,
    "CurrentStage" character varying(32) NOT NULL,
    "CurrentReadinessScore" integer,
    "PredictedScore" integer,
    "OnboardingCompletedAt" timestamp with time zone NOT NULL,
    "PathwayGeneratedAt" timestamp with time zone,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerReadingProfiles" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerSkillProfiles" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "CriterionCode" character varying(32) NOT NULL,
    "CurrentRating" double precision NOT NULL,
    "ConfidenceLevel" integer NOT NULL,
    "EvidenceCount" integer NOT NULL,
    "RecentScoresJson" text NOT NULL,
    "LastUpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerSkillProfiles" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerSkillScores" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "SkillCode" character varying(4) NOT NULL,
    "CurrentScore" numeric NOT NULL,
    "DiagnosticScore" numeric NOT NULL,
    "QuestionsAttempted" integer NOT NULL,
    "QuestionsCorrect" integer NOT NULL,
    "LastPracticedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerSkillScores" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerStrategyProgress" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "StrategyGuideId" character varying(64) NOT NULL,
    "ReadPercent" integer NOT NULL,
    "Completed" boolean NOT NULL,
    "Bookmarked" boolean NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "LastReadAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "BookmarkedAt" timestamp with time zone,
    CONSTRAINT "PK_LearnerStrategyProgress" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerStreaks" (
    "UserId" character varying(64) NOT NULL,
    "CurrentStreak" integer NOT NULL,
    "LongestStreak" integer NOT NULL,
    "LastActiveDate" date NOT NULL,
    "StreakFreezeCount" integer NOT NULL,
    "StreakFreezeUsedCount" integer NOT NULL,
    "LastFreezeUsedDate" date,
    CONSTRAINT "PK_LearnerStreaks" PRIMARY KEY ("UserId")
);


CREATE TABLE "LearnerVideoProgress" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "VideoLessonId" character varying(64) NOT NULL,
    "WatchedSeconds" integer NOT NULL,
    "Completed" boolean NOT NULL,
    "LastWatchedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerVideoProgress" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerVocabularies" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "TermId" character varying(64) NOT NULL,
    "Mastery" character varying(16) NOT NULL,
    "EaseFactor" double precision NOT NULL,
    "IntervalDays" integer NOT NULL,
    "ReviewCount" integer NOT NULL,
    "CorrectCount" integer NOT NULL,
    "NextReviewDate" date,
    "LastReviewedAt" timestamp with time zone,
    "AddedAt" timestamp with time zone NOT NULL,
    "SourceRef" character varying(128),
    "Starred" boolean NOT NULL,
    "StarReason" character varying(16),
    "LastErrorTypeCode" character varying(24),
    CONSTRAINT "PK_LearnerVocabularies" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerVocabularyItems" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "VocabularyWordId" uuid NOT NULL,
    "Source" character varying(32) NOT NULL,
    "Easiness" numeric NOT NULL,
    "IntervalDays" integer NOT NULL,
    "Repetitions" integer NOT NULL,
    "RetentionScore" integer NOT NULL,
    "NextReviewAt" timestamp with time zone NOT NULL,
    "LastReviewedAt" timestamp with time zone,
    "AddedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerVocabularyItems" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerWritingLessonProgresses" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "LessonId" uuid NOT NULL,
    "BodyRead" boolean NOT NULL,
    "DrillCompleted" boolean NOT NULL,
    "QuizScore" integer,
    "QuizAttempts" integer NOT NULL,
    "CompletedAt" timestamp with time zone,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerWritingLessonProgresses" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerWritingPathways" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "TotalWeeks" integer NOT NULL,
    "GeneratedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "WeeksJson" text NOT NULL,
    "WeaknessVectorJson" jsonb NOT NULL DEFAULT '{}',
    "SubSkillMasteryJson" jsonb NOT NULL DEFAULT '{}',
    "LastRecalculatedAt" timestamp with time zone,
    "DiagnosticSubmissionId" uuid,
    CONSTRAINT "PK_LearnerWritingPathways" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerWritingProfiles" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Profession" character varying(64) NOT NULL,
    "TargetBand" character varying(8) NOT NULL,
    "ExamDate" timestamp with time zone,
    "DaysPerWeek" integer NOT NULL,
    "MinutesPerDay" integer NOT NULL,
    "TargetCountry" character varying(32) NOT NULL,
    "LetterTypeFocusJson" text NOT NULL,
    "CurrentStage" character varying(32) NOT NULL,
    "CurrentReadinessScore" integer,
    "PredictedScore" integer,
    "LastDiagnosticEvaluationId" character varying(64),
    "OnboardingCompletedAt" timestamp with time zone,
    "PathwayGeneratedAt" timestamp with time zone,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "SubDiscipline" character varying(64),
    "YearsExperience" integer,
    "OptInCommunity" boolean NOT NULL,
    "OptInLeaderboard" boolean NOT NULL,
    "OptInDataForTraining" boolean NOT NULL,
    "OptInBuddy" boolean NOT NULL,
    "AccommodationProfileJson" jsonb NOT NULL DEFAULT '{}',
    "CanonVersionPinned" character varying(32),
    CONSTRAINT "PK_LearnerWritingProfiles" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerXps" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "TotalXp" integer NOT NULL,
    "CurrentLevel" integer NOT NULL,
    "XpToNextLevel" integer NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerXps" PRIMARY KEY ("Id")
);


CREATE TABLE "LearnerXPs" (
    "UserId" character varying(64) NOT NULL,
    "TotalXP" bigint NOT NULL,
    "WeeklyXP" bigint NOT NULL,
    "MonthlyXP" bigint NOT NULL,
    "Level" integer NOT NULL,
    "WeekStartDate" date NOT NULL,
    "MonthStartDate" date NOT NULL,
    CONSTRAINT "PK_LearnerXPs" PRIMARY KEY ("UserId")
);


CREATE TABLE "ListeningAttempts" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "PaperId" character varying(64) NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "DeadlineAt" timestamp with time zone,
    "SubmittedAt" timestamp with time zone,
    "LastActivityAt" timestamp with time zone NOT NULL,
    "Status" integer NOT NULL,
    "Mode" integer NOT NULL,
    "RowVersion" integer NOT NULL,
    "RawScore" integer,
    "ScaledScore" integer,
    "MaxRawScore" integer NOT NULL,
    "PolicySnapshotJson" text NOT NULL,
    "PaperRevisionId" character varying(64),
    "ScopeJson" text,
    "NavigationStateJson" jsonb,
    "WindowStartedAt" timestamp with time zone,
    "WindowDurationMs" integer,
    "AudioCueTimelineJson" jsonb,
    "TechReadinessJson" jsonb,
    "AnnotationsJson" jsonb,
    "HumanScoreOverridesJson" jsonb,
    "LastQuestionVersionMapJson" jsonb,
    "RulebookVersion" character varying(32),
    CONSTRAINT "PK_ListeningAttempts" PRIMARY KEY ("Id")
);


CREATE TABLE "ListeningDailyPlanItems" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "PlanDate" date NOT NULL,
    "Ordinal" integer NOT NULL,
    "ItemType" character varying(32) NOT NULL,
    "FocusSkill" character varying(4),
    "FocusAccent" character varying(16),
    "EstimatedMinutes" integer NOT NULL,
    "PayloadJson" text NOT NULL,
    "Status" character varying(16) NOT NULL,
    "StartedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_ListeningDailyPlanItems" PRIMARY KEY ("Id")
);


CREATE TABLE "ListeningLessons" (
    "Id" uuid NOT NULL,
    "Slug" character varying(128) NOT NULL,
    "Title" character varying(256) NOT NULL,
    "TitleAr" character varying(256) NOT NULL,
    "SkillCode" character varying(4) NOT NULL,
    "OrderIndex" integer NOT NULL,
    "EstimatedMinutes" integer NOT NULL,
    "VideoUrl" text,
    "BodyMarkdownEn" text NOT NULL,
    "BodyMarkdownAr" text NOT NULL,
    "DrillQuestionIdsJson" text NOT NULL,
    "QuizQuestionIdsJson" text NOT NULL,
    "PrerequisiteLessonId" uuid,
    "IsPublished" boolean NOT NULL,
    CONSTRAINT "PK_ListeningLessons" PRIMARY KEY ("Id")
);


CREATE TABLE "ListeningMockTemplates" (
    "Id" uuid NOT NULL,
    "Title" character varying(256) NOT NULL,
    "Difficulty" integer NOT NULL,
    "QuestionIdsJson" text NOT NULL,
    "DurationSeconds" integer NOT NULL,
    "IsPublished" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ListeningMockTemplates" PRIMARY KEY ("Id")
);


CREATE TABLE "ListeningParts" (
    "Id" character varying(64) NOT NULL,
    "PaperId" character varying(64) NOT NULL,
    "PartCode" integer NOT NULL,
    "MaxRawScore" integer NOT NULL,
    "Instructions" character varying(1024),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ListeningParts" PRIMARY KEY ("Id")
);


CREATE TABLE "ListeningPathwayProgress" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "StageCode" character varying(32) NOT NULL,
    "Status" integer NOT NULL,
    "StartedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    "ScaledScore" integer,
    "AttemptId" character varying(64),
    "UnlockOverrideBy" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ListeningPathwayProgress" PRIMARY KEY ("Id")
);


CREATE TABLE "ListeningPolicies" (
    "Id" character varying(32) NOT NULL,
    "AttemptsPerPaperPerUser" integer NOT NULL,
    "AttemptCooldownMinutes" integer NOT NULL,
    "BestScoreDisplay" character varying(16) NOT NULL,
    "ShowPastAttempts" boolean NOT NULL,
    "FullPaperTimerMinutes" integer NOT NULL,
    "GracePeriodSeconds" integer NOT NULL,
    "OnExpirySubmitPolicy" character varying(32) NOT NULL,
    "CountdownWarningsJson" text NOT NULL,
    "ExamReplayAllowed" boolean NOT NULL,
    "LearningReplayAllowed" boolean NOT NULL,
    "LearningEvidenceLoopEnabled" boolean NOT NULL,
    "ShortAnswerNormalisation" character varying(32) NOT NULL,
    "ShortAnswerAcceptSynonyms" boolean NOT NULL,
    "AiExtractionEnabled" boolean NOT NULL,
    "AiExtractionRequireHumanApproval" boolean NOT NULL,
    "AiExtractionMaxRetriesPerPaper" integer NOT NULL,
    "ShowExplanationsAfterSubmit" boolean NOT NULL,
    "ShowExplanationsOnlyIfWrong" boolean NOT NULL,
    "ShowCorrectAnswerOnReview" boolean NOT NULL,
    "DefaultExtraTimePct" integer NOT NULL,
    "ScreenReaderOptimised" boolean NOT NULL,
    "AutoExpireWorkerEnabled" boolean NOT NULL,
    "AutoExpireAfterMinutes" integer NOT NULL,
    "AllowResumeAfterExpiry" boolean NOT NULL,
    "RetainAnswerRowsDays" integer NOT NULL,
    "RetainAttemptHeadersDays" integer NOT NULL,
    "AnonymiseOnAccountDelete" boolean NOT NULL,
    "PreviewWindowMsA1" integer,
    "PreviewWindowMsA2" integer,
    "PreviewWindowMsC1" integer,
    "PreviewWindowMsC2" integer,
    "ReviewWindowMsA1" integer,
    "ReviewWindowMsA2" integer,
    "ReviewWindowMsC1" integer,
    "ReviewWindowMsC2FinalCbt" integer,
    "ReviewWindowMsC2FinalPaper" integer,
    "BetweenSectionTransitionMs" integer,
    "PartBQuestionWindowMs" integer,
    "OneWayLocksEnabled" boolean,
    "ConfirmDialogRequired" boolean,
    "UnansweredWarningRequired" boolean,
    "ConfirmTokenTtlMs" integer,
    "HighlightingEnabledPartA" boolean,
    "HighlightingEnabledPartBC" boolean,
    "OptionStrikethroughEnabled" boolean,
    "InAppZoomEnabled" boolean,
    "CtrlZoomBlocked" boolean,
    "AnnotationsPersistOnAdvance" boolean,
    "TechReadinessRequired" boolean,
    "TechReadinessTtlMs" integer,
    "FinalReviewAllPartsMsPaper" integer,
    "RowVersion" integer NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "UpdatedByAdminId" character varying(64),
    CONSTRAINT "PK_ListeningPolicies" PRIMARY KEY ("Id")
);


CREATE TABLE "ListeningPracticeNotes" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "PracticeSessionId" uuid,
    "ListeningQuestionId" character varying(64),
    "NoteMarkdown" text NOT NULL,
    "CharacterCount" integer NOT NULL,
    "LastSavedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ListeningPracticeNotes" PRIMARY KEY ("Id")
);


CREATE TABLE "ListeningPracticeSessions" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "SessionType" character varying(32) NOT NULL,
    "FocusSkill" character varying(4),
    "FocusAccent" character varying(16),
    "QuestionIdsJson" text NOT NULL,
    "AudioAssetIdsJson" text NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "DurationSeconds" integer,
    "Score" integer,
    "TotalQuestions" integer,
    "MetadataJson" text NOT NULL,
    CONSTRAINT "PK_ListeningPracticeSessions" PRIMARY KEY ("Id")
);


CREATE TABLE "ListeningQuestionAttempts" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ListeningQuestionId" character varying(64) NOT NULL,
    "PracticeSessionId" uuid,
    "AudioAssetId" uuid,
    "SelectedOption" character varying(256),
    "LearnerAnswer" text,
    "IsCorrect" boolean NOT NULL,
    "IsUnknown" boolean NOT NULL,
    "IsSpellingCorrectMeaningWrong" boolean NOT NULL,
    "IsMeaningCorrectSpellingWrong" boolean NOT NULL,
    "ReplaysUsed" integer NOT NULL,
    "TimeSpentSeconds" integer NOT NULL,
    "MarkedForReview" boolean NOT NULL,
    "NoteText" text,
    "AttemptedAt" timestamp with time zone NOT NULL,
    "InReviewQueue" boolean NOT NULL,
    "NextReviewAt" timestamp with time zone,
    "ReviewIntervalIndex" integer NOT NULL,
    "ConsecutiveCorrect" integer NOT NULL,
    CONSTRAINT "PK_ListeningQuestionAttempts" PRIMARY KEY ("Id")
);


CREATE TABLE "ListeningStrategies" (
    "Id" uuid NOT NULL,
    "Slug" character varying(128) NOT NULL,
    "Title" character varying(256) NOT NULL,
    "TitleAr" character varying(256) NOT NULL,
    "Category" character varying(64) NOT NULL,
    "ApplicablePartsJson" text NOT NULL,
    "EstimatedReadMinutes" integer NOT NULL,
    "BodyMarkdownEn" text NOT NULL,
    "BodyMarkdownAr" text NOT NULL,
    "VideoUrl" text,
    "AudioUrl" text,
    "LinkedDrillId" uuid,
    "UnlockStage" character varying(32) NOT NULL,
    "Difficulty" integer NOT NULL,
    "IsPublished" boolean NOT NULL,
    CONSTRAINT "PK_ListeningStrategies" PRIMARY KEY ("Id")
);


CREATE TABLE "ListeningTtsJobs" (
    "Id" character varying(64) NOT NULL,
    "ExtractId" character varying(64) NOT NULL,
    "RequestedBy" character varying(64) NOT NULL,
    "Status" integer NOT NULL,
    "RetryCount" integer NOT NULL,
    "ErrorMessage" character varying(2048),
    "RetryAfter" timestamp with time zone,
    "BatchId" character varying(64),
    "VoiceOverride" character varying(64),
    "ModelVariantOverride" character varying(32),
    "InstructionsOverride" character varying(1024),
    "SpeedOverride" double precision,
    "PitchOverride" double precision,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ListeningTtsJobs" PRIMARY KEY ("Id")
);


CREATE TABLE "ListeningUserPolicyOverrides" (
    "UserId" character varying(64) NOT NULL,
    "ExtraTimeEntitlementPct" integer NOT NULL,
    "BlockAttempts" boolean NOT NULL,
    "AccessibilityModeEnabled" boolean NOT NULL,
    "Reason" character varying(512),
    "GrantedByAdminId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone,
    CONSTRAINT "PK_ListeningUserPolicyOverrides" PRIMARY KEY ("UserId")
);


CREATE TABLE "LiveClassWaitlistEntries" (
    "Id" character varying(64) NOT NULL,
    "ClassSessionId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Position" integer NOT NULL,
    "JoinedWaitlistAt" timestamp with time zone NOT NULL,
    "NotifiedOfOpening" boolean NOT NULL,
    CONSTRAINT "PK_LiveClassWaitlistEntries" PRIMARY KEY ("Id")
);


CREATE TABLE "LiveClassWebhookEvents" (
    "Id" character varying(64) NOT NULL,
    "EventType" character varying(96) NOT NULL,
    "PayloadHash" character varying(128) NOT NULL,
    "RawPayload" text NOT NULL,
    "Status" integer NOT NULL,
    "ErrorMessage" character varying(1024),
    "ReceivedAt" timestamp with time zone NOT NULL,
    "ProcessedAt" timestamp with time zone,
    CONSTRAINT "PK_LiveClassWebhookEvents" PRIMARY KEY ("Id")
);


CREATE TABLE "ManualPaymentRequests" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "QuoteId" character varying(64),
    "AmountAmount" numeric(12,2) NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "Method" character varying(32) NOT NULL,
    "ProofUrl" character varying(2048) NOT NULL,
    "ProofHashHex" character varying(64) NOT NULL,
    "Reference" character varying(128) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "SubmittedAt" timestamp with time zone NOT NULL,
    "ReviewedAt" timestamp with time zone,
    "ReviewedByAdminId" character varying(64),
    "AdminNotes" character varying(1024),
    "AccessGrantedSubscriptionId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ManualPaymentRequests" PRIMARY KEY ("Id")
);


CREATE TABLE "MarketingAssets" (
    "Id" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "AssetType" character varying(32) NOT NULL,
    "MediaAssetId" character varying(64),
    "PackageId" character varying(64),
    "Status" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MarketingAssets" PRIMARY KEY ("Id")
);


CREATE TABLE "MediaAssets" (
    "Id" character varying(64) NOT NULL,
    "OriginalFilename" character varying(256) NOT NULL,
    "MimeType" character varying(64) NOT NULL,
    "Format" character varying(16) NOT NULL,
    "SizeBytes" bigint NOT NULL,
    "DurationSeconds" integer,
    "StoragePath" character varying(512) NOT NULL,
    "ThumbnailPath" character varying(512),
    "CaptionPath" character varying(512),
    "TranscriptPath" character varying(512),
    "Status" integer NOT NULL,
    "Sha256" character varying(64),
    "MediaKind" character varying(16),
    "UploadedBy" character varying(64),
    "UploadedAt" timestamp with time zone NOT NULL,
    "ProcessedAt" timestamp with time zone,
    CONSTRAINT "PK_MediaAssets" PRIMARY KEY ("Id")
);


CREATE TABLE "MobilePushTokens" (
    "Id" uuid NOT NULL,
    "AuthAccountId" character varying(64) NOT NULL,
    "Token" character varying(512) NOT NULL,
    "Platform" character varying(16) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MobilePushTokens" PRIMARY KEY ("Id")
);


CREATE TABLE "MockBundles" (
    "Id" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Slug" character varying(200) NOT NULL,
    "ExamFamilyCode" character varying(16) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "MockType" character varying(16) NOT NULL,
    "SubtestCode" character varying(32),
    "ProfessionId" character varying(32),
    "AppliesToAllProfessions" boolean NOT NULL,
    "Status" integer NOT NULL,
    "EstimatedDurationMinutes" integer NOT NULL,
    "Priority" integer NOT NULL,
    "TagsCsv" character varying(512) NOT NULL,
    "Difficulty" character varying(32) NOT NULL,
    "SourceStatus" character varying(32) NOT NULL,
    "QualityStatus" character varying(32) NOT NULL,
    "ReleasePolicy" character varying(32) NOT NULL,
    "TopicTagsCsv" character varying(512) NOT NULL,
    "SkillTagsCsv" character varying(512) NOT NULL,
    "WatermarkEnabled" boolean NOT NULL,
    "RandomiseQuestions" boolean NOT NULL,
    "SourceProvenance" character varying(256),
    "CreatedByAdminId" character varying(64),
    "UpdatedByAdminId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "PublishedAt" timestamp with time zone,
    "ArchivedAt" timestamp with time zone,
    CONSTRAINT "PK_MockBundles" PRIMARY KEY ("Id")
);


CREATE TABLE "MockEntitlementLedgers" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "AddOnId" character varying(64) NOT NULL,
    "MockType" character varying(32) NOT NULL,
    "ConsumedAt" timestamp with time zone NOT NULL,
    "MockAttemptId" character varying(64),
    CONSTRAINT "PK_MockEntitlementLedgers" PRIMARY KEY ("Id")
);


CREATE TABLE "MockItemAnalysisSnapshots" (
    "Id" character varying(64) NOT NULL,
    "MockBundleId" character varying(64) NOT NULL,
    "ContentPaperId" character varying(64) NOT NULL,
    "ItemId" character varying(64) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "Label" character varying(160),
    "TotalAttempts" integer NOT NULL,
    "CorrectCount" integer NOT NULL,
    "Difficulty" double precision NOT NULL,
    "DiscriminationIndex" double precision,
    "DistractorJson" text NOT NULL,
    "Flag" character varying(32),
    "GeneratedAt" timestamp with time zone NOT NULL,
    "RetiredAt" timestamp with time zone,
    "RetiredReason" character varying(512),
    "RetiredByAdminId" character varying(64),
    CONSTRAINT "PK_MockItemAnalysisSnapshots" PRIMARY KEY ("Id")
);


CREATE TABLE "MockReports" (
    "Id" character varying(64) NOT NULL,
    "MockAttemptId" character varying(64) NOT NULL,
    "State" integer NOT NULL,
    "PayloadJson" text NOT NULL,
    "GeneratedAt" timestamp with time zone,
    "PayloadSchemaVersion" character varying(16) NOT NULL,
    CONSTRAINT "PK_MockReports" PRIMARY KEY ("Id")
);


CREATE TABLE "NativeIapProductMappings" (
    "Id" character varying(64) NOT NULL,
    "Platform" character varying(16) NOT NULL,
    "StoreProductId" character varying(192) NOT NULL,
    "TargetType" character varying(32) NOT NULL,
    "TargetId" character varying(96) NOT NULL,
    "DisplayName" character varying(128),
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "CreatedByAdminId" character varying(64),
    "UpdatedByAdminId" character varying(64),
    CONSTRAINT "PK_NativeIapProductMappings" PRIMARY KEY ("Id")
);


CREATE TABLE "NotificationCampaignRecipients" (
    "Id" uuid NOT NULL,
    "CampaignId" uuid NOT NULL,
    "RecipientUserId" character varying(64) NOT NULL,
    "RecipientEmail" character varying(256) NOT NULL,
    "DeliveryStatus" integer NOT NULL,
    "ErrorMessage" character varying(512),
    "DeliveredAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_NotificationCampaignRecipients" PRIMARY KEY ("Id")
);


CREATE TABLE "NotificationCampaigns" (
    "Id" uuid NOT NULL,
    "Name" character varying(256) NOT NULL,
    "Subject" character varying(256) NOT NULL,
    "Body" text NOT NULL,
    "HtmlBody" text,
    "Channel" integer NOT NULL,
    "Status" integer NOT NULL,
    "SegmentJson" text NOT NULL,
    "RecipientCount" integer,
    "DeliveredCount" integer NOT NULL,
    "FailedCount" integer NOT NULL,
    "ScheduledAt" timestamp with time zone,
    "SentAt" timestamp with time zone,
    "VariantLabel" character varying(32),
    "ParentCampaignId" uuid,
    "CreatedByAdminId" character varying(64) NOT NULL,
    "ApprovedByAdminId" character varying(64),
    "ApprovedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_NotificationCampaigns" PRIMARY KEY ("Id")
);


CREATE TABLE "NotificationConsents" (
    "Id" uuid NOT NULL,
    "AuthAccountId" character varying(64) NOT NULL,
    "Channel" integer NOT NULL,
    "Category" character varying(64) NOT NULL,
    "IsGranted" boolean NOT NULL,
    "Source" character varying(64) NOT NULL,
    "Reason" character varying(512),
    "UpdatedByAdminId" character varying(64),
    "UpdatedByAdminName" character varying(128),
    "GrantedAt" timestamp with time zone,
    "RevokedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_NotificationConsents" PRIMARY KEY ("Id")
);


CREATE TABLE "NotificationDeliveryAttempts" (
    "Id" character varying(64) NOT NULL,
    "NotificationEventId" character varying(64) NOT NULL,
    "AuthAccountId" character varying(64) NOT NULL,
    "Channel" integer NOT NULL,
    "Status" integer NOT NULL,
    "SubscriptionId" character varying(64),
    "Provider" character varying(64),
    "MessageId" character varying(256),
    "ErrorCode" character varying(128),
    "ErrorMessage" character varying(1024),
    "ResponsePayloadJson" text NOT NULL,
    "AttemptedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_NotificationDeliveryAttempts" PRIMARY KEY ("Id")
);


CREATE TABLE "NotificationEvents" (
    "Id" character varying(64) NOT NULL,
    "RecipientAuthAccountId" character varying(64) NOT NULL,
    "RecipientRole" character varying(32) NOT NULL,
    "EventKey" character varying(128) NOT NULL,
    "Category" character varying(64) NOT NULL,
    "Title" character varying(256) NOT NULL,
    "Body" character varying(4096) NOT NULL,
    "ActionUrl" character varying(512),
    "Severity" integer NOT NULL,
    "State" integer NOT NULL,
    "EntityType" character varying(128) NOT NULL,
    "EntityId" character varying(128) NOT NULL,
    "VersionOrDateBucket" character varying(128) NOT NULL,
    "DedupeKey" character varying(256) NOT NULL,
    "PayloadJson" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ProcessedAt" timestamp with time zone,
    "FanoutAttempts" integer NOT NULL,
    CONSTRAINT "PK_NotificationEvents" PRIMARY KEY ("Id")
);


CREATE TABLE "NotificationInboxItems" (
    "Id" character varying(64) NOT NULL,
    "NotificationEventId" character varying(64) NOT NULL,
    "AuthAccountId" character varying(64) NOT NULL,
    "EventKey" character varying(128) NOT NULL,
    "Category" character varying(64) NOT NULL,
    "Title" character varying(256) NOT NULL,
    "Body" character varying(4096) NOT NULL,
    "ActionUrl" character varying(512),
    "Severity" integer NOT NULL,
    "IsRead" boolean NOT NULL,
    "ChannelsJson" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ReadAt" timestamp with time zone,
    CONSTRAINT "PK_NotificationInboxItems" PRIMARY KEY ("Id")
);


CREATE TABLE "NotificationPolicyOverrides" (
    "Id" uuid NOT NULL,
    "AudienceRole" character varying(32) NOT NULL,
    "EventKey" character varying(128) NOT NULL,
    "InAppEnabled" boolean,
    "EmailEnabled" boolean,
    "PushEnabled" boolean,
    "EmailMode" integer,
    "MaxDeliveriesPerHour" integer,
    "MaxDeliveriesPerDay" integer,
    "UpdatedByAdminId" character varying(64),
    "UpdatedByAdminName" character varying(128),
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_NotificationPolicyOverrides" PRIMARY KEY ("Id")
);


CREATE TABLE "NotificationPreferences" (
    "Id" uuid NOT NULL,
    "AuthAccountId" character varying(64) NOT NULL,
    "Timezone" character varying(64) NOT NULL,
    "GlobalInAppEnabled" boolean NOT NULL,
    "GlobalEmailEnabled" boolean NOT NULL,
    "GlobalPushEnabled" boolean NOT NULL,
    "QuietHoursEnabled" boolean NOT NULL,
    "QuietHoursStartMinutes" integer,
    "QuietHoursEndMinutes" integer,
    "EventOverridesJson" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_NotificationPreferences" PRIMARY KEY ("Id")
);


CREATE TABLE "NotificationRules" (
    "Id" uuid NOT NULL,
    "EventKey" text NOT NULL,
    "AudienceRole" text,
    "Channels" text NOT NULL,
    "Priority" integer NOT NULL,
    "DelaySeconds" integer,
    "ExpiryMinutes" integer,
    "FallbackChannels" text,
    "RequiredConsentCategory" text,
    "BypassQuietHours" boolean NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_NotificationRules" PRIMARY KEY ("Id")
);


CREATE TABLE "NotificationSuppressions" (
    "Id" uuid NOT NULL,
    "AuthAccountId" character varying(64) NOT NULL,
    "Channel" integer NOT NULL,
    "EventKey" character varying(128),
    "IsActive" boolean NOT NULL,
    "ReasonCode" character varying(128) NOT NULL,
    "Reason" character varying(1024),
    "CreatedByAdminId" character varying(64) NOT NULL,
    "CreatedByAdminName" character varying(128) NOT NULL,
    "ReleasedByAdminId" character varying(64),
    "ReleasedByAdminName" character varying(128),
    "StartsAt" timestamp with time zone,
    "ExpiresAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "ReleasedAt" timestamp with time zone,
    CONSTRAINT "PK_NotificationSuppressions" PRIMARY KEY ("Id")
);


CREATE TABLE "NotificationTemplates" (
    "Id" character varying(64) NOT NULL,
    "EventKey" character varying(128) NOT NULL,
    "Channel" character varying(32) NOT NULL,
    "Category" character varying(64),
    "Locale" character varying(16) NOT NULL,
    "Version" integer NOT NULL,
    "Description" character varying(512),
    "SubjectTemplate" character varying(256) NOT NULL,
    "BodyTemplate" text NOT NULL,
    "TextTemplate" text,
    "HtmlTemplate" text,
    "MetadataJson" jsonb NOT NULL DEFAULT '{}',
    "CreatedByAdminId" character varying(64),
    "UpdatedByAdminId" character varying(64),
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "PublishedAt" timestamp with time zone,
    CONSTRAINT "PK_NotificationTemplates" PRIMARY KEY ("Id")
);


CREATE TABLE "OrderRefunds" (
    "Id" uuid NOT NULL,
    "PaymentTransactionId" character varying(256) NOT NULL,
    "LearnerUserId" character varying(64) NOT NULL,
    "Gateway" character varying(16) NOT NULL,
    "GatewayRefundId" character varying(256) NOT NULL,
    "IdempotencyKey" character varying(64) NOT NULL,
    "RefundType" character varying(16) NOT NULL,
    "Amount" numeric NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "Reason" character varying(64),
    "AdminNote" character varying(1024),
    "RequestedByAdminId" character varying(64),
    "RequestedByAdminName" character varying(128),
    "ReversedWalletCredits" boolean NOT NULL,
    "ReversedEntitlements" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_OrderRefunds" PRIMARY KEY ("Id")
);


CREATE TABLE "PackageContentRules" (
    "Id" character varying(64) NOT NULL,
    "PackageId" character varying(64) NOT NULL,
    "RuleType" character varying(32) NOT NULL,
    "TargetId" character varying(64) NOT NULL,
    "TargetType" character varying(32) NOT NULL,
    CONSTRAINT "PK_PackageContentRules" PRIMARY KEY ("Id")
);


CREATE TABLE "PaymentDisputes" (
    "Id" uuid NOT NULL,
    "PaymentTransactionId" character varying(256) NOT NULL,
    "LearnerUserId" character varying(64) NOT NULL,
    "SubscriptionId" character varying(64),
    "Gateway" character varying(16) NOT NULL,
    "GatewayDisputeId" character varying(256) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "Reason" character varying(64),
    "AmountDisputed" numeric NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "EntitlementsFrozen" boolean NOT NULL,
    "OpenedAt" timestamp with time zone NOT NULL,
    "FundsWithdrawnAt" timestamp with time zone,
    "ResolvedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PaymentDisputes" PRIMARY KEY ("Id")
);


CREATE TABLE "PaymentMethodUpdateLinks" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "SubscriptionId" character varying(64) NOT NULL,
    "Token" character varying(128) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "UsedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PaymentMethodUpdateLinks" PRIMARY KEY ("Id")
);


CREATE TABLE "PaymentTransactions" (
    "Id" uuid NOT NULL,
    "LearnerUserId" character varying(64) NOT NULL,
    "Gateway" character varying(16) NOT NULL,
    "GatewayTransactionId" character varying(256) NOT NULL,
    "TransactionType" character varying(32) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "Amount" numeric NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "ProductType" character varying(32),
    "ProductId" character varying(128),
    "QuoteId" character varying(64),
    "PlanVersionId" character varying(64),
    "AddOnVersionIdsJson" character varying(1024) NOT NULL,
    "CouponVersionId" character varying(64),
    "MetadataJson" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PaymentTransactions" PRIMARY KEY ("Id")
);


CREATE TABLE "PaymentWebhookEvents" (
    "Id" uuid NOT NULL,
    "Gateway" character varying(16) NOT NULL,
    "EventType" character varying(128) NOT NULL,
    "GatewayEventId" character varying(256) NOT NULL,
    "ProcessingStatus" character varying(32) NOT NULL,
    "VerificationStatus" character varying(32) NOT NULL,
    "VerifiedAt" timestamp with time zone,
    "PayloadSha256" character varying(64),
    "ParserVersion" character varying(32),
    "GatewayTransactionId" character varying(256),
    "NormalizedStatus" character varying(32),
    "AttemptCount" integer NOT NULL,
    "RetryCount" integer NOT NULL,
    "LastAttemptedAt" timestamp with time zone,
    "LastRetriedAt" timestamp with time zone,
    "LastRetriedByAdminId" character varying(64),
    "LastRetriedByAdminName" character varying(128),
    "PayloadJson" jsonb NOT NULL,
    "ErrorMessage" text,
    "ReceivedAt" timestamp with time zone NOT NULL,
    "ProcessedAt" timestamp with time zone,
    CONSTRAINT "PK_PaymentWebhookEvents" PRIMARY KEY ("Id")
);


CREATE TABLE "PeerReviewFeedbacks" (
    "Id" character varying(64) NOT NULL,
    "PeerReviewRequestId" character varying(64) NOT NULL,
    "ReviewerUserId" character varying(64) NOT NULL,
    "OverallRating" integer NOT NULL,
    "Comments" character varying(2000) NOT NULL,
    "StrengthNotes" character varying(1000),
    "ImprovementNotes" character varying(1000),
    "HelpfulnessRating" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PeerReviewFeedbacks" PRIMARY KEY ("Id")
);


CREATE TABLE "PeerReviewRequests" (
    "Id" character varying(64) NOT NULL,
    "SubmitterUserId" character varying(64) NOT NULL,
    "ReviewerUserId" character varying(64),
    "AttemptId" character varying(64) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ClaimedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_PeerReviewRequests" PRIMARY KEY ("Id")
);


CREATE TABLE "PermissionTemplates" (
    "Id" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Description" character varying(512),
    "Permissions" text NOT NULL,
    "CreatedBy" character varying(128) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PermissionTemplates" PRIMARY KEY ("Id")
);


CREATE TABLE "PredictionSnapshots" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "PredictedScoreLow" integer NOT NULL,
    "PredictedScoreHigh" integer NOT NULL,
    "PredictedScoreMid" integer NOT NULL,
    "ConfidenceLevel" character varying(32) NOT NULL,
    "FactorsJson" text NOT NULL,
    "TrendJson" text NOT NULL,
    "EvaluationCount" integer NOT NULL,
    "ComputedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PredictionSnapshots" PRIMARY KEY ("Id")
);


CREATE TABLE "PricingExperimentAssignments" (
    "Id" character varying(64) NOT NULL,
    "ExperimentId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "VariantCode" character varying(32) NOT NULL,
    "Converted" boolean NOT NULL,
    "ConvertedAt" timestamp with time zone,
    "ConvertedAmount" numeric(12,2),
    "AssignedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PricingExperimentAssignments" PRIMARY KEY ("Id")
);


CREATE TABLE "PricingExperiments" (
    "Id" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Code" character varying(64) NOT NULL,
    "TargetType" character varying(32) NOT NULL,
    "TargetId" character varying(64) NOT NULL,
    "Region" character varying(16) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "RolloutPercent" integer NOT NULL,
    "VariantsJson" character varying(2048) NOT NULL,
    "StartedAt" timestamp with time zone,
    "EndedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "CreatedByAdminId" character varying(64),
    CONSTRAINT "PK_PricingExperiments" PRIMARY KEY ("Id")
);


CREATE TABLE "PrivateSpeakingAuditLogs" (
    "Id" character varying(64) NOT NULL,
    "BookingId" character varying(64),
    "ActorId" character varying(64) NOT NULL,
    "ActorRole" character varying(32) NOT NULL,
    "Action" character varying(64) NOT NULL,
    "Details" character varying(2048),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PrivateSpeakingAuditLogs" PRIMARY KEY ("Id")
);


CREATE TABLE "PrivateSpeakingConfigs" (
    "Id" character varying(64) NOT NULL,
    "IsEnabled" boolean NOT NULL,
    "DefaultSlotDurationMinutes" integer NOT NULL,
    "BufferMinutesBetweenSlots" integer NOT NULL,
    "MinBookingLeadTimeHours" integer NOT NULL,
    "MaxBookingAdvanceDays" integer NOT NULL,
    "ReservationTimeoutMinutes" integer NOT NULL,
    "DefaultPriceMinorUnits" integer NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "CancellationWindowHours" integer NOT NULL,
    "AllowReschedule" boolean NOT NULL,
    "RescheduleWindowHours" integer NOT NULL,
    "ReminderOffsetsHoursJson" character varying(256) NOT NULL,
    "DailyReminderEnabled" boolean NOT NULL,
    "DailyReminderHourUtc" integer NOT NULL,
    "CancellationPolicyText" character varying(2048),
    "BookingPolicyText" character varying(2048),
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PrivateSpeakingConfigs" PRIMARY KEY ("Id")
);


CREATE TABLE "Professions" (
    "Id" character varying(32) NOT NULL,
    "Code" character varying(32) NOT NULL,
    "Label" character varying(128) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "SortOrder" integer NOT NULL,
    CONSTRAINT "PK_Professions" PRIMARY KEY ("Id")
);


CREATE TABLE "PronunciationAssessments" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "DrillId" character varying(64),
    "AttemptId" character varying(64),
    "ConversationSessionId" character varying(64),
    "AccuracyScore" double precision NOT NULL,
    "FluencyScore" double precision NOT NULL,
    "CompletenessScore" double precision NOT NULL,
    "ProsodyScore" double precision NOT NULL,
    "OverallScore" double precision NOT NULL,
    "ProjectedSpeakingScaled" integer NOT NULL,
    "ProjectedSpeakingGrade" character varying(4) NOT NULL,
    "WordScoresJson" text NOT NULL,
    "ProblematicPhonemesJson" text NOT NULL,
    "FluencyMarkersJson" text NOT NULL,
    "FindingsJson" text NOT NULL,
    "FeedbackJson" text NOT NULL,
    "Provider" character varying(32) NOT NULL,
    "RulebookVersion" character varying(32) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PronunciationAssessments" PRIMARY KEY ("Id")
);


CREATE TABLE "PronunciationAttempts" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "DrillId" character varying(64) NOT NULL,
    "AudioStorageKey" character varying(512),
    "AudioSha256" character varying(64),
    "AudioBytes" bigint,
    "AudioMimeType" character varying(32),
    "AudioDurationMs" integer,
    "Status" character varying(16) NOT NULL,
    "AssessmentId" character varying(64),
    "ErrorCode" character varying(64),
    "ErrorMessage" character varying(512),
    "Provider" character varying(32),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "AudioReapAt" timestamp with time zone,
    CONSTRAINT "PK_PronunciationAttempts" PRIMARY KEY ("Id")
);


CREATE TABLE "PronunciationCards" (
    "Id" uuid NOT NULL,
    "Word" character varying(128) NOT NULL,
    "PronunciationIpa" character varying(128) NOT NULL,
    "BritishIpa" character varying(128) NOT NULL,
    "AustralianIpa" character varying(128) NOT NULL,
    "AmericanIpa" character varying(128) NOT NULL,
    "AudioBritishUrl" text,
    "AudioAustralianUrl" text,
    "AudioAmericanUrl" text,
    "DefinitionEn" text NOT NULL,
    "DefinitionAr" text NOT NULL,
    "SyllableCount" integer NOT NULL,
    "StressPattern" character varying(64) NOT NULL,
    "CommonMispronunciationsJson" text NOT NULL,
    "SimilarSoundingTrapsJson" text NOT NULL,
    "Difficulty" integer NOT NULL,
    "ProfessionRelevanceJson" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PronunciationCards" PRIMARY KEY ("Id")
);


CREATE TABLE "PronunciationDrills" (
    "Id" character varying(64) NOT NULL,
    "TargetPhoneme" character varying(32) NOT NULL,
    "Label" character varying(128) NOT NULL,
    "Profession" character varying(48) NOT NULL,
    "Focus" character varying(24) NOT NULL,
    "PrimaryRuleId" character varying(16),
    "ExampleWordsJson" text NOT NULL,
    "MinimalPairsJson" text NOT NULL,
    "SentencesJson" text NOT NULL,
    "AudioModelUrl" character varying(512),
    "AudioModelAssetId" character varying(64),
    "TipsHtml" text NOT NULL,
    "Difficulty" character varying(16) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "OrderIndex" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PronunciationDrills" PRIMARY KEY ("Id")
);


CREATE TABLE "PushSubscriptions" (
    "Id" uuid NOT NULL,
    "AuthAccountId" character varying(64) NOT NULL,
    "Endpoint" character varying(2048) NOT NULL,
    "P256dh" character varying(1024) NOT NULL,
    "Auth" character varying(1024) NOT NULL,
    "ExpiresAt" timestamp with time zone,
    "IsActive" boolean NOT NULL,
    "UserAgent" character varying(256),
    "FailureReasonCode" character varying(128),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "LastSuccessfulAt" timestamp with time zone,
    "LastFailureAt" timestamp with time zone,
    CONSTRAINT "PK_PushSubscriptions" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadinessHistories" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "WeekStartDate" date NOT NULL,
    "RecordedAt" timestamp with time zone NOT NULL,
    "Overall" numeric NOT NULL,
    "Writing" numeric NOT NULL,
    "Speaking" numeric NOT NULL,
    "Reading" numeric NOT NULL,
    "Listening" numeric NOT NULL,
    "Vocabulary" numeric NOT NULL,
    "Risk" character varying(16) NOT NULL DEFAULT 'Unknown',
    "TargetDateProbability" numeric,
    CONSTRAINT "PK_ReadinessHistories" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadinessSnapshots" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ComputedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "PayloadJson" text NOT NULL,
    "Version" integer NOT NULL,
    "OverallReadiness" numeric NOT NULL,
    "WritingReadiness" numeric NOT NULL,
    "SpeakingReadiness" numeric NOT NULL,
    "ReadingReadiness" numeric NOT NULL,
    "ListeningReadiness" numeric NOT NULL,
    "VocabularyReadiness" numeric NOT NULL,
    "OverallRisk" character varying(16) NOT NULL DEFAULT 'Unknown',
    "TargetDateProbability" numeric,
    "WeakestSubtest" character varying(32),
    "RecommendedStudyHoursPerWeek" integer NOT NULL,
    "ConfidenceLevel" character varying(16) NOT NULL DEFAULT 'Low',
    "DataPointCount" integer NOT NULL,
    CONSTRAINT "PK_ReadinessSnapshots" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingAnswerRevisions" (
    "Id" character varying(64) NOT NULL,
    "ReadingAttemptId" character varying(64) NOT NULL,
    "ReadingQuestionId" character varying(64) NOT NULL,
    "UserAnswerJson" character varying(2048) NOT NULL,
    "RecordedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ReadingAnswerRevisions" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingAssignments" (
    "Id" character varying(64) NOT NULL,
    "AssignedByUserId" character varying(64) NOT NULL,
    "AssignedToUserId" character varying(64) NOT NULL,
    "PaperId" character varying(64) NOT NULL,
    "Kind" character varying(24) NOT NULL,
    "ScopeJson" character varying(8192),
    "Note" character varying(1000),
    "DueAt" timestamp with time zone,
    "CompletedAttemptId" character varying(64),
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ReadingAssignments" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingAttemptFeedbacks" (
    "Id" character varying(64) NOT NULL,
    "ReadingAttemptId" character varying(64) NOT NULL,
    "Scope" character varying(16) NOT NULL,
    "TargetRef" character varying(128),
    "AuthorUserId" character varying(64) NOT NULL,
    "FeedbackText" character varying(4000) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ReadingAttemptFeedbacks" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingAttempts" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "PaperId" character varying(64) NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "DeadlineAt" timestamp with time zone,
    "PartBCTimerPausedAt" timestamp with time zone,
    "PartBCPausedSeconds" integer NOT NULL,
    "PartABreakUsed" boolean NOT NULL,
    "SubmittedAt" timestamp with time zone,
    "LastActivityAt" timestamp with time zone NOT NULL,
    "Status" integer NOT NULL,
    "RawScore" integer,
    "ScaledScore" integer,
    "MaxRawScore" integer NOT NULL,
    "PolicySnapshotJson" character varying(16384) NOT NULL,
    "PaperRevisionId" character varying(64),
    "RowVersion" integer NOT NULL,
    "Mode" integer NOT NULL,
    "ScopeJson" character varying(8192),
    "RulebookVersion" character varying(32),
    "ScoreOverrideRaw" integer,
    "ScoreOverrideScaled" integer,
    "ScoreOverrideReason" character varying(2000),
    "OverriddenByUserId" character varying(64),
    "OverriddenAt" timestamp with time zone,
    CONSTRAINT "PK_ReadingAttempts" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingDailyPlanItems" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "PlanDate" date NOT NULL,
    "Ordinal" integer NOT NULL,
    "ItemType" character varying(32) NOT NULL,
    "FocusSkill" character varying(4),
    "EstimatedMinutes" integer NOT NULL,
    "PayloadJson" text NOT NULL,
    "Status" character varying(16) NOT NULL,
    "StartedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_ReadingDailyPlanItems" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingErrorBankEntries" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ReadingQuestionId" character varying(64) NOT NULL,
    "PaperId" character varying(64) NOT NULL,
    "PartCode" integer NOT NULL,
    "LastWrongAttemptId" character varying(64) NOT NULL,
    "FirstSeenWrongAt" timestamp with time zone NOT NULL,
    "LastSeenWrongAt" timestamp with time zone NOT NULL,
    "TimesWrong" integer NOT NULL,
    "IsResolved" boolean NOT NULL,
    "ResolvedAt" timestamp with time zone,
    "ResolvedReason" character varying(32),
    CONSTRAINT "PK_ReadingErrorBankEntries" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingExtractionDrafts" (
    "Id" character varying(64) NOT NULL,
    "PaperId" character varying(64) NOT NULL,
    "MediaAssetId" character varying(64),
    "Status" integer NOT NULL,
    "ExtractedManifestJson" text,
    "RawAiResponseJson" character varying(65536),
    "Notes" character varying(2048),
    "IsStub" boolean NOT NULL,
    "CreatedByAdminId" character varying(64) NOT NULL,
    "ResolvedByAdminId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "ResolvedAt" timestamp with time zone,
    CONSTRAINT "PK_ReadingExtractionDrafts" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingLessons" (
    "Id" uuid NOT NULL,
    "Slug" character varying(128) NOT NULL,
    "Title" character varying(256) NOT NULL,
    "TitleAr" character varying(256) NOT NULL,
    "SkillCode" character varying(4) NOT NULL,
    "OrderIndex" integer NOT NULL,
    "EstimatedMinutes" integer NOT NULL,
    "VideoUrl" text,
    "BodyMarkdownEn" text NOT NULL,
    "BodyMarkdownAr" text NOT NULL,
    "DrillQuestionIdsJson" text NOT NULL,
    "QuizQuestionIdsJson" text NOT NULL,
    "PrerequisiteLessonId" uuid,
    "IsPublished" boolean NOT NULL,
    CONSTRAINT "PK_ReadingLessons" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingMockTemplates" (
    "Id" uuid NOT NULL,
    "Title" character varying(256) NOT NULL,
    "Difficulty" integer NOT NULL,
    "QuestionIdsJson" text NOT NULL,
    "IsPublished" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ReadingMockTemplates" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingPolicies" (
    "Id" character varying(32) NOT NULL,
    "AttemptsPerPaperPerUser" integer NOT NULL,
    "AttemptCooldownMinutes" integer NOT NULL,
    "BestScoreDisplay" character varying(16) NOT NULL,
    "ShowPastAttempts" boolean NOT NULL,
    "AllowAttemptOnArchivedPaper" boolean NOT NULL,
    "PartATimerStrictness" character varying(16) NOT NULL,
    "PartATimerMinutes" integer NOT NULL,
    "PartBCTimerMinutes" integer NOT NULL,
    "GracePeriodSeconds" integer NOT NULL,
    "OnExpirySubmitPolicy" character varying(32) NOT NULL,
    "CountdownWarningsJson" text NOT NULL,
    "EnabledQuestionTypesJson" text NOT NULL,
    "ShortAnswerNormalisation" character varying(32) NOT NULL,
    "ShortAnswerAcceptSynonyms" boolean NOT NULL,
    "MatchingAllowPartialCredit" boolean NOT NULL,
    "SentenceCompletionStrictness" character varying(32) NOT NULL,
    "UnknownTypeFallbackPolicy" character varying(32) NOT NULL,
    "NormalizeSmartQuotes" boolean NOT NULL,
    "NormalizeHyphenSpacing" boolean NOT NULL,
    "NormalizeUnitSpacing" boolean NOT NULL,
    "PartACaseInsensitive" boolean NOT NULL,
    "ShowExplanationsAfterSubmit" boolean NOT NULL,
    "ShowExplanationsOnlyIfWrong" boolean NOT NULL,
    "ShowCorrectAnswerOnReview" boolean NOT NULL,
    "AllowResultDownload" boolean NOT NULL,
    "AllowResultSharing" boolean NOT NULL,
    "AiExtractionEnabled" boolean NOT NULL,
    "AiExtractionRequireHumanApproval" boolean NOT NULL,
    "AiExtractionMaxRetriesPerPaper" integer NOT NULL,
    "AiExtractionModelOverride" character varying(64),
    "AiExtractionStrictSchemaMode" character varying(16) NOT NULL,
    "QuestionBankEnabled" boolean NOT NULL,
    "AssemblyStrategy" character varying(32) NOT NULL,
    "AllowLearnerRandomisation" boolean NOT NULL,
    "FontScaleUserControl" boolean NOT NULL,
    "HighContrastMode" boolean NOT NULL,
    "ScreenReaderOptimised" boolean NOT NULL,
    "AllowPaperReadingMode" boolean NOT NULL DEFAULT TRUE,
    "ExtraTimeApprovalWorkflow" boolean NOT NULL,
    "RequireFreshAuthForSubmit" boolean NOT NULL,
    "AllowMultipleConcurrentAttempts" boolean NOT NULL,
    "AttemptIpPinning" character varying(32) NOT NULL,
    "SubmitRateLimitPerMinute" integer NOT NULL,
    "AutosaveRateLimitPerMinute" integer NOT NULL,
    "PreventMultipleTabs" boolean NOT NULL,
    "RetainAnswerRowsDays" integer NOT NULL,
    "RetainAttemptHeadersDays" integer NOT NULL,
    "AnonymiseOnAccountDelete" boolean NOT NULL,
    "ShareAnonymousAnalytics" boolean NOT NULL,
    "AllowPausingAttempt" boolean NOT NULL,
    "AutoExpireWorkerEnabled" boolean NOT NULL,
    "AutoExpireAfterMinutes" integer NOT NULL,
    "AllowResumeAfterExpiry" boolean NOT NULL,
    "RowVersion" integer NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "UpdatedByAdminId" character varying(64),
    CONSTRAINT "PK_ReadingPolicies" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingPracticeSessions" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "SessionType" character varying(32) NOT NULL,
    "FocusSkill" character varying(4),
    "QuestionIdsJson" text NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "DurationSeconds" integer,
    "Score" integer,
    "TotalQuestions" integer,
    "MetadataJson" text NOT NULL,
    CONSTRAINT "PK_ReadingPracticeSessions" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingQuestionAttempts" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ReadingQuestionId" uuid NOT NULL,
    "PracticeSessionId" uuid,
    "SelectedOption" character varying(64),
    "IsCorrect" boolean NOT NULL,
    "IsUnknown" boolean NOT NULL,
    "TimeSpentSeconds" integer NOT NULL,
    "MarkedForReview" boolean NOT NULL,
    "NoteText" text,
    "AttemptedAt" timestamp with time zone NOT NULL,
    "InReviewQueue" boolean NOT NULL,
    "NextReviewAt" timestamp with time zone,
    "ReviewIntervalIndex" integer NOT NULL,
    "ConsecutiveCorrect" integer NOT NULL,
    CONSTRAINT "PK_ReadingQuestionAttempts" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingQuestionDiscussionComments" (
    "Id" uuid NOT NULL,
    "ReadingQuestionId" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Body" text NOT NULL,
    "Upvotes" integer NOT NULL,
    "IsFromTutor" boolean NOT NULL,
    "IsHidden" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ReadingQuestionDiscussionComments" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingStrategies" (
    "Id" uuid NOT NULL,
    "Slug" character varying(128) NOT NULL,
    "Title" character varying(256) NOT NULL,
    "TitleAr" character varying(256) NOT NULL,
    "Category" character varying(64) NOT NULL,
    "ApplicablePartsJson" text NOT NULL,
    "EstimatedReadMinutes" integer NOT NULL,
    "BodyMarkdownEn" text NOT NULL,
    "BodyMarkdownAr" text NOT NULL,
    "VideoUrl" text,
    "LinkedDrillId" uuid,
    "RelatedStrategyIdsJson" text NOT NULL,
    "UnlockStage" character varying(32) NOT NULL,
    "Difficulty" integer NOT NULL,
    "IsPublished" boolean NOT NULL,
    CONSTRAINT "PK_ReadingStrategies" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingStrategyProgresses" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "StrategyId" uuid NOT NULL,
    "MarkedAsRead" boolean NOT NULL,
    "Favorited" boolean NOT NULL,
    "ReadAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ReadingStrategyProgresses" PRIMARY KEY ("Id")
);


CREATE TABLE "ReadingUserPolicyOverrides" (
    "UserId" character varying(64) NOT NULL,
    "ExtraTimeEntitlementPct" integer NOT NULL,
    "BlockAttempts" boolean NOT NULL,
    "Reason" character varying(512),
    "GrantedByAdminId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone,
    CONSTRAINT "PK_ReadingUserPolicyOverrides" PRIMARY KEY ("UserId")
);


CREATE TABLE "RecallBookmarks" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "VocabularyTermId" character varying(64) NOT NULL,
    "Source" character varying(16) NOT NULL,
    "CreatedByFeatureCode" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_RecallBookmarks" PRIMARY KEY ("Id")
);


CREATE TABLE "RecallSetTags" (
    "Code" character varying(64) NOT NULL,
    "DisplayName" character varying(200) NOT NULL,
    "ShortLabel" character varying(64),
    "Description" text,
    "SortOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "ExamTypeCode" character varying(16),
    "CreatedByUserId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_RecallSetTags" PRIMARY KEY ("Code")
);


CREATE TABLE "ReferralCodes" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Code" character varying(16) NOT NULL,
    "TotalReferrals" integer NOT NULL,
    "ConvertedReferrals" integer NOT NULL,
    "TotalCreditsEarned" numeric NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ReferralCodes" PRIMARY KEY ("Id")
);


CREATE TABLE "ReferralRecords" (
    "Id" character varying(64) NOT NULL,
    "ReferrerUserId" character varying(64) NOT NULL,
    "ReferralCode" character varying(32) NOT NULL,
    "ReferredUserId" character varying(64),
    "Status" character varying(32) NOT NULL,
    "ReferrerCreditAmount" numeric NOT NULL,
    "ReferredDiscountPercent" numeric NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ActivatedAt" timestamp with time zone,
    "RewardedAt" timestamp with time zone,
    CONSTRAINT "PK_ReferralRecords" PRIMARY KEY ("Id")
);


CREATE TABLE "Referrals" (
    "Id" character varying(64) NOT NULL,
    "ReferrerUserId" character varying(64) NOT NULL,
    "ReferredUserId" character varying(64),
    "ReferredEmail" character varying(256) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "CreditAmount" numeric NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "RegisteredAt" timestamp with time zone,
    "ConvertedAt" timestamp with time zone,
    "CreditedAt" timestamp with time zone,
    CONSTRAINT "PK_Referrals" PRIMARY KEY ("Id")
);


CREATE TABLE "RegionPricings" (
    "Id" character varying(64) NOT NULL,
    "TargetType" character varying(32) NOT NULL,
    "TargetId" character varying(64) NOT NULL,
    "Region" character varying(16) NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "PriceAmount" numeric(12,2) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "CreatedByAdminId" character varying(64),
    "UpdatedByAdminId" character varying(64),
    CONSTRAINT "PK_RegionPricings" PRIMARY KEY ("Id")
);


CREATE TABLE "RemediationTasks" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "MockReportId" character varying(64) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "WeaknessTag" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Description" character varying(1000) NOT NULL,
    "RouteHref" character varying(256),
    "DayIndex" integer NOT NULL,
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_RemediationTasks" PRIMARY KEY ("Id")
);


CREATE TABLE "ReviewEscalations" (
    "Id" character varying(64) NOT NULL,
    "ReviewRequestId" character varying(64) NOT NULL,
    "OriginalReviewerId" character varying(64) NOT NULL,
    "SecondReviewerId" character varying(64),
    "SubtestCode" character varying(32) NOT NULL,
    "TriggerCriterion" character varying(64) NOT NULL,
    "AiScore" integer NOT NULL,
    "HumanScore" integer NOT NULL,
    "Divergence" integer NOT NULL,
    "Status" character varying(32) NOT NULL,
    "ResolutionNote" character varying(512),
    "FinalScore" integer,
    "ConfigId" character varying(64),
    "AttemptId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "ResolvedAt" timestamp with time zone,
    CONSTRAINT "PK_ReviewEscalations" PRIMARY KEY ("Id")
);


CREATE TABLE "ReviewItems" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "SourceType" character varying(32) NOT NULL,
    "SourceId" character varying(64),
    "SubtestCode" character varying(32) NOT NULL,
    "CriterionCode" character varying(32),
    "QuestionJson" text NOT NULL,
    "AnswerJson" text NOT NULL,
    "EaseFactor" double precision NOT NULL,
    "IntervalDays" integer NOT NULL,
    "ReviewCount" integer NOT NULL,
    "ConsecutiveCorrect" integer NOT NULL,
    "DueDate" date NOT NULL,
    "LastReviewedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "Status" character varying(16) NOT NULL,
    "Starred" boolean NOT NULL,
    "StarReason" character varying(16),
    CONSTRAINT "PK_ReviewItems" PRIMARY KEY ("Id")
);


CREATE TABLE "ReviewRequests" (
    "Id" character varying(64) NOT NULL,
    "AttemptId" character varying(64) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "State" integer NOT NULL,
    "TurnaroundOption" character varying(32) NOT NULL,
    "FocusAreasJson" text NOT NULL,
    "LearnerNotes" text NOT NULL,
    "PaymentSource" character varying(32) NOT NULL,
    "PriceSnapshot" numeric NOT NULL,
    "ReviewerCompensation" numeric NOT NULL,
    "CompensationPaid" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "EligibilitySnapshotJson" text NOT NULL,
    CONSTRAINT "PK_ReviewRequests" PRIMARY KEY ("Id")
);


CREATE TABLE "RuntimeSettings" (
    "Id" character varying(32) NOT NULL,
    "BrevoApiKeyEncrypted" text,
    "BrevoEmailVerificationTemplateId" integer,
    "BrevoPasswordResetTemplateId" integer,
    "SmtpHost" character varying(256),
    "SmtpPort" integer,
    "SmtpUsername" character varying(256),
    "SmtpPasswordEncrypted" text,
    "SmtpFromAddress" character varying(256),
    "SmtpFromName" character varying(256),
    "StripeSecretKeyEncrypted" text,
    "StripePublishableKey" character varying(256),
    "StripeWebhookSecretEncrypted" text,
    "StripeSuccessUrl" character varying(1024),
    "StripeCancelUrl" character varying(1024),
    "StripeTaxAutomaticEnabled" boolean,
    "StripeTaxRegistrationsCsv" character varying(1024),
    "StripeCustomerPortalConfigurationId" character varying(128),
    "StripeRadarHighRiskCountryAllowReview" boolean,
    "StripeRadarBlockEmailDomainsCsv" character varying(1024),
    "PayPalClientId" character varying(256),
    "PayPalClientSecretEncrypted" text,
    "PayPalWebhookIdEncrypted" text,
    "PayPalSuccessUrl" character varying(1024),
    "PayPalCancelUrl" character varying(1024),
    "SentryDsn" character varying(512),
    "SentryEnvironment" character varying(64),
    "SentrySampleRate" double precision,
    "BackupS3Url" character varying(1024),
    "BackupAwsAccessKeyId" character varying(256),
    "BackupAwsSecretAccessKeyEncrypted" text,
    "BackupGpgPassphraseEncrypted" text,
    "BackupAlertWebhook" character varying(1024),
    "GoogleClientId" character varying(256),
    "GoogleClientSecretEncrypted" text,
    "AppleClientId" character varying(256),
    "AppleTeamId" character varying(64),
    "AppleKeyId" character varying(64),
    "ApplePrivateKeyEncrypted" text,
    "FacebookAppId" character varying(256),
    "FacebookAppSecretEncrypted" text,
    "ApnsKeyId" character varying(64),
    "ApnsTeamId" character varying(64),
    "ApnsBundleId" character varying(256),
    "ApnsAuthKeyEncrypted" text,
    "FcmServerKeyEncrypted" text,
    "FcmProjectId" character varying(256),
    "VapidSubject" character varying(256),
    "VapidPublicKey" character varying(512),
    "VapidPrivateKeyEncrypted" text,
    "UploadScannerProvider" character varying(32),
    "UploadScannerHost" character varying(256),
    "UploadScannerPort" integer,
    "UploadScannerTimeoutSeconds" integer,
    "UploadScannerFailClosedOnError" boolean,
    "ZoomEnabled" boolean,
    "ZoomAccountId" character varying(256),
    "ZoomClientId" character varying(256),
    "ZoomClientSecretEncrypted" text,
    "ZoomApiBaseUrl" character varying(512),
    "ZoomTokenUrl" character varying(512),
    "ZoomHostUserId" character varying(256),
    "ZoomMeetingSdkKey" character varying(256),
    "ZoomMeetingSdkSecretEncrypted" text,
    "ZoomWebhookSecretTokenEncrypted" text,
    "ZoomWebhookRetryToleranceSeconds" integer,
    "ZoomAllowSandboxFallback" boolean,
    "LiveClassesAiRecordingProcessingEnabled" boolean,
    "SpeakingWhisperApiKeyEncrypted" text,
    "SpeakingWhisperBaseUrl" character varying(512),
    "SpeakingWhisperModel" character varying(64),
    "SpeakingLiveKitProvider" character varying(32),
    "SpeakingLiveKitApiKeyEncrypted" text,
    "SpeakingLiveKitApiSecretEncrypted" text,
    "SpeakingLiveKitWssUrl" character varying(512),
    "SpeakingLiveKitWebhookSigningSecretEncrypted" text,
    "SpeakingLiveKitEgressBucket" character varying(256),
    "SpeakingLiveKitDefaultMaxDurationSeconds" integer,
    "SpeakingLiveKitEgressEnabled" boolean,
    "SpeakingAnthropicApiKeyEncrypted" text,
    "SpeakingElevenLabsApiKeyEncrypted" text,
    "SpeakingAwsAccessKeyId" character varying(256),
    "SpeakingAwsSecretAccessKeyEncrypted" text,
    "SpeakingAwsRegion" character varying(64),
    "SpeakingAwsBucket" character varying(256),
    "SpeakingComplianceCurrentConsentVersion" character varying(64),
    "SpeakingComplianceCurrentLiveVideoConsentVersion" character varying(64),
    "SpeakingComplianceRetentionDaysDefault" integer,
    "SpeakingComplianceRetentionDaysWhenTutorReviewed" integer,
    "SpeakingComplianceAuditLogRetentionDays" integer,
    "SpeakingV2Enabled" boolean,
    "UpdatedByUserId" character varying(64),
    "UpdatedByUserName" character varying(128),
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_RuntimeSettings" PRIMARY KEY ("Id")
);


CREATE TABLE "ScheduleExceptions" (
    "Id" character varying(64) NOT NULL,
    "ReviewerId" character varying(64) NOT NULL,
    "Date" date NOT NULL,
    "IsBlocked" boolean NOT NULL,
    "StartTime" character varying(5),
    "EndTime" character varying(5),
    "Reason" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ScheduleExceptions" PRIMARY KEY ("Id")
);


CREATE TABLE "Scholarships" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "GrantedByAdminId" character varying(64) NOT NULL,
    "Reason" character varying(32) NOT NULL,
    "AccessTier" character varying(32) NOT NULL,
    "EntitlementsJson" character varying(2048) NOT NULL,
    "GrantedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone,
    "RevokedAt" timestamp with time zone,
    "RevokedByAdminId" character varying(64),
    "AdminNotes" character varying(1024),
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Scholarships" PRIMARY KEY ("Id")
);


CREATE TABLE "ScoreGuaranteePledges" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "SubscriptionId" character varying(64) NOT NULL,
    "BaselineScore" integer NOT NULL,
    "GuaranteedImprovement" integer NOT NULL,
    "ActualScore" integer,
    "Status" character varying(32) NOT NULL,
    "ProofDocumentUrl" character varying(512),
    "ClaimNote" character varying(512),
    "ReviewNote" character varying(512),
    "ReviewedBy" character varying(64),
    "ActivatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "ClaimSubmittedAt" timestamp with time zone,
    "ReviewedAt" timestamp with time zone,
    CONSTRAINT "PK_ScoreGuaranteePledges" PRIMARY KEY ("Id")
);


CREATE TABLE "ScoringPolicies" (
    "Id" character varying(64) NOT NULL,
    "BodyMarkdown" text NOT NULL,
    "PolicyJson" text NOT NULL,
    "IsActive" boolean NOT NULL,
    "UpdatedByUserId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ScoringPolicies" PRIMARY KEY ("Id")
);


CREATE TABLE "Settings" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ProfileJson" text NOT NULL,
    "NotificationsJson" text NOT NULL,
    "PrivacyJson" text NOT NULL,
    "AccessibilityJson" text NOT NULL,
    "AudioJson" text NOT NULL,
    "StudyJson" text NOT NULL,
    CONSTRAINT "PK_Settings" PRIMARY KEY ("Id")
);


CREATE TABLE "SignupExamTypeCatalog" (
    "Id" character varying(32) NOT NULL,
    "Label" character varying(64) NOT NULL,
    "Code" character varying(16) NOT NULL,
    "Description" character varying(256) NOT NULL,
    "SortOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    CONSTRAINT "PK_SignupExamTypeCatalog" PRIMARY KEY ("Id")
);


CREATE TABLE "SignupProfessionCatalog" (
    "Id" character varying(32) NOT NULL,
    "Label" character varying(128) NOT NULL,
    "CountryTargetsJson" text NOT NULL,
    "ExamTypeIdsJson" text NOT NULL,
    "Description" character varying(256) NOT NULL,
    "SortOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    CONSTRAINT "PK_SignupProfessionCatalog" PRIMARY KEY ("Id")
);


CREATE TABLE "SignupSessionCatalog" (
    "Id" character varying(64) NOT NULL,
    "Name" character varying(160) NOT NULL,
    "ExamTypeId" character varying(32) NOT NULL,
    "ProfessionIdsJson" text NOT NULL,
    "PriceLabel" character varying(32) NOT NULL,
    "StartDate" character varying(32) NOT NULL,
    "EndDate" character varying(32) NOT NULL,
    "DeliveryMode" character varying(32) NOT NULL,
    "Capacity" integer NOT NULL,
    "SeatsRemaining" integer NOT NULL,
    "SortOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    CONSTRAINT "PK_SignupSessionCatalog" PRIMARY KEY ("Id")
);


CREATE TABLE "SpeakingCalibrationSamples" (
    "Id" character varying(64) NOT NULL,
    "CreatedByAdminId" character varying(64) NOT NULL,
    "Title" character varying(160) NOT NULL,
    "Description" character varying(2000) NOT NULL,
    "SourceAttemptId" character varying(64) NOT NULL,
    "ProfessionId" character varying(32) NOT NULL,
    "Difficulty" character varying(16) NOT NULL,
    "GoldScoresJson" text NOT NULL,
    "CalibrationNotes" character varying(2000) NOT NULL,
    "Status" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "PublishedAt" timestamp with time zone,
    CONSTRAINT "PK_SpeakingCalibrationSamples" PRIMARY KEY ("Id")
);


CREATE TABLE "SpeakingCalibrationScores" (
    "Id" character varying(64) NOT NULL,
    "SampleId" character varying(64) NOT NULL,
    "TutorId" character varying(64) NOT NULL,
    "ScoresJson" text NOT NULL,
    "TotalAbsoluteError" double precision NOT NULL,
    "Notes" character varying(2000) NOT NULL,
    "SubmittedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SpeakingCalibrationScores" PRIMARY KEY ("Id")
);


CREATE TABLE "SpeakingCardBatchRequests" (
    "Id" character varying(64) NOT NULL,
    "ProfessionId" character varying(32) NOT NULL,
    "Count" integer NOT NULL,
    "GeneratedCount" integer NOT NULL,
    "TopicListJson" character varying(2000) NOT NULL,
    "DifficultyDistributionJson" character varying(500) NOT NULL,
    "Status" integer NOT NULL,
    "RequestedByAdminId" character varying(64) NOT NULL,
    "RequestedByAdminName" character varying(160),
    "IdempotencyKey" character varying(96),
    "Error" character varying(1024),
    "CreatedAt" timestamp with time zone NOT NULL,
    "StartedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_SpeakingCardBatchRequests" PRIMARY KEY ("Id")
);


CREATE TABLE "SpeakingComplianceConsents" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ConsentType" character varying(32) NOT NULL,
    "ConsentVersion" character varying(32) NOT NULL,
    "AcceptedAt" timestamp with time zone NOT NULL,
    "AcceptedFromIp" character varying(64),
    "UserAgent" character varying(512),
    "RevokedAt" timestamp with time zone,
    CONSTRAINT "PK_SpeakingComplianceConsents" PRIMARY KEY ("Id")
);


CREATE TABLE "SpeakingFeedbackComments" (
    "Id" character varying(64) NOT NULL,
    "AttemptId" character varying(64) NOT NULL,
    "ExpertId" character varying(64) NOT NULL,
    "TranscriptLineIndex" integer NOT NULL,
    "CriterionCode" character varying(48) NOT NULL,
    "Body" character varying(2000) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SpeakingFeedbackComments" PRIMARY KEY ("Id")
);


CREATE TABLE "SpeakingMockSessions" (
    "Id" character varying(64) NOT NULL,
    "MockSetId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Attempt1Id" character varying(64) NOT NULL,
    "Attempt2Id" character varying(64) NOT NULL,
    "Mode" character varying(16) NOT NULL,
    "State" integer NOT NULL,
    "OrchestratorState" character varying(16) NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "BridgeStartedAt" timestamp with time zone,
    "ReadinessBandSnapshot" character varying(32),
    "CombinedScaledSnapshot" integer,
    CONSTRAINT "PK_SpeakingMockSessions" PRIMARY KEY ("Id")
);


CREATE TABLE "SpeakingMockSets" (
    "Id" character varying(64) NOT NULL,
    "ProfessionId" character varying(32) NOT NULL,
    "Title" character varying(160) NOT NULL,
    "Description" character varying(2000) NOT NULL,
    "RolePlay1ContentId" character varying(64) NOT NULL,
    "RolePlay2ContentId" character varying(64) NOT NULL,
    "Status" integer NOT NULL,
    "Difficulty" character varying(16) NOT NULL,
    "CriteriaFocus" character varying(256) NOT NULL,
    "Tags" character varying(256) NOT NULL,
    "SortOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "PublishedAt" timestamp with time zone,
    CONSTRAINT "PK_SpeakingMockSets" PRIMARY KEY ("Id")
);


CREATE TABLE "SpeakingResultVisibilityConfigs" (
    "Id" character varying(64) NOT NULL,
    "RolePlayCardId" character varying(64),
    "ShowSubmissionReceived" boolean NOT NULL,
    "ShowAiEstimate" boolean NOT NULL,
    "ShowReadinessBand" boolean NOT NULL,
    "ShowTutorScore" boolean NOT NULL,
    "ShowFullCriteria" boolean NOT NULL,
    "ShowTranscript" boolean NOT NULL,
    "ShowTutorComments" boolean NOT NULL,
    "ShowRecommendedDrills" boolean NOT NULL,
    "AllowReattempt" boolean NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SpeakingResultVisibilityConfigs" PRIMARY KEY ("Id")
);


CREATE TABLE "SpeakingReviewVoiceNotes" (
    "Id" character varying(64) NOT NULL,
    "ReviewRequestId" character varying(64) NOT NULL,
    "ExpertUserId" character varying(64) NOT NULL,
    "MediaAssetId" character varying(64) NOT NULL,
    "DurationSeconds" integer NOT NULL,
    "TranscriptText" text,
    "WrittenNotes" character varying(4000),
    "RubricJson" character varying(8000) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SpeakingReviewVoiceNotes" PRIMARY KEY ("Id")
);


CREATE TABLE "SponsorAccounts" (
    "Id" character varying(64) NOT NULL,
    "AuthAccountId" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Type" character varying(32) NOT NULL,
    "ContactEmail" character varying(256) NOT NULL,
    "OrganizationName" character varying(256),
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SponsorAccounts" PRIMARY KEY ("Id")
);


CREATE TABLE "SponsorBillingEvents" (
    "Id" uuid NOT NULL,
    "SponsorId" character varying(64) NOT NULL,
    "SeatPackId" uuid,
    "EventType" character varying(32) NOT NULL,
    "Amount" numeric,
    "Currency" character varying(3),
    "SeatsDelta" integer,
    "Description" character varying(512),
    "ActorUserId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SponsorBillingEvents" PRIMARY KEY ("Id")
);


CREATE TABLE "SponsorLearnerLinks" (
    "Id" uuid NOT NULL,
    "SponsorId" character varying(64) NOT NULL,
    "LearnerId" character varying(64) NOT NULL,
    "LearnerConsented" boolean NOT NULL,
    "LinkedAt" timestamp with time zone NOT NULL,
    "ConsentedAt" timestamp with time zone,
    CONSTRAINT "PK_SponsorLearnerLinks" PRIMARY KEY ("Id")
);


CREATE TABLE "SponsorSeatAssignments" (
    "Id" uuid NOT NULL,
    "SeatPackId" uuid NOT NULL,
    "LearnerId" character varying(64) NOT NULL,
    "LearnerEmail" character varying(256) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "AssignedAt" timestamp with time zone NOT NULL,
    "RevokedAt" timestamp with time zone,
    CONSTRAINT "PK_SponsorSeatAssignments" PRIMARY KEY ("Id")
);


CREATE TABLE "SponsorSeatPacks" (
    "Id" uuid NOT NULL,
    "SponsorId" character varying(64) NOT NULL,
    "Name" character varying(256) NOT NULL,
    "TotalSeats" integer NOT NULL,
    "AssignedSeats" integer NOT NULL,
    "UnitPrice" numeric NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "StripePaymentId" character varying(256),
    "Status" character varying(16) NOT NULL,
    "PurchasedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SponsorSeatPacks" PRIMARY KEY ("Id")
);


CREATE TABLE "Sponsorships" (
    "Id" uuid NOT NULL,
    "SponsorUserId" character varying(64) NOT NULL,
    "LearnerUserId" character varying(64),
    "LearnerEmail" character varying(256) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "RevokedAt" timestamp with time zone,
    CONSTRAINT "PK_Sponsorships" PRIMARY KEY ("Id")
);


CREATE TABLE "StrategyGuides" (
    "Id" character varying(64) NOT NULL,
    "Slug" character varying(160),
    "ExamTypeCode" character varying(16) NOT NULL,
    "SubtestCode" character varying(32),
    "Title" character varying(200) NOT NULL,
    "Summary" character varying(512) NOT NULL,
    "ContentHtml" text NOT NULL,
    "ContentJson" text,
    "Category" character varying(32) NOT NULL,
    "ReadingTimeMinutes" integer NOT NULL,
    "SortOrder" integer NOT NULL,
    "Status" character varying(16) NOT NULL,
    "IsPreviewEligible" boolean NOT NULL,
    "ContentLessonId" character varying(64),
    "SourceProvenance" character varying(512),
    "RightsStatus" character varying(32),
    "FreshnessConfidence" character varying(32),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "PublishedAt" timestamp with time zone NOT NULL,
    "ArchivedAt" timestamp with time zone,
    CONSTRAINT "PK_StrategyGuides" PRIMARY KEY ("Id")
);


CREATE TABLE "StreakRecords" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Date" date NOT NULL,
    "HasActivity" boolean NOT NULL,
    "QuestionsAnsweredToday" integer NOT NULL,
    "CurrentStreak" integer NOT NULL,
    "LongestStreak" integer NOT NULL,
    CONSTRAINT "PK_StreakRecords" PRIMARY KEY ("Id")
);


CREATE TABLE "StudyCommitments" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "DailyMinutes" integer NOT NULL,
    "FreezeProtections" integer NOT NULL,
    "FreezeProtectionsUsed" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "DeactivatedAt" timestamp with time zone,
    CONSTRAINT "PK_StudyCommitments" PRIMARY KEY ("Id")
);


CREATE TABLE "StudyGroupMembers" (
    "Id" uuid NOT NULL,
    "GroupId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Role" character varying(32) NOT NULL,
    "JoinedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_StudyGroupMembers" PRIMARY KEY ("Id")
);


CREATE TABLE "StudyGroups" (
    "Id" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Description" character varying(512) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "CreatorUserId" character varying(64) NOT NULL,
    "MaxMembers" integer NOT NULL,
    "MemberCount" integer NOT NULL,
    "IsPublic" boolean NOT NULL,
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_StudyGroups" PRIMARY KEY ("Id")
);


CREATE TABLE "StudyPlanItems" (
    "Id" character varying(64) NOT NULL,
    "StudyPlanId" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "DurationMinutes" integer NOT NULL,
    "Rationale" character varying(1024) NOT NULL,
    "DueDate" date NOT NULL,
    "Status" integer NOT NULL,
    "Section" character varying(32) NOT NULL,
    "ContentId" character varying(64),
    "ItemType" character varying(32) NOT NULL,
    "SourceContentId" character varying(64),
    "ContentRoute" character varying(512),
    "LinkedReviewItemId" character varying(64),
    "PriorityScore" integer NOT NULL,
    "WeekIndex" integer NOT NULL,
    "ReplacedById" character varying(64),
    "FeedbackRating" integer,
    "CompletedAt" timestamp with time zone,
    "ActualMinutesSpent" integer,
    "SlotKind" character varying(32),
    "TagsJson" text,
    CONSTRAINT "PK_StudyPlanItems" PRIMARY KEY ("Id")
);


CREATE TABLE "StudyPlans" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Version" integer NOT NULL,
    "GeneratedAt" timestamp with time zone NOT NULL,
    "State" integer NOT NULL,
    "Checkpoint" text NOT NULL,
    "WeakSkillFocus" text NOT NULL,
    "RetakeRescueMode" text,
    "ExamFamilyCode" character varying(16) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "DiagnosticAttemptId" character varying(64),
    "WeekNumber" integer NOT NULL,
    "TotalWeeks" integer NOT NULL,
    "PlanWindowStart" date,
    "PlanWindowEnd" date,
    "TemplateId" character varying(64),
    "MinutesPerDayBudget" integer NOT NULL,
    "GenerationInputsHash" character varying(128),
    "SubtestWeightsJson" jsonb NOT NULL,
    "IsPremiumPersonalized" boolean NOT NULL,
    "EntitlementTierAtGeneration" character varying(32) NOT NULL,
    "IsActive" boolean NOT NULL,
    CONSTRAINT "PK_StudyPlans" PRIMARY KEY ("Id")
);


CREATE TABLE "StudyPlanTemplates" (
    "Id" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Slug" character varying(64) NOT NULL,
    "Description" character varying(1024),
    "ExamTypeCode" character varying(16) NOT NULL,
    "ExamFamilyCode" character varying(16) NOT NULL,
    "MinWeeks" integer NOT NULL,
    "MaxWeeks" integer NOT NULL,
    "TargetBand" character varying(8),
    "ProfessionId" character varying(32),
    "FocusTagsJson" jsonb NOT NULL,
    "DefaultMinutesPerDay" integer NOT NULL,
    "TemplateBodyJson" jsonb NOT NULL,
    "IsActive" boolean NOT NULL,
    "Version" integer NOT NULL,
    "CreatedBy" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_StudyPlanTemplates" PRIMARY KEY ("Id")
);


CREATE TABLE "StudyPlanTemplateTiers" (
    "Id" character varying(64) NOT NULL,
    "TemplateId" character varying(64) NOT NULL,
    "TierCode" character varying(32) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_StudyPlanTemplateTiers" PRIMARY KEY ("Id")
);


CREATE TABLE "SubscriptionItems" (
    "Id" character varying(64) NOT NULL,
    "SubscriptionId" character varying(64) NOT NULL,
    "ItemCode" character varying(64) NOT NULL,
    "ItemType" character varying(64) NOT NULL,
    "AddOnVersionId" character varying(64),
    "Quantity" integer NOT NULL,
    "Status" integer NOT NULL,
    "StartsAt" timestamp with time zone NOT NULL,
    "EndsAt" timestamp with time zone,
    "QuoteId" character varying(64),
    "CheckoutSessionId" character varying(256),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SubscriptionItems" PRIMARY KEY ("Id")
);


CREATE TABLE "Subscriptions" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "PlanId" character varying(64) NOT NULL,
    "PlanVersionId" character varying(64),
    "Status" integer NOT NULL,
    "NextRenewalAt" timestamp with time zone NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "ChangedAt" timestamp with time zone NOT NULL,
    "PriceAmount" numeric NOT NULL,
    "Currency" text NOT NULL,
    "Interval" text NOT NULL,
    "PausedUntil" timestamp with time zone,
    "GracePeriodUntil" timestamp with time zone,
    "ExpiresAt" timestamp with time zone,
    "WritingAssessmentsRemaining" integer NOT NULL,
    "SpeakingSessionsRemaining" integer NOT NULL,
    "AiCreditsRemaining" integer NOT NULL,
    "TutorBookUnlocked" boolean NOT NULL,
    "BasicEnglishUnlocked" boolean NOT NULL,
    CONSTRAINT "PK_Subscriptions" PRIMARY KEY ("Id")
);


CREATE TABLE "Subtests" (
    "Id" character varying(32) NOT NULL,
    "Code" character varying(32) NOT NULL,
    "Label" character varying(64) NOT NULL,
    "SupportsProfessionSpecificContent" boolean NOT NULL,
    CONSTRAINT "PK_Subtests" PRIMARY KEY ("Id")
);


CREATE TABLE "TaskTypes" (
    "Id" character varying(64) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Label" character varying(128) NOT NULL,
    "Description" character varying(512) NOT NULL,
    "ConfigJson" text NOT NULL,
    "CriteriaIdsJson" text NOT NULL,
    "Status" character varying(16) NOT NULL,
    "SortOrder" integer NOT NULL,
    CONSTRAINT "PK_TaskTypes" PRIMARY KEY ("Id")
);


CREATE TABLE "TaxRules" (
    "Id" character varying(64) NOT NULL,
    "Country" character varying(2) NOT NULL,
    "Region" character varying(16) NOT NULL,
    "TaxType" character varying(32) NOT NULL,
    "DisplayName" character varying(64) NOT NULL,
    "RatePercent" numeric(6,3) NOT NULL,
    "EffectiveFrom" timestamp with time zone NOT NULL,
    "EffectiveTo" timestamp with time zone,
    "ZeroRateForB2BReverseCharge" boolean NOT NULL,
    "IsTaxInclusiveDisplay" boolean NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_TaxRules" PRIMARY KEY ("Id")
);


CREATE TABLE "TeacherClasses" (
    "Id" character varying(64) NOT NULL,
    "OwnerUserId" character varying(64) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Description" character varying(1024),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_TeacherClasses" PRIMARY KEY ("Id")
);


CREATE TABLE "TestimonialAssets" (
    "Id" character varying(64) NOT NULL,
    "LearnerDisplayName" character varying(128),
    "Profession" character varying(32),
    "TestDate" date,
    "OverallGrade" character varying(8),
    "SubtestGradesJson" text,
    "TestimonialText" text,
    "MediaAssetId" character varying(64),
    "ConsentStatus" character varying(16) NOT NULL,
    "DisplayApproved" boolean NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_TestimonialAssets" PRIMARY KEY ("Id")
);


CREATE TABLE "TutorBookAudioScripts" (
    "Id" character varying(64) NOT NULL,
    "Chapter" character varying(32) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "AudioUrl" character varying(1024) NOT NULL,
    "TranscriptUrl" character varying(1024),
    "DisplayOrder" integer NOT NULL,
    "IsPublished" boolean NOT NULL,
    "CreatedByAdminId" character varying(64),
    "CreatedByAdminName" character varying(128),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_TutorBookAudioScripts" PRIMARY KEY ("Id")
);


CREATE TABLE "TutorBookUpdates" (
    "Id" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "BodyMarkdown" text NOT NULL,
    "Audience" character varying(16) NOT NULL,
    "PublishedAt" timestamp with time zone NOT NULL,
    "IsPublished" boolean NOT NULL,
    "CreatedByAdminId" character varying(64),
    "CreatedByAdminName" character varying(128),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_TutorBookUpdates" PRIMARY KEY ("Id")
);


CREATE TABLE "TutoringAvailabilities" (
    "Id" uuid NOT NULL,
    "ExpertUserId" character varying(64) NOT NULL,
    "DayOfWeek" integer NOT NULL,
    "StartTime" character varying(8) NOT NULL,
    "EndTime" character varying(8) NOT NULL,
    "Timezone" character varying(64) NOT NULL,
    "IsActive" boolean NOT NULL,
    CONSTRAINT "PK_TutoringAvailabilities" PRIMARY KEY ("Id")
);


CREATE TABLE "TutoringSessions" (
    "Id" character varying(64) NOT NULL,
    "LearnerUserId" character varying(64) NOT NULL,
    "ExpertUserId" character varying(64) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "SubtestFocus" character varying(32),
    "ScheduledAt" timestamp with time zone NOT NULL,
    "DurationMinutes" integer NOT NULL,
    "State" character varying(32) NOT NULL,
    "RoomUrl" character varying(512),
    "LearnerNotes" character varying(2048),
    "ExpertNotes" character varying(2048),
    "Price" numeric NOT NULL,
    "PaymentSource" character varying(32),
    "LearnerRating" integer,
    "LearnerFeedback" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "StartedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_TutoringSessions" PRIMARY KEY ("Id")
);


CREATE TABLE "UploadSessions" (
    "Id" character varying(64) NOT NULL,
    "AttemptId" character varying(64) NOT NULL,
    "UploadUrl" character varying(256) NOT NULL,
    "StorageKey" character varying(256) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "State" integer NOT NULL,
    CONSTRAINT "PK_UploadSessions" PRIMARY KEY ("Id")
);


CREATE TABLE "UsageForecastSnapshots" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "SnapshotDate" date NOT NULL,
    "FeatureCode" character varying(64) NOT NULL,
    "WindowDays" integer NOT NULL,
    "ForecastCalls" integer NOT NULL,
    "ForecastCredits" integer NOT NULL,
    "ForecastCostUsd" numeric(12,4) NOT NULL,
    "Ema30DailyCalls" numeric(12,3) NOT NULL,
    "PerFeatureJson" character varying(4096),
    "SuggestedTopUpCredits" integer NOT NULL,
    "ComputedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_UsageForecastSnapshots" PRIMARY KEY ("Id")
);


CREATE TABLE "UserAiPreferences" (
    "UserId" character varying(64) NOT NULL,
    "Mode" integer NOT NULL,
    "AllowPlatformFallback" boolean NOT NULL,
    "PerFeatureOverridesJson" character varying(2048) NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_UserAiPreferences" PRIMARY KEY ("UserId")
);


CREATE TABLE "UserNotes" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Title" character varying(120) NOT NULL,
    "BodyMarkdown" character varying(2000) NOT NULL,
    "Source" character varying(16) NOT NULL,
    "CreatedByFeatureCode" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_UserNotes" PRIMARY KEY ("Id")
);


CREATE TABLE "VideoLessons" (
    "Id" character varying(64) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    "SubtestCode" character varying(32),
    "Title" character varying(128) NOT NULL,
    "Description" character varying(1024) NOT NULL,
    "VideoUrl" character varying(512) NOT NULL,
    "ThumbnailUrl" character varying(512),
    "DurationSeconds" integer NOT NULL,
    "Category" character varying(32) NOT NULL,
    "InstructorName" character varying(32),
    "ChaptersJson" text NOT NULL,
    "ResourcesJson" text NOT NULL,
    "SortOrder" integer NOT NULL,
    "Status" character varying(16) NOT NULL,
    "PublishedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_VideoLessons" PRIMARY KEY ("Id")
);


CREATE TABLE "VocabularyLists" (
    "Id" uuid NOT NULL,
    "Slug" character varying(128) NOT NULL,
    "Name" character varying(256) NOT NULL,
    "NameAr" character varying(256) NOT NULL,
    "Description" text NOT NULL,
    "WordIdsJson" text NOT NULL,
    "IsPublished" boolean NOT NULL,
    CONSTRAINT "PK_VocabularyLists" PRIMARY KEY ("Id")
);


CREATE TABLE "VocabularyQuizResults" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "TermsQuizzed" integer NOT NULL,
    "CorrectCount" integer NOT NULL,
    "DurationSeconds" integer NOT NULL,
    "Format" character varying(32) NOT NULL,
    "ResultsJson" text NOT NULL,
    "CompletedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_VocabularyQuizResults" PRIMARY KEY ("Id")
);


CREATE TABLE "VocabularyTerms" (
    "Id" character varying(64) NOT NULL,
    "Term" character varying(128) NOT NULL,
    "Definition" character varying(1024),
    "ExampleSentence" character varying(2048),
    "ContextNotes" character varying(1024),
    "ExamTypeCode" character varying(16) NOT NULL,
    "ProfessionId" character varying(32),
    "Category" character varying(64) NOT NULL,
    "IpaPronunciation" character varying(64),
    "AudioUrl" character varying(256),
    "AudioSlowUrl" character varying(256),
    "AudioSentenceUrl" character varying(256),
    "AmericanSpelling" character varying(128),
    "AudioMediaAssetId" character varying(64),
    "AudioProvider" character varying(32),
    "AudioVoice" character varying(64),
    "AudioModelVariant" character varying(32),
    "AudioGeneratedAt" timestamp with time zone,
    "AudioBatchId" character varying(64),
    "ImageUrl" character varying(256),
    "SynonymsJson" text NOT NULL,
    "CollocationsJson" text NOT NULL,
    "RelatedTermsJson" text NOT NULL,
    "CommonMistakesJson" text NOT NULL,
    "SimilarSoundingJson" text NOT NULL,
    "RecallSetCodesJson" text NOT NULL,
    "ExamFrequencyCount" integer NOT NULL,
    "SourceProvenance" character varying(512),
    "IsFreePreview" boolean NOT NULL,
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_VocabularyTerms" PRIMARY KEY ("Id")
);


CREATE TABLE "VocabularyWords" (
    "Id" uuid NOT NULL,
    "Word" character varying(128) NOT NULL,
    "PartOfSpeech" character varying(32) NOT NULL,
    "DefinitionEn" text NOT NULL,
    "DefinitionAr" text NOT NULL,
    "PronunciationIpa" character varying(128) NOT NULL,
    "AudioUrl" text,
    "ExampleEn" text NOT NULL,
    "ExampleAr" text NOT NULL,
    "HealthcareContext" text NOT NULL,
    "ProfessionRelevanceJson" text NOT NULL,
    "Difficulty" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_VocabularyWords" PRIMARY KEY ("Id")
);


CREATE TABLE "Wallets" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "CreditBalance" integer NOT NULL,
    "LedgerSummaryJson" text NOT NULL,
    "LastUpdatedAt" timestamp with time zone NOT NULL,
    "RowVersion" bytea,
    CONSTRAINT "PK_Wallets" PRIMARY KEY ("Id")
);


CREATE TABLE "WalletTopUpTierConfigs" (
    "Id" uuid NOT NULL,
    "Slug" character varying(64),
    "Amount" integer NOT NULL,
    "Credits" integer NOT NULL,
    "Bonus" integer NOT NULL,
    "Label" character varying(80),
    "IsPopular" boolean NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" character varying(64),
    "UpdatedBy" character varying(64),
    CONSTRAINT "PK_WalletTopUpTierConfigs" PRIMARY KEY ("Id")
);


CREATE TABLE "WalletTransactions" (
    "Id" uuid NOT NULL,
    "WalletId" character varying(64) NOT NULL,
    "TransactionType" character varying(32) NOT NULL,
    "Amount" integer NOT NULL,
    "BalanceAfter" integer NOT NULL,
    "ReferenceType" character varying(32),
    "ReferenceId" character varying(128),
    "IdempotencyKey" character varying(128),
    "Description" character varying(256),
    "CreatedBy" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WalletTransactions" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingAttemptEvents" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "SubmissionId" uuid,
    "SessionId" character varying(64),
    "ScenarioId" uuid,
    "Mode" character varying(16) NOT NULL,
    "EventType" character varying(32) NOT NULL,
    "Timestamp" timestamp with time zone NOT NULL,
    "PayloadJson" jsonb NOT NULL DEFAULT '{}',
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingAttemptEvents" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingBuddyCheckIns" (
    "Id" uuid NOT NULL,
    "PairId" uuid NOT NULL,
    "WeekStartDate" date NOT NULL,
    "UserAReportJson" jsonb,
    "UserBReportJson" jsonb,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_WritingBuddyCheckIns" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingBuddyMessages" (
    "Id" uuid NOT NULL,
    "PairId" uuid NOT NULL,
    "FromUserId" character varying(64) NOT NULL,
    "BodyMarkdown" character varying(500) NOT NULL,
    "SentAt" timestamp with time zone NOT NULL,
    "ReadAt" timestamp with time zone,
    CONSTRAINT "PK_WritingBuddyMessages" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingBuddyPairs" (
    "Id" uuid NOT NULL,
    "UserAId" character varying(64) NOT NULL,
    "UserBId" character varying(64) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "EndedAt" timestamp with time zone,
    "EndedReason" character varying(64),
    "Profession" character varying(64) NOT NULL,
    "MatchedAtBand" character varying(8) NOT NULL,
    "Status" character varying(16) NOT NULL,
    CONSTRAINT "PK_WritingBuddyPairs" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingCalibrationLetters" (
    "Id" uuid NOT NULL,
    "ScenarioId" uuid NOT NULL,
    "LetterContent" text NOT NULL,
    "AuthorTier" character varying(16) NOT NULL,
    "DrAhmedGradeJson" jsonb NOT NULL DEFAULT '{}',
    "AddedAt" timestamp with time zone NOT NULL,
    "AddedById" character varying(64) NOT NULL,
    CONSTRAINT "PK_WritingCalibrationLetters" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingCalibrationResults" (
    "Id" uuid NOT NULL,
    "RunId" uuid NOT NULL,
    "CalibrationLetterId" uuid NOT NULL,
    "AiGradeJson" jsonb NOT NULL DEFAULT '{}',
    "AbsErrorRaw" integer NOT NULL,
    "BandMatch" boolean NOT NULL,
    CONSTRAINT "PK_WritingCalibrationResults" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingCalibrationRuns" (
    "Id" uuid NOT NULL,
    "RunDate" timestamp with time zone NOT NULL,
    "ModelVersion" character varying(64) NOT NULL,
    "TotalLetters" integer NOT NULL,
    "Within2PointsCount" integer NOT NULL,
    "MeanAbsError" double precision NOT NULL,
    "BandAgreementCount" integer NOT NULL,
    "NotesMarkdown" text NOT NULL,
    CONSTRAINT "PK_WritingCalibrationRuns" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingCanonRules" (
    "Id" character varying(16) NOT NULL,
    "Category" character varying(32) NOT NULL,
    "AppliesToLetterTypesJson" jsonb NOT NULL DEFAULT '[]',
    "AppliesToProfessionsJson" jsonb NOT NULL DEFAULT '[]',
    "Severity" character varying(8) NOT NULL,
    "RuleText" character varying(1000) NOT NULL,
    "CorrectExamplesJson" jsonb NOT NULL DEFAULT '[]',
    "IncorrectExamplesJson" jsonb NOT NULL DEFAULT '[]',
    "DetectionType" character varying(16) NOT NULL,
    "DetectionConfigJson" jsonb NOT NULL DEFAULT '{}',
    "LessonId" uuid,
    "Version" integer NOT NULL,
    "Active" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingCanonRules" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingCanonViolations" (
    "Id" uuid NOT NULL,
    "SubmissionId" uuid NOT NULL,
    "RuleId" character varying(16) NOT NULL,
    "Severity" character varying(8) NOT NULL,
    "Snippet" character varying(500),
    "LineNumber" integer,
    "CharStart" integer,
    "CharEnd" integer,
    "SuggestedFix" character varying(500),
    "Disputed" boolean NOT NULL,
    "DisputeResolution" character varying(500),
    "DetectedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingCanonViolations" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingCaseNoteDrillAttempts" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "DrillId" uuid NOT NULL,
    "ResponsesJson" jsonb NOT NULL DEFAULT '[]',
    "CorrectCount" integer NOT NULL,
    "TotalCount" integer NOT NULL,
    "ScorePercent" double precision NOT NULL,
    "TimeSpentSeconds" integer,
    "AttemptedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingCaseNoteDrillAttempts" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingCaseNoteDrills" (
    "Id" uuid NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Profession" character varying(64) NOT NULL,
    "LetterType" character varying(8) NOT NULL,
    "Format" character varying(32) NOT NULL,
    "CaseNotesMarkdown" text NOT NULL,
    "Difficulty" integer NOT NULL,
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingCaseNoteDrills" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingCaseNoteDrillSentences" (
    "Id" uuid NOT NULL,
    "DrillId" uuid NOT NULL,
    "Ordinal" integer NOT NULL,
    "SentenceText" text NOT NULL,
    "RelevanceLabel" character varying(16) NOT NULL,
    "Rationale" character varying(500),
    CONSTRAINT "PK_WritingCaseNoteDrillSentences" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingCoachSessions" (
    "Id" character varying(64) NOT NULL,
    "AttemptId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "SuggestionsGenerated" integer NOT NULL,
    "SuggestionsAccepted" integer NOT NULL,
    "SuggestionsDismissed" integer NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingCoachSessions" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingCoachSuggestions" (
    "Id" uuid NOT NULL,
    "SessionId" character varying(64) NOT NULL,
    "AttemptId" character varying(64) NOT NULL,
    "SuggestionType" character varying(32) NOT NULL,
    "OriginalText" text NOT NULL,
    "SuggestedText" text NOT NULL,
    "Explanation" character varying(512) NOT NULL,
    "StartOffset" integer NOT NULL,
    "EndOffset" integer NOT NULL,
    "Resolution" character varying(16),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingCoachSuggestions" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingCommonMistakes" (
    "Id" uuid NOT NULL,
    "Category" character varying(64) NOT NULL,
    "Summary" character varying(500) NOT NULL,
    "ExampleWrong" character varying(1000) NOT NULL,
    "ExampleRight" character varying(1000) NOT NULL,
    "CanonRuleId" character varying(16),
    "RelatedSubSkill" character varying(4),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingCommonMistakes" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingContentChecklistItems" (
    "Id" uuid NOT NULL,
    "ScenarioId" uuid NOT NULL,
    "ItemText" character varying(500) NOT NULL,
    "Category" character varying(64) NOT NULL,
    "Importance" character varying(16) NOT NULL,
    "RequiredStatus" character varying(16) NOT NULL,
    "LinkedCaseNoteSection" character varying(200),
    "ExpectedRepresentation" character varying(1000),
    "CommonError" character varying(1000),
    "Ordinal" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingContentChecklistItems" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingDailyPlanItems" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "PlanDate" date NOT NULL,
    "Ordinal" integer NOT NULL,
    "ItemType" character varying(32) NOT NULL,
    "FocusSkill" character varying(4),
    "FocusCriterion" character varying(32),
    "EstimatedMinutes" integer NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Description" character varying(512) NOT NULL,
    "ActionHref" character varying(512) NOT NULL,
    "ContentId" character varying(64),
    "PayloadJson" text NOT NULL,
    "Status" character varying(16) NOT NULL,
    "StartedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    "SkippedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingDailyPlanItems" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingDiagnosticSessions" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ScenarioId" uuid NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "ReadingPhaseEndedAt" timestamp with time zone,
    "SubmissionId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingDiagnosticSessions" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingDraftsV2" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ScenarioId" uuid NOT NULL,
    "Mode" character varying(16) NOT NULL,
    "Content" text NOT NULL,
    "WordCount" integer NOT NULL,
    "TimeSpentSeconds" integer NOT NULL,
    "LastSavedAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingDraftsV2" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingDrillAttempts" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "DrillId" uuid NOT NULL,
    "ResponseText" text,
    "IsCorrect" boolean NOT NULL,
    "FeedbackText" text,
    "TimeSpentSeconds" integer,
    "EaseFactor" double precision NOT NULL,
    "IntervalDays" integer NOT NULL,
    "Repetitions" integer NOT NULL,
    "NextDueAt" timestamp with time zone,
    "AttemptedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingDrillAttempts" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingDrills" (
    "Id" uuid NOT NULL,
    "DrillType" character varying(32) NOT NULL,
    "TargetSubSkill" character varying(4) NOT NULL,
    "TargetCanonRuleId" character varying(16),
    "AppliesToProfessionsJson" jsonb NOT NULL DEFAULT '[]',
    "AppliesToLetterTypesJson" jsonb NOT NULL DEFAULT '[]',
    "Difficulty" integer NOT NULL,
    "PromptMarkdown" text NOT NULL,
    "ExpectedAnswer" text,
    "AlternativesJson" jsonb NOT NULL DEFAULT '[]',
    "GradingMethod" character varying(16) NOT NULL,
    "GradingConfigJson" jsonb NOT NULL DEFAULT '{}',
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingDrills" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingExemplarAnnotations" (
    "Id" uuid NOT NULL,
    "ExemplarId" uuid NOT NULL,
    "Ordinal" integer NOT NULL,
    "CharStart" integer,
    "CharEnd" integer,
    "AnnotationType" character varying(64) NOT NULL,
    "RuleId" character varying(16),
    "Note" character varying(1000) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingExemplarAnnotations" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingExemplarEmbeddings" (
    "Id" uuid NOT NULL,
    "ExemplarId" uuid NOT NULL,
    "ModelId" character varying(64) NOT NULL,
    "Dimensions" integer NOT NULL,
    "EmbeddingJson" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingExemplarEmbeddings" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingExemplars" (
    "Id" uuid NOT NULL,
    "ScenarioId" uuid,
    "LetterType" character varying(8) NOT NULL,
    "Profession" character varying(64) NOT NULL,
    "LetterContent" text NOT NULL,
    "AnnotationsJson" jsonb NOT NULL DEFAULT '[]',
    "TargetBand" character varying(8) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "AuthorId" character varying(64) NOT NULL,
    "PublishedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingExemplars" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingFeedbackAnnotations" (
    "Id" uuid NOT NULL,
    "SubmissionId" uuid NOT NULL,
    "ReviewId" uuid,
    "TutorId" character varying(64) NOT NULL,
    "Criterion" character varying(8),
    "HighlightedText" character varying(2000) NOT NULL,
    "StartOffset" integer NOT NULL,
    "EndOffset" integer NOT NULL,
    "Severity" character varying(16) NOT NULL,
    "Suggestion" character varying(2000),
    "FeedbackText" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingFeedbackAnnotations" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingGrades" (
    "Id" uuid NOT NULL,
    "SubmissionId" uuid NOT NULL,
    "C1Purpose" smallint NOT NULL,
    "C2Content" smallint NOT NULL,
    "C3Conciseness" smallint NOT NULL,
    "C4Genre" smallint NOT NULL,
    "C5Organisation" smallint NOT NULL,
    "C6Language" smallint NOT NULL,
    "RawTotal" smallint NOT NULL,
    "EstimatedBand" integer NOT NULL,
    "BandLabel" character varying(8) NOT NULL,
    "PerCriterionFeedbackJson" jsonb NOT NULL DEFAULT '{}',
    "TopThreePrioritiesJson" jsonb NOT NULL DEFAULT '[]',
    "ConfidenceFlag" character varying(16),
    "ModelUsed" character varying(64) NOT NULL,
    "CanonVersion" character varying(32) NOT NULL,
    "AppealedByGradeId" uuid,
    "TutorReviewId" uuid,
    "GradedAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingGrades" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingLearnerMistakeStats" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "MistakeId" uuid NOT NULL,
    "OccurrenceCount" integer NOT NULL,
    "LastOccurredAt" timestamp with time zone NOT NULL,
    "FirstOccurredAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingLearnerMistakeStats" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingLessonCompletionsV2" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "LessonId" uuid NOT NULL,
    "CompletedAt" timestamp with time zone NOT NULL,
    "QuizScore" integer,
    "QuizAttempts" integer NOT NULL,
    CONSTRAINT "PK_WritingLessonCompletionsV2" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingLessons" (
    "Id" uuid NOT NULL,
    "Slug" character varying(128) NOT NULL,
    "Title" character varying(256) NOT NULL,
    "SkillCode" character varying(4) NOT NULL,
    "OrderIndex" integer NOT NULL,
    "EstimatedMinutes" integer NOT NULL,
    "BodyMarkdownEn" text NOT NULL,
    "DrillPrompt" text NOT NULL,
    "QuizJson" text NOT NULL,
    "PrerequisiteLessonId" uuid,
    "IsPublished" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingLessons" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingLessonsV2" (
    "Id" uuid NOT NULL,
    "SubSkill" character varying(4) NOT NULL,
    "OrderInCourse" integer NOT NULL,
    "Title" character varying(200) NOT NULL,
    "BodyMarkdown" text NOT NULL,
    "VideoUrl" character varying(512),
    "EstimatedMinutes" integer NOT NULL,
    "QuizQuestionsJson" jsonb NOT NULL DEFAULT '[]',
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingLessonsV2" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingMocks" (
    "Id" uuid NOT NULL,
    "ScenarioId" uuid NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Difficulty" integer NOT NULL,
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingMocks" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingMockSessions" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "MockId" uuid NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "ReadingPhaseEndedAt" timestamp with time zone,
    "SubmittedAt" timestamp with time zone,
    "SubmissionId" uuid,
    "Status" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingMockSessions" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingModerations" (
    "Id" uuid NOT NULL,
    "SubmissionId" uuid NOT NULL,
    "FirstMarkerId" character varying(64),
    "SecondMarkerId" character varying(64),
    "SeniorMarkerId" character varying(64),
    "FirstScoreJson" jsonb,
    "SecondScoreJson" jsonb,
    "FinalScoreJson" jsonb,
    "VariancePoints" integer,
    "VarianceReason" character varying(500),
    "FinalDecisionNote" character varying(1000),
    "Status" character varying(24) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingModerations" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingOcrJobs" (
    "Id" uuid NOT NULL,
    "SubmissionId" uuid,
    "UserId" character varying(64) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "Provider" character varying(16) NOT NULL,
    "ConfidenceScore" double precision,
    "ExtractedText" text,
    "ImageUrlsJson" jsonb NOT NULL DEFAULT '[]',
    "ErrorMessage" character varying(2000),
    "CreatedAt" timestamp with time zone NOT NULL,
    "StartedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_WritingOcrJobs" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingOptions" (
    "Id" character varying(32) NOT NULL,
    "AiGradingEnabled" boolean NOT NULL,
    "AiCoachEnabled" boolean NOT NULL,
    "KillSwitchReason" character varying(256),
    "FreeTierLimit" integer NOT NULL,
    "FreeTierWindowDays" integer NOT NULL,
    "FreeTierEnabled" boolean NOT NULL,
    "PreferredGradingProvider" character varying(64),
    "PreferredCoachProvider" character varying(64),
    "PreferredDraftProvider" character varying(64),
    "UpdatedAt" timestamp with time zone NOT NULL,
    "UpdatedByAdminId" character varying(64),
    CONSTRAINT "PK_WritingOptions" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingPathwayItems" (
    "Id" uuid NOT NULL,
    "PathwayId" uuid NOT NULL,
    "OrderIndex" integer NOT NULL,
    "Stage" character varying(32) NOT NULL,
    "Phase" character varying(32) NOT NULL,
    "FocusSkill" character varying(4),
    "FocusCriterion" character varying(32),
    "ItemKind" character varying(32) NOT NULL,
    "ContentRefId" character varying(64),
    "WeekNumber" integer,
    "Status" character varying(16) NOT NULL,
    "CompletedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingPathwayItems" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingReadinessScores" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "Date" date NOT NULL,
    "Score" integer NOT NULL,
    "MockAverageBand" numeric,
    "TrajectorySlope" numeric,
    "CanonCleanRate" numeric,
    "TimeMgmtScore" integer,
    "TypeConsistency" integer,
    "PredictedBandLabel" character varying(8),
    "ComputedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingReadinessScores" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingResultVisibilityConfigs" (
    "Id" character varying(64) NOT NULL,
    "ScenarioId" uuid,
    "ShowSubmissionReceived" boolean NOT NULL,
    "ShowAiEstimate" boolean NOT NULL,
    "ShowTutorScore" boolean NOT NULL,
    "ShowFullCriteria" boolean NOT NULL,
    "ShowAnnotatedResponse" boolean NOT NULL,
    "ShowMissingContent" boolean NOT NULL,
    "ShowModelAnswer" boolean NOT NULL,
    "ShowContentChecklist" boolean NOT NULL,
    "AllowRewrite" boolean NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingResultVisibilityConfigs" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingRuleViolations" (
    "Id" character varying(64) NOT NULL,
    "AttemptId" character varying(64) NOT NULL,
    "EvaluationId" character varying(64),
    "UserId" character varying(64) NOT NULL,
    "Profession" character varying(64) NOT NULL,
    "LetterType" character varying(64) NOT NULL,
    "RuleId" character varying(128) NOT NULL,
    "Severity" character varying(16) NOT NULL,
    "Source" character varying(16) NOT NULL,
    "Message" character varying(1024) NOT NULL,
    "Quote" character varying(1024),
    "GeneratedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingRuleViolations" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingScenarioEmbeddings" (
    "Id" uuid NOT NULL,
    "ScenarioId" uuid NOT NULL,
    "ModelId" character varying(64) NOT NULL,
    "Dimensions" integer NOT NULL,
    "EmbeddingJson" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingScenarioEmbeddings" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingScenarios" (
    "Id" uuid NOT NULL,
    "Title" character varying(200) NOT NULL,
    "LetterType" character varying(8) NOT NULL,
    "Profession" character varying(64) NOT NULL,
    "SubDiscipline" character varying(64),
    "TopicsJson" jsonb NOT NULL DEFAULT '[]',
    "Difficulty" integer NOT NULL,
    "CaseNotesMarkdown" text NOT NULL,
    "CaseNotesStructuredJson" jsonb,
    "EstimatedReadingMinutes" integer NOT NULL,
    "IsDiagnostic" boolean NOT NULL,
    "Status" character varying(16) NOT NULL,
    "Version" integer NOT NULL,
    "PreviousVersionId" uuid,
    "AuthorId" character varying(64) NOT NULL,
    "ApprovedById" character varying(64),
    "PublishedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "InternalCode" character varying(32),
    "TaskPromptMarkdown" text,
    "WriterRole" character varying(256),
    "TodayDate" character varying(64),
    "RecipientJson" jsonb,
    "ExpectedPurpose" text,
    "ExpectedAction" text,
    "CaseNoteSectionsJson" jsonb,
    "FixedInstructionsJson" jsonb NOT NULL DEFAULT '[]',
    "WordGuideMin" integer NOT NULL,
    "WordGuideMax" integer NOT NULL,
    "ReadingTimeSeconds" integer NOT NULL,
    "WritingTimeSeconds" integer NOT NULL,
    "SimulationModes" character varying(16) NOT NULL,
    "MarkingMode" character varying(16) NOT NULL,
    "RetakePolicyJson" jsonb,
    "ModelAnswerExemplarId" uuid,
    "SourceProvenance" character varying(512),
    "IntegrityAcknowledgedById" character varying(64),
    "IntegrityAcknowledgedAt" timestamp with time zone,
    "ContentOwnerId" character varying(64),
    "SourceContentPaperId" character varying(64),
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingScenarios" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingScenarioStructuredSentences" (
    "Id" uuid NOT NULL,
    "ScenarioId" uuid NOT NULL,
    "Ordinal" integer NOT NULL,
    "SentenceText" text NOT NULL,
    "RelevanceLabel" character varying(16) NOT NULL,
    "Notes" character varying(512),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingScenarioStructuredSentences" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingScoreAppeals" (
    "Id" uuid NOT NULL,
    "SubmissionId" uuid NOT NULL,
    "OriginalGradeId" uuid NOT NULL,
    "NewGradeId" uuid,
    "UserId" character varying(64) NOT NULL,
    "Reason" character varying(500) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "Resolution" character varying(16),
    "ResolutionNote" character varying(500),
    "DeltaRawPoints" integer,
    "RequestedAt" timestamp with time zone NOT NULL,
    "ResolvedAt" timestamp with time zone,
    CONSTRAINT "PK_WritingScoreAppeals" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingShowcasePosts" (
    "Id" uuid NOT NULL,
    "SubmissionId" uuid NOT NULL,
    "AuthorUserId" character varying(64) NOT NULL,
    "AnonymizedLetterContent" text NOT NULL,
    "Profession" character varying(64) NOT NULL,
    "LetterType" character varying(8) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "ApprovedById" character varying(64),
    "PublishedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingShowcasePosts" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingSubmissions" (
    "Id" uuid NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "ScenarioId" uuid NOT NULL,
    "Mode" character varying(16) NOT NULL,
    "LetterContent" text NOT NULL,
    "LetterContentHash" character varying(64) NOT NULL,
    "WordCount" integer NOT NULL,
    "TimeSpentSeconds" integer NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "SubmittedAt" timestamp with time zone NOT NULL,
    "IsRevision" boolean NOT NULL,
    "OriginalSubmissionId" uuid,
    "Status" character varying(16) NOT NULL,
    "GradingTier" character varying(16) NOT NULL,
    "InputSource" character varying(16) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WritingSubmissions" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingTutorCalibrations" (
    "Id" uuid NOT NULL,
    "TutorId" character varying(64) NOT NULL,
    "AgreementCoefficient" numeric NOT NULL,
    "SamplesReviewed" integer NOT NULL,
    "LastCalibratedAt" timestamp with time zone NOT NULL,
    "Status" character varying(16) NOT NULL,
    CONSTRAINT "PK_WritingTutorCalibrations" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingTutorReviewAssignments" (
    "Id" uuid NOT NULL,
    "SubmissionId" uuid NOT NULL,
    "TutorId" character varying(64) NOT NULL,
    "ClaimedAt" timestamp with time zone NOT NULL,
    "DueAt" timestamp with time zone NOT NULL,
    "Status" character varying(16) NOT NULL,
    "ReleasedAt" timestamp with time zone,
    CONSTRAINT "PK_WritingTutorReviewAssignments" PRIMARY KEY ("Id")
);


CREATE TABLE "WritingTutorReviews" (
    "Id" uuid NOT NULL,
    "SubmissionId" uuid NOT NULL,
    "TutorId" character varying(64) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "FreeTextFeedback" text NOT NULL,
    "PerCriterionCommentsJson" jsonb NOT NULL DEFAULT '{}',
    "ScoreOverrideJson" jsonb,
    "SubmittedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "MarkerSequence" character varying(16) NOT NULL,
    "IsContentChecklistMarked" boolean NOT NULL,
    "ContentChecklistVerdictJson" text NOT NULL,
    "AcceptedAiPreAssessmentJson" text,
    CONSTRAINT "PK_WritingTutorReviews" PRIMARY KEY ("Id")
);


CREATE TABLE "AiAssistantMessages" (
    "Id" character varying(64) NOT NULL,
    "ThreadId" character varying(64) NOT NULL,
    "Role" character varying(16) NOT NULL,
    "Content" text,
    "ToolCallsJson" text,
    "ToolCallId" character varying(128),
    "ToolName" character varying(64),
    "TokenCount" integer NOT NULL,
    "Model" character varying(128),
    "AiUsageRecordId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AiAssistantMessages" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AiAssistantMessages_AiAssistantThreads_ThreadId" FOREIGN KEY ("ThreadId") REFERENCES "AiAssistantThreads" ("Id") ON DELETE CASCADE
);


CREATE TABLE "AdminPermissionGrants" (
    "Id" character varying(64) NOT NULL,
    "AdminUserId" character varying(64) NOT NULL,
    "Permission" character varying(64) NOT NULL,
    "GrantedBy" character varying(128) NOT NULL,
    "GrantedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AdminPermissionGrants" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AdminPermissionGrants_ApplicationUserAccounts_AdminUserId" FOREIGN KEY ("AdminUserId") REFERENCES "ApplicationUserAccounts" ("Id") ON DELETE CASCADE
);


CREATE TABLE "AiUsageRecords" (
    "Id" character varying(64) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UserId" character varying(64),
    "AuthAccountId" character varying(64),
    "TenantId" character varying(64),
    "FeatureCode" character varying(64) NOT NULL,
    "ProviderId" character varying(64),
    "AccountId" character varying(64),
    "FailoverTrace" character varying(1024),
    "Model" character varying(128),
    "KeySource" integer NOT NULL,
    "RulebookVersion" character varying(32),
    "PromptTemplateId" character varying(64),
    "SystemPromptHash" character varying(64),
    "UserPromptHash" character varying(64),
    "PromptTokens" integer NOT NULL,
    "CompletionTokens" integer NOT NULL,
    "CostEstimateUsd" numeric NOT NULL,
    "Outcome" integer NOT NULL,
    "ErrorCode" character varying(64),
    "ErrorMessage" character varying(512),
    "LatencyMs" integer NOT NULL,
    "RetryCount" integer NOT NULL,
    "PolicyTrace" character varying(256),
    "PeriodMonthKey" character varying(16) NOT NULL,
    "PeriodDayKey" character varying(16) NOT NULL,
    CONSTRAINT "PK_AiUsageRecords" PRIMARY KEY ("CreatedAt", "Id"),
    CONSTRAINT "FK_AiUsageRecords_ApplicationUserAccounts_AuthAccountId" FOREIGN KEY ("AuthAccountId") REFERENCES "ApplicationUserAccounts" ("Id") ON DELETE SET NULL
);


CREATE TABLE "AuditEvents" (
    "Id" character varying(64) NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "ActorId" character varying(64) NOT NULL,
    "ActorAuthAccountId" character varying(64),
    "ActorName" character varying(128) NOT NULL,
    "Action" character varying(128) NOT NULL,
    "ResourceType" character varying(64) NOT NULL,
    "ResourceId" character varying(64),
    "Details" text,
    CONSTRAINT "PK_AuditEvents" PRIMARY KEY ("OccurredAt", "Id"),
    CONSTRAINT "FK_AuditEvents_ApplicationUserAccounts_ActorAuthAccountId" FOREIGN KEY ("ActorAuthAccountId") REFERENCES "ApplicationUserAccounts" ("Id")
);


CREATE TABLE "EmailOtpChallenges" (
    "Id" uuid NOT NULL,
    "ApplicationUserAccountId" character varying(64) NOT NULL,
    "Purpose" character varying(64) NOT NULL,
    "CodeHash" character varying(512) NOT NULL,
    "AttemptCount" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "VerifiedAt" timestamp with time zone,
    CONSTRAINT "PK_EmailOtpChallenges" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_EmailOtpChallenges_ApplicationUserAccounts_ApplicationUserA~" FOREIGN KEY ("ApplicationUserAccountId") REFERENCES "ApplicationUserAccounts" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ExpertUsers" (
    "Id" character varying(64) NOT NULL,
    "AuthAccountId" character varying(64),
    "Role" character varying(32) NOT NULL,
    "DisplayName" character varying(128) NOT NULL,
    "Email" character varying(256) NOT NULL,
    "SpecialtiesJson" text NOT NULL,
    "Timezone" character varying(64) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ExpertUsers" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ExpertUsers_ApplicationUserAccounts_AuthAccountId" FOREIGN KEY ("AuthAccountId") REFERENCES "ApplicationUserAccounts" ("Id")
);


CREATE TABLE "ExternalIdentityLinks" (
    "Id" uuid NOT NULL,
    "ApplicationUserAccountId" character varying(64) NOT NULL,
    "Provider" character varying(32) NOT NULL,
    "ProviderSubject" character varying(256) NOT NULL,
    "Email" character varying(256) NOT NULL,
    "FirstName" character varying(128),
    "LastName" character varying(128),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "LastSignedInAt" timestamp with time zone,
    CONSTRAINT "PK_ExternalIdentityLinks" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ExternalIdentityLinks_ApplicationUserAccounts_ApplicationUs~" FOREIGN KEY ("ApplicationUserAccountId") REFERENCES "ApplicationUserAccounts" ("Id") ON DELETE CASCADE
);


CREATE TABLE "MfaRecoveryCodes" (
    "Id" uuid NOT NULL,
    "ApplicationUserAccountId" character varying(64) NOT NULL,
    "CodeHash" character varying(512) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "RedeemedAt" timestamp with time zone,
    CONSTRAINT "PK_MfaRecoveryCodes" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MfaRecoveryCodes_ApplicationUserAccounts_ApplicationUserAcc~" FOREIGN KEY ("ApplicationUserAccountId") REFERENCES "ApplicationUserAccounts" ("Id") ON DELETE CASCADE
);


CREATE TABLE "RefreshTokenRecords" (
    "Id" uuid NOT NULL,
    "ApplicationUserAccountId" character varying(64) NOT NULL,
    "TokenHash" character varying(512) NOT NULL,
    "FamilyId" uuid NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "RevokedAt" timestamp with time zone,
    "LastUsedAt" timestamp with time zone,
    "DeviceInfo" character varying(512),
    "IpAddress" character varying(64),
    CONSTRAINT "PK_RefreshTokenRecords" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_RefreshTokenRecords_ApplicationUserAccounts_ApplicationUser~" FOREIGN KEY ("ApplicationUserAccountId") REFERENCES "ApplicationUserAccounts" ("Id") ON DELETE CASCADE
);


CREATE TABLE "UserAiCredentials" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "AuthAccountId" character varying(64),
    "ProviderCode" character varying(64) NOT NULL,
    "EncryptedKey" character varying(4096) NOT NULL,
    "KeyHint" character varying(16) NOT NULL,
    "ModelAllowlistCsv" character varying(1024) NOT NULL,
    "Status" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "LastUsedAt" timestamp with time zone,
    "LastErrorAt" timestamp with time zone,
    "LastErrorCode" character varying(32),
    "CooldownUntil" timestamp with time zone,
    CONSTRAINT "PK_UserAiCredentials" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_UserAiCredentials_ApplicationUserAccounts_AuthAccountId" FOREIGN KEY ("AuthAccountId") REFERENCES "ApplicationUserAccounts" ("Id")
);


CREATE TABLE "Users" (
    "Id" character varying(64) NOT NULL,
    "AuthAccountId" character varying(64),
    "Role" character varying(32) NOT NULL,
    "DisplayName" character varying(128) NOT NULL,
    "Email" character varying(256) NOT NULL,
    "Timezone" character varying(64) NOT NULL,
    "Locale" character varying(16) NOT NULL,
    "CurrentPlanId" character varying(64),
    "ActiveProfessionId" character varying(32),
    "OnboardingCurrentStep" integer NOT NULL,
    "OnboardingStepCount" integer NOT NULL,
    "OnboardingCompleted" boolean NOT NULL,
    "OnboardingStartedAt" timestamp with time zone,
    "OnboardingCompletedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "LastActiveAt" timestamp with time zone NOT NULL,
    "AccountStatus" character varying(32) NOT NULL,
    "CurrentStreak" integer NOT NULL,
    "LongestStreak" integer NOT NULL,
    "LastPracticeDate" timestamp with time zone,
    "TotalPracticeMinutes" integer NOT NULL,
    "TotalPracticeSessions" integer NOT NULL,
    "WeeklyActivityJson" text,
    "ActiveExamTypeCode" character varying(16) NOT NULL,
    CONSTRAINT "PK_Users" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Users_ApplicationUserAccounts_AuthAccountId" FOREIGN KEY ("AuthAccountId") REFERENCES "ApplicationUserAccounts" ("Id")
);


CREATE TABLE "BillingPrices" (
    "Id" uuid NOT NULL,
    "BillingProductId" uuid NOT NULL,
    "StripePriceId" character varying(64),
    "Currency" character varying(3) NOT NULL,
    "Amount" numeric NOT NULL,
    "Interval" character varying(16),
    "IntervalCount" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "Country" character varying(2),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_BillingPrices" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_BillingPrices_BillingProducts_BillingProductId" FOREIGN KEY ("BillingProductId") REFERENCES "BillingProducts" ("Id") ON DELETE CASCADE
);


CREATE TABLE "AppliedPromoCodes" (
    "Id" uuid NOT NULL,
    "CartId" uuid NOT NULL,
    "Code" character varying(64) NOT NULL,
    "DiscountAmount" numeric,
    "DiscountPercent" numeric,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AppliedPromoCodes" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AppliedPromoCodes_Carts_CartId" FOREIGN KEY ("CartId") REFERENCES "Carts" ("Id") ON DELETE CASCADE
);


CREATE TABLE "RolePlayCards" (
    "Id" character varying(64) NOT NULL,
    "ContentItemId" character varying(64) NOT NULL,
    "ProfessionId" character varying(32) NOT NULL,
    "ScenarioTitle" character varying(200) NOT NULL,
    "Setting" character varying(160) NOT NULL,
    "CandidateRole" character varying(64) NOT NULL,
    "InterlocutorRole" character varying(64) NOT NULL,
    "PatientName" character varying(80),
    "PatientAge" character varying(32),
    "Background" character varying(4000) NOT NULL,
    "Task1" character varying(500),
    "Task2" character varying(500),
    "Task3" character varying(500),
    "Task4" character varying(500),
    "Task5" character varying(500),
    "AllowedNotes" boolean NOT NULL,
    "PrepTimeSeconds" integer NOT NULL,
    "RolePlayTimeSeconds" integer NOT NULL,
    "PatientEmotion" character varying(64) NOT NULL,
    "CommunicationGoal" character varying(64) NOT NULL,
    "ClinicalTopic" character varying(96) NOT NULL,
    "Difficulty" character varying(16) NOT NULL,
    "CriteriaFocusJson" text NOT NULL,
    "Disclaimer" character varying(400) NOT NULL,
    "Status" integer NOT NULL,
    "IsLiveTutorEligible" boolean NOT NULL,
    "CreatedByUserId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "PublishedAt" timestamp with time zone,
    "ArchivedAt" timestamp with time zone,
    CONSTRAINT "PK_RolePlayCards" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_RolePlayCards_ContentItems_ContentItemId" FOREIGN KEY ("ContentItemId") REFERENCES "ContentItems" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "SpeakingDrillItems" (
    "Id" character varying(64) NOT NULL,
    "ContentItemId" character varying(64) NOT NULL,
    "DrillKind" integer NOT NULL,
    "TargetCriteriaJson" text NOT NULL,
    "RecommendedAfterSessionScoreBelow" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SpeakingDrillItems" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SpeakingDrillItems_ContentItems_ContentItemId" FOREIGN KEY ("ContentItemId") REFERENCES "ContentItems" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "ListeningExtractionDrafts" (
    "Id" character varying(64) NOT NULL,
    "PaperId" character varying(64) NOT NULL,
    "Status" integer NOT NULL,
    "ProposedAt" timestamp with time zone NOT NULL,
    "ProposedByUserId" character varying(64),
    "IsStub" boolean NOT NULL,
    "StubReason" character varying(512),
    "Summary" character varying(2048) NOT NULL,
    "ProposedQuestionsJson" text NOT NULL,
    "RawAiResponseJson" character varying(65536),
    "DecidedAt" timestamp with time zone,
    "DecidedByUserId" character varying(64),
    "DecisionReason" character varying(512),
    CONSTRAINT "PK_ListeningExtractionDrafts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ListeningExtractionDrafts_ContentPapers_PaperId" FOREIGN KEY ("PaperId") REFERENCES "ContentPapers" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ReadingParts" (
    "Id" character varying(64) NOT NULL,
    "PaperId" character varying(64) NOT NULL,
    "PartCode" integer NOT NULL,
    "TimeLimitMinutes" integer NOT NULL,
    "MaxRawScore" integer NOT NULL,
    "Instructions" character varying(1024),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ReadingParts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ReadingParts_ContentPapers_PaperId" FOREIGN KEY ("PaperId") REFERENCES "ContentPapers" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "InterlocutorTrainingProgress" (
    "Id" character varying(64) NOT NULL,
    "TutorId" character varying(64) NOT NULL,
    "ModuleId" character varying(64) NOT NULL,
    "CompletedAt" timestamp with time zone,
    "QuizScore" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_InterlocutorTrainingProgress" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_InterlocutorTrainingProgress_InterlocutorTrainingModules_Mo~" FOREIGN KEY ("ModuleId") REFERENCES "InterlocutorTrainingModules" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ListeningAttemptNotes" (
    "Id" character varying(64) NOT NULL,
    "ListeningAttemptId" character varying(64) NOT NULL,
    "ListeningExtractId" character varying(64) NOT NULL,
    "TranscriptMs" integer,
    "Text" character varying(4096) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ListeningAttemptNotes" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ListeningAttemptNotes_ListeningAttempts_ListeningAttemptId" FOREIGN KEY ("ListeningAttemptId") REFERENCES "ListeningAttempts" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ListeningExpertFeedbacks" (
    "Id" character varying(64) NOT NULL,
    "AttemptId" character varying(64) NOT NULL,
    "ExpertId" character varying(64) NOT NULL,
    "OverallFeedbackMarkdown" text NOT NULL,
    "PerQuestionFeedbackJson" text,
    "RecommendedAreasJson" text,
    "RawScoreOverride" integer,
    "ScoreOverrideReason" character varying(512),
    "SubmittedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_ListeningExpertFeedbacks" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ListeningExpertFeedbacks_ListeningAttempts_AttemptId" FOREIGN KEY ("AttemptId") REFERENCES "ListeningAttempts" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ListeningExtracts" (
    "Id" character varying(64) NOT NULL,
    "ListeningPartId" character varying(64) NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "Kind" integer NOT NULL,
    "Title" character varying(200) NOT NULL,
    "AccentCode" character varying(32),
    "SpeakersJson" text NOT NULL,
    "AudioStartMs" integer,
    "AudioEndMs" integer,
    "ReplayInLearningOnly" boolean NOT NULL,
    "TranscriptSegmentsJson" text NOT NULL,
    "TopicCsv" character varying(256),
    "DifficultyRating" integer,
    "AudioContentSha" character varying(64),
    "TtsVoice" character varying(64),
    "TtsModelVariant" character varying(32),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ListeningExtracts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ListeningExtracts_ListeningParts_ListeningPartId" FOREIGN KEY ("ListeningPartId") REFERENCES "ListeningParts" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ContentPaperAssets" (
    "Id" character varying(64) NOT NULL,
    "PaperId" character varying(64) NOT NULL,
    "Role" integer NOT NULL,
    "Part" character varying(16),
    "MediaAssetId" character varying(64) NOT NULL,
    "Title" character varying(200),
    "DisplayOrder" integer NOT NULL,
    "IsPrimary" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ContentPaperAssets" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ContentPaperAssets_ContentPapers_PaperId" FOREIGN KEY ("PaperId") REFERENCES "ContentPapers" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ContentPaperAssets_MediaAssets_MediaAssetId" FOREIGN KEY ("MediaAssetId") REFERENCES "MediaAssets" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "ResultTemplateAssets" (
    "Id" character varying(64) NOT NULL,
    "TemplateKey" character varying(128) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Description" text,
    "ProfessionId" character varying(32),
    "MediaAssetId" character varying(64) NOT NULL,
    "IsActive" boolean NOT NULL,
    "SortOrder" integer NOT NULL,
    "UploadedByUserId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ResultTemplateAssets" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ResultTemplateAssets_MediaAssets_MediaAssetId" FOREIGN KEY ("MediaAssetId") REFERENCES "MediaAssets" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "RulebookVersions" (
    "Id" text NOT NULL,
    "Kind" character varying(32) NOT NULL,
    "Profession" character varying(32) NOT NULL,
    "Version" text NOT NULL,
    "Status" character varying(16) NOT NULL,
    "AuthoritySource" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "PublishedAt" timestamp with time zone,
    "UpdatedByUserId" text,
    "ReferencePdfAssetId" character varying(64),
    CONSTRAINT "PK_RulebookVersions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_RulebookVersions_MediaAssets_ReferencePdfAssetId" FOREIGN KEY ("ReferencePdfAssetId") REFERENCES "MediaAssets" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "SpeakingSharedResources" (
    "Id" character varying(64) NOT NULL,
    "Kind" character varying(32) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "ProfessionId" character varying(32),
    "MediaAssetId" character varying(64) NOT NULL,
    "Status" integer NOT NULL,
    "PublishedAt" timestamp with time zone,
    "EffectiveFrom" timestamp with time zone,
    "UploadedByUserId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SpeakingSharedResources" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SpeakingSharedResources_MediaAssets_MediaAssetId" FOREIGN KEY ("MediaAssetId") REFERENCES "MediaAssets" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "WritingAttemptAssets" (
    "Id" character varying(64) NOT NULL,
    "AttemptId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "MediaAssetId" character varying(64) NOT NULL,
    "AssetKind" character varying(32) NOT NULL,
    "PageNumber" integer NOT NULL,
    "ExtractionState" character varying(32) NOT NULL,
    "ExtractedText" text NOT NULL,
    "ExtractionProvider" character varying(64),
    "ExtractionReasonCode" character varying(128),
    "ExtractionMessage" character varying(1024),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "ExtractedAt" timestamp with time zone,
    CONSTRAINT "PK_WritingAttemptAssets" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_WritingAttemptAssets_Attempts_AttemptId" FOREIGN KEY ("AttemptId") REFERENCES "Attempts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_WritingAttemptAssets_MediaAssets_MediaAssetId" FOREIGN KEY ("MediaAssetId") REFERENCES "MediaAssets" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "MockAttempts" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "MockBundleId" character varying(64),
    "MockType" character varying(16) NOT NULL,
    "SubtestCode" character varying(32),
    "Mode" character varying(32) NOT NULL,
    "Profession" character varying(32) NOT NULL,
    "ReviewSelection" character varying(32) NOT NULL,
    "StrictTimer" boolean NOT NULL,
    "ReservedReviewCredits" integer NOT NULL,
    "DeliveryMode" character varying(16) NOT NULL,
    "Strictness" character varying(32) NOT NULL,
    "RandomisationSeed" bigint,
    "ConfigJson" text NOT NULL,
    "State" integer NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "SubmittedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    "ReportId" text,
    "ExamFamilyCode" character varying(16) NOT NULL,
    "ExamTypeCode" character varying(16) NOT NULL,
    CONSTRAINT "PK_MockAttempts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MockAttempts_MockBundles_MockBundleId" FOREIGN KEY ("MockBundleId") REFERENCES "MockBundles" ("Id") ON DELETE SET NULL
);


CREATE TABLE "MockBundleSections" (
    "Id" character varying(64) NOT NULL,
    "MockBundleId" character varying(64) NOT NULL,
    "SectionOrder" integer NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "ContentPaperId" character varying(64) NOT NULL,
    "TimeLimitMinutes" integer NOT NULL,
    "ReviewEligible" boolean NOT NULL,
    "IsRequired" boolean NOT NULL,
    "ModelAnswerReleasePolicy" character varying(32) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MockBundleSections" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MockBundleSections_ContentPapers_ContentPaperId" FOREIGN KEY ("ContentPaperId") REFERENCES "ContentPapers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_MockBundleSections_MockBundles_MockBundleId" FOREIGN KEY ("MockBundleId") REFERENCES "MockBundles" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ReviewVoiceNotes" (
    "Id" character varying(64) NOT NULL,
    "ReviewRequestId" character varying(64) NOT NULL,
    "UploadedByReviewerId" character varying(64) NOT NULL,
    "MediaAssetId" character varying(64) NOT NULL,
    "DurationSeconds" integer,
    "TranscriptText" text NOT NULL,
    "WrittenNotes" text NOT NULL,
    "RubricJson" text NOT NULL,
    "Status" character varying(32) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ReviewVoiceNotes" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ReviewVoiceNotes_MediaAssets_MediaAssetId" FOREIGN KEY ("MediaAssetId") REFERENCES "MediaAssets" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ReviewVoiceNotes_ReviewRequests_ReviewRequestId" FOREIGN KEY ("ReviewRequestId") REFERENCES "ReviewRequests" ("Id") ON DELETE CASCADE
);


CREATE TABLE "TeacherClassMembers" (
    "Id" character varying(64) NOT NULL,
    "TeacherClassId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "AddedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_TeacherClassMembers" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_TeacherClassMembers_TeacherClasses_TeacherClassId" FOREIGN KEY ("TeacherClassId") REFERENCES "TeacherClasses" ("Id") ON DELETE CASCADE
);


CREATE TABLE "PrivateSpeakingTutorProfiles" (
    "Id" character varying(64) NOT NULL,
    "ExpertUserId" character varying(64) NOT NULL,
    "DisplayName" character varying(128) NOT NULL,
    "Bio" character varying(256),
    "Timezone" character varying(64) NOT NULL,
    "PriceOverrideMinorUnits" integer,
    "SlotDurationOverrideMinutes" integer,
    "SpecialtiesJson" character varying(1024) NOT NULL,
    "IsActive" boolean NOT NULL,
    "TotalSessions" integer NOT NULL,
    "AverageRating" double precision NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PrivateSpeakingTutorProfiles" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PrivateSpeakingTutorProfiles_ExpertUsers_ExpertUserId" FOREIGN KEY ("ExpertUserId") REFERENCES "ExpertUsers" ("Id") ON DELETE CASCADE
);


CREATE TABLE "LearnerRegistrationProfiles" (
    "Id" character varying(64) NOT NULL,
    "ApplicationUserAccountId" character varying(64) NOT NULL,
    "LearnerUserId" character varying(64) NOT NULL,
    "FirstName" character varying(128) NOT NULL,
    "LastName" character varying(128) NOT NULL,
    "ExamTypeId" character varying(32) NOT NULL,
    "ProfessionId" character varying(32) NOT NULL,
    "SessionId" character varying(64) NOT NULL,
    "CountryTarget" character varying(64) NOT NULL,
    "MobileNumber" character varying(32) NOT NULL,
    "AgreeToTerms" boolean NOT NULL,
    "AgreeToPrivacy" boolean NOT NULL,
    "MarketingOptIn" boolean NOT NULL,
    "UtmSource" character varying(128),
    "UtmMedium" character varying(128),
    "UtmCampaign" character varying(256),
    "UtmTerm" character varying(128),
    "UtmContent" character varying(128),
    "ReferrerUrl" character varying(512),
    "LandingPath" character varying(512),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerRegistrationProfiles" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_LearnerRegistrationProfiles_ApplicationUserAccounts_Applica~" FOREIGN KEY ("ApplicationUserAccountId") REFERENCES "ApplicationUserAccounts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_LearnerRegistrationProfiles_Users_LearnerUserId" FOREIGN KEY ("LearnerUserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);


CREATE TABLE "Tutors" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "DisplayName" character varying(128) NOT NULL,
    "DisplayNameAr" character varying(128),
    "Bio" character varying(4096) NOT NULL,
    "BioAr" character varying(4096),
    "AvatarUrl" character varying(512),
    "SpecialtiesJson" character varying(1024) NOT NULL,
    "LanguagesJson" character varying(512) NOT NULL,
    "HourlyRateUsd" numeric(10,2),
    "TimeZone" character varying(64) NOT NULL,
    "ZoomUserId" character varying(128),
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Tutors" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Tutors_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);


CREATE TABLE "CartItems" (
    "Id" uuid NOT NULL,
    "CartId" uuid NOT NULL,
    "BillingProductId" uuid NOT NULL,
    "BillingPriceId" uuid NOT NULL,
    "Quantity" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_CartItems" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CartItems_BillingPrices_BillingPriceId" FOREIGN KEY ("BillingPriceId") REFERENCES "BillingPrices" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_CartItems_BillingProducts_BillingProductId" FOREIGN KEY ("BillingProductId") REFERENCES "BillingProducts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_CartItems_Carts_CartId" FOREIGN KEY ("CartId") REFERENCES "Carts" ("Id") ON DELETE CASCADE
);


CREATE TABLE "InterlocutorScripts" (
    "Id" character varying(64) NOT NULL,
    "RolePlayCardId" character varying(64) NOT NULL,
    "OpeningResponse" character varying(500) NOT NULL,
    "Prompt1" character varying(500),
    "Prompt2" character varying(500),
    "Prompt3" character varying(500),
    "HiddenInformation" character varying(2000) NOT NULL,
    "ResistanceLevel" integer NOT NULL,
    "ClosingCue" character varying(500) NOT NULL,
    "EmotionalState" character varying(200) NOT NULL,
    "ProfessionRoleNotes" character varying(500),
    "LayLanguageTriggersJson" text NOT NULL,
    "CreatedByUserId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_InterlocutorScripts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_InterlocutorScripts_RolePlayCards_RolePlayCardId" FOREIGN KEY ("RolePlayCardId") REFERENCES "RolePlayCards" ("Id") ON DELETE CASCADE
);


CREATE TABLE "SpeakingSessions" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "RolePlayCardId" character varying(64) NOT NULL,
    "MockSetId" character varying(64),
    "MockSessionId" character varying(64),
    "Mode" integer NOT NULL,
    "State" integer NOT NULL,
    "InterlocutorActorId" character varying(64),
    "LiveRoomId" character varying(64),
    "AttemptId" character varying(64),
    "WarmupStartedAt" timestamp with time zone,
    "WarmupEndedAt" timestamp with time zone,
    "PrepStartedAt" timestamp with time zone,
    "RolePlayStartedAt" timestamp with time zone,
    "EndedAt" timestamp with time zone,
    "ElapsedSeconds" integer NOT NULL,
    "ConsentVersion" character varying(32) NOT NULL,
    "RulebookVersion" character varying(32),
    "ConsentAcceptedAt" timestamp with time zone,
    "PaperDestroyedAt" timestamp with time zone,
    "RecommendedDrillIdsJson" character varying(1024),
    "Rp1PrepStartedAt" timestamp with time zone,
    "Rp1StartedAt" timestamp with time zone,
    "Rp1EndedAt" timestamp with time zone,
    "Rp2PrepStartedAt" timestamp with time zone,
    "Rp2StartedAt" timestamp with time zone,
    "Rp2EndedAt" timestamp with time zone,
    "SubmittedAt" timestamp with time zone,
    "TechnicalIssueFlag" boolean NOT NULL,
    "TechnicalIssueNote" character varying(1000),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SpeakingSessions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SpeakingSessions_RolePlayCards_RolePlayCardId" FOREIGN KEY ("RolePlayCardId") REFERENCES "RolePlayCards" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "SpeakingDrillAttempts" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "DrillItemId" character varying(64) NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "Score" integer,
    "AudioRecordingId" character varying(64),
    "TranscriptId" character varying(64),
    "AiFeedbackJson" text NOT NULL,
    "Source" integer NOT NULL,
    CONSTRAINT "PK_SpeakingDrillAttempts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SpeakingDrillAttempts_SpeakingDrillItems_DrillItemId" FOREIGN KEY ("DrillItemId") REFERENCES "SpeakingDrillItems" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ReadingTexts" (
    "Id" character varying(64) NOT NULL,
    "ReadingPartId" character varying(64) NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Source" character varying(256),
    "BodyHtml" character varying(65536) NOT NULL,
    "WordCount" integer NOT NULL,
    "TopicTag" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ReadingTexts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ReadingTexts_ReadingParts_ReadingPartId" FOREIGN KEY ("ReadingPartId") REFERENCES "ReadingParts" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ListeningQuestions" (
    "Id" character varying(64) NOT NULL,
    "PaperId" character varying(64) NOT NULL,
    "ListeningPartId" character varying(64) NOT NULL,
    "ListeningExtractId" character varying(64),
    "QuestionNumber" integer NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "Points" integer NOT NULL,
    "QuestionType" integer NOT NULL,
    "Stem" character varying(2048) NOT NULL,
    "CorrectAnswerJson" text NOT NULL,
    "AcceptedSynonymsJson" text,
    "CaseSensitive" boolean NOT NULL,
    "ExplanationMarkdown" character varying(4096),
    "SkillTag" character varying(64),
    "SubSkillTagsCsv" character varying(64),
    "Accent" character varying(16),
    "TranscriptEvidenceText" character varying(2048),
    "TranscriptEvidenceStartMs" integer,
    "TranscriptEvidenceEndMs" integer,
    "SpeakerAttitude" integer,
    "DifficultyLevel" integer,
    "Version" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ListeningQuestions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ListeningQuestions_ListeningExtracts_ListeningExtractId" FOREIGN KEY ("ListeningExtractId") REFERENCES "ListeningExtracts" ("Id"),
    CONSTRAINT "FK_ListeningQuestions_ListeningParts_ListeningPartId" FOREIGN KEY ("ListeningPartId") REFERENCES "ListeningParts" ("Id") ON DELETE CASCADE
);


CREATE TABLE "RulebookRuleRows" (
    "Id" text NOT NULL,
    "RulebookVersionId" text NOT NULL,
    "Code" text NOT NULL,
    "SectionCode" text NOT NULL,
    "Title" text NOT NULL,
    "Body" text NOT NULL,
    "Severity" text NOT NULL,
    "AppliesToJson" text NOT NULL,
    "TurnStage" text,
    "ExemplarPhrasesJson" text,
    "ForbiddenPatternsJson" text,
    "CheckId" text,
    "ParamsJson" text,
    "ExamplesJson" text,
    "OrderIndex" integer NOT NULL,
    CONSTRAINT "PK_RulebookRuleRows" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_RulebookRuleRows_RulebookVersions_RulebookVersionId" FOREIGN KEY ("RulebookVersionId") REFERENCES "RulebookVersions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "RulebookSectionRows" (
    "Id" text NOT NULL,
    "RulebookVersionId" text NOT NULL,
    "Code" text NOT NULL,
    "Title" text NOT NULL,
    "OrderIndex" integer NOT NULL,
    CONSTRAINT "PK_RulebookSectionRows" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_RulebookSectionRows_RulebookVersions_RulebookVersionId" FOREIGN KEY ("RulebookVersionId") REFERENCES "RulebookVersions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "MockBookings" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "MockBundleId" character varying(64) NOT NULL,
    "MockAttemptId" character varying(64),
    "ScheduledStartAt" timestamp with time zone NOT NULL,
    "TimezoneIana" character varying(80) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "AssignedTutorId" character varying(64),
    "AssignedInterlocutorId" character varying(64),
    "RescheduleCount" integer NOT NULL,
    "ConsentToRecording" boolean NOT NULL,
    "DeliveryMode" character varying(16) NOT NULL,
    "LiveRoomState" character varying(32) NOT NULL,
    "LiveRoomTransitionVersion" integer NOT NULL,
    "ZoomMeetingId" character varying(128),
    "ZoomJoinUrl" character varying(512),
    "ZoomStartUrl" character varying(512),
    "ZoomMeetingPassword" character varying(64),
    "LearnerNotes" character varying(2000),
    "RecordingManifestJson" text,
    "RecordingDurationMs" bigint,
    "RecordingFinalizedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "CancelledAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_MockBookings" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MockBookings_MockAttempts_MockAttemptId" FOREIGN KEY ("MockAttemptId") REFERENCES "MockAttempts" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_MockBookings_MockBundles_MockBundleId" FOREIGN KEY ("MockBundleId") REFERENCES "MockBundles" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "MockContentReviews" (
    "Id" character varying(64) NOT NULL,
    "MockBundleId" character varying(64),
    "MockAttemptId" character varying(64),
    "ReportedByUserId" character varying(64),
    "ReviewType" character varying(32) NOT NULL,
    "Severity" character varying(16) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "Stage" character varying(32) NOT NULL,
    "Notes" character varying(2000) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ResolvedAt" timestamp with time zone,
    "ResolvedByAdminId" character varying(64),
    CONSTRAINT "PK_MockContentReviews" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MockContentReviews_MockAttempts_MockAttemptId" FOREIGN KEY ("MockAttemptId") REFERENCES "MockAttempts" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_MockContentReviews_MockBundles_MockBundleId" FOREIGN KEY ("MockBundleId") REFERENCES "MockBundles" ("Id") ON DELETE SET NULL
);


CREATE TABLE "MockReviewReservations" (
    "Id" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "MockAttemptId" character varying(64) NOT NULL,
    "WalletId" character varying(64) NOT NULL,
    "State" integer NOT NULL,
    "ReservedCredits" integer NOT NULL,
    "ConsumedCredits" integer NOT NULL,
    "ReleasedCredits" integer NOT NULL,
    "Selection" character varying(32) NOT NULL,
    "ReservedAt" timestamp with time zone NOT NULL,
    "ConsumedAt" timestamp with time zone,
    "ReleasedAt" timestamp with time zone,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "DebitTransactionId" uuid,
    "ReleaseTransactionId" uuid,
    CONSTRAINT "PK_MockReviewReservations" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MockReviewReservations_MockAttempts_MockAttemptId" FOREIGN KEY ("MockAttemptId") REFERENCES "MockAttempts" ("Id") ON DELETE CASCADE
);


CREATE TABLE "MockSectionAttempts" (
    "Id" character varying(64) NOT NULL,
    "MockAttemptId" character varying(64) NOT NULL,
    "MockBundleSectionId" character varying(64) NOT NULL,
    "SubtestCode" character varying(32) NOT NULL,
    "State" integer NOT NULL,
    "ContentPaperId" character varying(64) NOT NULL,
    "LaunchRoute" character varying(512) NOT NULL,
    "ContentAttemptId" character varying(64),
    "RawScore" integer,
    "RawScoreMax" integer,
    "ScaledScore" integer,
    "Grade" character varying(8),
    "FeedbackJson" text NOT NULL,
    "StartedAt" timestamp with time zone,
    "DeadlineAt" timestamp with time zone,
    "SubmittedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_MockSectionAttempts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MockSectionAttempts_MockAttempts_MockAttemptId" FOREIGN KEY ("MockAttemptId") REFERENCES "MockAttempts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_MockSectionAttempts_MockBundleSections_MockBundleSectionId" FOREIGN KEY ("MockBundleSectionId") REFERENCES "MockBundleSections" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "LiveClasses" (
    "Id" character varying(64) NOT NULL,
    "Slug" character varying(160) NOT NULL,
    "Title" character varying(180) NOT NULL,
    "TitleAr" character varying(180),
    "Description" character varying(4096) NOT NULL,
    "DescriptionAr" character varying(4096),
    "Type" integer NOT NULL,
    "ProfessionTrack" character varying(64) NOT NULL,
    "Level" character varying(32) NOT NULL,
    "TutorProfileId" character varying(64),
    "TutorDisplayName" character varying(128),
    "DefaultDurationMinutes" integer NOT NULL,
    "DefaultCapacity" integer NOT NULL,
    "CreditCost" integer NOT NULL,
    "PriceUsd" numeric(10,2),
    "IsRecurring" boolean NOT NULL,
    "RecurrenceJson" character varying(2048) NOT NULL,
    "Status" integer NOT NULL,
    "CoverImageUrl" character varying(512),
    "TagsJson" character varying(2048) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LiveClasses" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_LiveClasses_PrivateSpeakingTutorProfiles_TutorProfileId" FOREIGN KEY ("TutorProfileId") REFERENCES "PrivateSpeakingTutorProfiles" ("Id") ON DELETE SET NULL
);


CREATE TABLE "PrivateSpeakingAvailabilityOverrides" (
    "Id" character varying(64) NOT NULL,
    "TutorProfileId" character varying(64) NOT NULL,
    "Date" date NOT NULL,
    "OverrideType" integer NOT NULL,
    "StartTime" character varying(8),
    "EndTime" character varying(8),
    "Reason" character varying(256),
    CONSTRAINT "PK_PrivateSpeakingAvailabilityOverrides" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PrivateSpeakingAvailabilityOverrides_PrivateSpeakingTutorPr~" FOREIGN KEY ("TutorProfileId") REFERENCES "PrivateSpeakingTutorProfiles" ("Id") ON DELETE CASCADE
);


CREATE TABLE "PrivateSpeakingAvailabilityRules" (
    "Id" character varying(64) NOT NULL,
    "TutorProfileId" character varying(64) NOT NULL,
    "DayOfWeek" integer NOT NULL,
    "StartTime" character varying(8) NOT NULL,
    "EndTime" character varying(8) NOT NULL,
    "IsActive" boolean NOT NULL,
    "EffectiveFrom" date,
    "EffectiveTo" date,
    CONSTRAINT "PK_PrivateSpeakingAvailabilityRules" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PrivateSpeakingAvailabilityRules_PrivateSpeakingTutorProfil~" FOREIGN KEY ("TutorProfileId") REFERENCES "PrivateSpeakingTutorProfiles" ("Id") ON DELETE CASCADE
);


CREATE TABLE "PrivateSpeakingBookings" (
    "Id" character varying(64) NOT NULL,
    "LearnerUserId" character varying(64) NOT NULL,
    "TutorProfileId" character varying(64) NOT NULL,
    "Status" integer NOT NULL,
    "SessionStartUtc" timestamp with time zone NOT NULL,
    "DurationMinutes" integer NOT NULL,
    "TutorTimezone" character varying(64) NOT NULL,
    "LearnerTimezone" character varying(64) NOT NULL,
    "PriceMinorUnits" integer NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "StripeCheckoutSessionId" character varying(256),
    "StripePaymentIntentId" character varying(256),
    "PaymentStatus" integer NOT NULL,
    "PaymentConfirmedAt" timestamp with time zone,
    "EntitlementSubscriptionId" character varying(64),
    "EntitlementConsumed" boolean NOT NULL,
    "EntitlementConsumedAt" timestamp with time zone,
    "EntitlementRestoredAt" timestamp with time zone,
    "EntitlementRestorationReason" character varying(128),
    "ZoomMeetingId" bigint,
    "ZoomJoinUrl" character varying(512),
    "ZoomStartUrl" character varying(512),
    "ZoomMeetingPassword" character varying(64),
    "ZoomStatus" integer NOT NULL,
    "ZoomError" character varying(512),
    "ZoomRetryCount" integer NOT NULL,
    "ReservationExpiresAt" timestamp with time zone,
    "IdempotencyKey" character varying(128),
    "LearnerNotes" character varying(2048),
    "TutorNotes" character varying(2048),
    "LearnerRating" integer,
    "LearnerFeedback" character varying(1024),
    "CancelledBy" character varying(64),
    "CancellationReason" character varying(512),
    "CancelledAt" timestamp with time zone,
    "RescheduledFromBookingId" character varying(64),
    "RescheduledToBookingId" character varying(64),
    "GoogleCalendarEventId" character varying(256),
    "GoogleCalendarSyncStatus" character varying(32),
    "GoogleCalendarSyncError" character varying(512),
    "GoogleCalendarSyncedAt" timestamp with time zone,
    "RemindersSentJson" character varying(256) NOT NULL,
    "DailyReminderSent" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_PrivateSpeakingBookings" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PrivateSpeakingBookings_PrivateSpeakingTutorProfiles_TutorP~" FOREIGN KEY ("TutorProfileId") REFERENCES "PrivateSpeakingTutorProfiles" ("Id") ON DELETE CASCADE
);


CREATE TABLE "PrivateSpeakingTutorCalendarConnections" (
    "Id" character varying(64) NOT NULL,
    "TutorProfileId" character varying(64) NOT NULL,
    "ExpertUserId" character varying(64) NOT NULL,
    "Provider" character varying(32) NOT NULL,
    "CalendarId" character varying(256) NOT NULL,
    "ConnectedEmail" character varying(256),
    "RefreshTokenEncrypted" text,
    "Scopes" character varying(1024) NOT NULL,
    "ConnectedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "DisconnectedAt" timestamp with time zone,
    "LastCheckedAt" timestamp with time zone,
    "LastSyncedAt" timestamp with time zone,
    "LastError" character varying(512),
    CONSTRAINT "PK_PrivateSpeakingTutorCalendarConnections" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PrivateSpeakingTutorCalendarConnections_PrivateSpeakingTuto~" FOREIGN KEY ("TutorProfileId") REFERENCES "PrivateSpeakingTutorProfiles" ("Id") ON DELETE CASCADE
);


CREATE TABLE "TutorAvailabilities" (
    "Id" character varying(64) NOT NULL,
    "TutorId" character varying(64) NOT NULL,
    "DayOfWeek" integer NOT NULL,
    "StartTime" time without time zone NOT NULL,
    "EndTime" time without time zone NOT NULL,
    "IsActive" boolean NOT NULL,
    CONSTRAINT "PK_TutorAvailabilities" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_TutorAvailabilities_Tutors_TutorId" FOREIGN KEY ("TutorId") REFERENCES "Tutors" ("Id") ON DELETE CASCADE
);


CREATE TABLE "SpeakingAiAssessments" (
    "Id" character varying(64) NOT NULL,
    "SpeakingSessionId" character varying(64) NOT NULL,
    "TranscriptId" character varying(64) NOT NULL,
    "Provider" character varying(32) NOT NULL,
    "ModelId" character varying(96) NOT NULL,
    "PromptTemplateId" character varying(64) NOT NULL,
    "Intelligibility" integer NOT NULL,
    "Fluency" integer NOT NULL,
    "Appropriateness" integer NOT NULL,
    "GrammarExpression" integer NOT NULL,
    "RelationshipBuilding" integer NOT NULL,
    "PatientPerspective" integer NOT NULL,
    "Structure" integer NOT NULL,
    "InformationGathering" integer NOT NULL,
    "InformationGiving" integer NOT NULL,
    "EstimatedScaledScore" integer NOT NULL,
    "ReadinessBand" character varying(32) NOT NULL,
    "PerCriterionRationalesJson" text NOT NULL,
    "OverallSummary" character varying(4000) NOT NULL,
    "ConfidenceBand" character varying(16) NOT NULL,
    "GeneratedAt" timestamp with time zone NOT NULL,
    "RulebookFindingsJson" text NOT NULL,
    "IsAdvisory" boolean NOT NULL,
    CONSTRAINT "PK_SpeakingAiAssessments" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SpeakingAiAssessments_SpeakingSessions_SpeakingSessionId" FOREIGN KEY ("SpeakingSessionId") REFERENCES "SpeakingSessions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "SpeakingLiveRooms" (
    "Id" character varying(64) NOT NULL,
    "SpeakingSessionId" character varying(64) NOT NULL,
    "Provider" character varying(32) NOT NULL,
    "RoomName" character varying(128) NOT NULL,
    "LearnerIdentity" character varying(96) NOT NULL,
    "TutorIdentity" character varying(96) NOT NULL,
    "LiveKitRoomSid" character varying(96),
    "ScheduledStartUtc" timestamp with time zone NOT NULL,
    "ActualStartUtc" timestamp with time zone,
    "ActualEndUtc" timestamp with time zone,
    "State" integer NOT NULL,
    "EgressId" character varying(96),
    "EgressOutputUrl" character varying(500),
    "MaxDurationSeconds" integer NOT NULL,
    "RecordingEnabled" boolean NOT NULL,
    "RecordingConsentVersion" character varying(32) NOT NULL,
    "WebhookEventsJson" text NOT NULL,
    "BookingId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SpeakingLiveRooms" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SpeakingLiveRooms_SpeakingSessions_SpeakingSessionId" FOREIGN KEY ("SpeakingSessionId") REFERENCES "SpeakingSessions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "SpeakingModerationCases" (
    "Id" character varying(64) NOT NULL,
    "SpeakingSessionId" character varying(64) NOT NULL,
    "Reason" character varying(24) NOT NULL,
    "FirstMarkerId" character varying(64),
    "FirstAssessmentId" character varying(64),
    "FirstScoreJson" text,
    "SecondMarkerId" character varying(64),
    "SecondAssessmentId" character varying(64),
    "SecondScoreJson" text,
    "ModeratorId" character varying(64),
    "FinalAssessmentId" character varying(64),
    "FinalScoreJson" text,
    "VariancePoints" integer,
    "VarianceReason" character varying(500),
    "FinalDecisionNote" character varying(1000),
    "RequestReattempt" boolean NOT NULL,
    "Status" character varying(24) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SpeakingModerationCases" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SpeakingModerationCases_SpeakingSessions_SpeakingSessionId" FOREIGN KEY ("SpeakingSessionId") REFERENCES "SpeakingSessions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "SpeakingRecordings" (
    "Id" character varying(64) NOT NULL,
    "SpeakingSessionId" character varying(64) NOT NULL,
    "MediaAssetId" character varying(64) NOT NULL,
    "Kind" integer NOT NULL,
    "Source" integer NOT NULL,
    "DurationSeconds" integer NOT NULL,
    "SizeBytes" bigint NOT NULL,
    "Sha256" character varying(64) NOT NULL,
    "MimeType" character varying(96) NOT NULL,
    "ConsentVersion" character varying(32) NOT NULL,
    "IsArchived" boolean NOT NULL,
    "RetentionExpiresAt" timestamp with time zone,
    "EgressTrackId" character varying(64),
    "IsWarmup" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SpeakingRecordings" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SpeakingRecordings_MediaAssets_MediaAssetId" FOREIGN KEY ("MediaAssetId") REFERENCES "MediaAssets" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_SpeakingRecordings_SpeakingSessions_SpeakingSessionId" FOREIGN KEY ("SpeakingSessionId") REFERENCES "SpeakingSessions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "SpeakingTimestampedComments" (
    "Id" character varying(64) NOT NULL,
    "SpeakingSessionId" character varying(64) NOT NULL,
    "AuthorId" character varying(64) NOT NULL,
    "AuthorRole" character varying(16) NOT NULL,
    "TranscriptSegmentIndex" integer NOT NULL,
    "StartMs" integer NOT NULL,
    "EndMs" integer NOT NULL,
    "CriterionCode" character varying(48) NOT NULL,
    "Severity" character varying(16) NOT NULL,
    "BodyMarkdown" character varying(4000) NOT NULL,
    "LinkedRulebookEntryCode" character varying(64),
    "LinkedDrillId" character varying(64),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SpeakingTimestampedComments" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SpeakingTimestampedComments_SpeakingSessions_SpeakingSessio~" FOREIGN KEY ("SpeakingSessionId") REFERENCES "SpeakingSessions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "SpeakingTranscripts" (
    "Id" character varying(64) NOT NULL,
    "SpeakingSessionId" character varying(64) NOT NULL,
    "Provider" character varying(32) NOT NULL,
    "Language" character varying(8) NOT NULL,
    "SegmentsJson" text NOT NULL,
    "IsLatest" boolean NOT NULL,
    "WordCount" integer NOT NULL,
    "MeanConfidence" double precision NOT NULL,
    "GeneratedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SpeakingTranscripts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SpeakingTranscripts_SpeakingSessions_SpeakingSessionId" FOREIGN KEY ("SpeakingSessionId") REFERENCES "SpeakingSessions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "SpeakingTutorAssessments" (
    "Id" character varying(64) NOT NULL,
    "SpeakingSessionId" character varying(64) NOT NULL,
    "TutorId" character varying(64) NOT NULL,
    "MarkerRole" character varying(16) NOT NULL,
    "Intelligibility" integer NOT NULL,
    "Fluency" integer NOT NULL,
    "Appropriateness" integer NOT NULL,
    "GrammarExpression" integer NOT NULL,
    "RelationshipBuilding" integer NOT NULL,
    "PatientPerspective" integer NOT NULL,
    "Structure" integer NOT NULL,
    "InformationGathering" integer NOT NULL,
    "InformationGiving" integer NOT NULL,
    "EstimatedScaledScore" integer NOT NULL,
    "ReadinessBand" character varying(32) NOT NULL,
    "OverallFeedbackMarkdown" text NOT NULL,
    "StrengthsJson" text NOT NULL,
    "ImprovementsJson" text NOT NULL,
    "RecommendedDrillsJson" text NOT NULL,
    "RecommendedRulebookEntries" character varying(1000) NOT NULL,
    "IsFinal" boolean NOT NULL,
    "SubmittedAt" timestamp with time zone,
    "MarkingDurationSeconds" integer NOT NULL,
    "CalibrationDeltaJson" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SpeakingTutorAssessments" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SpeakingTutorAssessments_SpeakingSessions_SpeakingSessionId" FOREIGN KEY ("SpeakingSessionId") REFERENCES "SpeakingSessions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ReadingQuestions" (
    "Id" character varying(64) NOT NULL,
    "ReadingPartId" character varying(64) NOT NULL,
    "ReadingTextId" character varying(64),
    "DisplayOrder" integer NOT NULL,
    "Points" integer NOT NULL,
    "QuestionType" integer NOT NULL,
    "Stem" character varying(2048) NOT NULL,
    "OptionsJson" character varying(4096) NOT NULL,
    "ParagraphIndex" integer,
    "CorrectAnswerJson" character varying(512) NOT NULL,
    "AcceptedSynonymsJson" character varying(4096),
    "CaseSensitive" boolean NOT NULL,
    "ExplanationMarkdown" character varying(4096),
    "SkillTag" character varying(32),
    "Difficulty" integer,
    "EvidenceSentence" character varying(1024),
    "DistractorRationaleJson" character varying(4096),
    "OptionDistractorsJson" character varying(2048),
    "ReviewState" integer NOT NULL,
    "LatestReviewNote" character varying(2048),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ReadingQuestions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ReadingQuestions_ReadingParts_ReadingPartId" FOREIGN KEY ("ReadingPartId") REFERENCES "ReadingParts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ReadingQuestions_ReadingTexts_ReadingTextId" FOREIGN KEY ("ReadingTextId") REFERENCES "ReadingTexts" ("Id") ON DELETE SET NULL
);


CREATE TABLE "ListeningAnswers" (
    "Id" character varying(64) NOT NULL,
    "ListeningAttemptId" character varying(64) NOT NULL,
    "ListeningQuestionId" character varying(64) NOT NULL,
    "UserAnswerJson" text NOT NULL,
    "IsCorrect" boolean,
    "PointsEarned" integer NOT NULL,
    "SelectedDistractorCategory" integer,
    "MissReason" integer,
    "QuestionVersionSnapshot" integer,
    "OptionVersionSnapshot" integer,
    "AnsweredAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ListeningAnswers" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ListeningAnswers_ListeningAttempts_ListeningAttemptId" FOREIGN KEY ("ListeningAttemptId") REFERENCES "ListeningAttempts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ListeningAnswers_ListeningQuestions_ListeningQuestionId" FOREIGN KEY ("ListeningQuestionId") REFERENCES "ListeningQuestions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ListeningQuestionOptions" (
    "Id" character varying(64) NOT NULL,
    "ListeningQuestionId" character varying(64) NOT NULL,
    "OptionKey" character varying(2) NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "Text" character varying(1024) NOT NULL,
    "IsCorrect" boolean NOT NULL,
    "DistractorCategory" integer,
    "WhyWrongMarkdown" character varying(1024),
    "Version" integer NOT NULL,
    CONSTRAINT "PK_ListeningQuestionOptions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ListeningQuestionOptions_ListeningQuestions_ListeningQuesti~" FOREIGN KEY ("ListeningQuestionId") REFERENCES "ListeningQuestions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "MockLiveRoomTransitions" (
    "Id" character varying(64) NOT NULL,
    "BookingId" character varying(64) NOT NULL,
    "ActorId" character varying(64) NOT NULL,
    "ActorRole" character varying(32) NOT NULL,
    "FromState" character varying(32) NOT NULL,
    "ToState" character varying(32) NOT NULL,
    "Reason" character varying(512),
    "ClientTransitionId" character varying(96),
    "TransitionVersion" integer NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "MetadataJson" text NOT NULL,
    CONSTRAINT "PK_MockLiveRoomTransitions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MockLiveRoomTransitions_MockBookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES "MockBookings" ("Id") ON DELETE CASCADE
);


CREATE TABLE "MockProctoringEvents" (
    "Id" character varying(64) NOT NULL,
    "MockAttemptId" character varying(64) NOT NULL,
    "MockSectionAttemptId" character varying(64),
    "Kind" character varying(48) NOT NULL,
    "Severity" character varying(16) NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "MetadataJson" text NOT NULL,
    CONSTRAINT "PK_MockProctoringEvents" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MockProctoringEvents_MockAttempts_MockAttemptId" FOREIGN KEY ("MockAttemptId") REFERENCES "MockAttempts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_MockProctoringEvents_MockSectionAttempts_MockSectionAttempt~" FOREIGN KEY ("MockSectionAttemptId") REFERENCES "MockSectionAttempts" ("Id")
);


CREATE TABLE "LiveClassSessions" (
    "Id" character varying(64) NOT NULL,
    "LiveClassId" character varying(64) NOT NULL,
    "ScheduledStartAt" timestamp with time zone NOT NULL,
    "ScheduledEndAt" timestamp with time zone NOT NULL,
    "Capacity" integer NOT NULL,
    "EnrolledCount" integer NOT NULL,
    "Status" integer NOT NULL,
    "ZoomMeetingId" bigint,
    "ZoomMeetingNumber" character varying(64),
    "ZoomJoinUrl" character varying(512),
    "ZoomStartUrl" character varying(512),
    "ZoomPasscode" character varying(64),
    "ZoomError" character varying(512),
    "ZoomRetryCount" integer NOT NULL,
    "ActualStartAt" timestamp with time zone,
    "ActualEndAt" timestamp with time zone,
    "DurationMinutes" integer,
    "RecordingId" character varying(64),
    "CancellationReason" character varying(512),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LiveClassSessions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_LiveClassSessions_LiveClasses_LiveClassId" FOREIGN KEY ("LiveClassId") REFERENCES "LiveClasses" ("Id") ON DELETE CASCADE
);


CREATE TABLE "SpeakingLiveRoomTokens" (
    "Id" character varying(64) NOT NULL,
    "LiveRoomId" character varying(64) NOT NULL,
    "Identity" character varying(96) NOT NULL,
    "IssuedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "Role" integer NOT NULL,
    "RevokedAt" timestamp with time zone,
    "Capabilities" character varying(256) NOT NULL,
    CONSTRAINT "PK_SpeakingLiveRoomTokens" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SpeakingLiveRoomTokens_SpeakingLiveRooms_LiveRoomId" FOREIGN KEY ("LiveRoomId") REFERENCES "SpeakingLiveRooms" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ReadingAnswers" (
    "Id" character varying(64) NOT NULL,
    "ReadingAttemptId" character varying(64) NOT NULL,
    "ReadingQuestionId" character varying(64) NOT NULL,
    "UserAnswerJson" character varying(2048) NOT NULL,
    "IsCorrect" boolean,
    "PointsEarned" integer NOT NULL,
    "SelectedDistractorCategory" integer,
    "MissReason" character varying(64),
    "FlaggedForReview" boolean NOT NULL,
    "AnsweredAt" timestamp with time zone NOT NULL,
    "ElapsedMs" integer,
    "TotalElapsedMs" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ReadingAnswers" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ReadingAnswers_ReadingAttempts_ReadingAttemptId" FOREIGN KEY ("ReadingAttemptId") REFERENCES "ReadingAttempts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ReadingAnswers_ReadingQuestions_ReadingQuestionId" FOREIGN KEY ("ReadingQuestionId") REFERENCES "ReadingQuestions" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "ReadingQuestionReviewLogs" (
    "Id" character varying(64) NOT NULL,
    "ReadingQuestionId" character varying(64) NOT NULL,
    "FromState" integer NOT NULL,
    "ToState" integer NOT NULL,
    "ReviewerUserId" character varying(64) NOT NULL,
    "ReviewerDisplayName" character varying(200),
    "Note" character varying(2048),
    "TransitionedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ReadingQuestionReviewLogs" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ReadingQuestionReviewLogs_ReadingQuestions_ReadingQuestionId" FOREIGN KEY ("ReadingQuestionId") REFERENCES "ReadingQuestions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "LiveClassAttendances" (
    "Id" character varying(64) NOT NULL,
    "ClassSessionId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "EnrollmentId" character varying(64),
    "JoinedAt" timestamp with time zone NOT NULL,
    "LeftAt" timestamp with time zone,
    "DurationSeconds" integer NOT NULL,
    "ZoomParticipantUuid" character varying(128),
    "ReceivedRecordingAccess" boolean NOT NULL,
    CONSTRAINT "PK_LiveClassAttendances" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_LiveClassAttendances_LiveClassSessions_ClassSessionId" FOREIGN KEY ("ClassSessionId") REFERENCES "LiveClassSessions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "LiveClassEnrollments" (
    "Id" character varying(64) NOT NULL,
    "ClassSessionId" character varying(64) NOT NULL,
    "UserId" character varying(64) NOT NULL,
    "EnrolledAt" timestamp with time zone NOT NULL,
    "CreditsCharged" integer NOT NULL,
    "WalletTransactionId" uuid,
    "RefundWalletTransactionId" uuid,
    "Status" integer NOT NULL,
    "CancelledAt" timestamp with time zone,
    "CancellationReason" character varying(512),
    "IdempotencyKey" character varying(128) NOT NULL,
    CONSTRAINT "PK_LiveClassEnrollments" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_LiveClassEnrollments_LiveClassSessions_ClassSessionId" FOREIGN KEY ("ClassSessionId") REFERENCES "LiveClassSessions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "LiveClassRecordings" (
    "Id" character varying(64) NOT NULL,
    "ClassSessionId" character varying(64) NOT NULL,
    "ZoomRecordingId" character varying(128),
    "Status" integer NOT NULL,
    "S3VideoKey" character varying(512),
    "S3AudioKey" character varying(512),
    "S3TranscriptKey" character varying(512),
    "TranscriptText" text,
    "AiSummary" text,
    "AiSummaryAr" text,
    "ChaptersJson" text NOT NULL,
    "ActionItemsJson" text NOT NULL,
    "DurationSeconds" integer NOT NULL,
    "FileSizeBytes" bigint NOT NULL,
    "RecordedAt" timestamp with time zone NOT NULL,
    "ProcessedAt" timestamp with time zone,
    "ExpiresAt" timestamp with time zone,
    "FailureReason" character varying(512),
    CONSTRAINT "PK_LiveClassRecordings" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_LiveClassRecordings_LiveClassSessions_ClassSessionId" FOREIGN KEY ("ClassSessionId") REFERENCES "LiveClassSessions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ClassRecordingEmbeddings" (
    "Id" character varying(64) NOT NULL,
    "ClassRecordingId" character varying(64) NOT NULL,
    "ChunkIndex" integer NOT NULL,
    "ChunkText" text NOT NULL,
    "EmbeddingJson" text NOT NULL,
    "EmbeddingModel" character varying(64) NOT NULL,
    "StartTimeSeconds" integer NOT NULL,
    "EndTimeSeconds" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ClassRecordingEmbeddings" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ClassRecordingEmbeddings_LiveClassRecordings_ClassRecording~" FOREIGN KEY ("ClassRecordingId") REFERENCES "LiveClassRecordings" ("Id") ON DELETE CASCADE
);


CREATE UNIQUE INDEX "IX_AccountFreezeEntitlements_UserId" ON "AccountFreezeEntitlements" ("UserId");


CREATE INDEX "IX_AccountFreezePolicies_Version" ON "AccountFreezePolicies" ("Version");


CREATE INDEX "IX_AccountFreezeRecords_Status_EndedAt" ON "AccountFreezeRecords" ("Status", "EndedAt");


CREATE INDEX "IX_AccountFreezeRecords_Status_ScheduledStartAt" ON "AccountFreezeRecords" ("Status", "ScheduledStartAt");


CREATE UNIQUE INDEX "IX_AccountFreezeRecords_UserId" ON "AccountFreezeRecords" ("UserId") WHERE "IsCurrent" = TRUE;


CREATE INDEX "IX_AccountFreezeRecords_UserId_Status" ON "AccountFreezeRecords" ("UserId", "Status");


CREATE INDEX "IX_Achievements_Status_SortOrder" ON "Achievements" ("Status", "SortOrder");


CREATE UNIQUE INDEX "IX_AdminPermissionGrants_AdminUserId_Permission" ON "AdminPermissionGrants" ("AdminUserId", "Permission");


CREATE INDEX "IX_AdminUploadSessions_AdminUserId_ExpiresAt" ON "AdminUploadSessions" ("AdminUserId", "ExpiresAt");


CREATE INDEX "IX_AdminUploadSessions_State_ExpiresAt" ON "AdminUploadSessions" ("State", "ExpiresAt");


CREATE INDEX "IX_AffiliateAttributions_AffiliateId_ConvertedAt" ON "AffiliateAttributions" ("AffiliateId", "ConvertedAt");


CREATE UNIQUE INDEX "IX_AffiliateAttributions_UserId_AffiliateId" ON "AffiliateAttributions" ("UserId", "AffiliateId");


CREATE INDEX "IX_AffiliateCommissions_AffiliateId_Status" ON "AffiliateCommissions" ("AffiliateId", "Status");


CREATE UNIQUE INDEX "IX_AffiliateCommissions_PaymentTransactionId" ON "AffiliateCommissions" ("PaymentTransactionId");


CREATE UNIQUE INDEX "IX_Affiliates_Code" ON "Affiliates" ("Code");


CREATE INDEX "IX_Affiliates_Status" ON "Affiliates" ("Status");


CREATE INDEX "IX_AiAssistantMessages_ThreadId_CreatedAt" ON "AiAssistantMessages" ("ThreadId", "CreatedAt");


CREATE INDEX "IX_AiAssistantThreads_UserId_Role" ON "AiAssistantThreads" ("UserId", "Role");


CREATE INDEX "IX_AiAssistantThreads_UserId_UpdatedAt" ON "AiAssistantThreads" ("UserId", "UpdatedAt");


CREATE INDEX "IX_AiCodebaseChunks_FilePath" ON "AiCodebaseChunks" ("FilePath");


CREATE INDEX "IX_AiCodebaseChunks_Language" ON "AiCodebaseChunks" ("Language");


CREATE INDEX "IX_AIConfigVersions_TaskType_Status" ON "AIConfigVersions" ("TaskType", "Status");


CREATE INDEX "IX_AiCreditLedger_ExpiresAt" ON "AiCreditLedger" ("ExpiresAt");


CREATE INDEX "IX_AiCreditLedger_UserId_CreatedAt" ON "AiCreditLedger" ("UserId", "CreatedAt");


CREATE UNIQUE INDEX "UX_AiCreditLedger_Expiration_ReferenceId" ON "AiCreditLedger" ("Source", "ReferenceId") WHERE "ReferenceId" IS NOT NULL AND "Source" = 5;


CREATE UNIQUE INDEX "UX_AiCreditLedger_PlanRenewal_ReferenceId" ON "AiCreditLedger" ("ReferenceId") WHERE "ReferenceId" IS NOT NULL AND "Source" = 0;


CREATE UNIQUE INDEX "UX_AiCreditLedger_Purchase_ReferenceId" ON "AiCreditLedger" ("UserId", "ReferenceId", "Source") WHERE "ReferenceId" IS NOT NULL AND "Source" = 2;


CREATE UNIQUE INDEX "UX_AiCreditLedger_RefundAdjustment_ReferenceId" ON "AiCreditLedger" ("Source", "UserId", "ReferenceId") WHERE "ReferenceId" IS NOT NULL AND "Source" = 3 AND ("ReferenceId" LIKE 'addon-refund:%' OR "ReferenceId" LIKE 'plan-refund:%');


CREATE UNIQUE INDEX "UX_AiCreditLedger_UsageDebit_ReferenceId" ON "AiCreditLedger" ("ReferenceId", "Source") WHERE "ReferenceId" IS NOT NULL AND "Source" = 4;


CREATE UNIQUE INDEX "IX_AiFeatureRoutes_FeatureCode" ON "AiFeatureRoutes" ("FeatureCode");


CREATE INDEX "IX_AiFeatureToolGrants_FeatureCode" ON "AiFeatureToolGrants" ("FeatureCode");


CREATE UNIQUE INDEX "IX_AiFeatureToolGrants_FeatureCode_ToolCode" ON "AiFeatureToolGrants" ("FeatureCode", "ToolCode");


CREATE INDEX "IX_AiFileBackups_FilePath_CreatedAt" ON "AiFileBackups" ("FilePath", "CreatedAt");


CREATE INDEX "IX_AiFileBackups_ThreadId_CreatedAt" ON "AiFileBackups" ("ThreadId", "CreatedAt");


CREATE INDEX "IX_AiProviderAccounts_ProviderId_IsActive" ON "AiProviderAccounts" ("ProviderId", "IsActive");


CREATE INDEX "IX_AiProviderAccounts_ProviderId_Priority" ON "AiProviderAccounts" ("ProviderId", "Priority");


CREATE UNIQUE INDEX "IX_AiProviders_Code" ON "AiProviders" ("Code");


CREATE UNIQUE INDEX "IX_AiQuotaCounters_UserId_PeriodKey" ON "AiQuotaCounters" ("UserId", "PeriodKey");


CREATE UNIQUE INDEX "IX_AiQuotaPlans_Code" ON "AiQuotaPlans" ("Code");


CREATE INDEX "IX_AiToolInvocations_AiUsageRecordId_TurnIndex" ON "AiToolInvocations" ("AiUsageRecordId", "TurnIndex");


CREATE INDEX "IX_AiToolInvocations_FeatureCode_CreatedAt" ON "AiToolInvocations" ("FeatureCode", "CreatedAt");


CREATE INDEX "IX_AiToolInvocations_ToolCode_CreatedAt" ON "AiToolInvocations" ("ToolCode", "CreatedAt");


CREATE UNIQUE INDEX "IX_AiTools_Code" ON "AiTools" ("Code");


CREATE INDEX "IX_AiUsageRecords_AccountId_CreatedAt" ON "AiUsageRecords" ("AccountId", "CreatedAt");


CREATE INDEX "IX_AiUsageRecords_AuthAccountId" ON "AiUsageRecords" ("AuthAccountId");


CREATE INDEX "IX_AiUsageRecords_CreatedAt" ON "AiUsageRecords" ("CreatedAt");


CREATE INDEX "IX_AiUsageRecords_FeatureCode_CreatedAt" ON "AiUsageRecords" ("FeatureCode", "CreatedAt");


CREATE INDEX "IX_AiUsageRecords_ProviderId_CreatedAt" ON "AiUsageRecords" ("ProviderId", "CreatedAt");


CREATE INDEX "IX_AiUsageRecords_UserId_CreatedAt" ON "AiUsageRecords" ("UserId", "CreatedAt");


CREATE INDEX "IX_AnalyticsEvents_EventName_OccurredAt" ON "AnalyticsEvents" ("EventName", "OccurredAt");


CREATE INDEX "IX_AnalyticsEvents_UserId_EventName_OccurredAt" ON "AnalyticsEvents" ("UserId", "EventName", "OccurredAt");


CREATE INDEX "IX_ApplicationUserAccounts_DeletedAt" ON "ApplicationUserAccounts" ("DeletedAt");


CREATE INDEX "IX_ApplicationUserAccounts_LastLoginAt" ON "ApplicationUserAccounts" ("LastLoginAt");


CREATE UNIQUE INDEX "IX_ApplicationUserAccounts_NormalizedEmail" ON "ApplicationUserAccounts" ("NormalizedEmail");


CREATE INDEX "IX_AppliedPromoCodes_CartId" ON "AppliedPromoCodes" ("CartId");


CREATE INDEX "IX_Attempts_ContentId" ON "Attempts" ("ContentId");


CREATE INDEX "IX_Attempts_UserId_SubtestCode_State" ON "Attempts" ("UserId", "SubtestCode", "State");


CREATE INDEX "IX_AuditEvents_ActorAuthAccountId" ON "AuditEvents" ("ActorAuthAccountId");


CREATE INDEX "IX_AuditEvents_ActorId" ON "AuditEvents" ("ActorId");


CREATE INDEX "IX_AuditEvents_OccurredAt" ON "AuditEvents" ("OccurredAt");


CREATE INDEX "IX_AuditEvents_ResourceType_ResourceId" ON "AuditEvents" ("ResourceType", "ResourceId");


CREATE INDEX "IX_BackgroundJobs_State_AvailableAt" ON "BackgroundJobs" ("State", "AvailableAt");


CREATE INDEX "IX_BankAccountConfigs_Region_Currency_IsActive" ON "BankAccountConfigs" ("Region", "Currency", "IsActive");


CREATE UNIQUE INDEX "IX_BillingAddOns_Code" ON "BillingAddOns" ("Code");


CREATE INDEX "IX_BillingAddOns_Status_DisplayOrder" ON "BillingAddOns" ("Status", "DisplayOrder");


CREATE UNIQUE INDEX "IX_BillingAddOnVersions_AddOnId_VersionNumber" ON "BillingAddOnVersions" ("AddOnId", "VersionNumber");


CREATE INDEX "IX_BillingAddOnVersions_Code" ON "BillingAddOnVersions" ("Code");


CREATE INDEX "IX_BillingCouponRedemptions_CouponCode_UserId_RedeemedAt" ON "BillingCouponRedemptions" ("CouponCode", "UserId", "RedeemedAt");


CREATE INDEX "IX_BillingCouponRedemptions_CouponId_UserId_RedeemedAt" ON "BillingCouponRedemptions" ("CouponId", "UserId", "RedeemedAt");


CREATE INDEX "IX_BillingCouponRedemptions_CouponVersionId" ON "BillingCouponRedemptions" ("CouponVersionId");


CREATE UNIQUE INDEX "IX_BillingCoupons_Code" ON "BillingCoupons" ("Code");


CREATE INDEX "IX_BillingCoupons_Status_EndsAt" ON "BillingCoupons" ("Status", "EndsAt");


CREATE INDEX "IX_BillingCouponVersions_Code" ON "BillingCouponVersions" ("Code");


CREATE UNIQUE INDEX "IX_BillingCouponVersions_CouponId_VersionNumber" ON "BillingCouponVersions" ("CouponId", "VersionNumber");


CREATE INDEX "IX_BillingEvents_EntityType_EntityId_OccurredAt" ON "BillingEvents" ("EntityType", "EntityId", "OccurredAt");


CREATE INDEX "IX_BillingEvents_UserId_OccurredAt" ON "BillingEvents" ("UserId", "OccurredAt");


CREATE INDEX "IX_BillingMetricDailies_MetricCode_MetricDate" ON "BillingMetricDailies" ("MetricCode", "MetricDate");


CREATE UNIQUE INDEX "IX_BillingMetricDailies_MetricDate_MetricCode_Region" ON "BillingMetricDailies" ("MetricDate", "MetricCode", "Region");


CREATE UNIQUE INDEX "IX_BillingNotificationDispatchLogs_UserId_EventCode_EventId_Te~" ON "BillingNotificationDispatchLogs" ("UserId", "EventCode", "EventId", "TemplateCode");


CREATE INDEX "IX_BillingNotificationDispatchLogs_UserId_SentAt" ON "BillingNotificationDispatchLogs" ("UserId", "SentAt");


CREATE UNIQUE INDEX "IX_BillingNotificationTemplates_Code_Channel_LocaleTag" ON "BillingNotificationTemplates" ("Code", "Channel", "LocaleTag");


CREATE UNIQUE INDEX "IX_BillingPlans_Code" ON "BillingPlans" ("Code");


CREATE INDEX "IX_BillingPlans_Status_DisplayOrder" ON "BillingPlans" ("Status", "DisplayOrder");


CREATE INDEX "IX_BillingPlanVersions_Code" ON "BillingPlanVersions" ("Code");


CREATE UNIQUE INDEX "IX_BillingPlanVersions_PlanId_VersionNumber" ON "BillingPlanVersions" ("PlanId", "VersionNumber");


CREATE INDEX "IX_BillingPrices_BillingProductId" ON "BillingPrices" ("BillingProductId");


CREATE INDEX "IX_BillingPrices_StripePriceId" ON "BillingPrices" ("StripePriceId");


CREATE UNIQUE INDEX "IX_BillingProducts_Code" ON "BillingProducts" ("Code");


CREATE INDEX "IX_BillingQuotes_CouponVersionId" ON "BillingQuotes" ("CouponVersionId");


CREATE INDEX "IX_BillingQuotes_PlanVersionId" ON "BillingQuotes" ("PlanVersionId");


CREATE INDEX "IX_BillingQuotes_Status_ExpiresAt" ON "BillingQuotes" ("Status", "ExpiresAt");


CREATE INDEX "IX_BillingQuotes_UserId_CreatedAt" ON "BillingQuotes" ("UserId", "CreatedAt");


CREATE INDEX "IX_CancellationIntents_SubscriptionId_Status" ON "CancellationIntents" ("SubscriptionId", "Status");


CREATE INDEX "IX_CartItems_BillingPriceId" ON "CartItems" ("BillingPriceId");


CREATE INDEX "IX_CartItems_BillingProductId" ON "CartItems" ("BillingProductId");


CREATE INDEX "IX_CartItems_CartId" ON "CartItems" ("CartId");


CREATE INDEX "IX_Carts_SessionToken" ON "Carts" ("SessionToken");


CREATE INDEX "IX_Carts_Status" ON "Carts" ("Status");


CREATE INDEX "IX_Carts_UserId" ON "Carts" ("UserId");


CREATE INDEX "IX_Certificates_UserId" ON "Certificates" ("UserId");


CREATE UNIQUE INDEX "IX_Certificates_VerificationCode" ON "Certificates" ("VerificationCode");


CREATE UNIQUE INDEX "IX_CheckoutSessions_IdempotencyKey" ON "CheckoutSessions" ("IdempotencyKey");


CREATE INDEX "IX_CheckoutSessions_StripeSessionId" ON "CheckoutSessions" ("StripeSessionId");


CREATE INDEX "IX_CheckoutSessions_UserId" ON "CheckoutSessions" ("UserId");


CREATE INDEX "IX_ChurnRiskSnapshots_RiskBand_RiskScore" ON "ChurnRiskSnapshots" ("RiskBand", "RiskScore");


CREATE INDEX "IX_ChurnRiskSnapshots_SnapshotDate_RiskBand" ON "ChurnRiskSnapshots" ("SnapshotDate", "RiskBand");


CREATE INDEX "IX_ChurnRiskSnapshots_UserId_SnapshotDate" ON "ChurnRiskSnapshots" ("UserId", "SnapshotDate");


CREATE INDEX "IX_ClassFeedbacks_ClassSessionId" ON "ClassFeedbacks" ("ClassSessionId");


CREATE UNIQUE INDEX "IX_ClassFeedbacks_ClassSessionId_UserId" ON "ClassFeedbacks" ("ClassSessionId", "UserId");


CREATE INDEX "IX_ClassFeedbacks_UserId" ON "ClassFeedbacks" ("UserId");


CREATE INDEX "IX_ClassMaterials_ClassSessionId" ON "ClassMaterials" ("ClassSessionId");


CREATE INDEX "IX_ClassMaterials_LiveClassId" ON "ClassMaterials" ("LiveClassId");


CREATE INDEX "IX_ClassMaterials_LiveClassId_ClassSessionId" ON "ClassMaterials" ("LiveClassId", "ClassSessionId");


CREATE INDEX "IX_ClassRecordingEmbeddings_ClassRecordingId" ON "ClassRecordingEmbeddings" ("ClassRecordingId");


CREATE UNIQUE INDEX "IX_ClassRecordingEmbeddings_ClassRecordingId_ChunkIndex" ON "ClassRecordingEmbeddings" ("ClassRecordingId", "ChunkIndex");


CREATE UNIQUE INDEX "IX_CohortMembers_CohortId_LearnerId" ON "CohortMembers" ("CohortId", "LearnerId");


CREATE INDEX "IX_ContentCohortOverlays_ProgramId_CohortCode" ON "ContentCohortOverlays" ("ProgramId", "CohortCode");


CREATE INDEX "IX_ContentGenerationJobs_RequestedBy" ON "ContentGenerationJobs" ("RequestedBy");


CREATE INDEX "IX_ContentGenerationJobs_State_CreatedAt" ON "ContentGenerationJobs" ("State", "CreatedAt");


CREATE INDEX "IX_ContentImportBatches_CreatedBy" ON "ContentImportBatches" ("CreatedBy");


CREATE INDEX "IX_ContentImportBatches_Status_CreatedAt" ON "ContentImportBatches" ("Status", "CreatedAt");


CREATE INDEX "IX_ContentItems_DuplicateGroupId" ON "ContentItems" ("DuplicateGroupId");


CREATE INDEX "IX_ContentItems_ImportBatchId" ON "ContentItems" ("ImportBatchId");


CREATE INDEX "IX_ContentItems_InstructionLanguage" ON "ContentItems" ("InstructionLanguage");


CREATE INDEX "IX_ContentItems_IsPreviewEligible_Status" ON "ContentItems" ("IsPreviewEligible", "Status");


CREATE INDEX "IX_ContentItems_SourceProvenance" ON "ContentItems" ("SourceProvenance");


CREATE INDEX "IX_ContentItems_SubtestCode_Status" ON "ContentItems" ("SubtestCode", "Status");


CREATE INDEX "IX_ContentLessons_ModuleId_DisplayOrder" ON "ContentLessons" ("ModuleId", "DisplayOrder");


CREATE INDEX "IX_ContentModules_TrackId_DisplayOrder" ON "ContentModules" ("TrackId", "DisplayOrder");


CREATE UNIQUE INDEX "IX_ContentPackages_Code" ON "ContentPackages" ("Code");


CREATE INDEX "IX_ContentPackages_Status_DisplayOrder" ON "ContentPackages" ("Status", "DisplayOrder");


CREATE INDEX "IX_ContentPaperAssets_MediaAssetId" ON "ContentPaperAssets" ("MediaAssetId");


CREATE INDEX "IX_ContentPaperAssets_PaperId_Role" ON "ContentPaperAssets" ("PaperId", "Role");


CREATE UNIQUE INDEX "UX_PaperAsset_Primary_Per_RolePart" ON "ContentPaperAssets" ("PaperId", "Role", "Part", "IsPrimary");


CREATE INDEX "IX_ContentPapers_CardType" ON "ContentPapers" ("CardType");


CREATE INDEX "IX_ContentPapers_LetterType" ON "ContentPapers" ("LetterType");


CREATE INDEX "IX_ContentPapers_ProfessionId_SubtestCode" ON "ContentPapers" ("ProfessionId", "SubtestCode");


CREATE UNIQUE INDEX "IX_ContentPapers_Slug" ON "ContentPapers" ("Slug");


CREATE INDEX "IX_ContentPapers_SubtestCode_Status" ON "ContentPapers" ("SubtestCode", "Status");


CREATE UNIQUE INDEX "IX_ContentPrograms_Code" ON "ContentPrograms" ("Code");


CREATE INDEX "IX_ContentPrograms_ProgramType_InstructionLanguage" ON "ContentPrograms" ("ProgramType", "InstructionLanguage");


CREATE INDEX "IX_ContentPrograms_Status_DisplayOrder" ON "ContentPrograms" ("Status", "DisplayOrder");


CREATE INDEX "IX_ContentPublishRequests_ContentItemId_Status" ON "ContentPublishRequests" ("ContentItemId", "Status");


CREATE INDEX "IX_ContentPublishRequests_RequestedBy" ON "ContentPublishRequests" ("RequestedBy");


CREATE INDEX "IX_ContentPublishRequests_Stage" ON "ContentPublishRequests" ("Stage");


CREATE INDEX "IX_ContentReferences_ModuleId_DisplayOrder" ON "ContentReferences" ("ModuleId", "DisplayOrder");


CREATE INDEX "IX_ContentRevisions_ContentItemId_RevisionNumber" ON "ContentRevisions" ("ContentItemId", "RevisionNumber");


CREATE INDEX "IX_ContentSubmissions_ContributorId_Status" ON "ContentSubmissions" ("ContributorId", "Status");


CREATE INDEX "IX_ContentTracks_ProgramId_DisplayOrder" ON "ContentTracks" ("ProgramId", "DisplayOrder");


CREATE UNIQUE INDEX "IX_ConversationEvaluations_SessionId" ON "ConversationEvaluations" ("SessionId");


CREATE INDEX "IX_ConversationEvaluations_UserId_CreatedAt" ON "ConversationEvaluations" ("UserId", "CreatedAt");


CREATE INDEX "IX_ConversationSessionResumeTokens_ExpiresAt" ON "ConversationSessionResumeTokens" ("ExpiresAt");


CREATE UNIQUE INDEX "IX_ConversationSessionResumeTokens_TokenHash" ON "ConversationSessionResumeTokens" ("TokenHash");


CREATE INDEX "IX_ConversationSessionResumeTokens_UserId_SessionId" ON "ConversationSessionResumeTokens" ("UserId", "SessionId");


CREATE INDEX "IX_ConversationSessions_TemplateId" ON "ConversationSessions" ("TemplateId");


CREATE INDEX "IX_ConversationSessions_UserId_CreatedAt" ON "ConversationSessions" ("UserId", "CreatedAt");


CREATE INDEX "IX_ConversationSessions_UserId_State" ON "ConversationSessions" ("UserId", "State");


CREATE INDEX "IX_ConversationTemplates_Status_Difficulty" ON "ConversationTemplates" ("Status", "Difficulty");


CREATE INDEX "IX_ConversationTemplates_Status_TaskTypeCode_ProfessionId" ON "ConversationTemplates" ("Status", "TaskTypeCode", "ProfessionId");


CREATE INDEX "IX_ConversationTurnAnnotations_EvaluationId" ON "ConversationTurnAnnotations" ("EvaluationId");


CREATE INDEX "IX_ConversationTurnAnnotations_SessionId_TurnNumber" ON "ConversationTurnAnnotations" ("SessionId", "TurnNumber");


CREATE UNIQUE INDEX "IX_ConversationTurns_SessionId_ProviderEventId" ON "ConversationTurns" ("SessionId", "ProviderEventId");


CREATE UNIQUE INDEX "IX_ConversationTurns_SessionId_TurnClientId" ON "ConversationTurns" ("SessionId", "TurnClientId");


CREATE INDEX "IX_ConversationTurns_SessionId_TurnNumber" ON "ConversationTurns" ("SessionId", "TurnNumber");


CREATE INDEX "IX_CrossSellRules_TriggerProductCode" ON "CrossSellRules" ("TriggerProductCode");


CREATE UNIQUE INDEX "IX_CustomerSubscriptions_StripeSubscriptionId" ON "CustomerSubscriptions" ("StripeSubscriptionId");


CREATE INDEX "IX_CustomerSubscriptions_UserId" ON "CustomerSubscriptions" ("UserId");


CREATE INDEX "IX_DeflectionRules_TriggerReason_IsActive" ON "DeflectionRules" ("TriggerReason", "IsActive");


CREATE INDEX "IX_DiagnosticSessions_UserId_State" ON "DiagnosticSessions" ("UserId", "State");


CREATE INDEX "IX_DictationDrills_DrillType_Accent_IsPublished" ON "DictationDrills" ("DrillType", "Accent", "IsPublished");


CREATE UNIQUE INDEX "IX_DunningAttempts_InvoiceId_AttemptNumber" ON "DunningAttempts" ("InvoiceId", "AttemptNumber");


CREATE INDEX "IX_DunningAttempts_Outcome_ScheduledAt" ON "DunningAttempts" ("Outcome", "ScheduledAt");


CREATE INDEX "IX_DunningAttempts_SubscriptionId_InvoiceId" ON "DunningAttempts" ("SubscriptionId", "InvoiceId");


CREATE INDEX "IX_DunningCampaigns_Status_NextAttemptAt" ON "DunningCampaigns" ("Status", "NextAttemptAt");


CREATE INDEX "IX_DunningCampaigns_SubscriptionId_Status" ON "DunningCampaigns" ("SubscriptionId", "Status");


CREATE INDEX "IX_EmailOtpChallenges_ApplicationUserAccountId_Purpose_Expires~" ON "EmailOtpChallenges" ("ApplicationUserAccountId", "Purpose", "ExpiresAt");


CREATE INDEX "IX_EmailOtpChallenges_ExpiresAt" ON "EmailOtpChallenges" ("ExpiresAt");


CREATE INDEX "IX_Evaluations_AttemptId_State" ON "Evaluations" ("AttemptId", "State");


CREATE INDEX "IX_Evaluations_State_LastTransitionAt" ON "Evaluations" ("State", "LastTransitionAt");


CREATE INDEX "IX_ExamBookings_UserId_ExamDate" ON "ExamBookings" ("UserId", "ExamDate");


CREATE INDEX "IX_ExamFamilies_IsActive_SortOrder" ON "ExamFamilies" ("IsActive", "SortOrder");


CREATE INDEX "IX_ExamTypes_Status_SortOrder" ON "ExamTypes" ("Status", "SortOrder");


CREATE INDEX "IX_ExchangeRates_EffectiveFrom" ON "ExchangeRates" ("EffectiveFrom");


CREATE INDEX "IX_ExchangeRates_FromCurrency_ToCurrency_EffectiveFrom" ON "ExchangeRates" ("FromCurrency", "ToCurrency", "EffectiveFrom");


CREATE INDEX "IX_ExpertAnnotationTemplates_CreatedByExpertId" ON "ExpertAnnotationTemplates" ("CreatedByExpertId");


CREATE INDEX "IX_ExpertAvailabilities_ReviewerId" ON "ExpertAvailabilities" ("ReviewerId");


CREATE INDEX "IX_ExpertCalibrationResults_CalibrationCaseId_ReviewerId" ON "ExpertCalibrationResults" ("CalibrationCaseId", "ReviewerId");


CREATE INDEX "IX_ExpertCompensationRates_ExpertId" ON "ExpertCompensationRates" ("ExpertId");


CREATE INDEX "IX_ExpertEarnings_ExpertId" ON "ExpertEarnings" ("ExpertId");


CREATE INDEX "IX_ExpertEarnings_ReviewRequestId" ON "ExpertEarnings" ("ReviewRequestId");


CREATE INDEX "IX_ExpertMessageReplies_ThreadId" ON "ExpertMessageReplies" ("ThreadId");


CREATE INDEX "IX_ExpertMessageThreads_ExpertId" ON "ExpertMessageThreads" ("ExpertId");


CREATE INDEX "IX_ExpertMetricSnapshots_ReviewerId_WindowStart" ON "ExpertMetricSnapshots" ("ReviewerId", "WindowStart");


CREATE INDEX "IX_ExpertPayouts_ExpertId" ON "ExpertPayouts" ("ExpertId");


CREATE INDEX "IX_ExpertReviewAmends_ReviewRequestId" ON "ExpertReviewAmends" ("ReviewRequestId");


CREATE INDEX "IX_ExpertReviewAssignments_AssignedReviewerId" ON "ExpertReviewAssignments" ("AssignedReviewerId");


CREATE INDEX "IX_ExpertReviewAssignments_ReviewRequestId_ClaimState" ON "ExpertReviewAssignments" ("ReviewRequestId", "ClaimState");


CREATE INDEX "IX_ExpertReviewDrafts_ReviewRequestId_ReviewerId" ON "ExpertReviewDrafts" ("ReviewRequestId", "ReviewerId");


CREATE INDEX "IX_ExpertReviewerPayouts_PayPeriodStart_PayPeriodEnd" ON "ExpertReviewerPayouts" ("PayPeriodStart", "PayPeriodEnd");


CREATE INDEX "IX_ExpertReviewerPayouts_ReviewerId" ON "ExpertReviewerPayouts" ("ReviewerId");


CREATE INDEX "IX_ExpertReviewerPayouts_Status" ON "ExpertReviewerPayouts" ("Status");


CREATE INDEX "IX_ExpertSlaSnapshots_ReviewRequestId" ON "ExpertSlaSnapshots" ("ReviewRequestId");


CREATE UNIQUE INDEX "IX_ExpertUsers_AuthAccountId" ON "ExpertUsers" ("AuthAccountId");


CREATE INDEX "IX_ExternalIdentityLinks_ApplicationUserAccountId_Provider" ON "ExternalIdentityLinks" ("ApplicationUserAccountId", "Provider");


CREATE UNIQUE INDEX "IX_ExternalIdentityLinks_Provider_ProviderSubject" ON "ExternalIdentityLinks" ("Provider", "ProviderSubject");


CREATE UNIQUE INDEX "IX_FeatureFlags_Key" ON "FeatureFlags" ("Key");


CREATE INDEX "IX_ForumReplies_ThreadId_CreatedAt" ON "ForumReplies" ("ThreadId", "CreatedAt");


CREATE INDEX "IX_ForumThreads_CategoryId_LastActivityAt" ON "ForumThreads" ("CategoryId", "LastActivityAt");


CREATE INDEX "IX_FoundationResources_ResourceType_Status" ON "FoundationResources" ("ResourceType", "Status");


CREATE INDEX "IX_FreePreviewAssets_Status_DisplayOrder" ON "FreePreviewAssets" ("Status", "DisplayOrder");


CREATE UNIQUE INDEX "IX_GatewayRoutingConfigs_Region_Currency_ProductType_GatewayNa~" ON "GatewayRoutingConfigs" ("Region", "Currency", "ProductType", "GatewayName");


CREATE INDEX "IX_GatewayRoutingConfigs_Region_Currency_ProductType_Priority" ON "GatewayRoutingConfigs" ("Region", "Currency", "ProductType", "Priority");


CREATE INDEX "IX_Goals_UserId" ON "Goals" ("UserId");


CREATE INDEX "IX_GrammarLessons_ExamTypeCode_Category_Status" ON "GrammarLessons" ("ExamTypeCode", "Category", "Status");


CREATE UNIQUE INDEX "IX_IdempotencyRecords_Scope_Key" ON "IdempotencyRecords" ("Scope", "Key");


CREATE UNIQUE INDEX "IX_InterlocutorScripts_RolePlayCardId" ON "InterlocutorScripts" ("RolePlayCardId");


CREATE INDEX "IX_InterlocutorTrainingModules_Stage_Status" ON "InterlocutorTrainingModules" ("Stage", "Status");


CREATE INDEX "IX_InterlocutorTrainingProgress_ModuleId" ON "InterlocutorTrainingProgress" ("ModuleId");


CREATE UNIQUE INDEX "IX_InterlocutorTrainingProgress_TutorId_ModuleId" ON "InterlocutorTrainingProgress" ("TutorId", "ModuleId");


CREATE INDEX "IX_Invoices_CheckoutSessionId" ON "Invoices" ("CheckoutSessionId");


CREATE INDEX "IX_Invoices_PlanVersionId" ON "Invoices" ("PlanVersionId");


CREATE INDEX "IX_Invoices_QuoteId" ON "Invoices" ("QuoteId");


CREATE INDEX "IX_Invoices_UserId_IssuedAt" ON "Invoices" ("UserId", "IssuedAt");


CREATE UNIQUE INDEX "IX_Invoices_UserId_Number" ON "Invoices" ("UserId", "Number") WHERE "Number" IS NOT NULL;


CREATE INDEX "IX_LeaderboardEntries_ExamTypeCode_Period_PeriodStart_Rank" ON "LeaderboardEntries" ("ExamTypeCode", "Period", "PeriodStart", "Rank");


CREATE INDEX "IX_LeaderboardEntries_UserId_Period" ON "LeaderboardEntries" ("UserId", "Period");


CREATE UNIQUE INDEX "IX_LearnerAccentProgresses_UserId_Accent" ON "LearnerAccentProgresses" ("UserId", "Accent");


CREATE INDEX "IX_LearnerAchievements_UserId" ON "LearnerAchievements" ("UserId");


CREATE UNIQUE INDEX "IX_LearnerAchievements_UserId_AchievementId" ON "LearnerAchievements" ("UserId", "AchievementId");


CREATE UNIQUE INDEX "IX_LearnerBadges_UserId_BadgeCode" ON "LearnerBadges" ("UserId", "BadgeCode");


CREATE INDEX "IX_LearnerCertificates_UserId" ON "LearnerCertificates" ("UserId");


CREATE UNIQUE INDEX "IX_LearnerDictationProgresses_UserId_DictationDrillId" ON "LearnerDictationProgresses" ("UserId", "DictationDrillId");


CREATE INDEX "IX_LearnerDictationProgresses_UserId_NextReviewAt" ON "LearnerDictationProgresses" ("UserId", "NextReviewAt");


CREATE INDEX "IX_LearnerEscalations_SubmissionId" ON "LearnerEscalations" ("SubmissionId");


CREATE INDEX "IX_LearnerEscalations_UserId_Status" ON "LearnerEscalations" ("UserId", "Status");


CREATE UNIQUE INDEX "IX_LearnerGrammarProgress_UserId_LessonId" ON "LearnerGrammarProgress" ("UserId", "LessonId");


CREATE UNIQUE INDEX "IX_LearnerLessonProgresses_UserId_LessonId" ON "LearnerLessonProgresses" ("UserId", "LessonId");


CREATE UNIQUE INDEX "IX_LearnerListeningLessonProgresses_UserId_LessonId" ON "LearnerListeningLessonProgresses" ("UserId", "LessonId");


CREATE UNIQUE INDEX "IX_LearnerListeningProfiles_UserId" ON "LearnerListeningProfiles" ("UserId");


CREATE UNIQUE INDEX "IX_LearnerListeningSkillScores_UserId_SkillCode" ON "LearnerListeningSkillScores" ("UserId", "SkillCode");


CREATE UNIQUE INDEX "IX_LearnerListeningStrategyProgresses_UserId_StrategyId" ON "LearnerListeningStrategyProgresses" ("UserId", "StrategyId");


CREATE INDEX "IX_LearnerPronunciationCards_UserId_NextReviewAt" ON "LearnerPronunciationCards" ("UserId", "NextReviewAt");


CREATE UNIQUE INDEX "IX_LearnerPronunciationCards_UserId_PronunciationCardId" ON "LearnerPronunciationCards" ("UserId", "PronunciationCardId");


CREATE INDEX "IX_LearnerPronunciationDiscriminationAttempts_UserId_CreatedAt" ON "LearnerPronunciationDiscriminationAttempts" ("UserId", "CreatedAt");


CREATE INDEX "IX_LearnerPronunciationProgress_UserId_AverageScore" ON "LearnerPronunciationProgress" ("UserId", "AverageScore");


CREATE UNIQUE INDEX "IX_LearnerPronunciationProgress_UserId_PhonemeCode" ON "LearnerPronunciationProgress" ("UserId", "PhonemeCode");


CREATE UNIQUE INDEX "IX_LearnerReadingProfiles_UserId" ON "LearnerReadingProfiles" ("UserId");


CREATE UNIQUE INDEX "IX_LearnerRegistrationProfiles_ApplicationUserAccountId" ON "LearnerRegistrationProfiles" ("ApplicationUserAccountId");


CREATE UNIQUE INDEX "IX_LearnerRegistrationProfiles_LearnerUserId" ON "LearnerRegistrationProfiles" ("LearnerUserId");


CREATE INDEX "IX_LearnerSkillProfiles_UserId_ExamTypeCode_SubtestCode" ON "LearnerSkillProfiles" ("UserId", "ExamTypeCode", "SubtestCode");


CREATE UNIQUE INDEX "IX_LearnerSkillScores_UserId_SkillCode" ON "LearnerSkillScores" ("UserId", "SkillCode");


CREATE UNIQUE INDEX "IX_LearnerStrategyProgress_UserId_StrategyGuideId" ON "LearnerStrategyProgress" ("UserId", "StrategyGuideId");


CREATE UNIQUE INDEX "IX_LearnerVideoProgress_UserId_VideoLessonId" ON "LearnerVideoProgress" ("UserId", "VideoLessonId");


CREATE INDEX "IX_LearnerVocabularies_UserId_NextReviewDate" ON "LearnerVocabularies" ("UserId", "NextReviewDate");


CREATE INDEX "IX_LearnerVocabularies_UserId_Starred" ON "LearnerVocabularies" ("UserId", "Starred");


CREATE UNIQUE INDEX "IX_LearnerVocabularies_UserId_TermId" ON "LearnerVocabularies" ("UserId", "TermId");


CREATE INDEX "IX_LearnerVocabularyItems_UserId_NextReviewAt" ON "LearnerVocabularyItems" ("UserId", "NextReviewAt");


CREATE UNIQUE INDEX "IX_LearnerVocabularyItems_UserId_VocabularyWordId" ON "LearnerVocabularyItems" ("UserId", "VocabularyWordId");


CREATE UNIQUE INDEX "IX_LearnerWritingLessonProgresses_UserId_LessonId" ON "LearnerWritingLessonProgresses" ("UserId", "LessonId");


CREATE UNIQUE INDEX "IX_LearnerWritingPathways_UserId" ON "LearnerWritingPathways" ("UserId");


CREATE UNIQUE INDEX "IX_LearnerWritingProfiles_UserId" ON "LearnerWritingProfiles" ("UserId");


CREATE UNIQUE INDEX "IX_LearnerXps_UserId" ON "LearnerXps" ("UserId");


CREATE INDEX "IX_ListeningAnswers_ListeningQuestionId" ON "ListeningAnswers" ("ListeningQuestionId");


CREATE UNIQUE INDEX "UX_ListeningAnswer_Attempt_Question" ON "ListeningAnswers" ("ListeningAttemptId", "ListeningQuestionId");


CREATE INDEX "IX_ListeningAttemptNotes_ListeningAttemptId" ON "ListeningAttemptNotes" ("ListeningAttemptId");


CREATE INDEX "IX_ListeningAttempts_PaperId_StartedAt" ON "ListeningAttempts" ("PaperId", "StartedAt");


CREATE INDEX "IX_ListeningAttempts_UserId_Status" ON "ListeningAttempts" ("UserId", "Status");


CREATE INDEX "IX_ListeningDailyPlanItems_UserId_PlanDate" ON "ListeningDailyPlanItems" ("UserId", "PlanDate");


CREATE INDEX "IX_ListeningExpertFeedbacks_AttemptId" ON "ListeningExpertFeedbacks" ("AttemptId");


CREATE INDEX "IX_ListeningExtractionDrafts_PaperId_Status" ON "ListeningExtractionDrafts" ("PaperId", "Status");


CREATE INDEX "IX_ListeningExtractionDrafts_ProposedAt" ON "ListeningExtractionDrafts" ("ProposedAt");


CREATE INDEX "IX_ListeningExtracts_ListeningPartId_DisplayOrder" ON "ListeningExtracts" ("ListeningPartId", "DisplayOrder");


CREATE INDEX "IX_ListeningLessons_SkillCode_OrderIndex" ON "ListeningLessons" ("SkillCode", "OrderIndex");


CREATE UNIQUE INDEX "IX_ListeningLessons_Slug" ON "ListeningLessons" ("Slug");


CREATE UNIQUE INDEX "UX_ListeningPart_Paper_PartCode" ON "ListeningParts" ("PaperId", "PartCode");


CREATE UNIQUE INDEX "UX_ListeningPathwayProgress_User_Stage" ON "ListeningPathwayProgress" ("UserId", "StageCode");


CREATE INDEX "IX_ListeningPracticeNotes_UserId_PracticeSessionId_ListeningQu~" ON "ListeningPracticeNotes" ("UserId", "PracticeSessionId", "ListeningQuestionId");


CREATE INDEX "IX_ListeningPracticeSessions_UserId_StartedAt" ON "ListeningPracticeSessions" ("UserId", "StartedAt");


CREATE INDEX "IX_ListeningQuestionAttempts_UserId_AttemptedAt" ON "ListeningQuestionAttempts" ("UserId", "AttemptedAt");


CREATE INDEX "IX_ListeningQuestionAttempts_UserId_InReviewQueue_NextReviewAt" ON "ListeningQuestionAttempts" ("UserId", "InReviewQueue", "NextReviewAt");


CREATE UNIQUE INDEX "UX_ListeningQuestionOption_Question_Key" ON "ListeningQuestionOptions" ("ListeningQuestionId", "OptionKey");


CREATE INDEX "IX_ListeningQuestions_ListeningExtractId" ON "ListeningQuestions" ("ListeningExtractId");


CREATE INDEX "IX_ListeningQuestions_ListeningPartId_DisplayOrder" ON "ListeningQuestions" ("ListeningPartId", "DisplayOrder");


CREATE UNIQUE INDEX "UX_ListeningQuestion_Paper_Number" ON "ListeningQuestions" ("PaperId", "QuestionNumber");


CREATE UNIQUE INDEX "IX_ListeningStrategies_Slug" ON "ListeningStrategies" ("Slug");


CREATE INDEX "IX_ListeningTtsJobs_ExtractId" ON "ListeningTtsJobs" ("ExtractId");


CREATE INDEX "IX_ListeningTtsJobs_Status_CreatedAt" ON "ListeningTtsJobs" ("Status", "CreatedAt");


CREATE INDEX "IX_LiveClassAttendances_ClassSessionId_UserId" ON "LiveClassAttendances" ("ClassSessionId", "UserId");


CREATE INDEX "IX_LiveClassAttendances_ZoomParticipantUuid" ON "LiveClassAttendances" ("ZoomParticipantUuid");


CREATE UNIQUE INDEX "IX_LiveClassEnrollments_ClassSessionId_UserId" ON "LiveClassEnrollments" ("ClassSessionId", "UserId");


CREATE UNIQUE INDEX "IX_LiveClassEnrollments_IdempotencyKey" ON "LiveClassEnrollments" ("IdempotencyKey");


CREATE INDEX "IX_LiveClassEnrollments_UserId_Status" ON "LiveClassEnrollments" ("UserId", "Status");


CREATE UNIQUE INDEX "IX_LiveClasses_Slug" ON "LiveClasses" ("Slug");


CREATE INDEX "IX_LiveClasses_Status_ProfessionTrack_Level" ON "LiveClasses" ("Status", "ProfessionTrack", "Level");


CREATE INDEX "IX_LiveClasses_TutorProfileId" ON "LiveClasses" ("TutorProfileId");


CREATE UNIQUE INDEX "IX_LiveClassRecordings_ClassSessionId" ON "LiveClassRecordings" ("ClassSessionId");


CREATE INDEX "IX_LiveClassRecordings_ZoomRecordingId" ON "LiveClassRecordings" ("ZoomRecordingId");


CREATE INDEX "IX_LiveClassSessions_LiveClassId_ScheduledStartAt" ON "LiveClassSessions" ("LiveClassId", "ScheduledStartAt");


CREATE INDEX "IX_LiveClassSessions_ScheduledStartAt" ON "LiveClassSessions" ("ScheduledStartAt");


CREATE INDEX "IX_LiveClassSessions_Status_ScheduledStartAt" ON "LiveClassSessions" ("Status", "ScheduledStartAt");


CREATE INDEX "IX_LiveClassSessions_ZoomMeetingId" ON "LiveClassSessions" ("ZoomMeetingId");


CREATE INDEX "IX_LiveClassWaitlistEntries_ClassSessionId_Position" ON "LiveClassWaitlistEntries" ("ClassSessionId", "Position");


CREATE UNIQUE INDEX "IX_LiveClassWaitlistEntries_ClassSessionId_UserId" ON "LiveClassWaitlistEntries" ("ClassSessionId", "UserId");


CREATE INDEX "IX_LiveClassWebhookEvents_EventType_ReceivedAt" ON "LiveClassWebhookEvents" ("EventType", "ReceivedAt");


CREATE UNIQUE INDEX "IX_LiveClassWebhookEvents_PayloadHash" ON "LiveClassWebhookEvents" ("PayloadHash");


CREATE INDEX "IX_ManualPaymentRequests_ProofHashHex" ON "ManualPaymentRequests" ("ProofHashHex");


CREATE INDEX "IX_ManualPaymentRequests_Status_SubmittedAt" ON "ManualPaymentRequests" ("Status", "SubmittedAt");


CREATE INDEX "IX_ManualPaymentRequests_UserId_Status" ON "ManualPaymentRequests" ("UserId", "Status");


CREATE INDEX "IX_MarketingAssets_AssetType_Status" ON "MarketingAssets" ("AssetType", "Status");


CREATE INDEX "IX_MediaAssets_Status" ON "MediaAssets" ("Status");


CREATE INDEX "IX_MfaRecoveryCodes_ApplicationUserAccountId" ON "MfaRecoveryCodes" ("ApplicationUserAccountId");


CREATE INDEX "IX_MobilePushTokens_AuthAccountId_Platform" ON "MobilePushTokens" ("AuthAccountId", "Platform");


CREATE UNIQUE INDEX "IX_MobilePushTokens_Token" ON "MobilePushTokens" ("Token");


CREATE INDEX "IX_MockAttempts_MockBundleId" ON "MockAttempts" ("MockBundleId");


CREATE INDEX "IX_MockAttempts_UserId_State" ON "MockAttempts" ("UserId", "State");


CREATE INDEX "IX_MockBookings_AssignedTutorId_ScheduledStartAt" ON "MockBookings" ("AssignedTutorId", "ScheduledStartAt");


CREATE INDEX "IX_MockBookings_MockAttemptId" ON "MockBookings" ("MockAttemptId");


CREATE INDEX "IX_MockBookings_MockBundleId" ON "MockBookings" ("MockBundleId");


CREATE INDEX "IX_MockBookings_Status_ScheduledStartAt" ON "MockBookings" ("Status", "ScheduledStartAt");


CREATE INDEX "IX_MockBookings_UserId_ScheduledStartAt" ON "MockBookings" ("UserId", "ScheduledStartAt");


CREATE INDEX "IX_MockBundles_ProfessionId_MockType" ON "MockBundles" ("ProfessionId", "MockType");


CREATE UNIQUE INDEX "IX_MockBundles_Slug" ON "MockBundles" ("Slug");


CREATE INDEX "IX_MockBundles_Status_MockType" ON "MockBundles" ("Status", "MockType");


CREATE INDEX "IX_MockBundleSections_ContentPaperId" ON "MockBundleSections" ("ContentPaperId");


CREATE UNIQUE INDEX "IX_MockBundleSections_MockBundleId_SectionOrder" ON "MockBundleSections" ("MockBundleId", "SectionOrder");


CREATE INDEX "IX_MockBundleSections_MockBundleId_SubtestCode" ON "MockBundleSections" ("MockBundleId", "SubtestCode");


CREATE INDEX "IX_MockContentReviews_MockAttemptId" ON "MockContentReviews" ("MockAttemptId");


CREATE INDEX "IX_MockContentReviews_MockBundleId" ON "MockContentReviews" ("MockBundleId");


CREATE INDEX "IX_MockContentReviews_ReportedByUserId_CreatedAt" ON "MockContentReviews" ("ReportedByUserId", "CreatedAt");


CREATE INDEX "IX_MockContentReviews_Status_Severity" ON "MockContentReviews" ("Status", "Severity");


CREATE INDEX "IX_MockEntitlementLedgers_AddOnId" ON "MockEntitlementLedgers" ("AddOnId");


CREATE INDEX "IX_MockEntitlementLedgers_UserId_ConsumedAt" ON "MockEntitlementLedgers" ("UserId", "ConsumedAt");


CREATE INDEX "IX_MockEntitlementLedgers_UserId_MockType" ON "MockEntitlementLedgers" ("UserId", "MockType");


CREATE INDEX "IX_MockItemAnalysisSnapshots_ContentPaperId" ON "MockItemAnalysisSnapshots" ("ContentPaperId");


CREATE INDEX "IX_MockItemAnalysisSnapshots_MockBundleId" ON "MockItemAnalysisSnapshots" ("MockBundleId");


CREATE INDEX "IX_MockItemAnalysisSnapshots_MockBundleId_SubtestCode" ON "MockItemAnalysisSnapshots" ("MockBundleId", "SubtestCode");


CREATE UNIQUE INDEX "UX_MockItemAnalysis_Bundle_Item" ON "MockItemAnalysisSnapshots" ("MockBundleId", "ItemId");


CREATE UNIQUE INDEX "IX_MockLiveRoomTransitions_BookingId_ClientTransitionId" ON "MockLiveRoomTransitions" ("BookingId", "ClientTransitionId") WHERE "ClientTransitionId" IS NOT NULL;


CREATE INDEX "IX_MockLiveRoomTransitions_BookingId_OccurredAt" ON "MockLiveRoomTransitions" ("BookingId", "OccurredAt");


CREATE UNIQUE INDEX "IX_MockLiveRoomTransitions_BookingId_TransitionVersion" ON "MockLiveRoomTransitions" ("BookingId", "TransitionVersion");


CREATE INDEX "IX_MockProctoringEvents_Kind" ON "MockProctoringEvents" ("Kind");


CREATE INDEX "IX_MockProctoringEvents_MockAttemptId_OccurredAt" ON "MockProctoringEvents" ("MockAttemptId", "OccurredAt");


CREATE INDEX "IX_MockProctoringEvents_MockSectionAttemptId" ON "MockProctoringEvents" ("MockSectionAttemptId");


CREATE UNIQUE INDEX "IX_MockReviewReservations_MockAttemptId" ON "MockReviewReservations" ("MockAttemptId");


CREATE INDEX "IX_MockReviewReservations_UserId_State" ON "MockReviewReservations" ("UserId", "State");


CREATE INDEX "IX_MockSectionAttempts_ContentAttemptId" ON "MockSectionAttempts" ("ContentAttemptId");


CREATE UNIQUE INDEX "IX_MockSectionAttempts_MockAttemptId_MockBundleSectionId" ON "MockSectionAttempts" ("MockAttemptId", "MockBundleSectionId");


CREATE INDEX "IX_MockSectionAttempts_MockAttemptId_SubtestCode" ON "MockSectionAttempts" ("MockAttemptId", "SubtestCode");


CREATE INDEX "IX_MockSectionAttempts_MockBundleSectionId" ON "MockSectionAttempts" ("MockBundleSectionId");


CREATE INDEX "IX_NativeIapProductMappings_Platform_IsActive" ON "NativeIapProductMappings" ("Platform", "IsActive");


CREATE UNIQUE INDEX "IX_NativeIapProductMappings_Platform_StoreProductId" ON "NativeIapProductMappings" ("Platform", "StoreProductId") WHERE "IsActive" = TRUE;


CREATE INDEX "IX_NativeIapProductMappings_TargetType_TargetId" ON "NativeIapProductMappings" ("TargetType", "TargetId");


CREATE INDEX "IX_NotificationCampaignRecipients_CampaignId" ON "NotificationCampaignRecipients" ("CampaignId");


CREATE INDEX "IX_NotificationCampaignRecipients_RecipientUserId" ON "NotificationCampaignRecipients" ("RecipientUserId");


CREATE INDEX "IX_NotificationCampaigns_Status" ON "NotificationCampaigns" ("Status");


CREATE UNIQUE INDEX "IX_NotificationConsents_AuthAccountId_Channel_Category" ON "NotificationConsents" ("AuthAccountId", "Channel", "Category");


CREATE INDEX "IX_NotificationConsents_Channel_IsGranted_UpdatedAt" ON "NotificationConsents" ("Channel", "IsGranted", "UpdatedAt");


CREATE INDEX "IX_NotificationDeliveryAttempts_AuthAccountId_Channel_Status_A~" ON "NotificationDeliveryAttempts" ("AuthAccountId", "Channel", "Status", "AttemptedAt");


CREATE INDEX "IX_NotificationDeliveryAttempts_NotificationEventId_Channel_At~" ON "NotificationDeliveryAttempts" ("NotificationEventId", "Channel", "AttemptedAt");


CREATE INDEX "IX_NotificationDeliveryAttempts_Status_AttemptedAt" ON "NotificationDeliveryAttempts" ("Status", "AttemptedAt");


CREATE UNIQUE INDEX "IX_NotificationEvents_DedupeKey" ON "NotificationEvents" ("DedupeKey");


CREATE INDEX "IX_NotificationEvents_RecipientAuthAccountId_CreatedAt" ON "NotificationEvents" ("RecipientAuthAccountId", "CreatedAt");


CREATE INDEX "IX_NotificationEvents_State_CreatedAt" ON "NotificationEvents" ("State", "CreatedAt");


CREATE INDEX "IX_NotificationInboxItems_AuthAccountId_IsRead_CreatedAt" ON "NotificationInboxItems" ("AuthAccountId", "IsRead", "CreatedAt");


CREATE UNIQUE INDEX "IX_NotificationInboxItems_NotificationEventId" ON "NotificationInboxItems" ("NotificationEventId");


CREATE UNIQUE INDEX "IX_NotificationPolicyOverrides_AudienceRole_EventKey" ON "NotificationPolicyOverrides" ("AudienceRole", "EventKey");


CREATE UNIQUE INDEX "IX_NotificationPreferences_AuthAccountId" ON "NotificationPreferences" ("AuthAccountId");


CREATE INDEX "IX_NotificationSuppressions_AuthAccountId_Channel_EventKey_IsA~" ON "NotificationSuppressions" ("AuthAccountId", "Channel", "EventKey", "IsActive");


CREATE INDEX "IX_NotificationSuppressions_Channel_IsActive_ExpiresAt" ON "NotificationSuppressions" ("Channel", "IsActive", "ExpiresAt");


CREATE UNIQUE INDEX "IX_OrderRefunds_Gateway_GatewayRefundId" ON "OrderRefunds" ("Gateway", "GatewayRefundId");


CREATE UNIQUE INDEX "IX_OrderRefunds_IdempotencyKey" ON "OrderRefunds" ("IdempotencyKey");


CREATE INDEX "IX_OrderRefunds_PaymentTransactionId" ON "OrderRefunds" ("PaymentTransactionId");


CREATE INDEX "IX_PackageContentRules_PackageId_RuleType" ON "PackageContentRules" ("PackageId", "RuleType");


CREATE UNIQUE INDEX "IX_PaymentDisputes_Gateway_GatewayDisputeId" ON "PaymentDisputes" ("Gateway", "GatewayDisputeId");


CREATE INDEX "IX_PaymentDisputes_PaymentTransactionId" ON "PaymentDisputes" ("PaymentTransactionId");


CREATE INDEX "IX_PaymentMethodUpdateLinks_UserId_ExpiresAt" ON "PaymentMethodUpdateLinks" ("UserId", "ExpiresAt");


CREATE INDEX "IX_PaymentTransactions_CouponVersionId" ON "PaymentTransactions" ("CouponVersionId");


CREATE UNIQUE INDEX "IX_PaymentTransactions_GatewayTransactionId" ON "PaymentTransactions" ("GatewayTransactionId");


CREATE INDEX "IX_PaymentTransactions_LearnerUserId" ON "PaymentTransactions" ("LearnerUserId");


CREATE INDEX "IX_PaymentTransactions_LearnerUserId_CreatedAt" ON "PaymentTransactions" ("LearnerUserId", "CreatedAt");


CREATE INDEX "IX_PaymentTransactions_PlanVersionId" ON "PaymentTransactions" ("PlanVersionId");


CREATE INDEX "IX_PaymentTransactions_QuoteId" ON "PaymentTransactions" ("QuoteId");


CREATE INDEX "IX_PaymentTransactions_Status_CreatedAt" ON "PaymentTransactions" ("Status", "CreatedAt");


CREATE UNIQUE INDEX "IX_PaymentWebhookEvents_GatewayEventId" ON "PaymentWebhookEvents" ("GatewayEventId");


CREATE INDEX "IX_PaymentWebhookEvents_ProcessingStatus_ReceivedAt" ON "PaymentWebhookEvents" ("ProcessingStatus", "ReceivedAt");


CREATE INDEX "IX_PaymentWebhookEvents_VerificationStatus_ProcessingStatus" ON "PaymentWebhookEvents" ("VerificationStatus", "ProcessingStatus");


CREATE INDEX "IX_PeerReviewFeedbacks_PeerReviewRequestId" ON "PeerReviewFeedbacks" ("PeerReviewRequestId");


CREATE INDEX "IX_PeerReviewRequests_Status_CreatedAt" ON "PeerReviewRequests" ("Status", "CreatedAt");


CREATE INDEX "IX_PeerReviewRequests_SubmitterUserId" ON "PeerReviewRequests" ("SubmitterUserId");


CREATE UNIQUE INDEX "IX_PermissionTemplates_Name" ON "PermissionTemplates" ("Name");


CREATE INDEX "IX_PredictionSnapshots_UserId_ExamTypeCode_SubtestCode_Compute~" ON "PredictionSnapshots" ("UserId", "ExamTypeCode", "SubtestCode", "ComputedAt");


CREATE UNIQUE INDEX "IX_PricingExperimentAssignments_ExperimentId_UserId" ON "PricingExperimentAssignments" ("ExperimentId", "UserId");


CREATE INDEX "IX_PricingExperimentAssignments_ExperimentId_VariantCode" ON "PricingExperimentAssignments" ("ExperimentId", "VariantCode");


CREATE INDEX "IX_PricingExperiments_Status" ON "PricingExperiments" ("Status");


CREATE INDEX "IX_PricingExperiments_TargetType_TargetId" ON "PricingExperiments" ("TargetType", "TargetId");


CREATE INDEX "IX_PrivateSpeakingAuditLogs_ActorId_CreatedAt" ON "PrivateSpeakingAuditLogs" ("ActorId", "CreatedAt");


CREATE INDEX "IX_PrivateSpeakingAuditLogs_BookingId" ON "PrivateSpeakingAuditLogs" ("BookingId");


CREATE INDEX "IX_PrivateSpeakingAuditLogs_CreatedAt" ON "PrivateSpeakingAuditLogs" ("CreatedAt");


CREATE INDEX "IX_PrivateSpeakingAvailabilityOverrides_TutorProfileId_Date" ON "PrivateSpeakingAvailabilityOverrides" ("TutorProfileId", "Date");


CREATE INDEX "IX_PrivateSpeakingAvailabilityRules_TutorProfileId_DayOfWeek" ON "PrivateSpeakingAvailabilityRules" ("TutorProfileId", "DayOfWeek");


CREATE INDEX "IX_PrivateSpeakingBookings_EntitlementSubscriptionId" ON "PrivateSpeakingBookings" ("EntitlementSubscriptionId");


CREATE INDEX "IX_PrivateSpeakingBookings_GoogleCalendarEventId" ON "PrivateSpeakingBookings" ("GoogleCalendarEventId");


CREATE UNIQUE INDEX "IX_PrivateSpeakingBookings_IdempotencyKey" ON "PrivateSpeakingBookings" ("IdempotencyKey");


CREATE INDEX "IX_PrivateSpeakingBookings_LearnerUserId_SessionStartUtc" ON "PrivateSpeakingBookings" ("LearnerUserId", "SessionStartUtc");


CREATE INDEX "IX_PrivateSpeakingBookings_LearnerUserId_Status" ON "PrivateSpeakingBookings" ("LearnerUserId", "Status");


CREATE INDEX "IX_PrivateSpeakingBookings_Status" ON "PrivateSpeakingBookings" ("Status");


CREATE INDEX "IX_PrivateSpeakingBookings_StripeCheckoutSessionId" ON "PrivateSpeakingBookings" ("StripeCheckoutSessionId");


CREATE INDEX "IX_PrivateSpeakingBookings_TutorProfileId_SessionStartUtc" ON "PrivateSpeakingBookings" ("TutorProfileId", "SessionStartUtc");


CREATE INDEX "IX_PrivateSpeakingTutorCalendarConnections_ExpertUserId" ON "PrivateSpeakingTutorCalendarConnections" ("ExpertUserId");


CREATE UNIQUE INDEX "IX_PrivateSpeakingTutorCalendarConnections_TutorProfileId" ON "PrivateSpeakingTutorCalendarConnections" ("TutorProfileId");


CREATE UNIQUE INDEX "IX_PrivateSpeakingTutorProfiles_ExpertUserId" ON "PrivateSpeakingTutorProfiles" ("ExpertUserId");


CREATE INDEX "IX_PronunciationAssessments_DrillId" ON "PronunciationAssessments" ("DrillId");


CREATE INDEX "IX_PronunciationAssessments_UserId_CreatedAt" ON "PronunciationAssessments" ("UserId", "CreatedAt");


CREATE INDEX "IX_PronunciationAttempts_DrillId_CreatedAt" ON "PronunciationAttempts" ("DrillId", "CreatedAt");


CREATE INDEX "IX_PronunciationAttempts_Status" ON "PronunciationAttempts" ("Status");


CREATE INDEX "IX_PronunciationAttempts_UserId_CreatedAt" ON "PronunciationAttempts" ("UserId", "CreatedAt");


CREATE INDEX "IX_PronunciationAttempts_UserId_DrillId_CreatedAt" ON "PronunciationAttempts" ("UserId", "DrillId", "CreatedAt");


CREATE UNIQUE INDEX "IX_PronunciationCards_Word" ON "PronunciationCards" ("Word");


CREATE INDEX "IX_PushSubscriptions_AuthAccountId_IsActive" ON "PushSubscriptions" ("AuthAccountId", "IsActive");


CREATE UNIQUE INDEX "IX_PushSubscriptions_Endpoint" ON "PushSubscriptions" ("Endpoint");


CREATE INDEX "IX_ReadinessHistories_RecordedAt" ON "ReadinessHistories" ("RecordedAt");


CREATE UNIQUE INDEX "IX_ReadinessHistories_UserId_WeekStartDate" ON "ReadinessHistories" ("UserId", "WeekStartDate");


CREATE INDEX "IX_ReadinessSnapshots_ExpiresAt" ON "ReadinessSnapshots" ("ExpiresAt");


CREATE INDEX "IX_ReadinessSnapshots_UserId" ON "ReadinessSnapshots" ("UserId");


CREATE INDEX "IX_ReadinessSnapshots_UserId_ComputedAt" ON "ReadinessSnapshots" ("UserId", "ComputedAt");


CREATE INDEX "IX_ReadingAnswerRevisions_ReadingAttemptId_ReadingQuestionId" ON "ReadingAnswerRevisions" ("ReadingAttemptId", "ReadingQuestionId");


CREATE INDEX "IX_ReadingAnswer_AttemptId" ON "ReadingAnswers" ("ReadingAttemptId");


CREATE INDEX "IX_ReadingAnswers_ReadingQuestionId" ON "ReadingAnswers" ("ReadingQuestionId");


CREATE UNIQUE INDEX "UX_ReadingAnswer_Attempt_Question" ON "ReadingAnswers" ("ReadingAttemptId", "ReadingQuestionId");


CREATE INDEX "IX_ReadingAssignments_AssignedByUserId" ON "ReadingAssignments" ("AssignedByUserId");


CREATE INDEX "IX_ReadingAssignments_AssignedToUserId" ON "ReadingAssignments" ("AssignedToUserId");


CREATE INDEX "IX_ReadingAttemptFeedbacks_ReadingAttemptId" ON "ReadingAttemptFeedbacks" ("ReadingAttemptId");


CREATE INDEX "IX_ReadingAttempt_User_Paper_Mode_Status" ON "ReadingAttempts" ("UserId", "PaperId", "Mode", "Status");


CREATE INDEX "IX_ReadingAttempts_PaperId_StartedAt" ON "ReadingAttempts" ("PaperId", "StartedAt");


CREATE INDEX "IX_ReadingAttempts_UserId_PaperId" ON "ReadingAttempts" ("UserId", "PaperId");


CREATE INDEX "IX_ReadingAttempts_UserId_Status" ON "ReadingAttempts" ("UserId", "Status");


CREATE UNIQUE INDEX "UX_ReadingAttempt_UserPaperExam_InProgress" ON "ReadingAttempts" ("UserId", "PaperId", "Mode", "Status") WHERE "Mode" = 0 AND "Status" = 0;


CREATE INDEX "IX_ReadingDailyPlanItems_UserId_PlanDate" ON "ReadingDailyPlanItems" ("UserId", "PlanDate");


CREATE INDEX "IX_ReadingErrorBankEntries_UserId_IsResolved" ON "ReadingErrorBankEntries" ("UserId", "IsResolved");


CREATE INDEX "IX_ReadingErrorBankEntries_UserId_LastSeenWrongAt" ON "ReadingErrorBankEntries" ("UserId", "LastSeenWrongAt");


CREATE UNIQUE INDEX "UX_ReadingErrorBankEntry_User_Question" ON "ReadingErrorBankEntries" ("ReadingQuestionId", "UserId");


CREATE INDEX "IX_ReadingExtractionDrafts_CreatedAt" ON "ReadingExtractionDrafts" ("CreatedAt");


CREATE INDEX "IX_ReadingExtractionDrafts_PaperId_Status" ON "ReadingExtractionDrafts" ("PaperId", "Status");


CREATE UNIQUE INDEX "UX_ReadingPart_Paper_PartCode" ON "ReadingParts" ("PaperId", "PartCode");


CREATE INDEX "IX_ReadingQuestionAttempts_UserId_AttemptedAt" ON "ReadingQuestionAttempts" ("UserId", "AttemptedAt");


CREATE INDEX "IX_ReadingQuestionAttempts_UserId_InReviewQueue_NextReviewAt" ON "ReadingQuestionAttempts" ("UserId", "InReviewQueue", "NextReviewAt");


CREATE INDEX "IX_ReadingQuestionDiscussionComments_ReadingQuestionId" ON "ReadingQuestionDiscussionComments" ("ReadingQuestionId");


CREATE INDEX "IX_ReadingQuestionReviewLogs_ReadingQuestionId_TransitionedAt" ON "ReadingQuestionReviewLogs" ("ReadingQuestionId", "TransitionedAt");


CREATE INDEX "IX_ReadingQuestions_ReadingPartId_DisplayOrder" ON "ReadingQuestions" ("ReadingPartId", "DisplayOrder");


CREATE INDEX "IX_ReadingQuestions_ReadingTextId" ON "ReadingQuestions" ("ReadingTextId");


CREATE UNIQUE INDEX "IX_ReadingStrategyProgresses_UserId_StrategyId" ON "ReadingStrategyProgresses" ("UserId", "StrategyId");


CREATE INDEX "IX_ReadingTexts_ReadingPartId_DisplayOrder" ON "ReadingTexts" ("ReadingPartId", "DisplayOrder");


CREATE INDEX "IX_RecallBookmarks_UserId_CreatedAt" ON "RecallBookmarks" ("UserId", "CreatedAt");


CREATE UNIQUE INDEX "IX_RecallBookmarks_UserId_VocabularyTermId" ON "RecallBookmarks" ("UserId", "VocabularyTermId");


CREATE UNIQUE INDEX "IX_RecallSetTags_Code" ON "RecallSetTags" ("Code");


CREATE INDEX "IX_RecallSetTags_IsActive_SortOrder" ON "RecallSetTags" ("IsActive", "SortOrder");


CREATE UNIQUE INDEX "IX_ReferralCodes_Code" ON "ReferralCodes" ("Code");


CREATE UNIQUE INDEX "IX_ReferralCodes_UserId" ON "ReferralCodes" ("UserId");


CREATE UNIQUE INDEX "IX_ReferralRecords_ReferralCode" ON "ReferralRecords" ("ReferralCode");


CREATE INDEX "IX_ReferralRecords_ReferrerUserId" ON "ReferralRecords" ("ReferrerUserId");


CREATE INDEX "IX_Referrals_ReferrerUserId" ON "Referrals" ("ReferrerUserId");


CREATE INDEX "IX_RefreshTokenRecord_Active" ON "RefreshTokenRecords" ("ApplicationUserAccountId") WHERE "RevokedAt" IS NULL;


CREATE INDEX "IX_RefreshTokenRecords_ApplicationUserAccountId_ExpiresAt" ON "RefreshTokenRecords" ("ApplicationUserAccountId", "ExpiresAt");


CREATE UNIQUE INDEX "IX_RefreshTokenRecords_ApplicationUserAccountId_TokenHash" ON "RefreshTokenRecords" ("ApplicationUserAccountId", "TokenHash");


CREATE INDEX "IX_RegionPricings_Region_IsActive" ON "RegionPricings" ("Region", "IsActive");


CREATE UNIQUE INDEX "IX_RegionPricings_TargetType_TargetId_Region" ON "RegionPricings" ("TargetType", "TargetId", "Region");


CREATE INDEX "IX_RemediationTasks_MockReportId" ON "RemediationTasks" ("MockReportId");


CREATE INDEX "IX_RemediationTasks_UserId_Status" ON "RemediationTasks" ("UserId", "Status");


CREATE INDEX "IX_ResultTemplateAssets_IsActive_SortOrder" ON "ResultTemplateAssets" ("IsActive", "SortOrder");


CREATE INDEX "IX_ResultTemplateAssets_MediaAssetId" ON "ResultTemplateAssets" ("MediaAssetId");


CREATE INDEX "IX_ResultTemplateAssets_ProfessionId_IsActive" ON "ResultTemplateAssets" ("ProfessionId", "IsActive");


CREATE UNIQUE INDEX "IX_ResultTemplateAssets_TemplateKey" ON "ResultTemplateAssets" ("TemplateKey");


CREATE INDEX "IX_ReviewEscalations_ReviewRequestId_Status" ON "ReviewEscalations" ("ReviewRequestId", "Status");


CREATE INDEX "IX_ReviewEscalations_SecondReviewerId" ON "ReviewEscalations" ("SecondReviewerId");


CREATE INDEX "IX_ReviewItems_UserId_DueDate_Status" ON "ReviewItems" ("UserId", "DueDate", "Status");


CREATE INDEX "IX_ReviewItems_UserId_ExamTypeCode_Status" ON "ReviewItems" ("UserId", "ExamTypeCode", "Status");


CREATE INDEX "IX_ReviewRequests_AttemptId_State" ON "ReviewRequests" ("AttemptId", "State");


CREATE INDEX "IX_ReviewRequests_State_CreatedAt" ON "ReviewRequests" ("State", "CreatedAt");


CREATE INDEX "IX_ReviewVoiceNotes_MediaAssetId" ON "ReviewVoiceNotes" ("MediaAssetId");


CREATE INDEX "IX_ReviewVoiceNotes_ReviewRequestId_CreatedAt" ON "ReviewVoiceNotes" ("ReviewRequestId", "CreatedAt");


CREATE UNIQUE INDEX "IX_RolePlayCards_ContentItemId" ON "RolePlayCards" ("ContentItemId");


CREATE INDEX "IX_RolePlayCards_ProfessionId_Status" ON "RolePlayCards" ("ProfessionId", "Status");


CREATE UNIQUE INDEX "IX_RulebookRuleRows_RulebookVersionId_Code" ON "RulebookRuleRows" ("RulebookVersionId", "Code");


CREATE INDEX "IX_RulebookRuleRows_RulebookVersionId_SectionCode" ON "RulebookRuleRows" ("RulebookVersionId", "SectionCode");


CREATE UNIQUE INDEX "IX_RulebookSectionRows_RulebookVersionId_Code" ON "RulebookSectionRows" ("RulebookVersionId", "Code");


CREATE INDEX "IX_RulebookVersions_Kind_Profession_Status" ON "RulebookVersions" ("Kind", "Profession", "Status");


CREATE INDEX "IX_RulebookVersions_ReferencePdfAssetId" ON "RulebookVersions" ("ReferencePdfAssetId");


CREATE INDEX "IX_ScheduleExceptions_ReviewerId" ON "ScheduleExceptions" ("ReviewerId");


CREATE INDEX "IX_Scholarships_Status_ExpiresAt" ON "Scholarships" ("Status", "ExpiresAt");


CREATE INDEX "IX_Scholarships_UserId_Status" ON "Scholarships" ("UserId", "Status");


CREATE INDEX "IX_ScoreGuaranteePledges_UserId" ON "ScoreGuaranteePledges" ("UserId");


CREATE UNIQUE INDEX "IX_ScoringPolicies_IsActive" ON "ScoringPolicies" ("IsActive") WHERE "IsActive" = TRUE;


CREATE INDEX "IX_Settings_UserId" ON "Settings" ("UserId");


CREATE INDEX "IX_SignupExamTypeCatalog_IsActive_SortOrder" ON "SignupExamTypeCatalog" ("IsActive", "SortOrder");


CREATE INDEX "IX_SignupProfessionCatalog_IsActive_SortOrder" ON "SignupProfessionCatalog" ("IsActive", "SortOrder");


CREATE INDEX "IX_SignupSessionCatalog_IsActive_SortOrder" ON "SignupSessionCatalog" ("IsActive", "SortOrder");


CREATE INDEX "IX_SpeakingAiAssessments_SpeakingSessionId" ON "SpeakingAiAssessments" ("SpeakingSessionId");


CREATE INDEX "IX_SpeakingCalibrationSamples_Status_PublishedAt" ON "SpeakingCalibrationSamples" ("Status", "PublishedAt");


CREATE UNIQUE INDEX "IX_SpeakingCalibrationScores_SampleId_TutorId" ON "SpeakingCalibrationScores" ("SampleId", "TutorId");


CREATE INDEX "IX_SpeakingCalibrationScores_TutorId" ON "SpeakingCalibrationScores" ("TutorId");


CREATE UNIQUE INDEX "IX_SpeakingCardBatchRequests_IdempotencyKey" ON "SpeakingCardBatchRequests" ("IdempotencyKey");


CREATE INDEX "IX_SpeakingCardBatchRequests_Status_CreatedAt" ON "SpeakingCardBatchRequests" ("Status", "CreatedAt");


CREATE INDEX "IX_SpeakingComplianceConsents_ConsentType_ConsentVersion" ON "SpeakingComplianceConsents" ("ConsentType", "ConsentVersion");


CREATE INDEX "IX_SpeakingComplianceConsents_UserId_ConsentType" ON "SpeakingComplianceConsents" ("UserId", "ConsentType");


CREATE INDEX "IX_SpeakingDrillAttempts_DrillItemId" ON "SpeakingDrillAttempts" ("DrillItemId");


CREATE INDEX "IX_SpeakingDrillAttempts_UserId_DrillItemId" ON "SpeakingDrillAttempts" ("UserId", "DrillItemId");


CREATE INDEX "IX_SpeakingDrillItems_ContentItemId" ON "SpeakingDrillItems" ("ContentItemId");


CREATE INDEX "IX_SpeakingDrillItems_DrillKind" ON "SpeakingDrillItems" ("DrillKind");


CREATE INDEX "IX_SpeakingFeedbackComments_AttemptId_TranscriptLineIndex" ON "SpeakingFeedbackComments" ("AttemptId", "TranscriptLineIndex");


CREATE UNIQUE INDEX "IX_SpeakingLiveRooms_RoomName" ON "SpeakingLiveRooms" ("RoomName");


CREATE UNIQUE INDEX "IX_SpeakingLiveRooms_SpeakingSessionId" ON "SpeakingLiveRooms" ("SpeakingSessionId");


CREATE INDEX "IX_SpeakingLiveRoomTokens_LiveRoomId" ON "SpeakingLiveRoomTokens" ("LiveRoomId");


CREATE INDEX "IX_SpeakingMockSessions_MockSetId" ON "SpeakingMockSessions" ("MockSetId");


CREATE INDEX "IX_SpeakingMockSessions_UserId_StartedAt" ON "SpeakingMockSessions" ("UserId", "StartedAt");


CREATE INDEX "IX_SpeakingMockSets_Status_SortOrder" ON "SpeakingMockSets" ("Status", "SortOrder");


CREATE UNIQUE INDEX "IX_SpeakingModerationCases_SpeakingSessionId" ON "SpeakingModerationCases" ("SpeakingSessionId");


CREATE INDEX "IX_SpeakingModerationCases_Status" ON "SpeakingModerationCases" ("Status");


CREATE INDEX "IX_SpeakingRecordings_MediaAssetId" ON "SpeakingRecordings" ("MediaAssetId");


CREATE INDEX "IX_SpeakingRecordings_Sha256" ON "SpeakingRecordings" ("Sha256");


CREATE INDEX "IX_SpeakingRecordings_SpeakingSessionId" ON "SpeakingRecordings" ("SpeakingSessionId");


CREATE INDEX "IX_SpeakingResultVisibilityConfigs_RolePlayCardId" ON "SpeakingResultVisibilityConfigs" ("RolePlayCardId");


CREATE INDEX "IX_SpeakingReviewVoiceNotes_ReviewRequestId_CreatedAt" ON "SpeakingReviewVoiceNotes" ("ReviewRequestId", "CreatedAt");


CREATE INDEX "IX_SpeakingSessions_MockSessionId" ON "SpeakingSessions" ("MockSessionId");


CREATE INDEX "IX_SpeakingSessions_RolePlayCardId_State" ON "SpeakingSessions" ("RolePlayCardId", "State");


CREATE INDEX "IX_SpeakingSessions_UserId_State" ON "SpeakingSessions" ("UserId", "State");


CREATE INDEX "IX_SpeakingSharedResources_Kind_Status" ON "SpeakingSharedResources" ("Kind", "Status");


CREATE INDEX "IX_SpeakingSharedResources_MediaAssetId" ON "SpeakingSharedResources" ("MediaAssetId");


CREATE INDEX "IX_SpeakingSharedResources_ProfessionId_Kind" ON "SpeakingSharedResources" ("ProfessionId", "Kind");


CREATE INDEX "IX_SpeakingTimestampedComments_SpeakingSessionId_StartMs" ON "SpeakingTimestampedComments" ("SpeakingSessionId", "StartMs");


CREATE INDEX "IX_SpeakingTranscripts_SpeakingSessionId_IsLatest" ON "SpeakingTranscripts" ("SpeakingSessionId", "IsLatest");


CREATE INDEX "IX_SpeakingTutorAssessments_SpeakingSessionId_IsFinal" ON "SpeakingTutorAssessments" ("SpeakingSessionId", "IsFinal");


CREATE INDEX "IX_SponsorBillingEvents_SponsorId" ON "SponsorBillingEvents" ("SponsorId");


CREATE UNIQUE INDEX "IX_SponsorLearnerLinks_SponsorId_LearnerId" ON "SponsorLearnerLinks" ("SponsorId", "LearnerId");


CREATE INDEX "IX_SponsorSeatAssignments_LearnerId" ON "SponsorSeatAssignments" ("LearnerId");


CREATE INDEX "IX_SponsorSeatAssignments_SeatPackId" ON "SponsorSeatAssignments" ("SeatPackId");


CREATE INDEX "IX_SponsorSeatPacks_SponsorId" ON "SponsorSeatPacks" ("SponsorId");


CREATE INDEX "IX_StrategyGuides_ContentLessonId" ON "StrategyGuides" ("ContentLessonId");


CREATE INDEX "IX_StrategyGuides_ExamTypeCode_Category_Status" ON "StrategyGuides" ("ExamTypeCode", "Category", "Status");


CREATE UNIQUE INDEX "IX_StrategyGuides_Slug" ON "StrategyGuides" ("Slug");


CREATE UNIQUE INDEX "IX_StreakRecords_UserId_Date" ON "StreakRecords" ("UserId", "Date");


CREATE INDEX "IX_StudyCommitments_UserId" ON "StudyCommitments" ("UserId");


CREATE UNIQUE INDEX "IX_StudyGroupMembers_GroupId_UserId" ON "StudyGroupMembers" ("GroupId", "UserId");


CREATE INDEX "IX_StudyGroupMembers_UserId" ON "StudyGroupMembers" ("UserId");


CREATE INDEX "IX_StudyPlanItems_LinkedReviewItemId" ON "StudyPlanItems" ("LinkedReviewItemId");


CREATE INDEX "IX_StudyPlanItems_ReplacedById" ON "StudyPlanItems" ("ReplacedById");


CREATE INDEX "IX_StudyPlanItems_StudyPlanId_Section_Status" ON "StudyPlanItems" ("StudyPlanId", "Section", "Status");


CREATE INDEX "IX_StudyPlanItems_StudyPlanId_WeekIndex" ON "StudyPlanItems" ("StudyPlanId", "WeekIndex");


CREATE INDEX "IX_StudyPlans_TemplateId" ON "StudyPlans" ("TemplateId");


CREATE INDEX "IX_StudyPlans_UserId" ON "StudyPlans" ("UserId");


CREATE INDEX "IX_StudyPlans_UserId_IsActive" ON "StudyPlans" ("UserId", "IsActive");


CREATE INDEX "IX_StudyPlanTemplates_IsActive_ExamTypeCode" ON "StudyPlanTemplates" ("IsActive", "ExamTypeCode");


CREATE INDEX "IX_StudyPlanTemplates_IsActive_MinWeeks_MaxWeeks" ON "StudyPlanTemplates" ("IsActive", "MinWeeks", "MaxWeeks");


CREATE INDEX "IX_StudyPlanTemplates_ProfessionId" ON "StudyPlanTemplates" ("ProfessionId");


CREATE UNIQUE INDEX "IX_StudyPlanTemplates_Slug" ON "StudyPlanTemplates" ("Slug");


CREATE UNIQUE INDEX "IX_StudyPlanTemplateTiers_TemplateId_TierCode" ON "StudyPlanTemplateTiers" ("TemplateId", "TierCode");


CREATE INDEX "IX_SubscriptionItems_AddOnVersionId" ON "SubscriptionItems" ("AddOnVersionId");


CREATE INDEX "IX_SubscriptionItems_ItemCode_SubscriptionId" ON "SubscriptionItems" ("ItemCode", "SubscriptionId");


CREATE INDEX "IX_SubscriptionItems_SubscriptionId_Status" ON "SubscriptionItems" ("SubscriptionId", "Status");


CREATE INDEX "IX_Subscriptions_PlanVersionId" ON "Subscriptions" ("PlanVersionId");


CREATE INDEX "IX_Subscriptions_UserId" ON "Subscriptions" ("UserId");


CREATE INDEX "IX_TaskTypes_ExamTypeCode_SubtestCode_Status" ON "TaskTypes" ("ExamTypeCode", "SubtestCode", "Status");


CREATE INDEX "IX_TaxRules_Country_EffectiveFrom" ON "TaxRules" ("Country", "EffectiveFrom");


CREATE INDEX "IX_TaxRules_Region_IsActive" ON "TaxRules" ("Region", "IsActive");


CREATE INDEX "IX_TeacherClasses_OwnerUserId" ON "TeacherClasses" ("OwnerUserId");


CREATE UNIQUE INDEX "UX_TeacherClassMember_Class_User" ON "TeacherClassMembers" ("TeacherClassId", "UserId");


CREATE INDEX "IX_TestimonialAssets_DisplayApproved_DisplayOrder" ON "TestimonialAssets" ("DisplayApproved", "DisplayOrder");


CREATE INDEX "IX_TutorAvailabilities_TutorId" ON "TutorAvailabilities" ("TutorId");


CREATE INDEX "IX_TutorAvailabilities_TutorId_DayOfWeek" ON "TutorAvailabilities" ("TutorId", "DayOfWeek");


CREATE INDEX "IX_TutorBookUpdates_Audience" ON "TutorBookUpdates" ("Audience");


CREATE INDEX "IX_TutorBookUpdates_PublishedAt" ON "TutorBookUpdates" ("PublishedAt");


CREATE INDEX "IX_TutoringAvailabilities_ExpertUserId" ON "TutoringAvailabilities" ("ExpertUserId");


CREATE INDEX "IX_TutoringSessions_ExpertUserId_ScheduledAt" ON "TutoringSessions" ("ExpertUserId", "ScheduledAt");


CREATE INDEX "IX_TutoringSessions_LearnerUserId_ScheduledAt" ON "TutoringSessions" ("LearnerUserId", "ScheduledAt");


CREATE INDEX "IX_Tutors_IsActive" ON "Tutors" ("IsActive");


CREATE UNIQUE INDEX "IX_Tutors_UserId" ON "Tutors" ("UserId");


CREATE INDEX "IX_UploadSessions_AttemptId" ON "UploadSessions" ("AttemptId");


CREATE INDEX "IX_UsageForecastSnapshots_FeatureCode_SnapshotDate" ON "UsageForecastSnapshots" ("FeatureCode", "SnapshotDate");


CREATE INDEX "IX_UsageForecastSnapshots_UserId_SnapshotDate" ON "UsageForecastSnapshots" ("UserId", "SnapshotDate");


CREATE INDEX "IX_UserAiCredentials_AuthAccountId" ON "UserAiCredentials" ("AuthAccountId");


CREATE UNIQUE INDEX "IX_UserAiCredentials_UserId_ProviderCode" ON "UserAiCredentials" ("UserId", "ProviderCode");


CREATE INDEX "IX_UserNotes_UserId_CreatedAt" ON "UserNotes" ("UserId", "CreatedAt");


CREATE INDEX "IX_Users_AccountStatus" ON "Users" ("AccountStatus");


CREATE UNIQUE INDEX "IX_Users_AuthAccountId" ON "Users" ("AuthAccountId");


CREATE INDEX "IX_Users_Email" ON "Users" ("Email");


CREATE INDEX "IX_Users_LastActiveAt" ON "Users" ("LastActiveAt");


CREATE INDEX "IX_VideoLessons_ExamTypeCode_Category_Status" ON "VideoLessons" ("ExamTypeCode", "Category", "Status");


CREATE INDEX "IX_VocabularyQuizResults_UserId_CompletedAt" ON "VocabularyQuizResults" ("UserId", "CompletedAt");


CREATE INDEX "IX_VocabularyTerms_ExamTypeCode_Status_Category" ON "VocabularyTerms" ("ExamTypeCode", "Status", "Category");


CREATE INDEX "IX_VocabularyTerms_ExamTypeCode_Status_IsFreePreview" ON "VocabularyTerms" ("ExamTypeCode", "Status", "IsFreePreview");


CREATE INDEX "IX_VocabularyTerms_ProfessionId_Category_Status" ON "VocabularyTerms" ("ProfessionId", "Category", "Status");


CREATE INDEX "IX_VocabularyTerms_Term_ExamTypeCode_ProfessionId" ON "VocabularyTerms" ("Term", "ExamTypeCode", "ProfessionId");


CREATE INDEX "IX_Wallets_UserId" ON "Wallets" ("UserId");


CREATE UNIQUE INDEX "IX_WalletTopUpTierConfigs_Amount_Currency" ON "WalletTopUpTierConfigs" ("Amount", "Currency");


CREATE INDEX "IX_WalletTopUpTierConfigs_IsActive_DisplayOrder" ON "WalletTopUpTierConfigs" ("IsActive", "DisplayOrder");


CREATE UNIQUE INDEX "IX_WalletTopUpTierConfigs_Slug" ON "WalletTopUpTierConfigs" ("Slug") WHERE "Slug" IS NOT NULL;


CREATE INDEX "IX_WalletTransactions_WalletId" ON "WalletTransactions" ("WalletId");


CREATE INDEX "IX_WalletTransactions_WalletId_CreatedAt" ON "WalletTransactions" ("WalletId", "CreatedAt");


CREATE UNIQUE INDEX "IX_WalletTransactions_WalletId_IdempotencyKey" ON "WalletTransactions" ("WalletId", "IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL;


CREATE UNIQUE INDEX "IX_WalletTransactions_WalletId_TransactionType_ReferenceType_R~" ON "WalletTransactions" ("WalletId", "TransactionType", "ReferenceType", "ReferenceId") WHERE "ReferenceId" IS NOT NULL AND (("TransactionType" = 'top_up' AND "ReferenceType" = 'payment') OR ("TransactionType" = 'plan_grant' AND "ReferenceType" = 'subscription') OR ("TransactionType" = 'credit_purchase' AND "ReferenceType" = 'addon'));


CREATE INDEX "IX_WritingAttemptAssets_AttemptId_PageNumber" ON "WritingAttemptAssets" ("AttemptId", "PageNumber");


CREATE INDEX "IX_WritingAttemptAssets_MediaAssetId" ON "WritingAttemptAssets" ("MediaAssetId");


CREATE INDEX "IX_WritingAttemptAssets_UserId_CreatedAt" ON "WritingAttemptAssets" ("UserId", "CreatedAt");


CREATE INDEX "IX_WritingAttemptEvents_SessionId_Timestamp" ON "WritingAttemptEvents" ("SessionId", "Timestamp");


CREATE INDEX "IX_WritingAttemptEvents_SubmissionId" ON "WritingAttemptEvents" ("SubmissionId");


CREATE INDEX "IX_WritingAttemptEvents_UserId_SessionId" ON "WritingAttemptEvents" ("UserId", "SessionId");


CREATE UNIQUE INDEX "IX_WritingBuddyCheckIns_PairId_WeekStartDate" ON "WritingBuddyCheckIns" ("PairId", "WeekStartDate");


CREATE INDEX "IX_WritingBuddyMessage_Pair_SentAt_Desc" ON "WritingBuddyMessages" ("PairId", "SentAt" DESC);


CREATE INDEX "IX_WritingBuddyMessages_FromUserId_SentAt" ON "WritingBuddyMessages" ("FromUserId", "SentAt");


CREATE INDEX "IX_WritingBuddyPairs_Profession_Status_MatchedAtBand" ON "WritingBuddyPairs" ("Profession", "Status", "MatchedAtBand");


CREATE INDEX "IX_WritingBuddyPairs_UserAId" ON "WritingBuddyPairs" ("UserAId");


CREATE UNIQUE INDEX "IX_WritingBuddyPairs_UserAId_UserBId" ON "WritingBuddyPairs" ("UserAId", "UserBId") WHERE "Status" = 'active';


CREATE INDEX "IX_WritingBuddyPairs_UserBId" ON "WritingBuddyPairs" ("UserBId");


CREATE INDEX "IX_WritingCalibrationLetters_AuthorTier" ON "WritingCalibrationLetters" ("AuthorTier");


CREATE INDEX "IX_WritingCalibrationLetters_ScenarioId" ON "WritingCalibrationLetters" ("ScenarioId");


CREATE INDEX "IX_WritingCalibrationResults_CalibrationLetterId" ON "WritingCalibrationResults" ("CalibrationLetterId");


CREATE INDEX "IX_WritingCalibrationResults_RunId_AbsErrorRaw" ON "WritingCalibrationResults" ("RunId", "AbsErrorRaw");


CREATE INDEX "IX_WritingCalibrationRuns_ModelVersion" ON "WritingCalibrationRuns" ("ModelVersion");


CREATE INDEX "IX_WritingCalibrationRuns_RunDate" ON "WritingCalibrationRuns" ("RunDate");


CREATE INDEX "IX_WritingCanonRules_Category_Active" ON "WritingCanonRules" ("Category", "Active");


CREATE INDEX "IX_WritingCanonRules_DetectionType" ON "WritingCanonRules" ("DetectionType");


CREATE INDEX "IX_WritingCanonViolations_RuleId" ON "WritingCanonViolations" ("RuleId");


CREATE INDEX "IX_WritingCanonViolations_SubmissionId" ON "WritingCanonViolations" ("SubmissionId");


CREATE INDEX "IX_WritingCanonViolations_SubmissionId_RuleId" ON "WritingCanonViolations" ("SubmissionId", "RuleId");


CREATE INDEX "IX_WritingCaseNoteDrillAttempts_UserId_DrillId_AttemptedAt" ON "WritingCaseNoteDrillAttempts" ("UserId", "DrillId", "AttemptedAt" DESC);


CREATE INDEX "IX_WritingCaseNoteDrills_Profession_LetterType_Status" ON "WritingCaseNoteDrills" ("Profession", "LetterType", "Status");


CREATE UNIQUE INDEX "IX_WritingCaseNoteDrillSentences_DrillId_Ordinal" ON "WritingCaseNoteDrillSentences" ("DrillId", "Ordinal");


CREATE INDEX "IX_WritingCoachSessions_AttemptId" ON "WritingCoachSessions" ("AttemptId");


CREATE INDEX "IX_WritingCoachSuggestions_AttemptId_Resolution" ON "WritingCoachSuggestions" ("AttemptId", "Resolution");


CREATE INDEX "IX_WritingCommonMistakes_CanonRuleId" ON "WritingCommonMistakes" ("CanonRuleId");


CREATE INDEX "IX_WritingCommonMistakes_Category" ON "WritingCommonMistakes" ("Category");


CREATE INDEX "IX_WritingCommonMistakes_RelatedSubSkill" ON "WritingCommonMistakes" ("RelatedSubSkill");


CREATE INDEX "IX_WritingContentChecklistItems_ScenarioId_Ordinal" ON "WritingContentChecklistItems" ("ScenarioId", "Ordinal");


CREATE INDEX "IX_WritingContentChecklistItems_ScenarioId_RequiredStatus" ON "WritingContentChecklistItems" ("ScenarioId", "RequiredStatus");


CREATE INDEX "IX_WritingDailyPlanItems_UserId_PlanDate" ON "WritingDailyPlanItems" ("UserId", "PlanDate");


CREATE INDEX "IX_WritingDailyPlanItems_UserId_Status" ON "WritingDailyPlanItems" ("UserId", "Status");


CREATE INDEX "IX_WritingDiagnosticSessions_ExpiresAt" ON "WritingDiagnosticSessions" ("ExpiresAt");


CREATE INDEX "IX_WritingDiagnosticSessions_SubmissionId" ON "WritingDiagnosticSessions" ("SubmissionId");


CREATE INDEX "IX_WritingDiagnosticSessions_UserId_Id" ON "WritingDiagnosticSessions" ("UserId", "Id");


CREATE INDEX "IX_WritingDraftsV2_UserId_LastSavedAt" ON "WritingDraftsV2" ("UserId", "LastSavedAt" DESC);


CREATE UNIQUE INDEX "IX_WritingDraftsV2_UserId_ScenarioId_Mode" ON "WritingDraftsV2" ("UserId", "ScenarioId", "Mode");


CREATE INDEX "IX_WritingDrillAttempts_User_Drill_Time" ON "WritingDrillAttempts" ("UserId", "DrillId", "AttemptedAt" DESC);


CREATE INDEX "IX_WritingDrillAttempts_UserId_NextDueAt" ON "WritingDrillAttempts" ("UserId", "NextDueAt");


CREATE INDEX "IX_WritingDrills_DrillType_Status" ON "WritingDrills" ("DrillType", "Status");


CREATE INDEX "IX_WritingDrills_TargetCanonRuleId" ON "WritingDrills" ("TargetCanonRuleId");


CREATE INDEX "IX_WritingDrills_TargetSubSkill" ON "WritingDrills" ("TargetSubSkill");


CREATE UNIQUE INDEX "IX_WritingExemplarAnnotations_ExemplarId_Ordinal" ON "WritingExemplarAnnotations" ("ExemplarId", "Ordinal");


CREATE UNIQUE INDEX "IX_WritingExemplarEmbeddings_ExemplarId" ON "WritingExemplarEmbeddings" ("ExemplarId");


CREATE INDEX "IX_WritingExemplars_Profession_LetterType" ON "WritingExemplars" ("Profession", "LetterType");


CREATE INDEX "IX_WritingExemplars_ScenarioId" ON "WritingExemplars" ("ScenarioId");


CREATE INDEX "IX_WritingExemplars_Status" ON "WritingExemplars" ("Status");


CREATE INDEX "IX_WritingFeedbackAnnotations_ReviewId" ON "WritingFeedbackAnnotations" ("ReviewId");


CREATE INDEX "IX_WritingFeedbackAnnotations_SubmissionId" ON "WritingFeedbackAnnotations" ("SubmissionId");


CREATE INDEX "IX_WritingGrades_AppealedByGradeId" ON "WritingGrades" ("AppealedByGradeId");


CREATE UNIQUE INDEX "IX_WritingGrades_SubmissionId" ON "WritingGrades" ("SubmissionId");


CREATE INDEX "IX_WritingGrades_TutorReviewId" ON "WritingGrades" ("TutorReviewId");


CREATE INDEX "IX_WritingLearnerMistakeStats_UserId_LastOccurredAt" ON "WritingLearnerMistakeStats" ("UserId", "LastOccurredAt" DESC);


CREATE UNIQUE INDEX "IX_WritingLearnerMistakeStats_UserId_MistakeId" ON "WritingLearnerMistakeStats" ("UserId", "MistakeId");


CREATE UNIQUE INDEX "IX_WritingLessonCompletionsV2_UserId_LessonId" ON "WritingLessonCompletionsV2" ("UserId", "LessonId");


CREATE INDEX "IX_WritingLessons_SkillCode_OrderIndex" ON "WritingLessons" ("SkillCode", "OrderIndex");


CREATE UNIQUE INDEX "IX_WritingLessons_Slug" ON "WritingLessons" ("Slug");


CREATE INDEX "IX_WritingLessonsV2_Status" ON "WritingLessonsV2" ("Status");


CREATE INDEX "IX_WritingLessonsV2_SubSkill_OrderInCourse" ON "WritingLessonsV2" ("SubSkill", "OrderInCourse");


CREATE INDEX "IX_WritingMocks_ScenarioId" ON "WritingMocks" ("ScenarioId");


CREATE INDEX "IX_WritingMocks_Status" ON "WritingMocks" ("Status");


CREATE INDEX "IX_WritingMockSessions_MockId" ON "WritingMockSessions" ("MockId");


CREATE INDEX "IX_WritingMockSessions_Status" ON "WritingMockSessions" ("Status");


CREATE INDEX "IX_WritingMockSessions_SubmissionId" ON "WritingMockSessions" ("SubmissionId");


CREATE INDEX "IX_WritingMockSessions_UserId_StartedAt" ON "WritingMockSessions" ("UserId", "StartedAt");


CREATE INDEX "IX_WritingModerations_Status" ON "WritingModerations" ("Status");


CREATE UNIQUE INDEX "IX_WritingModerations_SubmissionId" ON "WritingModerations" ("SubmissionId");


CREATE INDEX "IX_WritingOcrJobs_Status" ON "WritingOcrJobs" ("Status");


CREATE INDEX "IX_WritingOcrJobs_SubmissionId" ON "WritingOcrJobs" ("SubmissionId");


CREATE INDEX "IX_WritingOcrJobs_UserId_CreatedAt" ON "WritingOcrJobs" ("UserId", "CreatedAt" DESC);


CREATE INDEX "IX_WritingPathwayItems_PathwayId_OrderIndex" ON "WritingPathwayItems" ("PathwayId", "OrderIndex");


CREATE INDEX "IX_WritingPathwayItems_PathwayId_Status" ON "WritingPathwayItems" ("PathwayId", "Status");


CREATE INDEX "IX_WritingReadinessScores_ComputedAt" ON "WritingReadinessScores" ("ComputedAt");


CREATE UNIQUE INDEX "IX_WritingReadinessScores_UserId_Date" ON "WritingReadinessScores" ("UserId", "Date");


CREATE UNIQUE INDEX "IX_WritingResultVisibilityConfigs_ScenarioId" ON "WritingResultVisibilityConfigs" ("ScenarioId");


CREATE INDEX "IX_WritingRuleViolations_AttemptId" ON "WritingRuleViolations" ("AttemptId");


CREATE INDEX "IX_WritingRuleViolations_Profession_GeneratedAt" ON "WritingRuleViolations" ("Profession", "GeneratedAt");


CREATE INDEX "IX_WritingRuleViolations_RuleId_GeneratedAt" ON "WritingRuleViolations" ("RuleId", "GeneratedAt");


CREATE UNIQUE INDEX "IX_WritingScenarioEmbeddings_ScenarioId" ON "WritingScenarioEmbeddings" ("ScenarioId");


CREATE INDEX "IX_WritingScenarios_InternalCode" ON "WritingScenarios" ("InternalCode");


CREATE INDEX "IX_WritingScenarios_IsDiagnostic" ON "WritingScenarios" ("IsDiagnostic");


CREATE INDEX "IX_WritingScenarios_Profession_LetterType" ON "WritingScenarios" ("Profession", "LetterType");


CREATE INDEX "IX_WritingScenarios_SourceContentPaperId" ON "WritingScenarios" ("SourceContentPaperId");


CREATE INDEX "IX_WritingScenarios_Status" ON "WritingScenarios" ("Status");


CREATE UNIQUE INDEX "IX_WritingScenarioStructuredSentences_ScenarioId_Ordinal" ON "WritingScenarioStructuredSentences" ("ScenarioId", "Ordinal");


CREATE INDEX "IX_WritingScoreAppeals_OriginalGradeId" ON "WritingScoreAppeals" ("OriginalGradeId");


CREATE INDEX "IX_WritingScoreAppeals_SubmissionId" ON "WritingScoreAppeals" ("SubmissionId");


CREATE INDEX "IX_WritingScoreAppeals_UserId_Status" ON "WritingScoreAppeals" ("UserId", "Status");


CREATE INDEX "IX_WritingShowcasePosts_Profession_LetterType_Status" ON "WritingShowcasePosts" ("Profession", "LetterType", "Status");


CREATE INDEX "IX_WritingShowcasePosts_Status_PublishedAt" ON "WritingShowcasePosts" ("Status", "PublishedAt" DESC);


CREATE UNIQUE INDEX "IX_WritingShowcasePosts_SubmissionId" ON "WritingShowcasePosts" ("SubmissionId");


CREATE INDEX "IX_WritingSubmissions_LetterContentHash" ON "WritingSubmissions" ("LetterContentHash");


CREATE INDEX "IX_WritingSubmissions_OriginalSubmissionId" ON "WritingSubmissions" ("OriginalSubmissionId");


CREATE INDEX "IX_WritingSubmissions_Status_Pending" ON "WritingSubmissions" ("Status") WHERE "Status" IN ('queued','grading');


CREATE INDEX "IX_WritingSubmissions_User_CreatedAt" ON "WritingSubmissions" ("UserId", "CreatedAt" DESC);


CREATE INDEX "IX_WritingSubmissions_UserId_ScenarioId" ON "WritingSubmissions" ("UserId", "ScenarioId");


CREATE UNIQUE INDEX "IX_WritingTutorCalibrations_TutorId" ON "WritingTutorCalibrations" ("TutorId");


CREATE INDEX "IX_WritingTutorReviewAssignments_DueAt" ON "WritingTutorReviewAssignments" ("DueAt");


CREATE INDEX "IX_WritingTutorReviewAssignments_SubmissionId" ON "WritingTutorReviewAssignments" ("SubmissionId");


CREATE INDEX "IX_WritingTutorReviewAssignments_TutorId_Status" ON "WritingTutorReviewAssignments" ("TutorId", "Status");


CREATE INDEX "IX_WritingTutorReviews_SubmissionId" ON "WritingTutorReviews" ("SubmissionId");


CREATE INDEX "IX_WritingTutorReviews_TutorId_Status" ON "WritingTutorReviews" ("TutorId", "Status");


