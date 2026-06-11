using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeakingExamRebuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExamSessionId",
                table: "SpeakingSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExamSlot",
                table: "SpeakingSessions",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardTypeId",
                table: "RolePlayCards",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisplayCardNumber",
                table: "RolePlayCards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExamSessionId",
                table: "PrivateSpeakingBookings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SessionFormat",
                table: "PrivateSpeakingBookings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "practice");

            migrationBuilder.AddColumn<string>(
                name: "PatientBackground",
                table: "InterlocutorScripts",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PatientTask1",
                table: "InterlocutorScripts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PatientTask2",
                table: "InterlocutorScripts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PatientTask3",
                table: "InterlocutorScripts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PatientTask4",
                table: "InterlocutorScripts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PatientTask5",
                table: "InterlocutorScripts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SpeakingCardTypes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingCardTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingExamSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    MockSetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CardAId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CardBId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionAId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SessionBId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IntroStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IntroEndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PrepAStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActiveAStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CardAEndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PrepBStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActiveBStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CardBEndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RulebookVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreditARefId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    CreditBRefId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    CombinedScaledSnapshot = table.Column<double>(type: "double precision", nullable: true),
                    ReadinessBandSnapshot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    BookingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingExamSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingSessions_ExamSessionId",
                table: "SpeakingSessions",
                column: "ExamSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePlayCards_CardTypeId",
                table: "RolePlayCards",
                column: "CardTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingCardTypes_IsActive_SortOrder",
                table: "SpeakingCardTypes",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingExamSessions_BookingId",
                table: "SpeakingExamSessions",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingExamSessions_UserId_State",
                table: "SpeakingExamSessions",
                columns: new[] { "UserId", "State" });

            migrationBuilder.AddForeignKey(
                name: "FK_RolePlayCards_SpeakingCardTypes_CardTypeId",
                table: "RolePlayCards",
                column: "CardTypeId",
                principalTable: "SpeakingCardTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RolePlayCards_SpeakingCardTypes_CardTypeId",
                table: "RolePlayCards");

            migrationBuilder.DropTable(
                name: "SpeakingCardTypes");

            migrationBuilder.DropTable(
                name: "SpeakingExamSessions");

            migrationBuilder.DropIndex(
                name: "IX_SpeakingSessions_ExamSessionId",
                table: "SpeakingSessions");

            migrationBuilder.DropIndex(
                name: "IX_RolePlayCards_CardTypeId",
                table: "RolePlayCards");

            migrationBuilder.DropColumn(
                name: "ExamSessionId",
                table: "SpeakingSessions");

            migrationBuilder.DropColumn(
                name: "ExamSlot",
                table: "SpeakingSessions");

            migrationBuilder.DropColumn(
                name: "CardTypeId",
                table: "RolePlayCards");

            migrationBuilder.DropColumn(
                name: "DisplayCardNumber",
                table: "RolePlayCards");

            migrationBuilder.DropColumn(
                name: "ExamSessionId",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropColumn(
                name: "SessionFormat",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropColumn(
                name: "PatientBackground",
                table: "InterlocutorScripts");

            migrationBuilder.DropColumn(
                name: "PatientTask1",
                table: "InterlocutorScripts");

            migrationBuilder.DropColumn(
                name: "PatientTask2",
                table: "InterlocutorScripts");

            migrationBuilder.DropColumn(
                name: "PatientTask3",
                table: "InterlocutorScripts");

            migrationBuilder.DropColumn(
                name: "PatientTask4",
                table: "InterlocutorScripts");

            migrationBuilder.DropColumn(
                name: "PatientTask5",
                table: "InterlocutorScripts");
        }
    }
}
