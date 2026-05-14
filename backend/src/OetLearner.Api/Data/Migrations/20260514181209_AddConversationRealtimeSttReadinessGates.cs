using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationRealtimeSttReadinessGates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RealtimeSttAllowManagedLearnerRealProvider",
                table: "ConversationSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RealtimeSttAllowRealProvider",
                table: "ConversationSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RealtimeSttAssumeLearnersAdult",
                table: "ConversationSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RealtimeSttEstimatedCostUsdPerMinute",
                table: "ConversationSettings",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RealtimeSttProviderConnectTimeoutSeconds",
                table: "ConversationSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RealtimeSttProviderSessionTopology",
                table: "ConversationSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RealtimeSttRegionId",
                table: "ConversationSettings",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RealtimeSttAllowManagedLearnerRealProvider",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttAllowRealProvider",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttAssumeLearnersAdult",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttEstimatedCostUsdPerMinute",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttProviderConnectTimeoutSeconds",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttProviderSessionTopology",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttRegionId",
                table: "ConversationSettings");

        }
    }
}
