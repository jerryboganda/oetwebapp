using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>DOC-14 — Scalability, Internationalisation &amp; Future Roadmap.</summary>
internal static class Doc14ScalabilityRoadmap
{
    public static DocumentationModuleSeed Build() => new(
        Code: "DOC-14",
        Title: "Scalability, Internationalisation & Future Roadmap",
        Description: "What the current architecture already supports at scale, the internationalisation groundwork already live, and a staged, honestly-labelled roadmap of committed, target and aspirational items.",
        SortOrder: 14,
        Sections:
        [
            new DocumentationSectionBlock(
                "Baseline: one backend already serving five client surfaces",
                "Before describing anything future-facing, it is worth stating plainly what already scales today. " +
                "A single ASP.NET Core backend already serves a Next.js web app, an Android app, an iOS app, a " +
                "Windows desktop app and a best-effort macOS desktop app, with the desktop and mobile shells built " +
                "as thin remote clients (Tauri 2 and Capacitor respectively) rather than as separately maintained " +
                "codebases (EV-SCALE-001). This is existing, live capability, not a target: it is the foundation " +
                "the rest of this document's roadmap items build on, and it is documented in full in DOC-10 " +
                "(Platform Architecture, Apps & Infrastructure Report)."),
            new DocumentationSectionBlock(
                "Provider abstraction: the mechanism that would carry new AI/exam-technology vendors",
                "The platform's AI layer is not hard-wired to one vendor. The `AiProviderDialect` enum in the " +
                "domain model already distinguishes eleven separate wire protocols the gateway can speak — OpenAI-" +
                "compatible, Anthropic native, Cloudflare Workers AI, GitHub Copilot/Models, Google Gemini native, " +
                "and dedicated dialects for Azure and ElevenLabs text-to-speech and speech-to-text, Whisper " +
                "transcription, and Azure phoneme scoring — behind one dispatch point (EV-SCALE-002). The " +
                "`AiProvider` entity itself is documented as a \"DB-backed provider registry ... so admins can " +
                "add/rotate providers without a redeploy\" (EV-SCALE-003). This is the concrete, already-built " +
                "mechanism that makes a **target** — not a committed, dated plan — of onboarding further AI/exam-" +
                "technology vendors as needs change: the extension point exists and is exercised today; adding one " +
                "more row to an existing table is a materially smaller step than the multi-provider abstraction " +
                "already shipped."),
            new DocumentationSectionBlock(
                "Storage abstraction: the mechanism that would carry a region or vendor change",
                "The same pattern exists one layer down, for file storage. `IFileStorage` is an interface, not a " +
                "concrete disk dependency, and its own source comment states the intent explicitly: implementers " +
                "can \"swap to S3 / R2\" without touching call sites (EV-SCALE-004). A second implementation, " +
                "`S3CompatibleFileStorage`, already exists and its header comment names three concrete, real " +
                "object-storage vendors it is written to support today by configuration alone — AWS S3, " +
                "DigitalOcean Spaces, and Cloudflare R2 (EV-SCALE-005). Nothing here claims the platform is " +
                "currently multi-region or currently running on any of those three; the honest claim is narrower " +
                "and verifiable: the code already contains a working, non-trivial abstraction that removes vendor " +
                "and region lock-in as an engineering blocker if that becomes a business need."),
            new DocumentationSectionBlock(
                "Multi-account failover: scaling call volume within a single AI vendor",
                "Scaling is not only about adding new vendors — it is also about a single vendor relationship " +
                "handling more volume without a code change. The `AiProviderAccount` entity lets one logical " +
                "provider hold several separate credential/quota slots at once (for example several API keys or " +
                "PATs for the same vendor), and the gateway is documented as failing over between them \"by " +
                "ascending priority under an atomic, race-safe SQL update\" once one account's quota is exhausted " +
                "(EV-SCALE-008). This is existing, shipped capacity-management infrastructure: it means a real " +
                "growth pressure — more learners generating more AI-graded submissions against the same vendor — " +
                "already has a built-in answer (add another credential slot) before it requires either a new " +
                "vendor integration or a support incident."),
            new DocumentationSectionBlock(
                "Multi-platform reach is actively maintained, not built once and left",
                "A platform can claim to run on five surfaces and still let most of them silently rot. That is not " +
                "the pattern here: each native surface has its own dedicated, currently-exercised CI workflow — " +
                "`mobile-ci.yml` for Android/iOS builds, `apple-compatibility.yml` for iOS/macOS platform-floor " +
                "verification, and dedicated desktop packaging/release workflows for the Tauri shell — alongside " +
                "the core `deploy.yml` web/API pipeline (EV-SCALE-009). Continued investment in that reach is " +
                "directly evidenced in the dated record covered in DOC-13: the Apple compatibility hardening effort " +
                "that raised the iOS floor and ran the iOS build job for the first time, and the synchronized " +
                "Android/iOS/desktop parity release that followed it days later, are both real, dated engineering " +
                "events on this platform, not a one-time setup. Scaling to more users on an existing surface " +
                "depends on that surface staying genuinely maintained, and the CI evidence shows it is."),
            new DocumentationSectionBlock(
                "Internationalisation: a live bilingual foundation, not a plan",
                "Internationalisation is not aspirational for this platform — it is already partially built and " +
                "live. The frontend uses `next-intl` with a nested (not flat) message-file structure enforced by " +
                "`i18n.ts`, and the repository ships two real, populated locale directories today, `messages/en` " +
                "and `messages/ar` (EV-SCALE-006). OET is inherently an international, English-proficiency exam " +
                "for a globally mobile healthcare workforce, so a bilingual interface layer already in production " +
                "is a directly relevant piece of scaling infrastructure rather than a cosmetic feature. Extending " +
                "the same message-catalogue structure to a further locale is a concrete, boundable engineering " +
                "task on top of an existing pattern, not a new subsystem."),
            new DocumentationSectionBlock(
                "Cost governance that is built to scale with volume, not just with vendor count",
                "Growing usage of AI-graded features is a real, foreseeable pressure on a platform that pays " +
                "per-call to external model providers, and the safety controls for that are already live rather " +
                "than planned. A global kill switch can hard-disable platform-keyed AI calls instantly, scoped to " +
                "either platform keys only or every call including learner-supplied keys; a configurable monthly " +
                "USD budget triggers an admin warning at a set percentage and can auto-engage the kill switch at a " +
                "hard-kill percentage; and anomaly detection can flag any user whose daily AI spend exceeds a " +
                "configurable multiple of their own trailing seven-day median (EV-SCALE-010). None of this is " +
                "specific to today's usage level — a budget ceiling and a per-user anomaly threshold are exactly " +
                "the kind of control that becomes more, not less, necessary as call volume grows, and both already " +
                "exist as admin-configurable settings rather than hard-coded constants."),
            new DocumentationSectionBlock(
                "Roadmap, labelled honestly: committed, target, aspirational",
                "**Committed** — the Free Mocks / free-sample program (free, credit-free attempts across " +
                "Listening, Reading, Writing and Speaking) is fully built and merged into the codebase but is " +
                "deliberately dark-launched behind a `free_samples_enabled` feature flag; it is code-complete and " +
                "owner-directed, and only a rollout decision remains — it is explicitly not yet on by default for " +
                "every learner (EV-SCALE-007). **Target** — near-term, boundable extensions that build directly on " +
                "abstractions already shipped and already exercised: (a) onboarding one or more additional AI or " +
                "exam-technology provider(s) into the existing `AiProvider` registry as a configuration/admin " +
                "action rather than a rewrite (EV-SCALE-002, EV-SCALE-003); (b) extending the live " +
                "English/Arabic message-catalogue structure to a further locale using the same `next-intl` pattern " +
                "(EV-SCALE-006). Neither of these has a committed date, a named vendor, or a named language in this " +
                "document — they are described as plausible next steps precisely because the underlying mechanism " +
                "already exists in the codebase, not because either has been scheduled. **Aspirational** — longer-" +
                "horizon possibilities that the current single-backend, multi-shell architecture makes " +
                "structurally conceivable but that do not exist in the codebase in any form today: a second " +
                "branded/white-label deployment or a B2B/institutional offering, and support for an additional " +
                "exam board's content model alongside OET. No multi-tenant code, no white-label configuration " +
                "surface, no second exam-board adapter, and no B2B contract or pricing model exists anywhere in " +
                "this repository as of this writing; these are stated here only as directions the architecture " +
                "does not foreclose, not as plans."),
            new DocumentationSectionBlock(
                "What this roadmap deliberately does not promise",
                "No item in this document carries a delivery date, a named commercial commitment, or an " +
                "announced product. The committed item above is committed in the narrow sense that it is already " +
                "built and merged — not that its public rollout date is fixed. The two target items are described " +
                "as plausible precisely because they extend a working abstraction already in production use, and " +
                "are not scoped, staffed, or scheduled as of this writing. The aspirational items are named only " +
                "because the architecture does not structurally rule them out; naming them here is not an " +
                "announcement, a roadmap commitment, or evidence that design or planning work has started. This " +
                "section exists so that every scaling claim in this pack traces back to a real abstraction already " +
                "in the codebase, and so that none of it can be mistaken for a live feature or a dated commitment " +
                "it is not."),
        ],
        Evidence:
        [
            new DocumentationEvidenceSeed("EV-SCALE-001", DocumentationEvidenceType.Architecture,
                "One ASP.NET Core backend serving five client surfaces (web, Android, iOS, Windows desktop, macOS desktop) via thin remote shells (Tauri 2, Capacitor).",
                "AGENTS.md, section \"Stack\"; README.md, section \"Stack\"; docs/tauri-desktop-shell.md (opening paragraph); capacitor.config.ts (server.url)"),
            new DocumentationEvidenceSeed("EV-SCALE-002", DocumentationEvidenceType.Code,
                "The AiProviderDialect enum: eleven distinct provider wire-protocols (OpenAI-compatible, Anthropic, Cloudflare, Copilot, Gemini native, Azure/ElevenLabs TTS, Azure ASR, Whisper ASR, Azure phoneme, ElevenLabs STT) behind one dispatch point.",
                "backend/src/OetLearner.Api/Domain/AiProviderEntities.cs (AiProviderDialect enum)"),
            new DocumentationEvidenceSeed("EV-SCALE-003", DocumentationEvidenceType.Code,
                "The AiProvider entity documented as a DB-backed provider registry letting admins add or rotate providers without a redeploy.",
                "backend/src/OetLearner.Api/Domain/AiProviderEntities.cs (AiProvider class doc comment)"),
            new DocumentationEvidenceSeed("EV-SCALE-004", DocumentationEvidenceType.Code,
                "IFileStorage interface documented as a thin storage abstraction so a later slice can swap to S3/R2 without touching call sites.",
                "backend/src/OetLearner.Api/Services/Content/IFileStorage.cs"),
            new DocumentationEvidenceSeed("EV-SCALE-005", DocumentationEvidenceType.Code,
                "S3CompatibleFileStorage header comment naming three concrete, already-supported object-storage vendors (AWS S3, DigitalOcean Spaces, Cloudflare R2), switchable by configuration alone.",
                "backend/src/OetLearner.Api/Services/Content/S3CompatibleFileStorage.cs"),
            new DocumentationEvidenceSeed("EV-SCALE-006", DocumentationEvidenceType.Code,
                "Live bilingual i18n foundation: next-intl with a nested message-file structure enforced by i18n.ts, and populated messages/en and messages/ar locale directories.",
                "i18n.ts; messages/en/, messages/ar/ (real populated locale directories in this checkout)"),
            new DocumentationEvidenceSeed("EV-SCALE-007", DocumentationEvidenceType.ProductUi,
                "Free Mocks / free-sample program: built and merged, dark-launched behind the free_samples_enabled feature flag, not on by default for every learner.",
                "CHANGELOG.md, section \"[Unreleased] - Free Mocks (2026-09-22)\""),
            new DocumentationEvidenceSeed("EV-SCALE-008", DocumentationEvidenceType.Code,
                "The AiProviderAccount entity: multiple credential/quota slots per provider with ascending-priority, atomic race-safe SQL-update failover once one account's quota is exhausted.",
                "backend/src/OetLearner.Api/Domain/AiProviderEntities.cs (AiProviderAccount, concurrency-contract doc comment)"),
            new DocumentationEvidenceSeed("EV-SCALE-009", DocumentationEvidenceType.Deployment,
                "Dedicated, currently-exercised per-surface CI workflows: mobile-ci.yml (Android/iOS build), apple-compatibility.yml (iOS/macOS platform-floor verification), and desktop packaging/release workflows, alongside the core deploy.yml web/API pipeline.",
                ".github/workflows/ (mobile-ci.yml, apple-compatibility.yml, and desktop release workflow files present in this checkout)"),
            new DocumentationEvidenceSeed("EV-SCALE-010", DocumentationEvidenceType.Security,
                "Live, admin-configurable AI cost-governance controls: a scoped global kill switch, a monthly USD budget with soft-warn/hard-kill percentage triggers, and per-user spend-anomaly detection against a trailing seven-day median.",
                "docs/AI-USAGE-POLICY.md, section 7 \"Global safety controls\""),
        ],
        Revision: 2);
}
