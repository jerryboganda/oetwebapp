using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>DOC-03 — AI Architecture &amp; Model Governance Report.</summary>
internal static class Doc03AiArchitecture
{
    public static DocumentationModuleSeed Build() => new(
        Code: "DOC-03",
        Title: "AI Architecture & Model Governance Report",
        Description: "Model/provider architecture, orchestration, vendor abstraction, evaluation, safety and versioning for every AI call the platform makes.",
        SortOrder: 3,
        Sections:
        [
            new DocumentationSectionBlock(
                "A single, database-backed provider registry",
                "Every AI vendor the platform can call — text, OCR, and speech — is represented as a row in one " +
                "database table, `AiProvider` (EV-AI-001). Each row records a stable code, a `Dialect` (which " +
                "concrete wire protocol the gateway must speak — OpenAI-compatible, Anthropic native, Cloudflare " +
                "Workers AI, GitHub Copilot/Models, Google Gemini native, or one of the speech dialects for Azure " +
                "and ElevenLabs TTS/ASR and Whisper), a `Category` (text chat, TTS, ASR, phoneme scoring, OCR, or " +
                "PDF extraction), the default model id, an allow-list of permitted models, and per-1k-token " +
                "pricing (EV-AI-001). This is a deliberate architectural choice: adding, rotating, or retiring a " +
                "vendor is a database write made from `/admin/ai-providers`, not a code change or a redeploy. The " +
                "same row also carries its own resilience configuration — a Polly retry count, a circuit-breaker " +
                "failure threshold, and a circuit-breaker rolling window in seconds (EV-AI-001) — so a failing " +
                "vendor degrades in isolation rather than cascading into the rest of the platform. A companion " +
                "table, `AiProviderAccount`, lets a single provider hold several credential/quota slots (for " +
                "example several GitHub Copilot PATs) so that when one account exhausts its monthly cap the " +
                "gateway fails over to the next by ascending priority under an atomic, race-safe SQL update " +
                "(EV-AI-002)."),
            new DocumentationSectionBlock(
                "The four canonical providers seeded at boot",
                "An idempotent startup hook, `CoreAiProviderSeeder`, guarantees four canonical provider rows " +
                "exist on every deployment so an administrator only has to paste a key — never hand-create a row " +
                "with a \"magic code\" (EV-AI-003). The seeded rows are real, named values read directly from the " +
                "seeder source: `anthropic` (dialect Anthropic, category TextChat, default model " +
                "`claude-sonnet-5`, with an admin-facing allow-list that also includes `claude-haiku-5`, " +
                "`claude-fable-5`, `claude-opus-4-8`, and `claude-haiku-4-5-20251001`); `mistral-ocr` (category " +
                "Ocr, default model `mistral-ocr-latest`, used for Listening Part A extraction and as the " +
                "scanned-PDF fallback across every content import); and `whisper-asr` (category Asr, default " +
                "model `whisper-1`, one shared speech-to-text key covering Speaking, Pronunciation, and " +
                "Conversation transcription); and `typesafe-jev` (dialect TypeSafeJev, category Judgment, seeded " +
                "inactive, holds the admin-pasted TypeSafe key and the GET /v1/models connectivity probe only — " +
                "never a chat default or a feature-route target) (EV-AI-003). Seeding is strictly additive — rows are created keyless " +
                "and are never overwritten once present, so an admin-pasted key, or a row already created by the " +
                "voice-provider seeder from environment configuration, is preserved rather than clobbered on the " +
                "next deploy (EV-AI-003)."),
            new DocumentationSectionBlock(
                "Vendor abstraction and the failover path",
                "Every dialect ultimately resolves to one interface, `IAiModelProvider`; adding a new vendor is " +
                "documented as \"a single-class change: implement `IAiModelProvider`, register it in DI, and keep " +
                "all grounding code in `AiGatewayService` / `RulebookPromptBuilder` untouched\" (EV-AI-004). The " +
                "shipped concrete implementations include a local `MockAiProvider` (test/offline fallback with no " +
                "external call) and an `OpenAiCompatibleProvider` that serves any OpenAI-compatible " +
                "`/chat/completions` endpoint, alongside the dialect-specific Anthropic, Copilot, Gemini-native, " +
                "and speech providers referenced above (EV-AI-004). Per-feature routing is a separate, equally " +
                "database-backed layer: `AiFeatureRoute` rows let an administrator pin an individual feature code " +
                "(for example `writing.grade` or `pronunciation.linguistic.score.v1`) to a specific provider and " +
                "model, overriding the failover-priority default, without touching code (EV-AI-001). The " +
                "documented default routing locks every text-LLM feature to the Anthropic provider with an OpenAI " +
                "fallback, while pronunciation linguistic scoring and class-recording transcription stay pinned " +
                "to their own specialised providers (Gemini native audio and Whisper respectively) because those " +
                "capabilities are not interchangeable across vendors (EV-AI-005)."),
            new DocumentationSectionBlock(
                "One coordinator, mandatory grounding, no bypass path",
                "The platform's own contributor rulebook states the invariant in one line: AI calls must \"route " +
                "through the coordinator (`IAiGatewayService` / `IDirectAiCallRecorder`); one `AiUsageRecord` per " +
                "physical provider call; never bypass grounding\" (EV-AI-006). `AiGatewayService` is documented as " +
                "\"the only path to any AI model\" for gateway-routed features: it owns the rulebook prompt " +
                "builder, the provider registry lookup, and a refusal check that inspects the outgoing system " +
                "prompt for a fixed header string, `\"OET AI — Rulebook-Grounded System Prompt\"`, and throws a " +
                "`PromptNotGroundedException` if it is absent (EV-AI-007). A small number of features cannot flow " +
                "through the chat gateway at all — OCR has no chat route, and speech-to-text and the Listening " +
                "Part A structuring call are direct provider calls — but the usage-policy document is explicit " +
                "that each of these still writes exactly one `AiUsageRecord` via `IDirectAiCallRecorder`, so it " +
                "surfaces in `/admin/ai-usage` like any gateway call (EV-AI-008). The design principle recorded " +
                "for this is blunt: \"Grounding is non-negotiable... The gateway physically refuses ungrounded " +
                "prompts. No policy below may weaken this\" (EV-AI-008)."),
            new DocumentationSectionBlock(
                "Judgment-only auxiliary layer (TypeSafe SystemOne / Jev)",
                "A second, narrower AI layer sits alongside the grading gateway for specific advisory tasks — " +
                "pre-gateway Writing-submission guarding, request routing, post-gateway citation verification of " +
                "AI findings, per-criterion advisory signals, companion-retrieval reranking, and in-role-play " +
                "conversation judgment (EV-AI-008). This provider, code `typesafe-jev`, is documented as returning " +
                "only typed Choice/Noul/Score answers, never free text, and its output is explicitly barred from " +
                "ever overriding the rulebook-grounded gateway verdict, the deterministic Writing rule engine, or " +
                "the placement engine — it is informational or gating-only, and every call still routes through " +
                "`IDirectAiCallRecorder` for the same one-row-per-call audit discipline (EV-AI-008)."),
            new DocumentationSectionBlock(
                "Safety controls: kill switch, budget ceilings, anomaly detection",
                "The usage policy defines a set of non-configurable invariants and a set of admin-tunable global " +
                "safety controls that sit above the per-provider retry/circuit-breaker configuration. A global " +
                "kill switch (`AiGlobalKillSwitch`) can hard-disable platform-keyed AI calls instantly, scoped to " +
                "either platform keys only or every call including BYOK; a monthly USD budget " +
                "(`AiGlobalBudgetUsd`) triggers an admin warning email at a configurable percentage and can " +
                "auto-engage the kill switch at a hard-kill percentage; and anomaly detection can flag any user " +
                "whose daily AI spend exceeds a configurable multiple of their own trailing seven-day median " +
                "(EV-AI-009). Separately, the platform records an explicit owner directive (23 Sep 2026) that " +
                "admin-authored content-drafting and extraction calls are exempt from the day/class-month ceilings " +
                "that apply to student-facing scoring and interactive-learning calls, while remaining fully " +
                "subject to the global monthly budget, the kill switch, and complete `AiUsageRecord` accounting " +
                "(EV-AI-009) — a distinction between operational tooling spend and learner-facing spend, not a " +
                "removal of oversight."),
            new DocumentationSectionBlock(
                "Credential custody and evaluation discipline",
                "Platform-held provider keys are encrypted at rest via ASP.NET Data Protection under a dedicated " +
                "purpose string, `AiProvider.PlatformKey.v1`, the same mechanism used for learner-supplied " +
                "(\"bring your own key\") credentials (EV-AI-001, EV-AI-009). Every provider exposes an admin-" +
                "triggered connectivity probe (`POST /v1/admin/ai/providers/{code}/test`) whose outcome is " +
                "classified into a small fixed vocabulary — `ok`, `auth`, `rate_limited`, `network`, `ungrounded`, " +
                "`unknown` — and whose one-line error text is capped and stored for diagnosis without ever " +
                "persisting the raw key (EV-AI-001). Response bodies from AI calls are not stored by default " +
                "(`StoreResponseBodies=false`), a deliberate choice recorded as reducing cross-user data-leakage " +
                "surface and PII footprint in favour of hashes, metadata, and the stamped rulebook-grounding " +
                "version on every audit row (EV-AI-009). Version drift is controlled the same way on the rules " +
                "side: every graded submission stores the exact rulebook version it was scored against, so a " +
                "rulebook or validator upgrade never silently re-grades historical work (EV-AI-010)."),
            new DocumentationSectionBlock(
                "Administrative visibility",
                "The `/admin/ai-providers` screen is the human-facing surface for this entire architecture: it " +
                "lists every registered provider with its dialect, category, pricing, and last-test status; " +
                "supports connectivity testing and model discovery per provider; and hosts two dedicated panels — " +
                "one for per-feature routing overrides and one for per-feature tool grants — plus a modal for " +
                "managing the multi-account credential pool described above (EV-AI-011). Complementary read-only " +
                "endpoints, `GET /v1/admin/ai/usage` and `GET /v1/admin/ai/usage/summary`, expose the paginated " +
                "call log and monthly roll-ups by feature, provider, outcome, and user, giving administrators a " +
                "verifiable trail for every one of the one-row-per-call `AiUsageRecord` entries this architecture " +
                "guarantees (EV-AI-012)."),
        ],
        Evidence:
        [
            new DocumentationEvidenceSeed("EV-AI-001", DocumentationEvidenceType.Code,
                "The AiProvider entity: Code, Dialect enum, Category enum, DefaultModel, AllowedModelsCsv, encrypted-key storage, retry count, and circuit-breaker threshold/window fields.",
                "backend/src/OetLearner.Api/Domain/AiProviderEntities.cs"),
            new DocumentationEvidenceSeed("EV-AI-002", DocumentationEvidenceType.Code,
                "The AiProviderAccount entity and its atomic, race-safe SQL update contract for multi-credential failover under a monthly request cap.",
                "backend/src/OetLearner.Api/Domain/AiProviderEntities.cs (AiProviderAccount, concurrency-contract doc comment)"),
            new DocumentationEvidenceSeed("EV-AI-003", DocumentationEvidenceType.Code,
                "CoreAiProviderSeeder: the idempotent startup seeder that guarantees the anthropic (default model claude-sonnet-5), mistral-ocr (mistral-ocr-latest), whisper-asr (whisper-1) and typesafe-jev (inactive, Judgment) provider rows exist, keyless and never overwritten.",
                "backend/src/OetLearner.Api/Services/Ai/CoreAiProviderSeeder.cs"),
            new DocumentationEvidenceSeed("EV-AI-004", DocumentationEvidenceType.Architecture,
                "Vendor-abstraction contract: IAiModelProvider as the single extension point; MockAiProvider and OpenAiCompatibleProvider as shipped implementations; adding a vendor is a single-class change.",
                "docs/RULEBOOKS.md, section 4 \"AI grounding — mission-critical contract\" / \"Providers\""),
            new DocumentationEvidenceSeed("EV-AI-005", DocumentationEvidenceType.AiModel,
                "Locked default routing (Anthropic primary / OpenAI fallback for text-LLM feature codes) and the two exceptions pinned to specialised providers (pronunciation linguistic scoring on Gemini native audio; class-recording transcription on Whisper).",
                "docs/AI-USAGE-POLICY.md, section 5 \"Feature-eligibility matrix\""),
            new DocumentationEvidenceSeed("EV-AI-006", DocumentationEvidenceType.Architecture,
                "Contributor-facing standing rule that every AI call routes through the coordinator, writes one AiUsageRecord per physical provider call, and must never bypass grounding.",
                "AGENTS.md, section \"OET Domain Invariants\" (AI calls bullet)"),
            new DocumentationEvidenceSeed("EV-AI-007", DocumentationEvidenceType.Code,
                "AiGatewayService documented as the sole path to any AI model, holding the grounded-prompt builder and the refusal check that throws PromptNotGroundedException when the rulebook header string is missing.",
                "docs/RULEBOOKS.md, section 4 \"AI grounding — mission-critical contract\" / \"Gateway refusal\""),
            new DocumentationEvidenceSeed("EV-AI-008", DocumentationEvidenceType.AiModel,
                "Direct (non-gateway) AI calls and the judgment-only TypeSafe SystemOne (Jev) layer, each recorded one-row-per-call via IDirectAiCallRecorder and explicitly barred from overriding gateway/rule-engine/placement verdicts.",
                "docs/AI-USAGE-POLICY.md, sections \"Direct (non-gateway) AI calls\" and \"TypeSafe SystemOne (Jev)\""),
            new DocumentationEvidenceSeed("EV-AI-009", DocumentationEvidenceType.Security,
                "Global safety controls (kill switch with scope enum, monthly USD budget with soft-warn/hard-kill percentages, spend-anomaly detection), the admin-batch budget-exemption directive of 2026-09-23, and the StoreResponseBodies=false default with its stated rationale.",
                "docs/AI-USAGE-POLICY.md, sections 7 \"Global safety controls\" and 8 \"Custody & security options\""),
            new DocumentationEvidenceSeed("EV-AI-010", DocumentationEvidenceType.Reliability,
                "Version-pinning invariant: every submission records the rulebook version it was graded against; publishing a new version does not retroactively re-grade historical submissions.",
                "docs/RULEBOOKS.md, section 7 \"Versioning & drift control\""),
            new DocumentationEvidenceSeed("EV-AI-011", DocumentationEvidenceType.ProductUi,
                "The /admin/ai-providers admin screen: provider list/CRUD, connectivity test + model discovery, per-feature routing panel, per-feature tool-grants panel, and the multi-account credential-pool modal.",
                "app/admin/ai-providers/page.tsx"),
            new DocumentationEvidenceSeed("EV-AI-012", DocumentationEvidenceType.Testing,
                "Admin-facing usage-audit endpoints: paginated per-call log and monthly summary roll-ups by feature/provider/outcome/user.",
                "docs/RULEBOOKS.md, section \"Usage accounting (Slice 1, AI Usage Management)\""),
        ]);
}
