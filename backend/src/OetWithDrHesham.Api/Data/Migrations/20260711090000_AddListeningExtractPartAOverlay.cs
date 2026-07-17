using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Adds OET Listening Part A "pdf_overlay" authoring support to
    /// <c>ListeningExtracts</c>: <c>AuthoringMethod</c> ("wysiwyg" default when
    /// null | "pdf_overlay") and <c>PartAOverlayBlanksJson</c> (normalized blank
    /// placements over the question-paper PDF). Both nullable; existing extracts
    /// are unaffected (null AuthoringMethod = wysiwyg). Additive, non-destructive.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260711090000_AddListeningExtractPartAOverlay")]
    public partial class AddListeningExtractPartAOverlay : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthoringMethod",
                table: "ListeningExtracts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartAOverlayBlanksJson",
                table: "ListeningExtracts",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AuthoringMethod", table: "ListeningExtracts");
            migrationBuilder.DropColumn(name: "PartAOverlayBlanksJson", table: "ListeningExtracts");
        }
    }
}
