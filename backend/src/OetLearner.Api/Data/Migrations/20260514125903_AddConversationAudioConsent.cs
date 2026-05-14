using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationAudioConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudioConsentVersion",
                table: "ConversationSessions",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecordingConsentAcceptedAt",
                table: "ConversationSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VendorConsentAcceptedAt",
                table: "ConversationSessions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioConsentVersion",
                table: "ConversationSessions");

            migrationBuilder.DropColumn(
                name: "RecordingConsentAcceptedAt",
                table: "ConversationSessions");

            migrationBuilder.DropColumn(
                name: "VendorConsentAcceptedAt",
                table: "ConversationSessions");
        }
    }
}
