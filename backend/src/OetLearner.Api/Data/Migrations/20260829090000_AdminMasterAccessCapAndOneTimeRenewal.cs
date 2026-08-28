using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

[DbContext(typeof(LearnerDbContext))]
[Migration("20260829090000_AdminMasterAccessCapAndOneTimeRenewal")]
public partial class AdminMasterAccessCapAndOneTimeRenewal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Master Access Expiry becomes a genuine sticky global cap when set by an
        // admin: package mutations tighten it (min semantics) but never extend it.
        migrationBuilder.AddColumn<bool>(
            name: "AccessExpiresAtIsAdminCap",
            table: "Users",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AccessExpiresAtIsAdminCap",
            table: "Users");
    }
}
