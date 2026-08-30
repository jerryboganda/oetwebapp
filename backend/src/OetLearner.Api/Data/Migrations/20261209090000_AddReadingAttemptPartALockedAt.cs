using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds the nullable <c>PartALockedAt</c> timestamp to
    /// <c>ReadingAttempts</c>, backing the candidate-initiated "Submit Part A"
    /// action in the Full Reading Exam (owner request 2026-08-29). Until now
    /// Part A's boundary was derived purely from
    /// <c>StartedAt + PartATimerMinutes</c>, so a candidate who finished early
    /// had to sit out the remainder of the 15-minute window.
    ///
    /// Strictly additive and fully back-compatible: NULL on every existing row,
    /// and <c>ReadingAttemptService.ResolvePartADeadline</c> takes the MINIMUM
    /// of <c>StartedAt + PartATimerMinutes</c> and this value. Every in-flight
    /// and historical attempt therefore keeps byte-identical Part A and Part
    /// B/C deadlines, and an early lock can only ever move the boundary
    /// earlier -- never later. No back-fill, no data rewrite, no index (the
    /// column is only ever read through an already-materialised attempt row).
    ///
    /// Hand-authored per repo convention: inline <c>[DbContext]</c>/
    /// <c>[Migration]</c>, no Designer file, ModelSnapshot deliberately
    /// untouched. Ordered after the already-committed
    /// 20261208090000_AddAiRawResponseRetention.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261209090000_AddReadingAttemptPartALockedAt")]
    public partial class AddReadingAttemptPartALockedAt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PartALockedAt",
                table: "ReadingAttempts",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PartALockedAt",
                table: "ReadingAttempts");
        }
    }
}
