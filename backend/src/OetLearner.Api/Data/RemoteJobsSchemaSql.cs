namespace OetLearner.Api.Data;

/// <summary>
/// The exact DDL of migration <c>20270110090000_AddRemoteWorkersAndJobs</c> (OET-RWP/1 section 9.1),
/// kept as constants so the migration and the PostgreSQL integration tests execute the same text.
/// Idempotent (<c>IF NOT EXISTS</c> everywhere) and additive: the blue/green slots overlap during a
/// rollout and neither slot is affected by tables it never reads.
///
/// <para>
/// One additive column beyond the protocol sketch: <c>RemoteWorkers.CurrentInstanceStartedAt</c>,
/// which orders two agent instances that share a token (the newer start wins; see
/// <c>RemoteWorkerService</c>).
/// </para>
/// </summary>
internal static class RemoteJobsSchemaSql
{
    /// <summary>Partial unique index allowing one open (Queued/Leased) canary per target node.</summary>
    public const string OpenCanaryIndex = "UX_RemoteJobs_OpenCanary";

    public const string Up = """
        CREATE TABLE IF NOT EXISTS "RemoteWorkers" (
            "Id"                       character varying(64)    NOT NULL,
            "NodeRef"                  character varying(64)    NOT NULL,
            "DisplayName"              character varying(128)   NOT NULL,
            "Status"                   character varying(16)    NOT NULL,
            "StatusReason"             character varying(200)   NULL,
            "StatusChangedAt"          timestamp with time zone NOT NULL,
            "Region"                   character varying(32)    NULL,
            "Provider"                 character varying(64)    NULL,
            "AllowedKinds"             text[]                   NOT NULL DEFAULT ARRAY[]::text[],
            "MaxConcurrency"           integer                  NOT NULL DEFAULT 2,
            "KindLimitsJson"           jsonb                    NOT NULL DEFAULT '{}'::jsonb,
            "CpuBudgetMilli"           integer                  NOT NULL DEFAULT 3000,
            "MemBudgetMiB"             integer                  NOT NULL DEFAULT 5120,
            "TmpBudgetMiB"             integer                  NOT NULL DEFAULT 3072,
            "PressureJson"             jsonb                    NOT NULL DEFAULT '{}'::jsonb,
            "PollJson"                 jsonb                    NOT NULL DEFAULT '{}'::jsonb,
            "DesiredAgentJson"         jsonb                    NOT NULL DEFAULT '{}'::jsonb,
            "Paused"                   boolean                  NOT NULL DEFAULT false,
            "PolicyRevision"           bigint                   NOT NULL DEFAULT 1,
            "AppliedRevision"          bigint                   NOT NULL DEFAULT 0,
            "AgentVersion"             character varying(32)    NULL,
            "AgentImageDigest"         character varying(80)    NULL,
            "ProtocolVersion"          integer                  NULL,
            "KindsJson"                jsonb                    NOT NULL DEFAULT '[]'::jsonb,
            "CurrentInstanceId"        character varying(64)    NULL,
            "CurrentInstanceStartedAt" timestamp with time zone NULL,
            "LastSeenAt"               timestamp with time zone NULL,
            "LastHeartbeatAt"          timestamp with time zone NULL,
            "LastClaimAt"              timestamp with time zone NULL,
            "LastCapacityJson"         jsonb                    NULL,
            "LastLoadJson"             jsonb                    NULL,
            "IntegrityStrikes"         integer                  NOT NULL DEFAULT 0,
            "StrikeWindowStartedAt"    timestamp with time zone NULL,
            "DeclinedInARow"           integer                  NOT NULL DEFAULT 0,
            "LastCanaryAt"             timestamp with time zone NULL,
            "LastCanaryOk"             boolean                  NULL,
            "CreatedAt"                timestamp with time zone NOT NULL,
            "UpdatedAt"                timestamp with time zone NOT NULL,
            "CreatedBy"                character varying(64)    NOT NULL,
            CONSTRAINT "PK_RemoteWorkers" PRIMARY KEY ("Id"),
            CONSTRAINT "CK_RemoteWorkers_Status" CHECK ("Status" IN
                ('Pending','Probation','Active','Draining','Disabled','Quarantined','Revoked')),
            CONSTRAINT "CK_RemoteWorkers_Limits" CHECK (
                "MaxConcurrency" BETWEEN 0 AND 8 AND "CpuBudgetMilli" BETWEEN 0 AND 64000
                AND "MemBudgetMiB" BETWEEN 0 AND 262144 AND "TmpBudgetMiB" BETWEEN 0 AND 65536
                AND "TmpBudgetMiB" <= "MemBudgetMiB")
        );
        ALTER TABLE "RemoteWorkers" ADD COLUMN IF NOT EXISTS "CurrentInstanceStartedAt" timestamp with time zone NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_RemoteWorkers_NodeRef" ON "RemoteWorkers" ("NodeRef");
        CREATE INDEX IF NOT EXISTS "IX_RemoteWorkers_Status" ON "RemoteWorkers" ("Status");

        CREATE TABLE IF NOT EXISTS "RemoteCredentials" (
            "TokenId"    character varying(16)    NOT NULL,
            "Kind"       character varying(8)     NOT NULL,
            "NodeId"     character varying(64)    NULL,
            "SecretHash" character varying(64)    NOT NULL,
            "CreatedAt"  timestamp with time zone NOT NULL,
            "ExpiresAt"  timestamp with time zone NOT NULL,
            "RevokedAt"  timestamp with time zone NULL,
            "LastUsedAt" timestamp with time zone NULL,
            "CreatedBy"  character varying(64)    NOT NULL,
            CONSTRAINT "PK_RemoteCredentials" PRIMARY KEY ("TokenId"),
            CONSTRAINT "FK_RemoteCredentials_RemoteWorkers" FOREIGN KEY ("NodeId")
                REFERENCES "RemoteWorkers" ("Id") ON DELETE CASCADE,
            CONSTRAINT "CK_RemoteCredentials_Kind"  CHECK ("Kind" IN ('node','fleet')),
            CONSTRAINT "CK_RemoteCredentials_Shape" CHECK (
                ("Kind" = 'node' AND "NodeId" IS NOT NULL) OR ("Kind" = 'fleet' AND "NodeId" IS NULL)),
            CONSTRAINT "CK_RemoteCredentials_Hash"  CHECK ("SecretHash" ~ '^[0-9a-f]{64}$'),
            CONSTRAINT "CK_RemoteCredentials_Id"    CHECK ("TokenId" ~ '^[0-9a-f]{16}$')
        );
        CREATE INDEX IF NOT EXISTS "IX_RemoteCredentials_Node" ON "RemoteCredentials" ("NodeId") WHERE "RevokedAt" IS NULL;

        CREATE TABLE IF NOT EXISTS "RemoteJobs" (
            "Id"                character varying(64)    NOT NULL,
            "Kind"              character varying(48)    NOT NULL,
            "SchemaVersion"     integer                  NOT NULL DEFAULT 1,
            "Purpose"           character varying(16)    NOT NULL DEFAULT 'apply',
            "ResourceType"      character varying(48)    NOT NULL,
            "ResourceId"        character varying(64)    NOT NULL,
            "IdempotencyKey"    character varying(256)   NOT NULL,
            "InputSha256"       character varying(64)    NOT NULL,
            "EngineVersion"     character varying(96)    NOT NULL,
            "SettingsHash"      character varying(64)    NOT NULL,
            "ParamsJson"        jsonb                    NOT NULL,
            "InputsJson"        jsonb                    NOT NULL,
            "LimitsJson"        jsonb                    NOT NULL,
            "Weight"            smallint                 NOT NULL DEFAULT 1,
            "Priority"          smallint                 NOT NULL DEFAULT 0,
            "TargetNodeId"      character varying(64)    NULL,
            "State"             character varying(16)    NOT NULL,
            "Attempt"           integer                  NOT NULL DEFAULT 0,
            "MaxAttempts"       integer                  NOT NULL DEFAULT 3,
            "ReleaseCount"      integer                  NOT NULL DEFAULT 0,
            "FenceToken"        bigint                   NOT NULL DEFAULT 0,
            "LeaseOwner"        character varying(64)    NULL,
            "LeaseExpiresAt"    timestamp with time zone NULL,
            "DeadlineAt"        timestamp with time zone NULL,
            "LeasedAt"          timestamp with time zone NULL,
            "ClaimNonce"        character varying(64)    NULL,
            "LastHeartbeatAt"   timestamp with time zone NULL,
            "NextAttemptAt"     timestamp with time zone NOT NULL,
            "FallbackAfter"     timestamp with time zone NULL,
            "EnqueuedBy"        character varying(64)    NOT NULL,
            "ResultSha256"      character varying(64)    NULL,
            "ResultSummaryJson" jsonb                    NULL,
            "ResultJson"        text                     NULL,
            "ApplyOutcome"      character varying(16)    NULL,
            "CompletedAt"       timestamp with time zone NULL,
            "SettledFence"      bigint                   NULL,
            "SettledBy"         character varying(64)    NULL,
            "SettledCode"       character varying(64)    NULL,
            "FailureCode"       character varying(64)    NULL,
            "FailureMessage"    character varying(256)   NULL,
            "LastFailedNodeId"  character varying(64)    NULL,
            "MetricsJson"       jsonb                    NULL,
            "CreatedAt"         timestamp with time zone NOT NULL,
            "UpdatedAt"         timestamp with time zone NOT NULL,
            CONSTRAINT "PK_RemoteJobs" PRIMARY KEY ("Id"),
            CONSTRAINT "CK_RemoteJobs_State" CHECK ("State" IN
                ('Queued','Leased','Succeeded','Failed','Quarantined','FallbackLocal','Cancelled')),
            CONSTRAINT "CK_RemoteJobs_Purpose" CHECK ("Purpose" IN ('apply','shadow','canary')),
            CONSTRAINT "CK_RemoteJobs_LeaseShape" CHECK (
                ("State" = 'Leased') = ("LeaseOwner" IS NOT NULL AND "LeaseExpiresAt" IS NOT NULL AND "DeadlineAt" IS NOT NULL)),
            CONSTRAINT "CK_RemoteJobs_Attempt" CHECK ("Attempt" >= 0 AND "Attempt" <= "MaxAttempts"),
            CONSTRAINT "CK_RemoteJobs_Fence"   CHECK ("FenceToken" >= 0),
            CONSTRAINT "CK_RemoteJobs_Hashes"  CHECK ("InputSha256" ~ '^[0-9a-f]{64}$' AND "SettingsHash" ~ '^[0-9a-f]{64}$'),
            CONSTRAINT "CK_RemoteJobs_ResultSize" CHECK ("ResultJson" IS NULL OR octet_length("ResultJson") <= 9437184)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_RemoteJobs_IdempotencyKey" ON "RemoteJobs" ("IdempotencyKey");
        CREATE INDEX IF NOT EXISTS "IX_RemoteJobs_Claim"       ON "RemoteJobs" ("Kind", "Priority" DESC, "NextAttemptAt") WHERE "State" = 'Queued';
        CREATE INDEX IF NOT EXISTS "IX_RemoteJobs_LeaseExpiry" ON "RemoteJobs" ("LeaseExpiresAt") WHERE "State" = 'Leased';
        CREATE INDEX IF NOT EXISTS "IX_RemoteJobs_LeaseOwner"  ON "RemoteJobs" ("LeaseOwner") WHERE "State" = 'Leased';
        CREATE INDEX IF NOT EXISTS "IX_RemoteJobs_Fallback"    ON "RemoteJobs" ("FallbackAfter") WHERE "State" = 'Queued' AND "FallbackAfter" IS NOT NULL;
        CREATE INDEX IF NOT EXISTS "IX_RemoteJobs_Resource"    ON "RemoteJobs" ("ResourceType", "ResourceId");
        CREATE INDEX IF NOT EXISTS "IX_RemoteJobs_State_Updated" ON "RemoteJobs" ("State", "UpdatedAt");
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_RemoteJobs_OpenCanary" ON "RemoteJobs" ("TargetNodeId")
            WHERE "Purpose" = 'canary' AND "State" IN ('Queued', 'Leased');

        CREATE TABLE IF NOT EXISTS "RemoteJobOutputs" (
            "JobId"      character varying(64)    NOT NULL,
            "Fence"      bigint                   NOT NULL,
            "Name"       character varying(64)    NOT NULL,
            "Sha256"     character varying(64)    NOT NULL,
            "SizeBytes"  bigint                   NOT NULL,
            "StorageKey" character varying(512)   NOT NULL,
            "CreatedAt"  timestamp with time zone NOT NULL,
            CONSTRAINT "PK_RemoteJobOutputs" PRIMARY KEY ("JobId", "Fence", "Name"),
            CONSTRAINT "FK_RemoteJobOutputs_RemoteJobs" FOREIGN KEY ("JobId") REFERENCES "RemoteJobs" ("Id") ON DELETE CASCADE,
            CONSTRAINT "CK_RemoteJobOutputs_Hash" CHECK ("Sha256" ~ '^[0-9a-f]{64}$')
        );
        """;

    public const string Down = """
        DROP TABLE IF EXISTS "RemoteJobOutputs";
        DROP TABLE IF EXISTS "RemoteJobs";
        DROP TABLE IF EXISTS "RemoteCredentials";
        DROP TABLE IF EXISTS "RemoteWorkers";
        """;
}
