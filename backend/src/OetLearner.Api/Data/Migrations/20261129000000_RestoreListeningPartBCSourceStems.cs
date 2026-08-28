using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Restores the source-backed Part B and Part C prompts for the one
    /// listening paper whose PDF content has been verified locally. Unknown
    /// sentinel/generic prompts are cleared instead of being guessed so the
    /// learner validation gate fails closed until an authoritative source is
    /// imported.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261129000000_RestoreListeningPartBCSourceStems")]
    public partial class RestoreListeningPartBCSourceStems : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Benchmark Listening Tests.pdf, Practice Test 1 (Nova paper).
            // These prompts were transcribed from the source PDF; they are
            // deliberately stored in the normalized relational question data
            // rather than reconstructed by the candidate-facing components.
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestions""
                SET ""Stem"" = CASE ""QuestionNumber""
                    WHEN 25 THEN 'You hear a conversation between a GP and a patient about recurring diarrhoea. What does the GP ask the patient to confirm?'
                    WHEN 26 THEN 'You hear a physician in the A&E department talking with a patient presenting with abdominal pain. The physician wants to deal with the patient’s dehydration by...'
                    WHEN 27 THEN 'You hear a ward briefing about a newly admitted patient. What does the doctor conducting the briefing want to confirm?'
                    WHEN 28 THEN 'You hear part of a training on communication between providers. What will be the priority of the new role discussed?'
                    WHEN 29 THEN 'You hear a trainee doctor discussing stress with a senior GP. What does the senior doctor identify as the cause of the problem in general practice?'
                    WHEN 30 THEN 'You hear a conversation between a nurse and a doctor about scheduling appointments. Why did the nurse mention that she had already spent an hour on the task?'
                    WHEN 31 THEN 'The speaker points out that many of the foot conditions people suffer from…'
                    WHEN 32 THEN 'What is the main purpose of the project?'
                    WHEN 33 THEN 'What types of patients were deemed suitable for self-care?'
                    WHEN 34 THEN 'What was offered to patients considered to be low risk?'
                    WHEN 35 THEN 'How did the project result in cost savings?'
                    WHEN 36 THEN 'According to the speaker, the primary benefit to patients was…'
                    WHEN 37 THEN 'What is the current role of the interviewee?'
                    WHEN 38 THEN 'According to the interviewee, what is causing the lack of physician integration?'
                    WHEN 39 THEN 'Why was the interviewer surprised by the interviewee’s initial response about challenges?'
                    WHEN 40 THEN 'According to the interviewee, what is the most serious result of short-staffing?'
                    WHEN 41 THEN 'The interviewee notes that long hours can negatively affect…'
                    WHEN 42 THEN 'What challenge does the interviewee say might be surprising to many?'
                    ELSE ""Stem""
                END
                WHERE ""PaperId"" = '77114cbc020347858619a88928ed0e32'
                  AND ""QuestionNumber"" BETWEEN 25 AND 42;
            ");

            // The same verified PDF is the source of the three learner-facing
            // choices for every Part B/C item. Update existing normalized
            // option rows by their stable A/B/C key; this is deliberately an
            // UPDATE rather than an insert so existing answer/version records
            // and distractor metadata remain intact. A missing option row is
            // still surfaced by the publish validator instead of being
            // fabricated here.
            migrationBuilder.Sql(@"
                WITH source_options(""QuestionNumber"", ""OptionKey"", ""Text"") AS (
                    VALUES
                        (25, 'A', 'Whether she caught the disease in St Petersburgh, Russia.'),
                        (25, 'B', 'Whether she was in the vicinity of a place with the disease recently.'),
                        (25, 'C', 'Whether she had been to the tropics recently as the disease is common there.'),
                        (26, 'A', 'Ordering an x-ray of the patient’s abdomen.'),
                        (26, 'B', 'Having blood drawn in order to run tests.'),
                        (26, 'C', 'Having an IV put in while other tests are run.'),
                        (27, 'A', 'The reason the patient is in pain.'),
                        (27, 'B', 'The reason the condition was triggered.'),
                        (27, 'C', 'The reason the patient’s eyes are sore.'),
                        (28, 'A', 'To improve communication between doctors and patients'),
                        (28, 'B', 'To improve communication between patients and nurses'),
                        (28, 'C', 'To improve communication between doctors and nurses.'),
                        (29, 'A', 'The unwillingness of younger GPs to work locally or full time.'),
                        (29, 'B', 'The number of doctors opting to go into general practice.'),
                        (29, 'C', 'The difficulty with balancing the long days with family life.'),
                        (30, 'A', 'To show that she had already put in reasonable effort before asking for help.'),
                        (30, 'B', 'To show that she does not have enough information to complete the task quickly.'),
                        (30, 'C', 'To show that she is also feeling the effect of the hospital being short-handed at the moment.'),
                        (31, 'A', 'Are more common among the elderly.'),
                        (31, 'B', 'Do not necessitate medical intervention.'),
                        (31, 'C', 'Can be dangerous if handled by someone without skill or knowledge.'),
                        (32, 'A', 'To save money by empowering patients with minor conditions'),
                        (32, 'B', 'To discharge as many patients as possible for the podiatry service'),
                        (32, 'C', 'To focus all of the podiatry service’s efforts on high risk conditions'),
                        (33, 'A', 'Those with high risk medical or podiatric needs'),
                        (33, 'B', 'Those able to take care of low risk conditions'),
                        (33, 'C', 'Those with a medically-trained family member'),
                        (34, 'A', 'Small group educational sessions'),
                        (34, 'B', 'One-to-one assessment appointments'),
                        (34, 'C', 'Small group assessment sessions'),
                        (35, 'A', 'By reducing the total number of patients seen by podiatrists'),
                        (35, 'B', 'By reducing the number of podiatrists seeing patients'),
                        (35, 'C', 'By reducing the number of appointments involving low risk patients'),
                        (36, 'A', 'The quality of care that podiatry patients received'),
                        (36, 'B', 'The amount of time patients had to wait to be seen'),
                        (36, 'C', 'The level of satisfaction of all podiatry patients'),
                        (37, 'A', 'A leader of an organization that works to promote nursing'),
                        (37, 'B', 'A nurse with experience working with elderly patients'),
                        (37, 'C', 'A health practitioner with a background in emergency medicine'),
                        (38, 'A', 'The poor communications systems in place'),
                        (38, 'B', 'The lack of permanent physicians at hospitals'),
                        (38, 'C', 'The integrated care model recently adopted'),
                        (39, 'A', 'Because he does not agree with what she has said.'),
                        (39, 'B', 'Because he has information that contradicts what she said.'),
                        (39, 'C', 'Because he has heard information about another problem.'),
                        (40, 'A', 'Job satisfaction and burnout of nurses'),
                        (40, 'B', 'Significant turnover in health systems'),
                        (40, 'C', 'Increased risks to patient safety'),
                        (41, 'A', 'The mental abilities of nurses'),
                        (41, 'B', 'The physical abilities of nurses'),
                        (41, 'C', 'The family and social life of nurses'),
                        (42, 'A', 'Domestic violence'),
                        (42, 'B', 'Bullying by physicians'),
                        (42, 'C', 'Bullying by patients')
                )
                UPDATE ""ListeningQuestionOptions"" AS lqo
                SET ""Text"" = source_options.""Text""
                FROM ""ListeningQuestions"" AS lq,
                     source_options
                WHERE lqo.""ListeningQuestionId"" = lq.""Id""
                  AND source_options.""QuestionNumber"" = lq.""QuestionNumber""
                  AND source_options.""OptionKey"" = lqo.""OptionKey""
                  AND lq.""PaperId"" = '77114cbc020347858619a88928ed0e32'
                  AND lq.""QuestionNumber"" BETWEEN 25 AND 42;
            ");

            // Keep legacy records from exposing a PDF sentinel or an invented
            // generic prompt. A blank stem is intentional: the structure gate
            // reports the record as invalid until source content is supplied.
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestions""
                SET ""Stem"" = ''
                WHERE ""QuestionNumber"" BETWEEN 25 AND 42
                  AND LOWER(BTRIM(""Stem"")) IN ('see pdf', 'cpdf', 'pdf', 'view pdf', '')
                  AND LOWER(BTRIM(""Stem"")) NOT IN (
                      'you hear a conversation between a gp and a patient about recurring diarrhoea. what does the gp ask the patient to confirm?',
                      'you hear a physician in the a&e department talking with a patient presenting with abdominal pain. the physician wants to deal with the patient’s dehydration by...',
                      'you hear a ward briefing about a newly admitted patient. what does the doctor conducting the briefing want to confirm?',
                      'you hear part of a training on communication between providers. what will be the priority of the new role discussed?',
                      'you hear a trainee doctor discussing stress with a senior gp. what does the senior doctor identify as the cause of the problem in general practice?',
                      'you hear a conversation between a nurse and a doctor about scheduling appointments. why did the nurse mention that she had already spent an hour on the task?'
                  );

                UPDATE ""ListeningQuestions""
                SET ""Stem"" = ''
                WHERE ""QuestionNumber"" BETWEEN 25 AND 42
                  AND (
                      LOWER(BTRIM(""Stem"")) LIKE 'what does the speaker identify as the main clinical priority%'
                      OR LOWER(BTRIM(""Stem"")) LIKE 'what is the speaker%main point in this extract%'
                  )
                  AND ""PaperId"" <> '77114cbc020347858619a88928ed0e32';
            ");

            // Remove extraction artifacts from persisted options. Runtime
            // sanitation remains in place for older rows that are not migrated.
            migrationBuilder.Sql(@"
                UPDATE ""ListeningQuestionOptions""
                SET ""Text"" = REGEXP_REPLACE(""Text"", '\s*[-=]{2,}\s*PAGE\s*\d+\s*[-=]{2,}', '', 'gi')
                WHERE ""Text"" ~* '[-=]{2,}\s*PAGE\s*\d+\s*[-=]{2,}';

                UPDATE ""ListeningQuestionOptions""
                SET ""Text"" = REGEXP_REPLACE(""Text"", '\s*PAGE\s*\d+', '', 'gi')
                WHERE ""Text"" ~* 'PAGE\s+\d+';

                UPDATE ""ListeningQuestionOptions""
                SET ""Text"" = REGEXP_REPLACE(""Text"", '\s*o\s*Practice Test\s*\d+\s*:?', '', 'gi')
                WHERE ""Text"" ~* 'Practice Test\s*\d+';

                UPDATE ""ListeningQuestionOptions""
                SET ""Text"" = REPLACE(""Text"", CHR(61623), '')
                WHERE ""Text"" LIKE '%' || CHR(61623) || '%';
            ");

            // Keep the JSON-authored fallback in sync for the verified paper.
            // jsonb_set preserves every unrelated extracted-content key and
            // the original question order.
            migrationBuilder.Sql(@"
                UPDATE ""ContentPapers"" AS cp
                SET ""ExtractedTextJson"" = (
                    jsonb_set(
                        cp.""ExtractedTextJson""::jsonb,
                        '{listeningQuestions}',
                        (
                            SELECT jsonb_agg(
                                CASE item->>'number'
                                    WHEN '25' THEN jsonb_set(item, '{stem}', to_jsonb('You hear a conversation between a GP and a patient about recurring diarrhoea. What does the GP ask the patient to confirm?'::text), true)
                                    WHEN '26' THEN jsonb_set(item, '{stem}', to_jsonb('You hear a physician in the A&E department talking with a patient presenting with abdominal pain. The physician wants to deal with the patient’s dehydration by...'::text), true)
                                    WHEN '27' THEN jsonb_set(item, '{stem}', to_jsonb('You hear a ward briefing about a newly admitted patient. What does the doctor conducting the briefing want to confirm?'::text), true)
                                    WHEN '28' THEN jsonb_set(item, '{stem}', to_jsonb('You hear part of a training on communication between providers. What will be the priority of the new role discussed?'::text), true)
                                    WHEN '29' THEN jsonb_set(item, '{stem}', to_jsonb('You hear a trainee doctor discussing stress with a senior GP. What does the senior doctor identify as the cause of the problem in general practice?'::text), true)
                                    WHEN '30' THEN jsonb_set(item, '{stem}', to_jsonb('You hear a conversation between a nurse and a doctor about scheduling appointments. Why did the nurse mention that she had already spent an hour on the task?'::text), true)
                                    WHEN '31' THEN jsonb_set(item, '{stem}', to_jsonb('The speaker points out that many of the foot conditions people suffer from…'::text), true)
                                    WHEN '32' THEN jsonb_set(item, '{stem}', to_jsonb('What is the main purpose of the project?'::text), true)
                                    WHEN '33' THEN jsonb_set(item, '{stem}', to_jsonb('What types of patients were deemed suitable for self-care?'::text), true)
                                    WHEN '34' THEN jsonb_set(item, '{stem}', to_jsonb('What was offered to patients considered to be low risk?'::text), true)
                                    WHEN '35' THEN jsonb_set(item, '{stem}', to_jsonb('How did the project result in cost savings?'::text), true)
                                    WHEN '36' THEN jsonb_set(item, '{stem}', to_jsonb('According to the speaker, the primary benefit to patients was…'::text), true)
                                    WHEN '37' THEN jsonb_set(item, '{stem}', to_jsonb('What is the current role of the interviewee?'::text), true)
                                    WHEN '38' THEN jsonb_set(item, '{stem}', to_jsonb('According to the interviewee, what is causing the lack of physician integration?'::text), true)
                                    WHEN '39' THEN jsonb_set(item, '{stem}', to_jsonb('Why was the interviewer surprised by the interviewee’s initial response about challenges?'::text), true)
                                    WHEN '40' THEN jsonb_set(item, '{stem}', to_jsonb('According to the interviewee, what is the most serious result of short-staffing?'::text), true)
                                    WHEN '41' THEN jsonb_set(item, '{stem}', to_jsonb('The interviewee notes that long hours can negatively affect…'::text), true)
                                    WHEN '42' THEN jsonb_set(item, '{stem}', to_jsonb('What challenge does the interviewee say might be surprising to many?'::text), true)
                                    ELSE item
                                END
                                ORDER BY ordinality
                            )
                            FROM jsonb_array_elements(cp.""ExtractedTextJson""::jsonb->'listeningQuestions')
                                WITH ORDINALITY AS q(item, ordinality)
                        ),
                        true
                    )
                )::text
                WHERE cp.""Id"" = '77114cbc020347858619a88928ed0e32'
                  AND jsonb_typeof(cp.""ExtractedTextJson""::jsonb->'listeningQuestions') = 'array';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally irreversible. Restoring sentinel/generic stems
            // would reintroduce the release-blocking candidate defect.
        }
    }
}
