using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// 2026-09-09 — Speaking corpus reclassification, the remaining nine professions (physiotherapy, dentistry, optometry, occupational-therapy, dietetics, podiatry, radiography, speech-pathology, veterinary) slice
/// (63 cards, 43 corrected).
///
/// Applies the outcome of a dual-agent independent review of every card's
/// content against the PDF §8A-9 taxonomy (see the audit manifest under
/// docs/speaking-corpus-reclassification/ for the full before/after mapping
/// and per-card reasoning). Two independent reviewers classified every card
/// blind to its prior category; agreements were accepted, disagreements were
/// arbitrated by re-reading the card against the source rules. Genuinely
/// uncertain cards were left as "Other Cards" rather than forced.
///
/// CategorySource becomes 'reviewed' for every row in this slice (the review
/// covered all of them, even where the outcome matched the prior value).
/// CategoryClassifierVersion/CategoryClassifiedAt are cleared — those track
/// the deterministic classifier specifically, not this review pass.
///
/// <para>Down() restores the exact pre-migration PrimaryCategory/
/// SecondaryTagsJson/CategoryNeedsReview/CategorySource captured in the
/// before-state manifest (a true, non-lossy revert of those four columns;
/// CategoryClassifierVersion/CategoryClassifiedAt are not restored to their
/// exact prior values on revert — a disclosed, low-value simplification,
/// since nothing in the app reads them except for display/audit).</para>
///
/// <para>⚠ Postgres-only SQL. The test suite uses SQLite via
/// EnsureCreatedAsync() and builds straight from the model, bypassing
/// migrations entirely.</para>
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260909091800_SpeakingCorpusReclassificationOtherProfessions")]
public partial class SpeakingCorpusReclassificationOtherProfessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
UPDATE "RolePlayCards" AS t
SET "PrimaryCategory" = v.category,
    "SecondaryTagsJson" = v.tags,
    "CategoryNeedsReview" = v.needs_review,
    "CategorySource" = 'reviewed',
    "CategoryClassifierVersion" = NULL,
    "CategoryClassifiedAt" = NULL
FROM (VALUES
  ('rpc-2187503340754c7c9e80daf93c0f74ad', 'First Visit', '[]', FALSE),
  ('rpc-26d1aed1fa154ec4b23404e7ca7bede2', 'First Visit', '[]', FALSE),
  ('rpc-2f367152d30f469bb28a39b739fc397c', 'First Visit', '[]', FALSE),
  ('rpc-3591f581c0f147878747518dc9621236', 'Already Known Patient', '[]', FALSE),
  ('rpc-37ee6c2f63764d529fa22d6f03d8461c', 'First Visit', '["Reluctant"]', FALSE),
  ('rpc-3d6f2602111447ca951258b30fc5a4a9', 'First Visit', '["Reluctant"]', FALSE),
  ('rpc-633a006328c04aba85d7d4aa5b2e2f1b', 'First Visit', '[]', FALSE),
  ('rpc-7c8e9d16959045969f92d67115cc2b28', 'First Visit', '[]', FALSE),
  ('rpc-909a61c04fd34d7f9dc18a0a4ea04820', 'First Visit', '[]', FALSE),
  ('rpc-9203a69f0f7343288445d82e6ba45ff9', 'First Visit', '["Breaking Bad News"]', FALSE),
  ('rpc-93e03cc40647440dad0e7d9d8113639f', 'Already Known Patient', '["Reluctant"]', FALSE),
  ('rpc-ba807e7341514e8d8a3d893a81f56d43', 'First Visit', '["Breaking Bad News"]', FALSE),
  ('rpc-bc0227f5b1cc4eafa70a159df0bc899c', 'Already Known Patient', '[]', FALSE),
  ('rpc-c0be88996bb04839b2043a0e079f99d3', 'First Visit', '[]', FALSE),
  ('rpc-c8422a1e02ca47e2b48b53c43f023d9f', 'Already Known Patient', '["Reluctant"]', FALSE),
  ('rpc-c9e424d73f42412fbdb9609169d8c4f5', 'First Visit', '[]', FALSE),
  ('rpc-d171456ecd304a14820a6b4da621241c', 'First Visit', '[]', FALSE),
  ('rpc-dc4a35f75ca94425ab6792c7869ec5ce', 'First Visit', '[]', FALSE),
  ('rpc-dc919877509e4c14ad71408ee2a90efb', 'First Visit', '[]', FALSE),
  ('rpc-f37b2758645f40548c5f6243a0e513c0', 'First Visit', '[]', FALSE),
  ('rpc-f72dab57180148b5a2c7dd0448789649', 'First Visit', '[]', FALSE),
  ('rpc-c4ac7ebd8dc743a4bc4f7bea8efe5baf', 'First Visit', '[]', FALSE),
  ('rpc-c9a97910ac154978bab7e596e8c69992', 'First Visit', '[]', FALSE),
  ('rpc-d0248f6b1d9b4946a4ef5c21f078f76f', 'First Visit', '[]', FALSE),
  ('rpc-d186271abdc84a05bbcffd7e43bacb13', 'First Visit', '[]', FALSE),
  ('rpc-e4b6f5b83587453991f44e1723ce5488', 'Examination Card', '[]', FALSE),
  ('rpc-e79ca031f9374a72a885831162d177df', 'Examination Card', '["Reluctant"]', FALSE),
  ('rpc-f8804908f707458c8a5e1db31e03aa59', 'Examination Card', '[]', FALSE),
  ('rpc-03b531367e8644b09f99770e61b8e621', 'First Visit', '[]', FALSE),
  ('rpc-5bff22d551dd48028e255e171cf93fc1', 'Already Known Patient', '[]', FALSE),
  ('rpc-cfd3cbb1a93f4d3195e851b80e0ed0e8', 'First Visit', '[]', FALSE),
  ('rpc-e854eefb8654437caca09cefc2586299', 'Second Visit / Follow-up', '[]', FALSE),
  ('rpc-293274e2b4cd457688fd4de45b4f0b6d', 'Already Known Patient', '[]', FALSE),
  ('rpc-3214e682e79f4a869419213487ab2cdd', 'Examination Card', '[]', FALSE),
  ('rpc-5f7ce5aee7f744eda46b274e16a9f0fb', 'First Visit', '[]', FALSE),
  ('rpc-67391990bd8d49ed84a3c1c11486c626', 'Second Visit / Follow-up', '["Reluctant"]', FALSE),
  ('rpc-1ccf8f81ed2c49e09796223d5f7ec6ef', 'Second Visit / Follow-up', '[]', FALSE),
  ('rpc-218d7b4c5c6b4773a092c13e43a41b82', 'Examination Card', '[]', FALSE),
  ('rpc-6b7b1ff26c90497a9d868a9ae57df4e1', 'Second Visit / Follow-up', '["Angry"]', FALSE),
  ('rpc-77fae7f40d5f4ff49a91050caea38c16', 'Emergency / Emergency Department', '[]', FALSE),
  ('rpc-2f933bbe0e624e459160686737cdbd70', 'Reluctant Patient', '[]', FALSE),
  ('rpc-e794917d3d25465daa0aba8440a55eda', 'Second Visit / Follow-up', '["Angry"]', FALSE),
  ('rpc-e1f120aab6ec4d7790290e399bd12ccb', 'First Visit', '[]', FALSE),
  ('rpc-420c37e40ad3411aaaa8008bff15372a', 'Angry Patient', '[]', FALSE),
  ('rpc-65470d900b2a4a56b3fbf7f76e6fc02a', 'Examination Card', '[]', FALSE),
  ('rpc-b85ce4ee76c845a1af079fdd6a68c77b', 'Second Visit / Follow-up', '[]', FALSE),
  ('rpc-ce4a483af2394132bec59c99831f7b1b', 'First Visit', '[]', FALSE),
  ('rpc-e9b25f5e89f147efbd019b7020579236', 'Already Known Patient', '[]', FALSE),
  ('rpc-117e22247e784d77a43ff2422dcf6989', 'First Visit', '["Reluctant"]', FALSE),
  ('rpc-2ed1e586031b4061b48b98de76487d2d', 'First Visit', '[]', FALSE),
  ('rpc-55bd73122b7f4543963681f2f384467a', 'First Visit', '[]', FALSE),
  ('rpc-70a96458e4f1496da789d746964c40bc', 'Examination Card', '["Angry"]', FALSE),
  ('rpc-e0dc78f296a3472fb5d8c4d8a568087f', 'First Visit', '[]', FALSE),
  ('rpc-1bce538aef8b4f9f9bd195ac176057ae', 'Already Known Patient', '[]', FALSE),
  ('rpc-35291272ace24294a35e7bf9962af3df', 'Reluctant Patient', '[]', FALSE),
  ('rpc-8ed74f03c7d0429e84973ba0380ef891', 'Examination Card', '["Reluctant"]', FALSE),
  ('rpc-8f10569ac5c843329f31a2e1cdb27076', 'Second Visit / Follow-up', '[]', FALSE),
  ('rpc-98f1ffc80d7a4f0782dfd9b811c45804', 'First Visit', '[]', FALSE),
  ('rpc-062f9977c1af4fb3836319bf0e8b66fb', 'Second Visit / Follow-up', '[]', FALSE),
  ('rpc-278d5222f8ec4b85b16a1392d5c6d2ea', 'Examination Card', '[]', FALSE),
  ('rpc-ad34d5a50aba42bbb9d5a9552e627ee7', 'Already Known Patient', '[]', FALSE),
  ('rpc-f2aceabd22f74f9f96dd6d20b0a4415a', 'Angry Patient', '[]', FALSE),
  ('rpc-f2def12192a841498776a20570bcd75e', 'First Visit', '[]', FALSE)
) AS v(id, category, tags, needs_review)
WHERE t."Id" = v.id;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
UPDATE "RolePlayCards" AS t
SET "PrimaryCategory" = v.category,
    "SecondaryTagsJson" = v.tags,
    "CategoryNeedsReview" = v.needs_review
FROM (VALUES
  ('rpc-2187503340754c7c9e80daf93c0f74ad', 'Other Cards', '[]', TRUE),
  ('rpc-26d1aed1fa154ec4b23404e7ca7bede2', 'Second Visit / Follow-up', '[]', FALSE),
  ('rpc-2f367152d30f469bb28a39b739fc397c', 'Already Known Patient', '["Angry"]', FALSE),
  ('rpc-3591f581c0f147878747518dc9621236', 'Already Known Patient', '[]', FALSE),
  ('rpc-37ee6c2f63764d529fa22d6f03d8461c', 'Other Cards', '[]', TRUE),
  ('rpc-3d6f2602111447ca951258b30fc5a4a9', 'Already Known Patient', '[]', FALSE),
  ('rpc-633a006328c04aba85d7d4aa5b2e2f1b', 'Already Known Patient', '[]', FALSE),
  ('rpc-7c8e9d16959045969f92d67115cc2b28', 'Other Cards', '[]', TRUE),
  ('rpc-909a61c04fd34d7f9dc18a0a4ea04820', 'Already Known Patient', '[]', FALSE),
  ('rpc-9203a69f0f7343288445d82e6ba45ff9', 'Already Known Patient', '["Angry"]', FALSE),
  ('rpc-93e03cc40647440dad0e7d9d8113639f', 'Already Known Patient', '[]', FALSE),
  ('rpc-ba807e7341514e8d8a3d893a81f56d43', 'Already Known Patient', '["Angry"]', FALSE),
  ('rpc-bc0227f5b1cc4eafa70a159df0bc899c', 'Other Cards', '[]', TRUE),
  ('rpc-c0be88996bb04839b2043a0e079f99d3', 'Other Cards', '[]', TRUE),
  ('rpc-c8422a1e02ca47e2b48b53c43f023d9f', 'Already Known Patient', '[]', FALSE),
  ('rpc-c9e424d73f42412fbdb9609169d8c4f5', 'Other Cards', '[]', TRUE),
  ('rpc-d171456ecd304a14820a6b4da621241c', 'Other Cards', '[]', TRUE),
  ('rpc-dc4a35f75ca94425ab6792c7869ec5ce', 'Other Cards', '[]', TRUE),
  ('rpc-dc919877509e4c14ad71408ee2a90efb', 'Other Cards', '[]', TRUE),
  ('rpc-f37b2758645f40548c5f6243a0e513c0', 'Other Cards', '[]', TRUE),
  ('rpc-f72dab57180148b5a2c7dd0448789649', 'Other Cards', '[]', TRUE),
  ('rpc-c4ac7ebd8dc743a4bc4f7bea8efe5baf', 'Other Cards', '[]', TRUE),
  ('rpc-c9a97910ac154978bab7e596e8c69992', 'First Visit', '[]', FALSE),
  ('rpc-d0248f6b1d9b4946a4ef5c21f078f76f', 'Second Visit / Follow-up', '[]', FALSE),
  ('rpc-d186271abdc84a05bbcffd7e43bacb13', 'Already Known Patient', '[]', FALSE),
  ('rpc-e4b6f5b83587453991f44e1723ce5488', 'Examination Card', '[]', FALSE),
  ('rpc-e79ca031f9374a72a885831162d177df', 'Examination Card', '[]', FALSE),
  ('rpc-f8804908f707458c8a5e1db31e03aa59', 'Second Visit / Follow-up', '["Angry"]', FALSE),
  ('rpc-03b531367e8644b09f99770e61b8e621', 'Already Known Patient', '["Reluctant"]', FALSE),
  ('rpc-5bff22d551dd48028e255e171cf93fc1', 'Already Known Patient', '[]', FALSE),
  ('rpc-cfd3cbb1a93f4d3195e851b80e0ed0e8', 'First Visit', '[]', FALSE),
  ('rpc-e854eefb8654437caca09cefc2586299', 'Second Visit / Follow-up', '[]', FALSE),
  ('rpc-293274e2b4cd457688fd4de45b4f0b6d', 'Already Known Patient', '[]', FALSE),
  ('rpc-3214e682e79f4a869419213487ab2cdd', 'Already Known Patient', '[]', FALSE),
  ('rpc-5f7ce5aee7f744eda46b274e16a9f0fb', 'First Visit', '[]', FALSE),
  ('rpc-67391990bd8d49ed84a3c1c11486c626', 'Second Visit / Follow-up', '[]', FALSE),
  ('rpc-1ccf8f81ed2c49e09796223d5f7ec6ef', 'Already Known Patient', '["Angry"]', FALSE),
  ('rpc-218d7b4c5c6b4773a092c13e43a41b82', 'Examination Card', '[]', FALSE),
  ('rpc-6b7b1ff26c90497a9d868a9ae57df4e1', 'Angry Patient', '[]', FALSE),
  ('rpc-77fae7f40d5f4ff49a91050caea38c16', 'Already Known Patient', '[]', FALSE),
  ('rpc-2f933bbe0e624e459160686737cdbd70', 'Reluctant Patient', '[]', FALSE),
  ('rpc-e794917d3d25465daa0aba8440a55eda', 'Angry Patient', '[]', FALSE),
  ('rpc-e1f120aab6ec4d7790290e399bd12ccb', 'Other Cards', '[]', TRUE),
  ('rpc-420c37e40ad3411aaaa8008bff15372a', 'Other Cards', '[]', TRUE),
  ('rpc-65470d900b2a4a56b3fbf7f76e6fc02a', 'Examination Card', '[]', FALSE),
  ('rpc-b85ce4ee76c845a1af079fdd6a68c77b', 'Second Visit / Follow-up', '[]', FALSE),
  ('rpc-ce4a483af2394132bec59c99831f7b1b', 'Already Known Patient', '[]', FALSE),
  ('rpc-e9b25f5e89f147efbd019b7020579236', 'Already Known Patient', '[]', FALSE),
  ('rpc-117e22247e784d77a43ff2422dcf6989', 'Already Known Patient', '[]', FALSE),
  ('rpc-2ed1e586031b4061b48b98de76487d2d', 'Other Cards', '[]', TRUE),
  ('rpc-55bd73122b7f4543963681f2f384467a', 'Other Cards', '[]', TRUE),
  ('rpc-70a96458e4f1496da789d746964c40bc', 'Second Visit / Follow-up', '[]', FALSE),
  ('rpc-e0dc78f296a3472fb5d8c4d8a568087f', 'Already Known Patient', '[]', FALSE),
  ('rpc-1bce538aef8b4f9f9bd195ac176057ae', 'Already Known Patient', '[]', FALSE),
  ('rpc-35291272ace24294a35e7bf9962af3df', 'Already Known Patient', '["Reluctant"]', FALSE),
  ('rpc-8ed74f03c7d0429e84973ba0380ef891', 'Second Visit / Follow-up', '[]', FALSE),
  ('rpc-8f10569ac5c843329f31a2e1cdb27076', 'Other Cards', '[]', TRUE),
  ('rpc-98f1ffc80d7a4f0782dfd9b811c45804', 'First Visit', '[]', FALSE),
  ('rpc-062f9977c1af4fb3836319bf0e8b66fb', 'Second Visit / Follow-up', '[]', FALSE),
  ('rpc-278d5222f8ec4b85b16a1392d5c6d2ea', 'Examination Card', '[]', FALSE),
  ('rpc-ad34d5a50aba42bbb9d5a9552e627ee7', 'Already Known Patient', '[]', FALSE),
  ('rpc-f2aceabd22f74f9f96dd6d20b0a4415a', 'Angry Patient', '[]', FALSE),
  ('rpc-f2def12192a841498776a20570bcd75e', 'First Visit', '[]', FALSE)
) AS v(id, category, tags, needs_review)
WHERE t."Id" = v.id;
""");
        migrationBuilder.Sql("""
UPDATE "RolePlayCards" AS t
SET "CategorySource" = v.source
FROM (VALUES
  ('rpc-2187503340754c7c9e80daf93c0f74ad', 'classifier'),
  ('rpc-26d1aed1fa154ec4b23404e7ca7bede2', 'legacy'),
  ('rpc-2f367152d30f469bb28a39b739fc397c', 'legacy'),
  ('rpc-3591f581c0f147878747518dc9621236', 'legacy'),
  ('rpc-37ee6c2f63764d529fa22d6f03d8461c', 'classifier'),
  ('rpc-3d6f2602111447ca951258b30fc5a4a9', 'legacy'),
  ('rpc-633a006328c04aba85d7d4aa5b2e2f1b', 'legacy'),
  ('rpc-7c8e9d16959045969f92d67115cc2b28', 'classifier'),
  ('rpc-909a61c04fd34d7f9dc18a0a4ea04820', 'legacy'),
  ('rpc-9203a69f0f7343288445d82e6ba45ff9', 'legacy'),
  ('rpc-93e03cc40647440dad0e7d9d8113639f', 'legacy'),
  ('rpc-ba807e7341514e8d8a3d893a81f56d43', 'legacy'),
  ('rpc-bc0227f5b1cc4eafa70a159df0bc899c', 'classifier'),
  ('rpc-c0be88996bb04839b2043a0e079f99d3', 'classifier'),
  ('rpc-c8422a1e02ca47e2b48b53c43f023d9f', 'legacy'),
  ('rpc-c9e424d73f42412fbdb9609169d8c4f5', 'classifier'),
  ('rpc-d171456ecd304a14820a6b4da621241c', 'classifier'),
  ('rpc-dc4a35f75ca94425ab6792c7869ec5ce', 'classifier'),
  ('rpc-dc919877509e4c14ad71408ee2a90efb', 'classifier'),
  ('rpc-f37b2758645f40548c5f6243a0e513c0', 'classifier'),
  ('rpc-f72dab57180148b5a2c7dd0448789649', 'classifier'),
  ('rpc-c4ac7ebd8dc743a4bc4f7bea8efe5baf', 'classifier'),
  ('rpc-c9a97910ac154978bab7e596e8c69992', 'classifier'),
  ('rpc-d0248f6b1d9b4946a4ef5c21f078f76f', 'legacy'),
  ('rpc-d186271abdc84a05bbcffd7e43bacb13', 'legacy'),
  ('rpc-e4b6f5b83587453991f44e1723ce5488', 'legacy'),
  ('rpc-e79ca031f9374a72a885831162d177df', 'legacy'),
  ('rpc-f8804908f707458c8a5e1db31e03aa59', 'legacy'),
  ('rpc-03b531367e8644b09f99770e61b8e621', 'legacy'),
  ('rpc-5bff22d551dd48028e255e171cf93fc1', 'legacy'),
  ('rpc-cfd3cbb1a93f4d3195e851b80e0ed0e8', 'classifier'),
  ('rpc-e854eefb8654437caca09cefc2586299', 'legacy'),
  ('rpc-293274e2b4cd457688fd4de45b4f0b6d', 'legacy'),
  ('rpc-3214e682e79f4a869419213487ab2cdd', 'legacy'),
  ('rpc-5f7ce5aee7f744eda46b274e16a9f0fb', 'classifier'),
  ('rpc-67391990bd8d49ed84a3c1c11486c626', 'legacy'),
  ('rpc-1ccf8f81ed2c49e09796223d5f7ec6ef', 'legacy'),
  ('rpc-218d7b4c5c6b4773a092c13e43a41b82', 'legacy'),
  ('rpc-6b7b1ff26c90497a9d868a9ae57df4e1', 'legacy'),
  ('rpc-77fae7f40d5f4ff49a91050caea38c16', 'legacy'),
  ('rpc-2f933bbe0e624e459160686737cdbd70', 'legacy'),
  ('rpc-e794917d3d25465daa0aba8440a55eda', 'legacy'),
  ('rpc-e1f120aab6ec4d7790290e399bd12ccb', 'classifier'),
  ('rpc-420c37e40ad3411aaaa8008bff15372a', 'classifier'),
  ('rpc-65470d900b2a4a56b3fbf7f76e6fc02a', 'legacy'),
  ('rpc-b85ce4ee76c845a1af079fdd6a68c77b', 'legacy'),
  ('rpc-ce4a483af2394132bec59c99831f7b1b', 'legacy'),
  ('rpc-e9b25f5e89f147efbd019b7020579236', 'legacy'),
  ('rpc-117e22247e784d77a43ff2422dcf6989', 'legacy'),
  ('rpc-2ed1e586031b4061b48b98de76487d2d', 'classifier'),
  ('rpc-55bd73122b7f4543963681f2f384467a', 'classifier'),
  ('rpc-70a96458e4f1496da789d746964c40bc', 'legacy'),
  ('rpc-e0dc78f296a3472fb5d8c4d8a568087f', 'legacy'),
  ('rpc-1bce538aef8b4f9f9bd195ac176057ae', 'legacy'),
  ('rpc-35291272ace24294a35e7bf9962af3df', 'legacy'),
  ('rpc-8ed74f03c7d0429e84973ba0380ef891', 'legacy'),
  ('rpc-8f10569ac5c843329f31a2e1cdb27076', 'classifier'),
  ('rpc-98f1ffc80d7a4f0782dfd9b811c45804', 'legacy'),
  ('rpc-062f9977c1af4fb3836319bf0e8b66fb', 'legacy'),
  ('rpc-278d5222f8ec4b85b16a1392d5c6d2ea', 'legacy'),
  ('rpc-ad34d5a50aba42bbb9d5a9552e627ee7', 'legacy'),
  ('rpc-f2aceabd22f74f9f96dd6d20b0a4415a', 'legacy'),
  ('rpc-f2def12192a841498776a20570bcd75e', 'legacy')
) AS v(id, source)
WHERE t."Id" = v.id;
""");
    }
}
