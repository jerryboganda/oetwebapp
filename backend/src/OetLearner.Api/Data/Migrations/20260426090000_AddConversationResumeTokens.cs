using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationResumeTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE TABLE IF NOT EXISTS ""ConversationSessionResumeTokens"" (
    ""Id"" character varying(64) NOT NULL,
    ""SessionId"" character varying(64) NOT NULL,
    ""UserId"" character varying(64) NOT NULL,
    ""TokenHash"" character varying(128) NOT NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""ExpiresAt"" timestamp with time zone NOT NULL,
    ""LastUsedAt"" timestamp with time zone NULL,
    ""ConsumedAt"" timestamp with time zone NULL,
    ""RevokedAt"" timestamp with time zone NULL,
    CONSTRAINT ""PK_ConversationSessionResumeTokens"" PRIMARY KEY (""Id""),
    CONSTRAINT ""FK_ConversationSessionResumeTokens_ConversationSessions_SessionId"" FOREIGN KEY (""SessionId"") REFERENCES ""ConversationSessions"" (""Id"") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ConversationSessionResumeTokens_TokenHash"" ON ""ConversationSessionResumeTokens"" (""TokenHash"");
CREATE INDEX IF NOT EXISTS ""IX_ConversationSessionResumeTokens_UserId_SessionId"" ON ""ConversationSessionResumeTokens"" (""UserId"", ""SessionId"");
CREATE INDEX IF NOT EXISTS ""IX_ConversationSessionResumeTokens_ExpiresAt"" ON ""ConversationSessionResumeTokens"" (""ExpiresAt"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ConversationSessionResumeTokens");
        }
    }
}
