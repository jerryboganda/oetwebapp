using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Creates the three admin-permission tables that were declared in
    /// <c>LearnerDbContext</c> and referenced by <c>AuthService</c>,
    /// <c>AdminService</c>, and <c>SeedData</c>, but never had a
    /// corresponding migration. Every admin sign-in executes a
    /// <c>SELECT Permission FROM AdminPermissionGrants WHERE AdminUserId = ?</c>
    /// query, so the absence of this table caused HTTP 500 "unexpected
    /// server error" on any admin login.
    ///
    /// Applied manually on production via create-admin-permissions-tables.sh
    /// on 2026-04-18; this migration file makes the fix durable across
    /// fresh deploys. The IF NOT EXISTS patterns let the migration run
    /// cleanly on environments where the tables were already created by
    /// the manual script.
    /// </remarks>
    public partial class AddAdminRoleBasedAccessControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AdminPermissionGrants
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""AdminPermissionGrants"" (
    ""Id""           character varying(64)        NOT NULL,
    ""AdminUserId""  character varying(64)        NOT NULL,
    ""Permission""   character varying(64)        NOT NULL,
    ""GrantedBy""    character varying(128)       NOT NULL,
    ""GrantedAt""    timestamp with time zone     NOT NULL,
    CONSTRAINT ""PK_AdminPermissionGrants"" PRIMARY KEY (""Id""),
    CONSTRAINT ""FK_AdminPermissionGrants_ApplicationUserAccounts_AdminUserId""
        FOREIGN KEY (""AdminUserId"")
        REFERENCES ""ApplicationUserAccounts"" (""Id"")
        ON DELETE CASCADE
);");

            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AdminPermissionGrants_AdminUserId_Permission""
    ON ""AdminPermissionGrants"" (""AdminUserId"", ""Permission"");");

            // AdminUsers
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""AdminUsers"" (
    ""Id""           character varying(64)        NOT NULL,
    ""DisplayName""  character varying(256)       NOT NULL,
    ""Email""        character varying(256)       NOT NULL,
    ""Role""         character varying(64)        NOT NULL DEFAULT 'unassigned',
    ""IsActive""     boolean                      NOT NULL DEFAULT true,
    ""CreatedAt""    timestamp with time zone     NOT NULL,
    CONSTRAINT ""PK_AdminUsers"" PRIMARY KEY (""Id"")
);");

            // PermissionTemplates
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""PermissionTemplates"" (
    ""Id""           character varying(64)        NOT NULL,
    ""Name""         character varying(128)       NOT NULL,
    ""Description""  character varying(512),
    ""Permissions""  text                         NOT NULL DEFAULT '[]',
    ""CreatedBy""    character varying(128)       NOT NULL,
    ""CreatedAt""    timestamp with time zone     NOT NULL,
    CONSTRAINT ""PK_PermissionTemplates"" PRIMARY KEY (""Id"")
);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""PermissionTemplates"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""AdminUsers"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_AdminPermissionGrants_AdminUserId_Permission"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""AdminPermissionGrants"";");
        }
    }
}
