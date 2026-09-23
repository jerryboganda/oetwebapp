using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>DOC-13 — Innovation Timeline, Release History &amp; R&amp;D Record.</summary>
internal static class Doc13InnovationTimeline
{
    public static DocumentationModuleSeed Build() => new(
        Code: "DOC-13",
        Title: "Innovation Timeline, Release History & R&D Record",
        Description: "A dated, sourced chronology of engineering milestones, releases, owner governance decisions and continuing R&D, built from the repository's own dated records rather than recollection.",
        SortOrder: 13,
        Sections:
        [
            new DocumentationSectionBlock(
                "How this timeline is sourced",
                "Every date below is taken directly from a dated entry in the repository's own working records — " +
                "the always-loaded contributor contract (`AGENTS.md`), the project's continuity log " +
                "(`.github/agent-state.local.md`), the release changelog (`CHANGELOG.md`), the security control " +
                "register, and the git commit history itself — rather than from memory or estimation. Where a " +
                "commit hash and date are cited, they were read directly from `git log` output against this " +
                "checkout. This document does not claim completeness: it reports what is dated and checkable, and " +
                "omits anything that cannot be pinned to a real source."),
            new DocumentationSectionBlock(
                "Foundation: production infrastructure and release discipline (June–July 2026)",
                "The platform's production storage layer — the named Docker volumes holding papers, media, user " +
                "data and backups — was created on 2026-06-03 under the project name `oetwebsite`, and its exact " +
                "live volume names are still the ones documented today (EV-TIMELINE-001). On 2026-07-05 the " +
                "product owner issued the platform's standing release policy, the \"Ship-It Workflow\": every " +
                "development task is owned end-to-end by the engineer making the change, through to a verified " +
                "live deployment, rather than stopping at \"pushed\" (EV-TIMELINE-002). That directive was not " +
                "static — it was tightened on 2026-08-24 to close a gap where an agent could report a task done " +
                "without having watched the deploy through to a healthy result (EV-TIMELINE-002)."),
            new DocumentationSectionBlock(
                "August 2026: incident-driven governance and a production AI gateway",
                "On 2026-08-24, an Antigravity-based AI gateway (v0.2) reached production, deployed as commit " +
                "`451e7bb6c`, initially live in a degraded standby mode pending a production API key " +
                "(EV-TIMELINE-003) — an example of shipping infrastructure ahead of activation, deliberately " +
                "gated rather than exposed half-configured. On 2026-09-05 the owner issued a hard rule for Android " +
                "releases requiring every previously-live Play Store track to move together, after a real " +
                "incident left Closed Testing users stuck seeing \"Open\" instead of \"Update\" on a stale build " +
                "(EV-TIMELINE-004). This is a representative pattern across the record: a production incident is " +
                "traced to a root cause, and the fix is codified as a standing rule rather than a one-off patch."),
            new DocumentationSectionBlock(
                "Early September 2026: a release-cadence rule and a real-money-safe grading proof",
                "On 2026-09-04 the owner issued a standing definition of what \"cut app releases\" means for this " +
                "platform: any agent hearing that instruction must cut all three native pathways together — " +
                "Android, iOS and the Windows desktop updater — rather than treating it as a single-platform " +
                "request, and this was written down as a canonical playbook rather than left as tacit knowledge " +
                "(EV-TIMELINE-015). The following two days produced one of the record's clearest pieces of testing " +
                "rigor: a live production catalogue scan on 2026-09-05 against real Writing task data found 194 " +
                "published tasks with zero yet meeting every publish gate, and on 2026-09-06 a genuine end-to-end " +
                "Writing submission was exercised on a live learner account — an empty submission returned 201, " +
                "was graded, and its report was retrievable; a normal submission was independently recovered to a " +
                "real band-D grade; a retried submission resolved to the identical grade with zero duplicate credit " +
                "debit (EV-TIMELINE-016). This is a concrete example of the platform validating a financially-" +
                "sensitive flow (AI-graded, credit-metered submissions) against production data and real money " +
                "logic before declaring it safe, rather than relying on unit tests alone."),
            new DocumentationSectionBlock(
                "September 2026: compute policy and cross-platform hardening",
                "On 2026-09-11 the owner issued a non-negotiable rule restricting all build/test/verification " +
                "compute to GitHub Actions, explicitly to protect the production VPS and local development " +
                "machines from workload it should never carry (EV-TIMELINE-005). The following day, 2026-09-12, " +
                "a cross-platform Apple compatibility hardening effort (PR #220) merged as commit `d7c0c67db`: it " +
                "raised the declared iOS support floor from 14.0 to 16.4 to match what the actual web stack " +
                "requires, and its CI run included the iOS build-and-simulator-launch job executing for the first " +
                "time in the project's history rather than being skipped (EV-TIMELINE-006)."),
            new DocumentationSectionBlock(
                "13–17 September 2026: five successive rounds of clinical-content governance",
                "A distinct, well-documented R&D thread is the platform's Writing Model Answer validator, which " +
                "was hardened through five dated, numbered rounds of owner-reviewed clinical-writing rules in one " +
                "week. Rounds one and two (OA-01..OA-15, OA2-01..OA2-20) landed on 2026-09-13 and 2026-09-14, " +
                "governing register, dosing-frequency wording, and letter-closure structure (EV-TIMELINE-007). A " +
                "third round (OA3-01..OA3-05) followed on 2026-09-15, a Senior Assessor Release Audit " +
                "(OA5-01..OA5-38) on 2026-09-16, and a cross-model audit (OA6-01..OA6-02) on 2026-09-17 — each " +
                "round shipped against its own named, versioned validator (for example " +
                "`writing-rules.senior-assessor-audit.2026-09-16.1`, rule pack `2.4.0-senior-assessor-audit`), so " +
                "every graded submission can be traced to the exact rule set it was checked against " +
                "(EV-TIMELINE-008). In parallel, the same week's cross-platform parity release " +
                "(`R-2026-09-14-PARITY-2`) cut synchronized builds across all three native shells — Android 1.4.14 " +
                "(versionCode 9), iOS 1.4.14 (build 9), and Windows desktop 0.7.7 — from the same underlying web " +
                "deploy (EV-TIMELINE-009)."),
            new DocumentationSectionBlock(
                "Late September 2026: governance simplification and staged feature rollout",
                "On 2026-09-22 the owner made two governance decisions in the same window. First, the CI operating " +
                "model itself changed: rather than treating a private-repo Actions billing block as an escalation, " +
                "the standing rule became \"flip the repo public, then use GitHub Actions\" as the default path, " +
                "with the private self-hosted runner demoted to an optional fallback (EV-TIMELINE-010). Second, a " +
                "security-control simplification was made and recorded in the security control register: " +
                "per-action authenticator step-up for admins was removed by owner directive, while the separation-" +
                "of-duties permission split was explicitly retained, shipped as commit `98b43697a` " +
                "(EV-TIMELINE-011). The same date's changelog entry records a Free Mocks program — free, credit-" +
                "free sample attempts across Listening, Reading, Writing and Speaking — built and merged but " +
                "deliberately dark-launched behind a `free_samples_enabled` feature flag rather than switched on " +
                "for every learner immediately (EV-TIMELINE-012). The following day, 2026-09-23, three further " +
                "pull requests merged in sequence: the Speaking free-mocks feature's final form (#241), a fix for " +
                "a production Speaking session-creation failure caused by an undersized database column (#242), " +
                "and a fix restoring grading for six allied-health Speaking professions that had no rulebook at " +
                "all (#243) (EV-TIMELINE-013)."),
            new DocumentationSectionBlock(
                "Commit volume as a proxy for R&D intensity",
                "Counted directly from this checkout's own git history (`git log --since=2026-07-01 " +
                "--until=2026-09-24 --oneline`), 1,325 commits were recorded against the repository's main " +
                "development history between 1 July and 24 September 2026 (EV-TIMELINE-014). That is not a " +
                "one-off release cut; it is the volume produced by the pattern shown throughout this timeline — " +
                "dated owner rulings, root-caused production fixes, and versioned validator rounds landing " +
                "continuously across roughly twelve weeks. The record in `.github/agent-state.local.md` and " +
                "`CHANGELOG.md` is a living one and was still being actively updated as of the date this pack was " +
                "compiled, so this timeline should be read as a snapshot of continuing R&D rather than a closed " +
                "history. For context, the same checkout's full commit history (`git log --oneline --all`) runs to " +
                "3,011 commits, meaning the twelve-week window above accounts for well over a third of the " +
                "project's entire recorded engineering history to date (EV-TIMELINE-017)."),
            new DocumentationSectionBlock(
                "A consistent pattern across the record: incident, root cause, standing rule",
                "Read end to end, this timeline is not a list of unrelated events — it shows one repeated " +
                "engineering habit. A concrete production incident occurs (Closed Testing users stuck on a stale " +
                "Android build; a Writing catalogue that looked publishable but failed every runtime gate; a " +
                "billing/Actions block mid-release); the team traces it to a specific, named root cause rather than " +
                "patching the symptom (a missing cross-track sync step; a validator and its runtime check reading " +
                "two different sources of truth; a CI provider's private-repo billing policy); and the fix is " +
                "captured as a standing, dated, owner-reviewed rule rather than left as a one-off patch " +
                "(EV-TIMELINE-004, EV-TIMELINE-010, EV-TIMELINE-016). The same discipline applies in the other " +
                "direction, too, when a control turns out to be unnecessary friction rather than protection: the " +
                "2026-09-22 removal of per-action authenticator step-up for admins was not a silent rollback but a " +
                "recorded, reasoned owner decision, with the compensating control (separation-of-duties permission " +
                "checks) explicitly named as retained (EV-TIMELINE-011). That combination — willing to add rules " +
                "under evidence, and willing to remove them under evidence — is the throughline of this record."),
        ],
        Evidence:
        [
            new DocumentationEvidenceSeed("EV-TIMELINE-001", DocumentationEvidenceType.DataKnowledge,
                "Production storage volumes created 2026-06-03 under project oetwebsite, with the live volume names still documented as current.",
                "AGENTS.md, section \"Storage Persistence\""),
            new DocumentationEvidenceSeed("EV-TIMELINE-002", DocumentationEvidenceType.DataKnowledge,
                "The Ship-It Workflow release policy, dated 2026-07-05 and tightened 2026-08-24.",
                "AGENTS.md, section \"Ship-It Workflow — COMPULSORY (owner directive 2026-07-05, tightened 2026-08-24)\""),
            new DocumentationEvidenceSeed("EV-TIMELINE-003", DocumentationEvidenceType.DataKnowledge,
                "Antigravity AI gateway v0.2 production deployment (commit 451e7bb6c, 2026-08-24), initially live in degraded standby pending a production API key.",
                ".github/agent-state.local.md (\"Antigravity integration DEPLOYED TO PRODUCTION\" entry)"),
            new DocumentationEvidenceSeed("EV-TIMELINE-004", DocumentationEvidenceType.DataKnowledge,
                "Owner rule (2026-09-05) requiring every previously-live Android release track to move together, issued after Closed Testing users were left stuck on a stale build.",
                "AGENTS.md, section \"OET Domain Invariants\" (App releases bullet)"),
            new DocumentationEvidenceSeed("EV-TIMELINE-005", DocumentationEvidenceType.DataKnowledge,
                "Owner directive (2026-09-11) restricting all build/test/verification compute to GitHub Actions.",
                "AGENTS.md, section \"GITHUB ACTIONS IS THE ONLY AUTHORIZED COMPUTE ENVIRONMENT\""),
            new DocumentationEvidenceSeed("EV-TIMELINE-006", DocumentationEvidenceType.Testing,
                "Apple compatibility hardening (PR #220) merged 2026-09-12 as commit d7c0c67db, raising the iOS floor to 16.4 and running the iOS build/simulator CI job for the first time.",
                "git log (commit d7c0c67db, 2026-09-12); .github/agent-state.local.md (\"Apple platform compatibility hardening\" entry)"),
            new DocumentationEvidenceSeed("EV-TIMELINE-007", DocumentationEvidenceType.DataKnowledge,
                "Writing Model Answer owner-clarification rounds one and two (OA-01..OA-15, OA2-01..OA2-20), dated 2026-09-13 and 2026-09-14.",
                "AGENTS.md, section \"OET Writing Model Answers — COMPULSORY (owner directives 2026-09-13 + 2026-09-14)\""),
            new DocumentationEvidenceSeed("EV-TIMELINE-008", DocumentationEvidenceType.DataKnowledge,
                "Writing Model Answer rounds three through five (OA3, 2026-09-15; Senior Assessor Audit OA5, 2026-09-16; cross-model audit OA6, 2026-09-17), each tied to a named, versioned validator and rule pack.",
                "AGENTS.md, section \"OET Writing Model Answers — COMPULSORY\" (Owner Clarifications Round 3 / Senior Assessor Release Audit / Cross-model audit sub-sections)"),
            new DocumentationEvidenceSeed("EV-TIMELINE-009", DocumentationEvidenceType.Deployment,
                "Cross-platform parity release R-2026-09-14-PARITY-2: Android 1.4.14 (versionCode 9), iOS 1.4.14 (build 9), Windows desktop 0.7.7, cut from the same web deploy.",
                "CHANGELOG.md, section \"[R-2026-09-14-PARITY-2] - 2026-09-14\""),
            new DocumentationEvidenceSeed("EV-TIMELINE-010", DocumentationEvidenceType.DataKnowledge,
                "Owner directive (2026-09-22) making \"flip the repo public, then use GitHub Actions\" the default CI path, with the private self-hosted runner as an optional fallback.",
                "AGENTS.md, section \"GitHub Actions on a public-when-working repo — COMPULSORY (owner directive 2026-09-22)\""),
            new DocumentationEvidenceSeed("EV-TIMELINE-011", DocumentationEvidenceType.Security,
                "Owner-directed removal of per-action authenticator step-up for admins (2026-09-22), separation-of-duties permission split retained; shipped as commit 98b43697a and recorded as a waived control (PAY-20) in the security control register.",
                "git log (commit 98b43697a, 2026-09-22); docs/security/control-register.json (PAY-20 waiver entry)"),
            new DocumentationEvidenceSeed("EV-TIMELINE-012", DocumentationEvidenceType.ProductUi,
                "Free Mocks program (free-sample attempts across Listening/Reading/Writing/Speaking) built and merged, dark-launched behind the free_samples_enabled feature flag.",
                "CHANGELOG.md, section \"[Unreleased] - Free Mocks (2026-09-22)\""),
            new DocumentationEvidenceSeed("EV-TIMELINE-013", DocumentationEvidenceType.Code,
                "Three merged pull requests on 2026-09-23: #241 (Speaking free mocks, final form), #242 (production Speaking session-creation fix, undersized database column), #243 (rulebook fix restoring grading for six allied-health Speaking professions).",
                "git log --oneline -20 (merge commits for PR #241, #242, #243, run against this checkout)"),
            new DocumentationEvidenceSeed("EV-TIMELINE-014", DocumentationEvidenceType.DataKnowledge,
                "1,325 commits recorded on the repository's main development history between 2026-07-01 and 2026-09-24.",
                "git log --since=2026-07-01 --until=2026-09-24 --oneline (run against this checkout; count of returned lines)"),
            new DocumentationEvidenceSeed("EV-TIMELINE-015", DocumentationEvidenceType.DataKnowledge,
                "Owner order (2026-09-04) defining \"cut app releases\" as all three native pathways together (Android, iOS, Windows desktop), written down as a canonical playbook.",
                ".github/agent-state.local.md (\"App release playbook\" entry, \"Owner order 2026-09-04\")"),
            new DocumentationEvidenceSeed("EV-TIMELINE-016", DocumentationEvidenceType.Testing,
                "Live production catalogue scan (2026-09-05, 194 published Writing tasks, 0 initially publish-ready) followed by a genuine end-to-end paid submission test on a live learner account (2026-09-06): empty submission graded and retrievable, a real recovered band-D grade, and a retried submission resolving to the identical grade with zero duplicate credit debit.",
                ".github/agent-state.local.md (\"LIVE CATALOGUE SCAN 2026-09-05\" and \"FINAL DECISION EXECUTION 2026-09-06\" entries)"),
            new DocumentationEvidenceSeed("EV-TIMELINE-017", DocumentationEvidenceType.DataKnowledge,
                "3,011 total commits across this checkout's full recorded history, for scale comparison against the 1,325 counted in the 1 July - 24 September 2026 window.",
                "git log --oneline --all (run against this checkout; count of returned lines)"),
        ]);
}
