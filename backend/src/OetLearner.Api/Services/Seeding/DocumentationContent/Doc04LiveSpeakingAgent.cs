using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>DOC-04 — Universal Live Speaking Agent Technical Report.</summary>
internal static class Doc04LiveSpeakingAgent
{
    public static DocumentationModuleSeed Build() => new(
        Code: "DOC-04",
        Title: "Universal Live Speaking Agent Technical Report",
        Description: "Realtime voice role-play architecture, patient-persona behaviour, profession routing, the separation of live interaction from post-session scoring, session lifecycle, and provider abstraction for the Speaking module.",
        SortOrder: 4,
        Sections:
        [
            new DocumentationSectionBlock(
                "System context and realtime turn loop",
                "The Speaking module runs a self-practice conversational role-play in which a learner speaks to an " +
                "AI-driven patient persona and receives a spoken reply, turn by turn, inside a single browser session. " +
                "The module's own architecture document sets out the full request path: the learner's browser sends an " +
                "audio chunk to `ConversationHub` (a SignalR hub) over HTTPS/WSS; the hub sends that audio to a " +
                "speech-to-text provider for transcription; the resulting text, together with a cached persona system " +
                "prompt, is sent to a language model; the model's reply text is sent to a text-to-speech provider; and " +
                "the synthesised audio plus the transcript segment is streamed back to the learner (EV-SPEAK-001). The " +
                "documented default routing for this turn loop uses Anthropic's `claude-haiku-4-5` for the patient's " +
                "conversational replies (feature route `speaking.patient.turn.v1`) and a separate, larger model, " +
                "`claude-sonnet-4-6`, for scoring (`speaking.score.v2`) — a deliberate split between a fast, low-latency " +
                "model for the live back-and-forth and a stronger model reserved for the assessment step that happens " +
                "after the conversation (EV-SPEAK-002)."),
            new DocumentationSectionBlock(
                "Patient-persona role-play and fact-gating",
                "The learner-facing scenario (`RolePlayCard`) and the hidden patient persona (`InterlocutorScript`) are " +
                "modelled as a paired one-to-one relationship: the candidate sees the task brief, the persona's clinical " +
                "detail is never sent to the client directly (EV-SPEAK-003). A dedicated persona runtime entity, " +
                "`SpeakingSimulationV11PersonaRuntimeSnapshot`, stores the persona's allowed facts, prohibited facts, and " +
                "the conditions under which a fact may be revealed as separate JSON-typed columns (`AllowedFactsJson`, " +
                "`ProhibitedFactsJson`, `RevealConditionsJson`), together with a running record of which facts have " +
                "actually been carried into the conversation so far (`CarriedFactsJson`) (EV-SPEAK-004). This gives the " +
                "persona a controlled memory: it can withhold information the candidate has not yet elicited, and it " +
                "will not volunteer facts the scenario design marks as prohibited, without a separate service having to " +
                "re-derive that state on every turn. The runtime snapshot is keyed uniquely per speaking session " +
                "(`SpeakingSessionId` unique index) and per exam card slot, so a multi-card exam attempt keeps each " +
                "persona's memory isolated from the others (EV-SPEAK-004)."),
            new DocumentationSectionBlock(
                "Profession routing",
                "Role-play content is scoped to a profession through `ProfessionReference`, which both `RolePlayCard` " +
                "and the learner's own `ActiveProfessionId` reference; the learner-facing card listing endpoint " +
                "(`GET /v1/speaking/role-play-cards`) filters published cards to the caller's active profession plus " +
                "any universal cards, and the single-card detail endpoint returns 404 on a profession mismatch rather " +
                "than silently substituting content (EV-SPEAK-005). Card authoring is itself profession-scoped: an " +
                "admin drafting a new card through `POST /v1/admin/speaking/cards/ai-draft` (feature route " +
                "`card.draft.v1`) picks the target profession before generation, and a card cannot be published " +
                "without a profession set (EV-SPEAK-006)."),
            new DocumentationSectionBlock(
                "Separation of realtime patient behaviour from post-session scoring",
                "The platform keeps the live persona's conversational behaviour and the eventual score in separate " +
                "services and separate tables. `SpeakingPatientTurnService` and the persona runtime snapshot govern " +
                "what the persona says and remembers during the live conversation; a distinct assessment pipeline — " +
                "`SpeakingSimulationV11AssessmentService`, `SpeakingSimulationV11AudioAssessmentService`, and the " +
                "`SpeakingAiAssessment` entity — runs only after the session ends, produces an advisory score " +
                "(`IsAdvisory = true`), and is never the entity that drives what the persona said mid-conversation " +
                "(EV-SPEAK-007). The scoring model itself enforces evidence, not just narrative plausibility: every " +
                "AI-supplied criterion rationale must include a verbatim transcript quote, and " +
                "`SpeakingAiAssessmentService` checks that the quote is an actual substring of the stored transcript " +
                "segments before accepting it — a non-verifiable quote flips the assessment's confidence band to " +
                "`low` and flags it for tutor review rather than being presented as a confident score (EV-SPEAK-008). " +
                "A second, independent scoring path exists for human tutors: `SpeakingTutorAssessment` is the " +
                "authoritative record once `IsFinal` is set, and it is stored separately from the advisory AI " +
                "assessment so the two never overwrite one another (EV-SPEAK-009)."),
            new DocumentationSectionBlock(
                "Session lifecycle and state machine",
                "A `SpeakingSession` moves through a guarded state machine — `WarmUp → Prep → Active → Finished`, with " +
                "`Cancelled` reachable from any non-terminal state and `Expired` reachable from `Active` on an idle " +
                "timeout. Any attempt to skip a state (for example jumping straight from `WarmUp` to `Active`) throws " +
                "an `invalid_state_transition` error, and the guard behaviour is checked by " +
                "`SpeakingStateMachineGuardsTests` (EV-SPEAK-010). A two-role-play mock exam composes two of these " +
                "sessions end to end through its own state machine — " +
                "`Pending → Prep1 → Active1 → Finished1 → Bridge → Prep2 → Active2 → Finished2 → Aggregated` — with the " +
                "final `Aggregated` state produced by `MockReportAggregationService`, which averages the two sessions' " +
                "advisory assessments into a single combined readiness band (EV-SPEAK-011). A separate live-tutor " +
                "session type, `SpeakingLiveRoom`, follows its own lifecycle (`Scheduled → Provisioning → Active → " +
                "Ended`, with a `Failed` branch on provider error), driven by LiveKit webhook events that are appended, " +
                "HMAC-verified, to the room's own webhook log before being trusted (EV-SPEAK-012)."),
            new DocumentationSectionBlock(
                "Live tutor sessions and provider abstraction",
                "Beyond AI self-practice, the module supports a live, video-capable 1:1 session between a learner and a " +
                "human tutor over LiveKit Cloud: both parties request a short-lived, per-participant JWT from the API " +
                "(`GET /v1/speaking/live-rooms/{id}/token`), the tutor's token additionally grants room-admin rights, " +
                "and LiveKit performs track-composite recording to an S3-compatible bucket while the tutor's live cue " +
                "events are relayed to the learner over a second SignalR hub, `SpeakingLiveRoomHub` (EV-SPEAK-013). " +
                "Every external capability in the module is behind an interface rather than a hard-coded vendor call: " +
                "text-to-speech goes through `IConversationTtsProvider` (ElevenLabs by default, with Azure, an " +
                "open-source engine, and a mock provider available as alternates via " +
                "`ConversationTtsProviderSelector`); speech-to-text goes through `ISpeakingTranscriptionProvider` " +
                "(Whisper by default); and the language-model calls for both the live persona and the scoring step " +
                "route through a shared AI gateway (`AiProviderRegistry`, supporting Anthropic, an OpenAI-compatible " +
                "interface, and Cloudflare Workers AI) with a documented per-feature fallback — for example the " +
                "patient-turn route falls back from Anthropic to OpenAI's `gpt-4o-mini` on a provider failure " +
                "(EV-SPEAK-014). This abstraction is what allows a provider to be swapped or rolled back — the " +
                "documented swap procedure is to add a new provider account, re-point one feature route to it at a " +
                "10% rollout, monitor latency and error rate, then promote or roll back — without changing the " +
                "session, persona, or scoring logic described above (EV-SPEAK-015)."),
            new DocumentationSectionBlock(
                "Compliance, retention, and disclosure",
                "Voice recordings are treated as biometric, special-category data: a session cannot begin recording " +
                "without a versioned `SpeakingComplianceConsent` (`recording.v1`, with a separate " +
                "`live_video_with_tutor.v1` consent for tutor video sessions), consent is stamped at session creation, " +
                "and it is re-surfaced on every results screen (EV-SPEAK-016). Retention is time-boxed and differs by " +
                "review path — a self-practice recording that no tutor reviews is retained for a shorter default " +
                "window than one a tutor has reviewed — and an hourly worker (`SpeakingAudioRetentionWorker`) purges " +
                "expired recordings and writes an audit event for each deletion (EV-SPEAK-016). Every results surface " +
                "renders a fixed disclaimer identifying the AI score as a practice estimate, not an official OET " +
                "score, and this disclaimer is a configured string (`SpeakingComplianceOptions.ScoreDisclaimer`) " +
                "rather than free text an individual screen could omit (EV-SPEAK-017)."),
        ],
        Evidence:
        [
            new DocumentationEvidenceSeed("EV-SPEAK-001", DocumentationEvidenceType.Architecture,
                "Documented realtime turn-loop sequence diagram: learner audio -> ConversationHub -> ASR -> cached persona prompt -> LLM -> TTS -> audio back to learner.",
                "docs/speaking/architecture.md, section \"Real-time flow (AI self-practice turn)\""),
            new DocumentationEvidenceSeed("EV-SPEAK-002", DocumentationEvidenceType.AiModel,
                "AI provider matrix showing the live patient-turn route (claude-haiku-4-5) is a distinct, lower-latency model from the scoring route (claude-sonnet-4-6).",
                "docs/speaking/ai-providers.md, feature routes speaking.patient.turn.v1 and speaking.score.v2"),
            new DocumentationEvidenceSeed("EV-SPEAK-003", DocumentationEvidenceType.Architecture,
                "RolePlayCard (candidate-facing) is modelled as a 1:1 pair with InterlocutorScript (hidden patient persona).",
                "docs/speaking/data-model.md, entity quick-reference table"),
            new DocumentationEvidenceSeed("EV-SPEAK-004", DocumentationEvidenceType.Code,
                "Persona runtime snapshot entity storing allowed/prohibited facts, reveal conditions and carried facts as JSONB, unique per speaking session.",
                "backend/src/OetLearner.Api/Data/LearnerDbContext.SpeakingSimulationV11.cs (SpeakingSimulationV11PersonaRuntimeSnapshot mapping)"),
            new DocumentationEvidenceSeed("EV-SPEAK-005", DocumentationEvidenceType.ProductUi,
                "Learner role-play card endpoints filter by ActiveProfessionId and universal cards; single-card lookup 404s on profession mismatch.",
                "docs/speaking/api-surface.md, Learner table (GET /v1/speaking/role-play-cards, GET /v1/speaking/role-play-cards/{id})"),
            new DocumentationEvidenceSeed("EV-SPEAK-006", DocumentationEvidenceType.ProductUi,
                "Admin card authoring flow requires profession selection before AI draft generation and blocks publish without a profession set.",
                "docs/speaking/content-model.md, sections \"Authoring flow\" and \"Lifecycle\""),
            new DocumentationEvidenceSeed("EV-SPEAK-007", DocumentationEvidenceType.Architecture,
                "Advisory AI assessment (IsAdvisory = true) and authoritative tutor assessment are separate entities, produced only after the session lifecycle ends, distinct from the live patient-turn service.",
                "docs/speaking/data-model.md and docs/speaking/scoring.md, section \"Two assessor types\"; Services/Speaking/SpeakingSimulationV11AssessmentService.cs"),
            new DocumentationEvidenceSeed("EV-SPEAK-008", DocumentationEvidenceType.DataKnowledge,
                "Every AI criterion rationale requires a verbatim transcript quote; SpeakingAiAssessmentService verifies the quote is a substring of the stored transcript before accepting it, otherwise confidence drops to low.",
                "docs/speaking/scoring.md, section \"Evidence verification\""),
            new DocumentationEvidenceSeed("EV-SPEAK-009", DocumentationEvidenceType.Architecture,
                "SpeakingTutorAssessment is a separate authoritative-scoring entity from SpeakingAiAssessment, becoming final only when IsFinal is set.",
                "docs/speaking/data-model.md, entity quick-reference table"),
            new DocumentationEvidenceSeed("EV-SPEAK-010", DocumentationEvidenceType.Testing,
                "SpeakingSession guarded state machine (WarmUp/Prep/Active/Finished/Cancelled/Expired) with invalid transitions throwing invalid_state_transition, verified by a dedicated guard test suite.",
                "docs/speaking/state-machines.md, section \"SpeakingSession\"; SpeakingStateMachineGuardsTests"),
            new DocumentationEvidenceSeed("EV-SPEAK-011", DocumentationEvidenceType.Architecture,
                "Two-role-play mock exam state machine ending in an Aggregated state produced by MockReportAggregationService averaging both sessions' assessments.",
                "docs/speaking/state-machines.md, section \"SpeakingMockSession\""),
            new DocumentationEvidenceSeed("EV-SPEAK-012", DocumentationEvidenceType.Reliability,
                "SpeakingLiveRoom lifecycle driven by HMAC-verified LiveKit webhook events appended to WebhookEventsJson.",
                "docs/speaking/state-machines.md, section \"SpeakingLiveRoom\""),
            new DocumentationEvidenceSeed("EV-SPEAK-013", DocumentationEvidenceType.Architecture,
                "Live tutor flow: per-participant LiveKit JWT minting, track-composite egress to S3, SignalR cue relay via SpeakingLiveRoomHub.",
                "docs/speaking/architecture.md, section \"Live tutor flow (LiveKit)\""),
            new DocumentationEvidenceSeed("EV-SPEAK-014", DocumentationEvidenceType.Architecture,
                "TTS/ASR/LLM providers are all behind interfaces (IConversationTtsProvider, ISpeakingTranscriptionProvider, AiProviderRegistry) with documented per-feature fallback providers.",
                "docs/speaking/ai-providers.md, sections \"TTS + ASR\" and feature-route fallback column"),
            new DocumentationEvidenceSeed("EV-SPEAK-015", DocumentationEvidenceType.Reliability,
                "Documented provider swap procedure: add provider account, re-point feature route at 10% rollout, monitor latency/error rate, promote or rollback.",
                "docs/speaking/ai-providers.md, section \"Swap procedure\""),
            new DocumentationEvidenceSeed("EV-SPEAK-016", DocumentationEvidenceType.Security,
                "Versioned recording consent required before recording; differentiated retention windows by tutor-review status; hourly retention worker writes an audit event per deletion.",
                "docs/speaking/compliance.md, sections \"Consent\" and \"Retention\""),
            new DocumentationEvidenceSeed("EV-SPEAK-017", DocumentationEvidenceType.ProductUi,
                "Fixed, configured score disclaimer rendered on every Speaking results screen identifying AI scores as a practice estimate, not an official OET score.",
                "docs/speaking/compliance.md, section \"Score disclaimer\"; docs/speaking/scoring.md, section \"Disclaimer\""),
        ]);
}
