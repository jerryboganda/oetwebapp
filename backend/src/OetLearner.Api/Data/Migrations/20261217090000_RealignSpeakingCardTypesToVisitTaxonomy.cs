using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Realign <c>SpeakingCardTypes</c> onto the VISIT-TYPE taxonomy that the
    /// rulebooks and the owner's own source card sets already use.
    ///
    /// <para>
    /// Background: <c>SpeakingCardTypeSeed</c> originally bootstrapped a
    /// <i>communication-function</i> taxonomy (Diagnosis / Counselling /
    /// Reassurance / Persuasion / Bad news / Health education). That matched
    /// neither <c>tables.cardTypes</c> in
    /// <c>rulebooks/speaking/{profession}/rulebook.v1.json</c> nor the way the
    /// owner's card sets are organised (First visit, Follow-up, Examination,
    /// Already known patient, Emergency, Breaking bad news). Because the seeder
    /// is a one-off "only when the table is empty" bootstrap, existing
    /// databases would never pick up the corrected list on their own — hence
    /// this migration.
    /// </para>
    ///
    /// <para>Data-safety: this migration is purely additive plus a soft
    /// deactivation. It (1) inserts the six visit-type rows if absent, and
    /// (2) sets <c>IsActive = false</c> on the six legacy communication-function
    /// rows <b>only where no RolePlayCard references them</b>. Nothing is
    /// deleted, and a legacy type that is actually in use stays active so
    /// historical cards keep their label.</para>
    ///
    /// <para>⚠ <b>Postgres-only SQL.</b> The test suite uses SQLite via
    /// <c>EnsureCreatedAsync()</c> and builds straight from the model,
    /// bypassing migrations entirely.</para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261217090000_RealignSpeakingCardTypesToVisitTaxonomy")]
    public partial class RealignSpeakingCardTypesToVisitTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Insert the visit-type taxonomy. ON CONFLICT DO NOTHING keeps
            //    this idempotent and never overwrites an admin's own edits.
            migrationBuilder.Sql(@"
                INSERT INTO ""SpeakingCardTypes""
                    (""Id"", ""Name"", ""Description"", ""SortOrder"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"")
                VALUES
                    ('sct-seed-first-visit-routine', 'First visit — routine',
                     'The patient is presenting for the first time with a non-urgent problem. The candidate takes a history from scratch, establishes the reason for attendance, and works toward a provisional explanation and plan.',
                     1, TRUE, now(), now()),
                    ('sct-seed-first-visit-emergency', 'First visit — emergency',
                     'A first presentation in an urgent or emergency setting. The candidate must calm the patient or relative, gather the critical history quickly, and explain immediate management without losing empathy under time pressure.',
                     2, TRUE, now(), now()),
                    ('sct-seed-follow-up', 'Follow-up (second visit)',
                     'The patient is returning for review of a problem already diagnosed or treated. The candidate checks progress since the last visit, responds to what has changed, and adjusts the plan. Look for ''coming back for'', ''returning for'', ''follow-up'', ''review of'' (see RULE_35).',
                     3, TRUE, now(), now()),
                    ('sct-seed-examination', 'Examination card',
                     'The card requires the candidate to explain, seek consent for, or act on a physical examination — describing what will happen, why it is needed, and what was found.',
                     4, TRUE, now(), now()),
                    ('sct-seed-already-known-patient', 'Already known patient',
                     'The candidate already knows this patient or has their notes to hand, so no full history is needed. The task centres on addressing specific questions, concerns or new information.',
                     5, TRUE, now(), now()),
                    ('sct-seed-breaking-bad-news', 'Breaking bad news',
                     'The candidate delivers serious or unexpected news with appropriate pacing — a warning shot, silence, and emotional support — before moving to next steps (see rulebook section 06).',
                     6, TRUE, now(), now())
                ON CONFLICT (""Id"") DO NOTHING;
            ");

            // 2) Retire the legacy communication-function rows that nothing
            //    points at. Soft-deactivate only — never delete, and never
            //    touch one that a card still references.
            migrationBuilder.Sql(@"
                UPDATE ""SpeakingCardTypes"" t
                   SET ""IsActive"" = FALSE,
                       ""UpdatedAt"" = now()
                 WHERE t.""Id"" IN (
                        'sct-seed-diagnosis',
                        'sct-seed-counselling',
                        'sct-seed-reassurance',
                        'sct-seed-persuasion',
                        'sct-seed-bad-news',
                        'sct-seed-health-education')
                   AND t.""IsActive"" = TRUE
                   AND NOT EXISTS (
                        SELECT 1 FROM ""RolePlayCards"" c WHERE c.""CardTypeId"" = t.""Id"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reactivate the legacy rows and drop the visit-type rows that no
            // card references. Anything an admin has since attached to a card
            // is left alone.
            migrationBuilder.Sql(@"
                UPDATE ""SpeakingCardTypes""
                   SET ""IsActive"" = TRUE, ""UpdatedAt"" = now()
                 WHERE ""Id"" IN (
                        'sct-seed-diagnosis',
                        'sct-seed-counselling',
                        'sct-seed-reassurance',
                        'sct-seed-persuasion',
                        'sct-seed-bad-news',
                        'sct-seed-health-education');
            ");
            migrationBuilder.Sql(@"
                DELETE FROM ""SpeakingCardTypes"" t
                 WHERE t.""Id"" IN (
                        'sct-seed-first-visit-routine',
                        'sct-seed-first-visit-emergency',
                        'sct-seed-follow-up',
                        'sct-seed-examination',
                        'sct-seed-already-known-patient',
                        'sct-seed-breaking-bad-news')
                   AND NOT EXISTS (
                        SELECT 1 FROM ""RolePlayCards"" c WHERE c.""CardTypeId"" = t.""Id"");
            ");
        }
    }
}
