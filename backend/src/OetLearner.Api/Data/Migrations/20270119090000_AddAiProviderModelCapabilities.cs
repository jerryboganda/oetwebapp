using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Z.AI / GLM provider support (hand-authored, additive): live-probed per-model capabilities and the
    /// stored "may be picked automatically" flag.
    ///
    /// <para>
    /// Two changes, no backfill, no destructive operation, so old code keeps running against the new schema
    /// during a blue/green overlap:
    /// </para>
    /// <list type="bullet">
    /// <item><c>AiProviderModelCapabilities</c> — one row per (provider code, model). A provider row serves
    /// many models whose abilities genuinely differ, so capability cannot live on the provider row. Every
    /// flag is the result of a live probe, never vendor documentation.</item>
    /// <item><c>AiProviders.ParticipatesInAutoSelection</c> — replaces a hardcoded code list
    /// (<c>OpenCodeProviderDefaults.ExplicitOnlyCodes</c>) with owner-editable state, so "configurable
    /// from the admin panel" is literally true. <b>The column default is <c>false</c> and the
    /// backfill below is what preserves today's behaviour:</b> without it the migration would flip
    /// <em>every</em> existing row out of the implicit-default pick, and <c>anthropic</c> (priority
    /// 20) is what currently wins it — so a silent fallthrough to the mock provider would be the
    /// first symptom. The rule used to exclude exactly two things, and the backfill excludes
    /// exactly the same ones: <c>opencode</c> by code, and the subscription sidecars by their
    /// literal marker key, which the runtime predicate still checks independently.</item>
    /// </list>
    ///
    /// <para>
    /// A missing capability row means "unknown", and every consumer treats unknown as unsupported (fail
    /// closed), so this table is safe to add ahead of the probe that populates it.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270119090000_AddAiProviderModelCapabilities")]
    public partial class AddAiProviderModelCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ParticipatesInAutoSelection",
                table: "AiProviders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Behaviour-preserving backfill. The pre-existing predicate was
            // "not a marker-key row and not an explicit-only code", and the only code on the
            // explicit-only list was 'opencode'. Rows with a literal subscription marker key stay
            // excluded at runtime by AiProviderDefaultEligibility.IsDefaultEligible regardless of
            // this column, so they are excluded here too purely to keep the stored state honest
            // about what the screen will show.
            migrationBuilder.Sql("""
                UPDATE "AiProviders"
                SET "ParticipatesInAutoSelection" = TRUE
                WHERE "Code" <> 'opencode';
                """);

            migrationBuilder.CreateTable(
                name: "AiProviderModelCapabilities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SupportsTools = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsVision = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsDocuments = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsJsonMode = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsEmbeddings = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsStreaming = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsThinking = table.Column<bool>(type: "boolean", nullable: false),
                    ThinkingCanBeDisabled = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedReasoningEffortsCsv = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MaxTokensCeiling = table.Column<int>(type: "integer", nullable: false),
                    ContextTokens = table.Column<int>(type: "integer", nullable: false),
                    ProbeStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProbeDetail = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ProbedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviderModelCapabilities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderModelCapabilities_ProviderCode_Model",
                table: "AiProviderModelCapabilities",
                columns: new[] { "ProviderCode", "Model" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiProviderModelCapabilities");

            migrationBuilder.DropColumn(
                name: "ParticipatesInAutoSelection",
                table: "AiProviders");
        }
    }
}