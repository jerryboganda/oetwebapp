using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>DOC-10 — Platform Architecture, Apps &amp; Infrastructure Report.</summary>
internal static class Doc10PlatformArchitecture
{
    public static DocumentationModuleSeed Build() => new(
        Code: "DOC-10",
        Title: "Platform Architecture, Apps & Infrastructure Report",
        Description: "Web, mobile and desktop application architecture, backend services, storage design, deployment pipeline and environment separation.",
        SortOrder: 10,
        Sections:
        [
            new DocumentationSectionBlock(
                "One backend, five client surfaces",
                "The platform is built around a single ASP.NET Core Minimal API backend that every client " +
                "surface calls: a Next.js web app, an Android app, an iOS app, a Windows desktop app, and a " +
                "best-effort macOS desktop app. The repository's own always-loaded contributor contract states " +
                "the stack in one line: \"Frontend: Next.js 16 (App Router), React 19, TypeScript, Tailwind CSS " +
                "v4, motion v12. Backend: ASP.NET Core Minimal API, EF Core, PostgreSQL, SignalR. Desktop/mobile: " +
                "Tauri 2 and Capacitor.\" (EV-PLATFORM-001). No client surface maintains its own copy of scoring " +
                "logic, rulebook content, or entitlement state — those live once, in the backend, and every shell " +
                "reads them over the same HTTP/SignalR boundary. This single-backend shape is what makes it " +
                "possible for one engineering change to reach web, Android, iOS and desktop users without five " +
                "separate implementations of the same business rule."),
            new DocumentationSectionBlock(
                "Web frontend: Next.js App Router with an enforced HTTP boundary",
                "The web app is a Next.js App Router application: pages live under `app/**/page.tsx` and prefer " +
                "Server Components; `'use client'` is added only where interactivity requires it; hooks such as " +
                "`useParams()`/`usePathname()` are treated as nullable and guarded accordingly (EV-PLATFORM-002). " +
                "A specific, enforced rule shapes how the frontend talks to the backend: HTTP calls from " +
                "`app`/`components`/`hooks`/`lib` code must go through a shared `apiClient` or the typed helpers " +
                "in `lib/api.ts`, with a short, named exception list (route handlers, external URLs, analytics " +
                "beacons, raw streaming/progress uploads, service-worker/runtime bridge code, and lower-level " +
                "`lib/network/**` internals) (EV-PLATFORM-002). Concentrating network calls behind one client " +
                "means every backend contract change has a small, greppable set of call sites to update, rather " +
                "than an unknown number of ad hoc `fetch` calls scattered through the UI."),
            new DocumentationSectionBlock(
                "Backend: ASP.NET Core Minimal API, EF Core, PostgreSQL, SignalR",
                "The backend is one Minimal API project. Endpoint handlers live under `Endpoints/`; services, " +
                "DTOs, entities, data access, security and configuration stay in their own established folders, " +
                "with dependency injection, cancellation tokens, server-side authorization and existing EF Core " +
                "PostgreSQL patterns expected of every new addition (EV-PLATFORM-003). Real-time features are " +
                "carried over SignalR hubs rather than polling. Database schema changes are deliberately not " +
                "auto-generated in the usual EF Core way: the project's own engineering rule is that migrations " +
                "are hand-authored, because a raw `dotnet ef migrations add` output on this schema has been " +
                "observed to re-create live tables and break deployment, so every migration is a future-dated, " +
                "explicitly named file with an inline `[Migration]` attribute (EV-PLATFORM-004). This is an " +
                "operational safeguard specific to running the same schema continuously in production rather than " +
                "a generic EF Core convention."),
            new DocumentationSectionBlock(
                "Real-time features: named SignalR hubs, not a single generic socket",
                "SignalR is not used as one undifferentiated real-time channel; the backend defines seven distinct " +
                "hub classes under `Hubs/`, each scoped to one feature area: `AiAssistantHub`, `ConversationHub` " +
                "(with a dedicated `ConversationHub.SpeakingRoleplay` partial for live role-play scenarios), " +
                "`SpeakingLiveRoomHub`, `WritingCoachHub`, `WritingSubmissionHub`, and `WritingTodayHub` " +
                "(EV-PLATFORM-017). Separating hubs by feature — a live Speaking room, a Writing coach session, " +
                "Writing submission-status push updates, and a \"today's tasks\" feed — rather than multiplexing " +
                "everything through one connection keeps each real-time surface independently authorizable, " +
                "testable, and scalable, and keeps a defect in one live feature (for example a Speaking role-play " +
                "session) from being able to affect the connection state of an unrelated one (for example Writing " +
                "submission status)."),
            new DocumentationSectionBlock(
                "Admin UI as a governed surface within the same web app",
                "The admin console is not a separate application; it is delivered from the same Next.js app, under " +
                "`app/admin/**`, `components/domain/admin/**` and `components/admin/**`, but it is explicitly " +
                "carved out as a distinct design and operational surface: any change to those paths is required " +
                "to load a dedicated admin-operational-discipline instruction file first, and generic landing-page " +
                "treatment is explicitly disallowed there (EV-PLATFORM-018). This matters architecturally because " +
                "the admin surface is where the platform's most consequential actions live — content publishing, " +
                "AI provider configuration, runtime settings, and the billing/entitlement tools referenced " +
                "elsewhere in this pack — so it is treated as a first-class, separately governed part of the same " +
                "codebase rather than a bolt-on internal tool."),
            new DocumentationSectionBlock(
                "Desktop and mobile: thin shells around the same web app",
                "Both non-web surfaces are documented as thin shells rather than independently built native " +
                "applications. The Windows/macOS desktop shell is built with Tauri 2 and is described as \"a " +
                "remote-only thin client that loads `https://app.oetwithdrhesham.co.uk` (no bundled frontend; " +
                "least-privilege ACL in `src-tauri/capabilities/`)\" (EV-PLATFORM-004). The Android and iOS apps " +
                "are built with Capacitor and \"wrap the same web app\" (EV-PLATFORM-004). This is directly " +
                "visible in the shell's own configuration: `capacitor.config.ts` sets `server.url` to the " +
                "production web app URL by default, gives Android an HTTPS scheme and iOS a `capacitor` scheme, " +
                "and ships a self-contained, network-free `error.html` recovery screen for a failed first load " +
                "(EV-PLATFORM-005). Because the shells load the live web app rather than a bundled copy, a normal " +
                "web deploy reaches installed apps without a new app-store build in most cases — a pattern the " +
                "project's own release history records directly for a shipped fix: \"No app rebuild needed for " +
                "any of it — Capacitor (`server.url`) and Tauri both load the remote URL.\" (EV-PLATFORM-006)."),
            new DocumentationSectionBlock(
                "Storage: an abstraction built for a vendor swap, not a single vendor",
                "Media and user files never touch the filesystem directly from application code. The interface " +
                "`IFileStorage` is documented in its own source as \"a thin abstraction in front of disk storage\" " +
                "whose purpose is that a later slice \"can swap to S3 / R2 by implementing this interface\", and " +
                "that \"all upload / download / delete paths through the app should depend on this, never on raw " +
                "`File.*` or `Path.*` directly\" (EV-PLATFORM-007). Two concrete implementations exist today: " +
                "`LocalFileStorage`, which resolves opaque POSIX-style storage keys to disk paths with explicit " +
                "path-traversal guards, and `S3CompatibleFileStorage`, whose own header comment names the vendors " +
                "it already targets by configuration alone — AWS S3, DigitalOcean Spaces, and Cloudflare R2 — " +
                "switched purely via a `Storage:Provider` setting with no code change (EV-PLATFORM-008). The same " +
                "discipline extends to where content physically lives: production papers, media, user data and " +
                "backups live in named Docker volumes that are independent of the web/API containers, so " +
                "recreating a container never deletes them, and the platform names its own live volumes " +
                "explicitly rather than leaving this undocumented (EV-PLATFORM-009)."),
            new DocumentationSectionBlock(
                "Data platform: PostgreSQL with pgvector, configuration as an audited resource",
                "The database is PostgreSQL end to end, and the project's own continuous-integration contract " +
                "specifies that backend test runs execute sharded against a real \"Postgres/pgvector\" instance " +
                "rather than a generic in-memory stand-in (EV-PLATFORM-013) — the `pgvector` extension is what " +
                "backs similarity/embedding-style lookups anywhere the platform needs them, run against the same " +
                "engine used in production rather than emulated. Configuration and secrets are treated with the " +
                "same seriousness as data: the platform's engineering rule states that \"runtime settings/secrets: " +
                "services read through `IRuntimeSettingsProvider`, with encrypted DB value over env fallback and " +
                "audit on writes\" (EV-PLATFORM-013). That is a deliberate design choice above a plain " +
                "`appsettings.json`/environment-variable model: a setting an administrator changes at runtime is " +
                "encrypted at rest, falls back to environment configuration only when no database override exists, " +
                "and every write is audited — so \"who changed this API key or feature flag, and when\" is always " +
                "answerable from the database itself, not from server access logs or tribal knowledge."),
            new DocumentationSectionBlock(
                "Content pipeline: provenance and publish gates around every uploaded asset",
                "Exam content — Reading papers, Listening audio, and every other media asset a candidate can see — " +
                "does not become visible to a learner the moment a file is uploaded. The platform's own invariant " +
                "for this is explicit: \"Content uploads: use `ContentPaper -> ContentPaperAsset -> MediaAsset`, " +
                "chunked admin upload endpoints, `IFileStorage`, provenance, publish gates, and audit events\" " +
                "(EV-PLATFORM-014). Reading this literally: an uploaded file is not itself the published artifact; " +
                "it is wrapped in a `MediaAsset` record, attached to a `ContentPaperAsset`, and only surfaced " +
                "through a `ContentPaper` once an explicit publish step has run — with the chunked-upload mechanism " +
                "so a large audio or PDF file does not require a single, fragile HTTP request, and with an audit " +
                "trail recording who published what and when. This three-table indirection is what lets the " +
                "storage-vendor abstraction described above and the content-visibility rules described elsewhere " +
                "in this pack (candidate-visible flags, publish gates) compose safely: swapping where a file " +
                "physically lives never changes who is allowed to see it, because those are two different layers " +
                "of the same pipeline."),
            new DocumentationSectionBlock(
                "Supply-chain scanning and live health verification on every deploy",
                "Two further, concrete pieces of infrastructure close the loop between \"code was built\" and " +
                "\"code is safely running in production.\" First, a dedicated CI workflow file, `sbom-sca.yml`, " +
                "exists in the repository alongside the deploy and test workflows, indicating that software-bill-" +
                "of-materials and software-composition-analysis scanning is a standing, automated part of the " +
                "pipeline rather than an ad hoc or manual step (EV-PLATFORM-015). Second, the release process does " +
                "not consider a deploy complete on the strength of a green build alone: the platform's own release " +
                "procedure requires confirming a specific, named set of live endpoints after every deploy — the " +
                "web app's `/api/health` route, the API's `/health/ready` and `/health/live` routes — and checking " +
                "that the running VPS container images carry the exact commit SHA that was just built, before the " +
                "task is considered shipped (EV-PLATFORM-016). Both mechanisms exist specifically because a build " +
                "that compiles is not proof that the right version of the right code is what learners are actually " +
                "using; the platform verifies that directly, against live infrastructure, on every release."),
            new DocumentationSectionBlock(
                "Toolchain: pinned versions, not \"whatever is installed\"",
                "The project pins its toolchain explicitly rather than leaving it to whatever happens to be on a " +
                "given machine: \"Package manager is pnpm 10 (`corepack`/`pnpm@10.33.0`), Node 22.x, .NET SDK " +
                "10.x — all installed on the Windows host; run validation directly here (never on the VPS)\" " +
                "(EV-PLATFORM-020). Frontend and backend also have separate, documented local ports (the Next.js " +
                "dev server and the API) with a proxy configuration for local development, and the desktop and " +
                "mobile shells have their own additional toolchain requirements layered on top — `rustup` and " +
                "WebView2/MSVC for the Tauri desktop build, JDK 21 for Capacitor's Android build — reflecting that " +
                "each shell, while thin at runtime, still has a real native build pipeline behind it " +
                "(EV-PLATFORM-020). Pinning these versions is what makes the GitHub Actions build environment " +
                "reproducible against the same versions a developer would use locally to inspect and edit code."),
            new DocumentationSectionBlock(
                "Testing architecture: sharded, database-backed, workflow-scoped",
                "Verification is not a single monolithic test run. Backend tests execute as `dotnet test` against a " +
                "real, sharded Postgres/pgvector instance rather than against a lightweight substitute " +
                "(EV-PLATFORM-013), and the frontend has its own separate layers: `pnpm test` for Vitest unit " +
                "tests, and a dedicated `pnpm run test:e2e:smoke` script for a Playwright smoke matrix that " +
                "requires the local stack to be up (EV-PLATFORM-021). At full scale in CI, this becomes " +
                "substantial: the platform's own operating guidance states that \"a full QA Smoke (13 e2e shards + " +
                "backend + frontend, parallel on hosted infra) takes roughly an hour\" (EV-PLATFORM-021) — a " +
                "concrete, quantified description of the test suite's real size, not an approximation. Test " +
                "execution is explicitly confined to GitHub Actions under the same compute-location policy " +
                "described below, so this sharded, hour-long verification pass runs on disposable CI infrastructure " +
                "on every push to `main`, never on the production VPS and never, for anything beyond a small " +
                "targeted check, on a developer's own machine."),
            new DocumentationSectionBlock(
                "Local development: containerized dependencies, native application processes",
                "Day-to-day local development deliberately mixes two models rather than forcing everything into " +
                "one: the database and API can run in containers (a Podman-based stack exposing the API on port " +
                "8080, proxied to the Next.js dev server via `.env.development.local`), while the frontend itself " +
                "runs as a native `pnpm run dev` process for fast iteration rather than being rebuilt inside a " +
                "container on every change (EV-PLATFORM-022). A single documented command, `/start-dev`, brings up " +
                "the Podman database-and-API stack together with the native Next.js process in one step " +
                "(EV-PLATFORM-022). This mirrors the same principle seen at production scale — infrastructure that " +
                "genuinely benefits from container isolation (a database, a backend API) is containerized, while " +
                "the process a developer is actively iterating on is not forced through a container rebuild loop " +
                "just for consistency's sake."),
            new DocumentationSectionBlock(
                "Deployment pipeline: build once on CI, deploy as prebuilt images",
                "The deploy path is stated as a single line in the repository's own operating guidance: \"push to " +
                "`main` → GitHub Actions `Build & Deploy (web + API)` → GHCR images → blue/green on the VPS ... " +
                "The VPS only pulls prebuilt images; never build or test there.\" (EV-PLATFORM-004). This is not " +
                "just policy prose — the concrete workflow files exist in `.github/workflows/`, including " +
                "`deploy.yml` (web + API build → GHCR → VPS blue/green deploy with a health gate), `qa-smoke.yml` " +
                "(sharded frontend Vitest/lint/typecheck/build and backend `dotnet test` against Postgres/" +
                "pgvector) and `mobile-ci.yml` (mobile/Android build), each named as an authorized CI entry point " +
                "in the contributor contract (EV-PLATFORM-010). The wider workflow directory also holds dedicated " +
                "pipelines for Apple platform compatibility, mobile release cutting, and desktop packaging, " +
                "confirming that build/test/release automation is broken out per surface rather than handled by " +
                "one monolithic script (EV-PLATFORM-011)."),
            new DocumentationSectionBlock(
                "Eleven environment-specific compose configurations, not one config for everything",
                "The repository ships eleven distinct Docker Compose files rather than one file reused everywhere " +
                "with overrides guessed at deploy time: `docker-compose.dev.yml`, `docker-compose.hotreload.yml`, " +
                "`docker-compose.local.yml`, `docker-compose.backend.yml`, `docker-compose.desktop.yml`, " +
                "`docker-compose.staging.yml`, `docker-compose.vps.yml`, `docker-compose.production.yml`, " +
                "`docker-compose.production.build.yml`, `docker-compose.production.hostports.yml`, and " +
                "`docker-compose.production.prebuilt-web.yml` (EV-PLATFORM-019). Each name maps to a genuinely " +
                "different operating mode — local hot-reload development, a desktop-backend runtime, a staging " +
                "environment, and several production variants distinguishing a from-source build from a prebuilt-" +
                "image pull and from host-port exposure. Keeping these explicit and separate, instead of one " +
                "compose file with runtime flags, is what makes it possible to state precisely, in the deployment " +
                "pipeline described below, that the production VPS only ever runs the prebuilt-image variant and " +
                "never the build variant — the separation is enforced by which file is used, not by developer " +
                "discipline alone."),
            new DocumentationSectionBlock(
                "Environment separation: where computation is, and is not, allowed to run",
                "The platform enforces a hard three-way split between where code is written, where it is built " +
                "and tested, and where it serves traffic. The always-loaded contributor contract states this as " +
                "a non-negotiable, owner-issued rule: \"Local dev machine = READ + INSPECT + EDIT + COMMIT + PUSH " +
                "only. GitHub Actions = BUILD + RUN + TEST + LINT + TYPECHECK + ANALYZE + GENERATE + VERIFY + " +
                "PACKAGE. Production VPS = DEPLOY + SERVE PRODUCTION ONLY.\" with an explicit statement that there " +
                "are \"no discretionary exceptions\" and that hidden local compute paths — local Docker, WSL, " +
                "local VMs, background processes, or subagents — are not permitted workarounds (EV-PLATFORM-012). " +
                "The production VPS itself is described in the same governance material as a single, multi-tenant " +
                "machine that also hosts other stacks, which is precisely why it is restricted to pulling prebuilt " +
                "images and running health checks rather than doing any building, testing, or ad hoc debugging " +
                "(EV-PLATFORM-004). Together, this gives the platform three cleanly separated environments — " +
                "development, verification, and production — with the verification stage running on disposable, " +
                "auditable CI infrastructure rather than on the machine serving live learners."),
        ],
        Evidence:
        [
            new DocumentationEvidenceSeed("EV-PLATFORM-001", DocumentationEvidenceType.Architecture,
                "The platform's declared technology stack: Next.js 16 App Router / React 19 / TypeScript / Tailwind v4 frontend; ASP.NET Core Minimal API / EF Core / PostgreSQL / SignalR backend; Tauri 2 and Capacitor for desktop/mobile.",
                "AGENTS.md, section \"Stack\""),
            new DocumentationEvidenceSeed("EV-PLATFORM-002", DocumentationEvidenceType.Code,
                "Frontend engineering rules: App Router page conventions, Server Component preference, nullable-hook guards, and the mandatory apiClient/lib/api.ts HTTP boundary with its named exceptions.",
                "AGENTS.md, section \"Frontend Rules\""),
            new DocumentationEvidenceSeed("EV-PLATFORM-003", DocumentationEvidenceType.Code,
                "Backend engineering rules: Minimal API endpoints under Endpoints/, DI/cancellation-token/server-side-authz conventions, and EF Core PostgreSQL patterns.",
                "AGENTS.md, section \"Backend Rules\""),
            new DocumentationEvidenceSeed("EV-PLATFORM-004", DocumentationEvidenceType.Architecture,
                "Per-surface architecture description (frontend, backend, hand-authored EF migrations, Tauri/Capacitor shells) and the one-line deploy path (push to main -> GitHub Actions Build & Deploy -> GHCR -> blue/green on the VPS, VPS pulls prebuilt images only).",
                "CLAUDE.md, section \"Architecture\""),
            new DocumentationEvidenceSeed("EV-PLATFORM-005", DocumentationEvidenceType.Code,
                "Capacitor shell configuration: appId, remote server.url pointed at the production web app, per-platform URL scheme, and the offline error.html recovery screen.",
                "capacitor.config.ts"),
            new DocumentationEvidenceSeed("EV-PLATFORM-006", DocumentationEvidenceType.DataKnowledge,
                "Real release note confirming a shipped web-only fix reached the installed Android/desktop apps with no app rebuild, because both shells load the remote URL.",
                ".github/agent-state.local.md (15/16 Sep owner briefs entry, \"No app rebuild needed for any of it\")"),
            new DocumentationEvidenceSeed("EV-PLATFORM-007", DocumentationEvidenceType.Code,
                "The IFileStorage interface and its documented purpose: a thin abstraction over disk storage so a later slice can swap to S3/R2, with all media I/O required to depend on it instead of raw File.*/Path.* calls.",
                "backend/src/OetLearner.Api/Services/Content/IFileStorage.cs"),
            new DocumentationEvidenceSeed("EV-PLATFORM-008", DocumentationEvidenceType.Code,
                "S3CompatibleFileStorage: a working IFileStorage implementation whose header comment names three concrete swappable object-storage vendors (AWS S3, DigitalOcean Spaces, Cloudflare R2), selected purely by configuration.",
                "backend/src/OetLearner.Api/Services/Content/S3CompatibleFileStorage.cs"),
            new DocumentationEvidenceSeed("EV-PLATFORM-009", DocumentationEvidenceType.Reliability,
                "Production storage-persistence design: papers/media/users/backups live in named Docker volumes independent of the web/API containers, with the live volume names stated explicitly, so container rebuilds cannot delete content.",
                "AGENTS.md, section \"Storage Persistence\""),
            new DocumentationEvidenceSeed("EV-PLATFORM-010", DocumentationEvidenceType.Deployment,
                "The table of authorized CI entry points: qa-smoke.yml (frontend + backend test matrix), deploy.yml (web+API build, GHCR, VPS blue/green deploy with health gate), and mobile-ci.yml (mobile/Android build).",
                "AGENTS.md, section \"Authorized CI entry points\""),
            new DocumentationEvidenceSeed("EV-PLATFORM-011", DocumentationEvidenceType.Deployment,
                "The repository's workflow directory, confirming dedicated per-surface pipelines beyond the core deploy workflow (Apple compatibility, mobile release, desktop packaging, and others exist as real files).",
                ".github/workflows/ (deploy.yml, mobile-ci.yml, mobile-release.yml, apple-compatibility.yml, qa-smoke.yml, and others)"),
            new DocumentationEvidenceSeed("EV-PLATFORM-012", DocumentationEvidenceType.Architecture,
                "The compute-location policy: local machine is read/edit/commit/push only, GitHub Actions is the sole build/test/verify environment, and the production VPS is deploy-and-serve only, with no discretionary exceptions.",
                "AGENTS.md, section \"GITHUB ACTIONS IS THE ONLY AUTHORIZED COMPUTE ENVIRONMENT\""),
            new DocumentationEvidenceSeed("EV-PLATFORM-013", DocumentationEvidenceType.Architecture,
                "Backend tests run sharded against a real Postgres/pgvector instance in CI, and runtime settings/secrets are read through IRuntimeSettingsProvider with encrypted DB value over env fallback and an audit trail on writes.",
                "AGENTS.md, section \"Authorized CI entry points\" (Postgres/pgvector); AGENTS.md, section \"OET Domain Invariants\" (Runtime settings/secrets bullet)"),
            new DocumentationEvidenceSeed("EV-PLATFORM-014", DocumentationEvidenceType.Architecture,
                "The content-upload invariant: ContentPaper -> ContentPaperAsset -> MediaAsset, chunked admin upload endpoints, IFileStorage, provenance, publish gates, and audit events for every uploaded exam asset.",
                "AGENTS.md, section \"OET Domain Invariants\" (Content uploads bullet)"),
            new DocumentationEvidenceSeed("EV-PLATFORM-015", DocumentationEvidenceType.Security,
                "A dedicated software-bill-of-materials / software-composition-analysis CI workflow exists alongside the build and deploy workflows.",
                ".github/workflows/sbom-sca.yml (file present in this checkout)"),
            new DocumentationEvidenceSeed("EV-PLATFORM-016", DocumentationEvidenceType.Reliability,
                "The release procedure's mandatory post-deploy live verification: the web app's /api/health route and the API's /health/ready and /health/live routes must return healthy, and the running VPS image tags must match the deployed commit SHA, before a task is considered shipped.",
                "AGENTS.md, section \"Ship-It Workflow — COMPULSORY\" (step 6)"),
            new DocumentationEvidenceSeed("EV-PLATFORM-017", DocumentationEvidenceType.Code,
                "Seven named SignalR hub classes, each scoped to one real-time feature area (AI assistant, conversation/speaking role-play, Speaking live room, Writing coach, Writing submission status, Writing today feed).",
                "backend/src/OetLearner.Api/Hubs/ (AiAssistantHub.cs, ConversationHub.cs, ConversationHub.SpeakingRoleplay.cs, SpeakingLiveRoomHub.cs, WritingCoachHub.cs, WritingSubmissionHub.cs, WritingTodayHub.cs)"),
            new DocumentationEvidenceSeed("EV-PLATFORM-018", DocumentationEvidenceType.ProductUi,
                "The admin console (app/admin/**, components/domain/admin/**, components/admin/**) is a governed surface within the same web app, requiring a dedicated operational-discipline instruction file and explicitly barred from generic landing-page treatment.",
                "AGENTS.md, section \"Admin UI\""),
            new DocumentationEvidenceSeed("EV-PLATFORM-019", DocumentationEvidenceType.Deployment,
                "Eleven distinct Docker Compose files for eleven distinct operating modes (dev, hotreload, local, backend, desktop, staging, vps, production, production.build, production.hostports, production.prebuilt-web), confirming the VPS runs a dedicated prebuilt-image variant rather than a shared/overridden config.",
                "Repository root (docker-compose.*.yml files present in this checkout)"),
            new DocumentationEvidenceSeed("EV-PLATFORM-020", DocumentationEvidenceType.Code,
                "Pinned toolchain versions (pnpm 10 / pnpm@10.33.0, Node 22.x, .NET SDK 10.x) and the additional native toolchain requirements for the Tauri desktop build (rustup, WebView2/MSVC) and Capacitor Android build (JDK 21).",
                "CLAUDE.md, section \"Commands\""),
            new DocumentationEvidenceSeed("EV-PLATFORM-021", DocumentationEvidenceType.Testing,
                "The frontend test layering (Vitest unit tests via pnpm test; a Playwright smoke matrix via pnpm run test:e2e:smoke) and the quantified full QA Smoke run size (13 e2e shards plus backend and frontend, parallel on hosted infra, roughly one hour).",
                "CLAUDE.md, section \"Commands\"; AGENTS.md, section \"GitHub Actions on a public-when-working repo — COMPULSORY\""),
            new DocumentationEvidenceSeed("EV-PLATFORM-022", DocumentationEvidenceType.Architecture,
                "Local development model: a Podman-based container stack for the database/API (port 8080, proxied via .env.development.local) alongside a native pnpm run dev frontend process, brought up together by a single documented /start-dev command.",
                "CLAUDE.md, sections \"Commands\" and \"Project knowledge (load before assuming)\""),
        ]);
}
