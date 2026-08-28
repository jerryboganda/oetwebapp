using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// W1 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01),
    /// following the W0 Listening retry-storm repair
    /// (20261101090000_AddListeningAnswerAiSkipAndRetry).
    ///
    /// Creates the four new, empty control-plane tables described in
    /// <c>Domain/AiOperationEntities.cs</c>, <c>Domain/AiOperationAttemptEntities.cs</c>,
    /// <c>Domain/AiBudgetEntities.cs</c>, and <c>Domain/AiCreditReservationEntities.cs</c>:
    /// <list type="bullet">
    ///   <item><c>AiOperations</c> — one durable row per logical AI unit of
    ///   work, unique on <c>IdempotencyKey</c>.</item>
    ///   <item><c>AiOperationAttempts</c> — plain (non-partitioned) child
    ///   table whose composite primary key (<c>OperationId</c>,
    ///   <c>AttemptNumber</c>) is the authoritative "exactly one attempt per
    ///   number" constraint.</item>
    ///   <item><c>AiBudgetPeriods</c> — platform spend reservation per
    ///   (Scope, PeriodKey), unique on that pair.</item>
    ///   <item><c>AiCreditReservations</c> — learner credit hold per
    ///   operation, unique on <c>BusinessReference</c> for idempotent
    ///   reserve/commit/release.</item>
    /// </list>
    ///
    /// <para>
    /// Strictly additive: four brand-new tables, zero existing tables
    /// touched. Nothing reads or writes these tables yet — see the domain
    /// file doc comments for the full W1 scope statement. Hand-authored per
    /// repo convention: inline <c>[Migration]</c>/<c>[DbContext]</c>, no
    /// Designer file, ModelSnapshot deliberately untouched.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261102090000_AddAiControlPlaneCore")]
    public partial class AddAiControlPlaneCore : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiOperations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Module = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResourceVersion = table.Column<int>(type: "integer", nullable: true),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PromptVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RulebookVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    OperationClass = table.Column<int>(type: "integer", nullable: false),
                    SelectedProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SelectedModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreditReservationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BudgetReservationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AttemptLimit = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResultRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiOperations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_AiOperations_IdempotencyKey",
                table: "AiOperations",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiOperations_State_NextAttemptAt",
                table: "AiOperations",
                columns: new[] { "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiOperations_LeaseExpiresAt",
                table: "AiOperations",
                column: "LeaseExpiresAt");

            migrationBuilder.CreateTable(
                name: "AiOperationAttempts",
                columns: table => new
                {
                    OperationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    AiUsageRecordId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProviderInvoked = table.Column<bool>(type: "boolean", nullable: false),
                    ProviderRequestId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProviderHttpStatus = table.Column<int>(type: "integer", nullable: true),
                    RetryReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorClass = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    // Composite PK: the authoritative exactly-one-attempt
                    // structural guarantee. This table is never partitioned.
                    table.PrimaryKey("PK_AiOperationAttempts", x => new { x.OperationId, x.AttemptNumber });
                    table.ForeignKey(
                        name: "FK_AiOperationAttempts_AiOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "AiOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiBudgetPeriods",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PeriodKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LimitUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    ReservedUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    CommittedUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiBudgetPeriods", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_AiBudgetPeriods_Scope_PeriodKey",
                table: "AiBudgetPeriods",
                columns: new[] { "Scope", "PeriodKey" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "AiCreditReservations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OperationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BucketKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Units = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    BusinessReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCreditReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiCreditReservations_AiOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "AiOperations",
                        principalColumn: "Id",
                        // Restrict, not Cascade: a credit reservation is a
                        // financial record and must never silently disappear
                        // because its operation row was removed.
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_AiCreditReservations_BusinessReference",
                table: "AiCreditReservations",
                column: "BusinessReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiCreditReservations_OperationId",
                table: "AiCreditReservations",
                column: "OperationId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiCreditReservations");
            migrationBuilder.DropTable(name: "AiOperationAttempts");
            migrationBuilder.DropTable(name: "AiBudgetPeriods");
            migrationBuilder.DropTable(name: "AiOperations");
        }
    }
}
