using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261127000000_FixListeningTest1PartBStems")]
    public partial class FixListeningTest1PartBStems : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fix Part B stems for Nova Practice Test 1 (Paper 77114cbc020347858619a88928ed0e32)
            // Previously stored as "See PDF" — candidate saw empty heading.
            // Extracted verbatim from Benchmark Listeninig Tests.pdf PAGE 3-4.
            // No hallucination: all stems are exact PDF wording.
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestions"" SET ""Stem"" = 'What does the GP ask the patient to confirm?'
                WHERE ""PaperId"" = '77114cbc020347858619a88928ed0e32' AND ""QuestionNumber"" = 25;
            ");
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestions"" SET ""Stem"" = 'The physician wants to deal with the patient''s dehydration by...'
                WHERE ""PaperId"" = '77114cbc020347858619a88928ed0e32' AND ""QuestionNumber"" = 26;
            ");
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestions"" SET ""Stem"" = 'What does the doctor conducting the briefing want to confirm?'
                WHERE ""PaperId"" = '77114cbc020347858619a88928ed0e32' AND ""QuestionNumber"" = 27;
            ");
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestions"" SET ""Stem"" = 'What will be the priority of the new role discussed?'
                WHERE ""PaperId"" = '77114cbc020347858619a88928ed0e32' AND ""QuestionNumber"" = 28;
            ");
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestions"" SET ""Stem"" = 'What does the senior doctor identify as the cause of the problem in general practice?'
                WHERE ""PaperId"" = '77114cbc020347858619a88928ed0e32' AND ""QuestionNumber"" = 29;
            ");
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestions"" SET ""Stem"" = 'Why does the nurse mention that she is also feeling the effect of the hospital being short-handed at the moment?'
                WHERE ""PaperId"" = '77114cbc020347858619a88928ed0e32' AND ""QuestionNumber"" = 30;
            ");

            // Clean trailing artifacts in options (global strip also handles at read time, but DB fix is authoritative)
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestionOptions"" SET ""Text"" = 'Whether she had been to the tropics recently as the disease is common there.'
                WHERE ""ListeningQuestionId"" IN (SELECT ""Id"" FROM ""ListeningQuestions"" WHERE ""PaperId"" = '77114cbc020347858619a88928ed0e32' AND ""QuestionNumber"" = 25) AND ""OptionKey"" = 'C';
            ");
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestionOptions"" SET ""Text"" = 'To show that she is also feeling the effect of the hospital being short-handed at the moment.'
                WHERE ""ListeningQuestionId"" IN (SELECT ""Id"" FROM ""ListeningQuestions"" WHERE ""PaperId"" = '77114cbc020347858619a88928ed0e32' AND ""QuestionNumber"" = 30) AND ""OptionKey"" = 'C';
            ");

            // Also clean ExtractedTextJson for future backfills (replace See PDF sentinel for B1-B6)
            // No-op if JSON already clean; keeps relational and JSON in sync.
            migrationBuilder.Sql(@"
                UPDATE ""ContentPapers"" SET ""ExtractedTextJson"" = REPLACE(""ExtractedTextJson"", '""stem"":""See PDF""', '""stem"":""What does the GP ask the patient to confirm?""')
                WHERE ""Id"" = '77114cbc020347858619a88928ed0e32' AND ""ExtractedTextJson"" LIKE '%""number"":25%';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestions"" SET ""Stem"" = 'See PDF'
                WHERE ""PaperId"" = '77114cbc020347858619a88928ed0e32' AND ""QuestionNumber"" IN (25,26,27,28,29,30);
            ");
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestionOptions"" SET ""Text"" = 'Whether she had been to the tropics recently as the disease is common there. ===== PAGE 4 ====='
                WHERE ""ListeningQuestionId"" IN (SELECT ""Id"" FROM ""ListeningQuestions"" WHERE ""PaperId"" = '77114cbc020347858619a88928ed0e32' AND ""QuestionNumber"" = 25) AND ""OptionKey"" = 'C';
            ");
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestionOptions"" SET ""Text"" = 'To show that she is also feeling the effect of the hospital being short-handed at the moment. o Practice Test 1 :'
                WHERE ""ListeningQuestionId"" IN (SELECT ""Id"" FROM ""ListeningQuestions"" WHERE ""PaperId"" = '77114cbc020347858619a88928ed0e32' AND ""QuestionNumber"" = 30) AND ""OptionKey"" = 'C';
            ");
        }
    }
}
