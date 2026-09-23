using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>DOC-07 — Placement Test &amp; Adaptive Assessment Report.</summary>
internal static class Doc07PlacementTest
{
    public static DocumentationModuleSeed Build() => new(
        Code: "DOC-07",
        Title: "Placement Test & Adaptive Assessment Report",
        Description: "Four-skill placement test coverage, scoring and level mapping, controlled-beta status, reliability safeguards, and the candidate result report.",
        SortOrder: 7,
        Sections:
        [
            new DocumentationSectionBlock(
                "Four-skill coverage",
                "The platform's free placement test is served through a server-side gateway, `PlacementGateway`, that " +
                "proxies every candidate action to a dedicated assessment engine while the OET API authenticates the " +
                "learner and enforces access (EV-PLACE-001). The gateway exposes a distinct request surface for each " +
                "of the four skills: a receptive-skills path covering listening and reading (`GetReceptiveResultAsync`, " +
                "`GetResultsStatusAsync`), a Writing path with a draft-save and a submit step per task " +
                "(`GetWritingTasksAsync`, `SaveWritingDraftAsync`, `SubmitWritingAsync`), and a Speaking path that " +
                "issues tasks and accepts an uploaded recording per task (`GetSpeakingTasksAsync`, " +
                "`SubmitSpeakingAsync`, `UploadRecordingAsync`) (EV-PLACE-001). A session's stimulus audio (for the " +
                "listening component) is streamed back to the learner through the same authenticated proxy rather " +
                "than from a public media URL, and a full combined result across all four skills is retrievable " +
                "through `GetFullResultAsync` (EV-PLACE-001)."),
            new DocumentationSectionBlock(
                "Result durability and candidate report",
                "The candidate's placement result is not left solely on the assessment engine. `PlacementResult` is " +
                "an OET-owned table holding one row per completed engine session, storing the engine's full result " +
                "report verbatim as JSON, the ruleset version the session ran under (for provenance — what the " +
                "result was measured with), and a status of either `completed` (all four skills measured) or " +
                "`partial` (the learner stopped early) — the entity's own documentation states explicitly that an " +
                "uneven, partial skill profile is treated as a first-class result, not a failure (EV-PLACE-002). The " +
                "entity's summary describes the stored report as a skill-first profile: a per-skill CEFR-aligned " +
                "status and band, a headline summary, a confidence indicator, and a readiness layer, rather than a " +
                "single blended number (EV-PLACE-002). This durable copy is what the learner's account-linked " +
                "history is built from, independent of the engine's own retention policy for session recordings."),
            new DocumentationSectionBlock(
                "Scoring anchor",
                "Placement reuses the platform's single canonical OET scoring specification rather than an " +
                "independent scale. That specification fixes the shared anchor point across Listening, Reading and " +
                "Speaking — a raw 30/42 on Listening or Reading maps exactly to 350/500 (Grade B), and Speaking's " +
                "pass mark is 350/500 universally, with no country dependency — and states as a hard rule that no " +
                "caller may inline a pass threshold; every comparison must route through the shared helpers, " +
                "`lib/scoring.ts` on the frontend or `OetScoring` on the backend, which are documented as " +
                "behaviourally identical and independently tested (72 and 98 assertions respectively) (EV-PLACE-003). " +
                "Writing's pass mark is explicitly country-aware in the same specification (350/500 for the UK, " +
                "Ireland, Australia, New Zealand and Canada; 300/500 for the USA and Qatar), and a Writing " +
                "determination without a resolved country must surface an explicit `country_required` state rather " +
                "than default silently (EV-PLACE-003)."),
            new DocumentationSectionBlock(
                "Controlled-beta status and access gating",
                "Placement is feature-flagged rather than unconditionally live. Every route under `/v1/placement` " +
                "carries a `PlacementEnabledFilter`, and the status endpoint additionally reports a `betaOnly` mode " +
                "in which access is granted only to an allow-listed set of beta email addresses, resolved from the " +
                "authenticated user's own email claim (EV-PLACE-004). The candidate-facing status response reports " +
                "only whether the module is enabled and whether the caller has access — the underlying admin " +
                "configuration is not exposed to the learner. This is consistent with the module's current status " +
                "as implemented in the codebase and gated for a controlled release rather than an unconditional " +
                "public launch."),
            new DocumentationSectionBlock(
                "Accommodations and reliability safeguards",
                "Extra time is modelled as an explicitly admin-approved grant, never a candidate-selectable option: " +
                "`PlacementAccommodation` records who approved a grant, when, and at what percentage, and the request " +
                "DTO a candidate submits when starting a session has no field through which extra time could be " +
                "requested directly — the server looks up any active grant itself before creating the engine session " +
                "(EV-PLACE-005). The session-creation endpoint then fails closed on a grant-application mismatch: if " +
                "the assessment engine's response does not echo back the same extra-time percentage and approval id " +
                "that was requested, the attempt is refused outright — with a non-retryable error, specifically so an " +
                "automatic client retry cannot create a second engine session — rather than silently starting the " +
                "candidate on a standard, unaccommodated clock (EV-PLACE-006). A separate `PlacementAccommodationUse` " +
                "row snapshots the exact extra-time percentage that was actually applied to each session, so a later " +
                "change to the standing grant can never rewrite what a past attempt received (EV-PLACE-005). The " +
                "proxy layer also does not forward the assessment engine's own candidate bearer token to the " +
                "browser: every subsequent action for a session is re-authenticated through the OET API using the " +
                "learner's own session and a server-held service token, and an upload's client-reported content " +
                "type is defensively parsed (falling back to a safe default) rather than trusted outright, so a " +
                "browser-supplied header value cannot crash the upload path (EV-PLACE-006)."),
            new DocumentationSectionBlock(
                "Reviewer and admin oversight",
                "The gateway exposes a distinct reviewer/admin surface, separate from the candidate path: a review " +
                "queue, per-session review detail with the ability to select a specific submitted task, a review-only " +
                "audio playback route, and explicit rescore and human-score actions that a staff reviewer can apply " +
                "to a session's result (EV-PLACE-001). Upstream failures from the engine are mapped to different, " +
                "role-appropriate messages depending on whether the caller is a candidate or a staff reviewer — for " +
                "example a 403 on a candidate route is reported as \"that session belongs to another account\", while " +
                "the same status on a staff route is reported as an engine-connection problem for an administrator " +
                "to check — and the engine's own response body is logged for diagnosis but never echoed back to the " +
                "caller, so internal engine detail cannot leak into a candidate-facing error (EV-PLACE-007)."),
        ],
        Evidence:
        [
            new DocumentationEvidenceSeed("EV-PLACE-001", DocumentationEvidenceType.Architecture,
                "Server-side gateway to the placement engine exposing distinct Listening/Reading, Writing and Speaking request paths plus a combined full-result call and a reviewer surface.",
                "backend/src/OetLearner.Api/Services/Placement/PlacementGateway.cs"),
            new DocumentationEvidenceSeed("EV-PLACE-002", DocumentationEvidenceType.DataKnowledge,
                "PlacementResult stores one durable, OET-owned row per completed session with a skill-first CEFR profile, ruleset-version provenance, and completed/partial status where partial is a first-class result.",
                "backend/src/OetLearner.Api/Domain/PlacementEntities.cs (PlacementResult)"),
            new DocumentationEvidenceSeed("EV-PLACE-003", DocumentationEvidenceType.DataKnowledge,
                "Canonical OET scoring specification: Listening/Reading 30/42 == 350/500 anchor, Speaking 350/500 universal pass, country-aware Writing pass mark, no-inline-threshold rule, cross-language parity tests.",
                "docs/SCORING.md"),
            new DocumentationEvidenceSeed("EV-PLACE-004", DocumentationEvidenceType.ProductUi,
                "Placement routes gated by a PlacementEnabledFilter with a beta-only access mode resolved against an allow-listed set of beta emails.",
                "backend/src/OetLearner.Api/Endpoints/PlacementEndpoints.cs (/v1/placement/status)"),
            new DocumentationEvidenceSeed("EV-PLACE-005", DocumentationEvidenceType.Security,
                "Admin-approved-only extra-time accommodation entity with no candidate-facing request field, plus a per-session use record snapshotting the percentage actually applied.",
                "backend/src/OetLearner.Api/Domain/PlacementEntities.cs (PlacementAccommodation, PlacementAccommodationUse)"),
            new DocumentationEvidenceSeed("EV-PLACE-006", DocumentationEvidenceType.Reliability,
                "Session creation fails closed (non-retryable) if the engine does not echo back the approved accommodation; candidate bearer token is never forwarded to the browser; uploaded content-type is defensively parsed with a safe fallback.",
                "backend/src/OetLearner.Api/Endpoints/PlacementEndpoints.cs; backend/src/OetLearner.Api/Services/Placement/PlacementGateway.cs (UploadRecordingAsync)"),
            new DocumentationEvidenceSeed("EV-PLACE-007", DocumentationEvidenceType.Reliability,
                "Role-appropriate upstream error mapping (candidate vs staff routes) with the engine's raw response body logged for diagnosis but never echoed to the caller.",
                "backend/src/OetLearner.Api/Services/Placement/PlacementGateway.cs (MapUpstreamFailure)"),
        ]);
}
