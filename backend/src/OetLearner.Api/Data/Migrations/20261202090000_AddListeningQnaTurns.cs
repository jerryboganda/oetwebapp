using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// W5 — <c>ListeningQnaTurns</c> with unique (SessionId, ClientTurnId)
    /// so a duplicate Q&amp;A POST returns the stored answer.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261202090000_AddListeningQnaTurns")]
    public partial class AddListeningQnaTurns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "ListeningQnaTurns" (
                    "Id" character varying(64) NOT NULL,
                    "SessionId" character varying(128) NOT NULL,
                    "ClientTurnId" character varying(64) NOT NULL,
                    "UserId" character varying(64) NOT NULL,
                    "AttemptId" character varying(64) NOT NULL,
                    "QuestionId" character varying(64) NOT NULL,
                    "Message" text NOT NULL,
                    "Reply" text NOT NULL,
                    "AiOperationId" character varying(64),
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_ListeningQnaTurns" PRIMARY KEY ("Id")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_ListeningQnaTurns_Session_ClientTurn"
                    ON "ListeningQnaTurns" ("SessionId", "ClientTurnId");

                CREATE INDEX IF NOT EXISTS "IX_ListeningQnaTurns_User_Attempt_Question"
                    ON "ListeningQnaTurns" ("UserId", "AttemptId", "QuestionId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ListeningQnaTurns");
        }
    }
}
