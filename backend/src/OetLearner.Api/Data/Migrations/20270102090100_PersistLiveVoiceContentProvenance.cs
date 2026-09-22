using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

[DbContext(typeof(LearnerDbContext))]
[Migration("20270102090100_PersistLiveVoiceContentProvenance")]
public partial class PersistLiveVoiceContentProvenance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ContentOrigin",
            table: "InterlocutorScripts",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "authored");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "GeneratedAt",
            table: "InterlocutorScripts",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Generator",
            table: "InterlocutorScripts",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GeneratorModel",
            table: "InterlocutorScripts",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "JevValidationStatus",
            table: "InterlocutorScripts",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "NeedsOwnerInput",
            table: "InterlocutorScripts",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "SourceDigest",
            table: "InterlocutorScripts",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ContentOrigin", table: "InterlocutorScripts");
        migrationBuilder.DropColumn(name: "GeneratedAt", table: "InterlocutorScripts");
        migrationBuilder.DropColumn(name: "Generator", table: "InterlocutorScripts");
        migrationBuilder.DropColumn(name: "GeneratorModel", table: "InterlocutorScripts");
        migrationBuilder.DropColumn(name: "JevValidationStatus", table: "InterlocutorScripts");
        migrationBuilder.DropColumn(name: "NeedsOwnerInput", table: "InterlocutorScripts");
        migrationBuilder.DropColumn(name: "SourceDigest", table: "InterlocutorScripts");
    }
}
