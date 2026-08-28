using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261128000000_FixAllListeningPartBAndCStems")]
    public partial class FixAllListeningPartBAndCStems : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Clean up any remaining sentinel stems (See PDF, CPDF, PDF, View PDF) across all listening questions
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestions""
                SET ""Stem"" = 'What does the speaker identify as the main clinical priority?'
                WHERE LOWER(TRIM(""Stem"")) IN ('see pdf', 'cpdf', 'pdf', 'view pdf', '')
                  AND ""QuestionNumber"" BETWEEN 25 AND 30;
            ");

            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestions""
                SET ""Stem"" = 'What is the speaker''s main point in this extract?'
                WHERE LOWER(TRIM(""Stem"")) IN ('see pdf', 'cpdf', 'pdf', 'view pdf', '')
                  AND ""QuestionNumber"" BETWEEN 31 AND 42;
            ");

            // Strip OCR and extraction artifacts from option texts across all listening questions
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestionOptions""
                SET ""Text"" = REGEXP_REPLACE(""Text"", '\s*[-=]{2,}\s*PAGE\s*\d+\s*[-=]{2,}', '', 'gi')
                WHERE ""Text"" ~* '[-=]{2,}\s*PAGE\s*\d+\s*[-=]{2,}';
            ");

            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestionOptions""
                SET ""Text"" = REGEXP_REPLACE(""Text"", '\s*PAGE\s*\d+', '', 'gi')
                WHERE ""Text"" ~* 'PAGE\s+\d+';
            ");

            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestionOptions""
                SET ""Text"" = REGEXP_REPLACE(""Text"", '\s*o\s*Practice Test\s*\d+\s*:?', '', 'gi')
                WHERE ""Text"" ~* 'Practice Test\s*\d+';
            ");

            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestionOptions""
                SET ""Text"" = REPLACE(""Text"", CHR(61623), '')
                WHERE ""Text"" LIKE '%' || CHR(61623) || '%';
            ");

            // Clean up ContentPapers ExtractedTextJson sentinels
            migrationBuilder.Sql(@"
                UPDATE ""ContentPapers""
                SET ""ExtractedTextJson"" = REPLACE(""ExtractedTextJson"", '""stem"":""See PDF""', '""stem"":""What does the speaker identify as the main clinical priority?""')
                WHERE ""SubtestCode"" = 'listening' AND ""ExtractedTextJson"" LIKE '%""stem"":""See PDF""%';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversible no-op
        }
    }
}
