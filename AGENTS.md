# AGENTS.md — OET Prep Platform

> A comprehensive OET (Occupational English Test) preparation platform with learner, expert, admin, and sponsor portals. Built as a Next.js 15 frontend with an ASP.NET Core 10 API backend, packaged for web, desktop (Electron), and mobile (Capacitor).

## Project Overview

| Layer | Tech | Location |
| ------- | ------ | ---------- |
| Frontend | Next.js 15.4, React 19, TypeScript 5.9, Tailwind CSS 4, motion v12 | `app/`, `components/`, `lib/`, `hooks/`, `contexts/` |
| Backend API | ASP.NET Core 10, EF Core, PostgreSQL 17, SignalR | `backend/` |
| Desktop | Electron 41, electron-builder | `electron/` |
| Mobile | Capacitor 6, iOS + Android | `ios/`, `android/`, `capacitor-web/` |
| Infrastructure | Docker Compose, Nginx Proxy Manager, standalone output | `Dockerfile`, `docker-compose.*.yml` |

### Key Stats

- **241 routes** across 4 portals (learner, expert, admin, sponsor)
- **113 Vitest unit test files, 675 tests** (Vitest + React Testing Library)
- **34 Playwright E2E spec files** across 13 role/browser projects
- **686 backend endpoint map calls** with 16 granular admin permissions

---

## Setup Commands

```bash
# Install frontend dependencies
npm install

# Start frontend dev server (port 3000)
npm run dev

# Start backend API (port 5062)
npm run backend:run
# —or— with hot reload:
npm run backend:watch

# Start desktop dev mode
npm run desktop:dev

# Mobile sync + run
npm run mobile:sync
npm run mobile:run:android
npm run mobile:run:ios
```

---

## Build & Verification

Always validate before committing:

```bash
# Type-check (must return 0 errors)
npx tsc --noEmit

# Lint (must return 0 errors/warnings)
npm run lint

# Unit tests (must be 113/113 files, 675/675 tests)
npm test

# Production build (must compile 169+ pages)
npm run build

# Backend build
npm run backend:build

# Backend tests
npm run backend:test
```

### E2E Testing

```bash
# Install browsers first
npm run test:e2e:install

# Bootstrap auth states
npm run test:e2e:auth

# Run full matrix (Chromium + Firefox × roles)
npm run test:e2e

# Smoke tests only
npm run test:e2e:smoke

# Desktop E2E
npm run test:e2e:desktop

# View report
npm run test:e2e:report
```

---

## Code Conventions

### TypeScript

- **Strict mode** enabled; zero tolerance for `tsc --noEmit` errors.
- Prefer `.tsx`/`.ts` for all new files. No `.js` in app code.
- Use Zod 4 for runtime validation at system boundaries.
- `tsconfig.json` has `"types": ["vitest/globals"]` so `describe`/`it`/`expect`/`vi` are globally available.

### React / Next.js

- App Router only. All pages are `app/*/page.tsx`.
- `output: 'standalone'` for Docker deployment.
- Use Server Components by default; add `'use client'` only when needed.
- `useParams()` returns `Record<string, string | string[]> | null` — always null-check.
- `usePathname()` returns `string | null` — always null-check.

### Imports / Barrels

- Use direct file imports (for example `@/components/ui/button`) instead of folder barrels for component code.
- Do not add `components/**/index.ts` or re-export-only `lib/**/index.ts` barrels. The legacy component barrels have been removed; `lib/rulebook/index.ts` is retained as an intentional public engine surface.
- When touching imports, keep them on direct file paths rather than reintroducing a barrel.

### API Client

- Frontend/backend HTTP calls from `app/`, `components/`, `hooks/`, and `lib/` must use `apiClient` or a typed helper in `lib/api.ts`.
- Direct `fetch()` is allowed only for documented exceptions: Next.js route handlers, service-worker/runtime bridge code, external third-party URLs, analytics beacons, raw streaming/progress uploads, and the lower-level network implementation in `lib/network/**`.
- If a callsite needs a missing capability, extend `lib/api.ts` first and add a focused unit test for the client behavior.

### Component APIs (Critical)

- `Badge` uses variant `'danger'`, NOT `'destructive'`.
- `Button` uses variant `'primary'`, NOT `'default'`.
- `LearnerPageHeroModel` uses `description`, NOT `subtitle`.
- `CurrentUser` type uses `userId`/`displayName`/`isEmailVerified`.

### Motion / Animation

- Import from `motion/react` (NOT `framer-motion`).
- Mock in tests with Proxy pattern + `stripMotion()` to remove motion-specific props.
- Use `@testing-library/user-event` (NOT `fireEvent`) for async `onClick` handlers.
- In tests, avoid ambiguous regex like `/welcome/i` when both a stepper label and heading contain the same word — use exact text instead.

### Styling

- Tailwind CSS 4 utility classes. No CSS modules.
- Follow `DESIGN.md` for design tokens, color system, and component patterns.
- Responsive: mobile-first breakpoints.

### Backend (.NET)

- Minimal API pattern with endpoint files in `Endpoints/`.
- Services in `Services/`, DTOs in `Contracts/`, entities in `Domain/`.
- EF Core with PostgreSQL; migrations in `Data/Migrations/`.
- JWT authentication with refresh tokens; 16 granular admin permissions.

---

## Project Structure

```text
app/                          # Next.js App Router pages (241 routes)
├── (auth)/                   # Auth pages: sign-in, register, MFA, password reset
├── admin/                    # Admin CMS portal (40+ pages)
├── expert/                   # Expert console (review, calibration, onboarding)
├── sponsor/                  # Sponsor portal (learner management, billing)
├── dashboard/                # Learner dashboard
├── billing/                  # Subscription, referral, score guarantee
├── community/                # Forum threads, groups
├── conversation/             # AI conversation practice
├── diagnostic/               # Diagnostic tests (L/R/W/S)
├── listening/ reading/ writing/ speaking/  # Skill modules
├── mocks/                    # Full mock exam system
├── api/                      # Route handlers (backend proxy, health)
└── ...                       # goals, progress, achievements, etc.

backend/
├── OetLearner.sln
└── src/OetLearner.Api/
    ├── Program.cs            # App startup + DI
    ├── Endpoints/            # Minimal API endpoint files
    ├── Services/             # Business logic (Auth, Content, Review, etc.)
    ├── Domain/               # Entity models
    ├── Data/                 # EF Core DbContext + migrations
    ├── Contracts/            # Request/response DTOs
    ├── Hubs/                 # SignalR real-time hubs
    ├── Security/             # Auth handlers, token validation
    └── Configuration/        # Settings classes

components/
├── ui/                       # Reusable UI: Button, Badge, Modal, DataTable, etc.
├── domain/                   # Domain components: LearnerSurface, WritingEditor, etc.
├── layout/                   # AppShell, DashboardShell, ExpertShell, etc.
└── auth/                     # SignInForm, RegisterForm

lib/
├── api.ts                    # API client with retry logic
├── analytics.ts              # Event tracking
├── motion.ts                 # Motion tokens and utilities
├── admin-permissions.ts      # 16 granular permission types
├── csv-export.ts             # Client-side CSV export
├── mobile/                   # Capacitor native integrations
│   ├── push-notifications.ts
│   ├── deep-link-handler.ts
│   ├── secure-storage.ts
│   ├── biometric-auth.ts
│   └── ...
└── hooks/                    # Custom React hooks

electron/                     # Desktop: main.cjs, preload, IPC
tests/e2e/                    # Playwright E2E tests
```

---

## Deployment

### Production Environment

| Component | Detail |
| ----------- | -------- |
| VPS | `185.252.233.186`, `/root/oetwebsite/` |
| Frontend | `app.oetwithdrhesham.co.uk` (port 3000) |
| API | `api.oetwithdrhesham.co.uk` (port 8080) |
| Database | PostgreSQL 17 (internal network) |
| Proxy | Nginx Proxy Manager (external network `nginx-proxy-manager_default`) |
| Docker project | `oetwebsite` |

### Deploy Command

```bash
ssh root@185.252.233.186
cd /root/oetwebsite
git fetch origin && git reset --hard origin/main
docker compose --env-file .env.production -f docker-compose.production.yml up -d --build
```

### Staging / GitHub-only flow

- Local staging artifacts live in `docker-compose.staging.yml`, `.env.staging.example`, and `docs/STAGING-LOCAL-GITHUB-PLAN.md`.
- `.env.staging` must be created on the staging host with non-production secrets and is never committed.
- `.github/workflows/deploy-staging.yml` is guarded by repository variable `ENABLE_STAGING_DEPLOY=true`; without that variable, pushes to `main` do not deploy.
- Production deploys remain tag/manual controlled. Do not run production VPS, production Docker, or Nginx Proxy Manager commands from cleanup/planning branches.

### Docker Architecture

```text
┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│   web:3000  │───▶│ api:8080    │───▶│ postgres:5432│
│  (Next.js)  │    │(.NET Core)  │    │ (PG 17)     │
└──────┬──────┘    └──────┬──────┘    └─────────────┘
       │                  │
       └──────────────────┘
              │
        npm_proxy network ──▶ Nginx Proxy Manager
```

### IMPORTANT

- Docker volumes use `oetwebsite_` prefix. **Never** recreate `oet_postgres_data` without backup.
- `.env.production` must be present on the VPS (not in Git).
- Web container healthcheck: `GET /api/health`.

---

## Testing Strategy

### Unit Tests (Vitest)

- **Config:** `vitest.config.ts` — jsdom environment, globals enabled, `vitest.setup.ts`.
- **Pattern:** Co-located `*.test.tsx` files or `__tests__/` directories.
- **Mocking:** `vi.mock()` with `vi.hoisted()` for module mocks. Proxy-based `motion/react` mock.
- **Aliases:** `@/` maps to project root. `recharts` is auto-mocked.
- **Run:** `npm test` or `npm run test:watch`.

### E2E Tests (Playwright)

- **Config:** `playwright.config.ts` — multi-project matrix by browser × auth role.
- **Auth:** Bootstrap via `tests/e2e/setup/auth.setup.ts`.
- **Roles:** `unauth`, `learner`, `expert`, `admin`.
- **Run:** `npm run test:e2e` (full) or `npm run test:e2e:smoke`.

### Backend Tests (.NET)

- **Solution:** `backend/OetLearner.sln` (includes test project).
- **Run:** `npm run backend:test` or `dotnet test backend/OetLearner.sln`.
- **Database:** SQLite in-memory for tests (catches concurrency regressions better than EF InMemory).

---

## Security Considerations

- **JWT** with refresh token rotation. Tokens validated on every request via `OnTokenValidated`.
- **RBAC**: 16 granular admin permissions (e.g., `ManageUsers`, `ManageBilling`, `ManageContent`).
- **Middleware**: Route-level protection; all `/admin/*`, `/expert/*`, `/sponsor/*` paths require auth + role.
- **CSP**: Content Security Policy headers configured in `next.config.ts`.
- **X-Frame-Options**: DENY. **X-Content-Type-Options**: nosniff.
- **Backend proxy**: Only forwards `/v1/*` paths; strips `X-Debug-*` and forwarding headers.
- **Desktop**: IPC sender validation via `validateSenderFrame()`. Electron Fuses: RunAsNode=false, NodeCLI=false.
- **Mobile**: Certificate pinning, Keychain/Keystore secure storage, biometric auth.
- **External auth**: `next` param rejects scheme-relative paths (`//evil.com`).
- **Admin suspension**: Enforced in JWT validation + sign-in/refresh flows; also revokes refresh tokens.

---

## Skill Routing (for AI Agents)

Use the installed project skills based on the area being changed:

| Area | Skills to Use |
| ------ | --------------- |
| `app/`, `components/`, `contexts/`, `hooks/`, `lib/` | `next-best-practices`, `vercel-react-best-practices`, `vercel-composition-patterns`, `tailwind-css-patterns`, `typescript-advanced-types` |
| UI polish, accessibility, SEO | `frontend-design`, `accessibility`, `seo` |
| `tests/e2e/`, Playwright configs | `playwright-best-practices` |
| Unit tests | `vitest` |
| Node tooling, route handlers | `nodejs-backend-patterns`, `nodejs-best-practices` |
| `electron/`, desktop packaging | `electron-pro` |
| `backend/` | `dotnet-best-practices`, `aspnet-minimal-api-openapi`, `dotnet-design-pattern-review` |

---

## Environment Variables

### Frontend (`.env.local`)

```text
NEXT_PUBLIC_API_BASE_URL=http://localhost:5062
APP_URL=http://localhost:3000
```

### Backend (via Docker `.env.production`)

```text
ConnectionStrings__DefaultConnection=Host=postgres;...
Auth__JwtSecret=<secret>
Auth__RefreshTokenSecret=<secret>
Brevo__ApiKey=<key>
Stripe__SecretKey=<key>
```

> **Never** commit `.env*` files. `.gitignore` excludes all `.env*` except `.env.example`.

---

## Common Gotchas

- **OET Scoring (MISSION CRITICAL)**: All pass/fail logic, raw↔scaled conversion, and country-aware Writing thresholds MUST route through `lib/scoring.ts` (TS) or `OetLearner.Api.Services.OetScoring` (.NET). Full spec: **[`docs/SCORING.md`](docs/SCORING.md)**. Key invariants: `30/42 ≡ 350/500` for Listening/Reading; Writing pass is **350** for UK/IE/AU/NZ/CA and **300** for US/QA; Speaking is always 350. Never compare `score >= 350` inline.
- **OET Rulebooks (MISSION CRITICAL)**: All Writing / Speaking rule enforcement MUST route through `lib/rulebook` (TS) or `OetLearner.Api.Services.Rulebook` (.NET). Full spec: **[`docs/RULEBOOKS.md`](docs/RULEBOOKS.md)**. Canonical content lives in `rulebooks/**/rulebook.v*.json`. Never read those JSON files from UI or endpoint code directly — always use the engine API.
- **AI calls (MISSION CRITICAL)**: Every AI invocation MUST go through the grounded gateway: `buildAiGroundedPrompt()` (TS) or `AiGatewayService.BuildGroundedPrompt()` + `CompleteAsync()` (.NET). The .NET gateway **physically refuses** ungrounded prompts with `PromptNotGroundedException`. The prompt embeds the rulebook + the canonical scoring + strict guardrails. Adding a new AI provider = implement `IAiModelProvider`; grounding code is never touched. Every call (success, provider error, or refusal) produces exactly one `AiUsageRecord` row via `IAiUsageRecorder` — see **[`docs/AI-USAGE-POLICY.md`](docs/AI-USAGE-POLICY.md)** for the configurable policy model (quotas, BYOK, fallback, kill-switch, overage).
- **Content uploads (MISSION CRITICAL)**: All learner content lives as `ContentPaper` (curatorial unit) → `ContentPaperAsset` (typed file slot by `PaperAssetRole`) → `MediaAsset` (physical, SHA-256 content-addressed). Admins CRUD via `/v1/admin/papers/*` and the chunked `/v1/admin/uploads/*` endpoints; bulk ZIP import via `/v1/admin/imports/zip`. Storage goes through `IFileStorage` so S3/R2 swap is DI-only. Publish gate requires both all role-specific primary assets (`IContentPaperService.RequiredRolesFor`) and a non-empty `SourceProvenance`. Every mutation writes an `AuditEvent`. See **[`docs/CONTENT-UPLOAD-PLAN.md`](docs/CONTENT-UPLOAD-PLAN.md)**. Never write raw files via `File.*` or `Path.*` directly — always `IFileStorage`.
- **OET Statement of Results card (MISSION CRITICAL)**: The learner-facing result card in `components/domain/OetStatementOfResultsCard.tsx` is a pixel-faithful reproduction of the CBLA official Statement of Results. Design contract: **[`docs/OET-RESULT-CARD-SPEC.md`](docs/OET-RESULT-CARD-SPEC.md)** — do not restyle, do not "improve", and never remove the practice disclaimer. Any change requires a pixel diff against the reference screenshots in `Project Real Content/Create Similar Table Formats for Results to show to Candidates/`. Adapter from internal `MockReport` → `OetStatementOfResults` is `lib/adapters/oet-sor-adapter.ts`; use it exclusively rather than constructing the shape by hand at call sites.
- **Reading Authoring (MISSION CRITICAL)**: Reading papers are graded exact-match against authored structure, not against AI. Canonical shape **20 (Part A) + 6 (Part B) + 16 (Part C) = 42 items** enforced at the publish gate. Grading routes through `OetLearner.Api.Services.Reading.ReadingGradingService`, which ONLY uses `OetScoring.OetRawToScaled` for raw→scaled (anchor: `30/42 ≡ 350/500`). Learner-facing endpoints use separate DTOs that **never** serialise `CorrectAnswerJson` / `ExplanationMarkdown` / `AcceptedSynonymsJson` — enforced at the projection layer in `ReadingLearnerEndpoints.cs`. See **[`docs/READING-AUTHORING-PLAN.md`](docs/READING-AUTHORING-PLAN.md)** and **[`docs/READING-AUTHORING-POLICY.md`](docs/READING-AUTHORING-POLICY.md)** for the configurable policy model (retry, timer, explanation visibility, AI extraction, question bank, accessibility, security, retention, lifecycle).
- **Grammar Module (MISSION CRITICAL)**: All grammar lessons are server-authoritative — grading, publish gate, entitlement, and AI drafts run on the backend. Canonical rulebook: `rulebooks/grammar/<profession>/rulebook.v1.json` (schema `kind` enum extended to include `grammar`). AI drafts MUST route through `OetLearner.Api.Services.Grammar.GrammarDraftService` which builds a grounded prompt via `IAiGatewayService.BuildGroundedPrompt(Kind = Grammar, Task = GenerateGrammarLesson)` with `FeatureCode = AiFeatureCodes.AdminGrammarDraft` (platform-only — BYOK refused). Every `appliedRuleIds` value in the reply must exist in the loaded grammar rulebook; unusable replies fall back to a deterministic starter template with a `warning` surfaced to the admin. Free tier is capped at **3 lessons per rolling 7-day window** (`GrammarEntitlementService`, `/v1/grammar/entitlement`). Full spec: **[`docs/GRAMMAR-MODULE.md`](docs/GRAMMAR-MODULE.md)**.
- **Pronunciation Module (MISSION CRITICAL)**: All pronunciation drills, attempts, and scoring are server-authoritative. Canonical rulebook: `rulebooks/pronunciation/<profession>/rulebook.v1.json` (schema `kind` enum extended to include `pronunciation`). Scoring NEVER bypasses `IPronunciationAsrProviderSelector` — Azure / Whisper / Mock are selected via `PronunciationOptions.Provider`; there is NO RNG scoring anywhere. Advisory band projection anchored at **70/100 ≡ 350/500** via `OetScoring.PronunciationProjectedScaled()` (or `lib/scoring.ts:pronunciationProjectedScaled()` on the client) — never compare `overall >= 70` inline. AI scoring/feedback/drill-drafting MUST route through `IAiGatewayService.BuildGroundedPrompt(Kind = Pronunciation, Task = ScorePronunciationAttempt | GeneratePronunciationDrill | GeneratePronunciationFeedback)` with feature codes `AiFeatureCodes.PronunciationScore | PronunciationFeedback | AdminPronunciationDraft` (platform-only for scoring + admin drafting). All audio I/O goes through `IFileStorage`; retention is `PronunciationOptions.AudioRetentionDays` swept by `PronunciationAudioRetentionWorker`. Publish gate requires phoneme + label + tips + ≥3 example words + ≥1 sentence. Full specs: **[`docs/PRONUNCIATION.md`](docs/PRONUNCIATION.md)** and **[`docs/PRONUNCIATION-AUTHORING-POLICY.md`](docs/PRONUNCIATION-AUTHORING-POLICY.md)**.
- **AI Conversation Module (MISSION CRITICAL)**: All AI conversation sessions, audio, AI replies and evaluation are server-authoritative. Canonical rulebook: `rulebooks/conversation/<profession>/rulebook.v1.json` (schema `kind` enum extended to include `conversation`). ASR NEVER bypasses `IConversationAsrProviderSelector` — Azure / Whisper / Deepgram / Mock selected via `ConversationOptions.AsrProvider`. TTS NEVER bypasses `IConversationTtsProviderSelector` — Azure / ElevenLabs / CosyVoice / ChatTTS / GPT-SoVITS / Mock / off selected via `ConversationOptions.TtsProvider`. Advisory rubric projection anchored at **mean 4.2/6 ≡ 350/500** via `OetScoring.ConversationProjectedScaled()` (or `lib/scoring.ts:conversationProjectedScaled()`) — never compare `mean >= 4.2` inline. AI opening / reply / evaluation / scenario-drafting MUST route through `IAiGatewayService.BuildGroundedPrompt(Kind = Conversation, Task = GenerateConversationOpening | GenerateConversationReply | EvaluateConversation | GenerateConversationScenario)` with feature codes `AiFeatureCodes.ConversationOpening | ConversationReply | ConversationEvaluation | AdminConversationDraft` (platform-only for evaluation + admin). The gateway refuses ungrounded prompts with `PromptNotGroundedException`. All audio I/O goes through `IConversationAudioService` → `IFileStorage` (content-addressed SHA-256); retention is `ConversationOptions.AudioRetentionDays` swept by `ConversationAudioRetentionWorker`. Publish gate for `ConversationTemplate` requires title + scenario + role + patient context + ≥3 objectives + duration + valid task type (`oet-roleplay` | `oet-handover`). Every evaluation seeds `ReviewItem` rows with `SourceType = "conversation_issue"` for rule-cited mistakes. Full spec: **[`docs/CONVERSATION.md`](docs/CONVERSATION.md)**.
- **PowerShell on Windows**: Run `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass` first, or use `cmd /c npm ...`.
- **`motion/react` not `framer-motion`**: This project uses `motion` v12 package. Import from `motion/react`.
- **Desktop OAuth**: Use `oet-prep://` scheme (double slash required).
- **Docker volumes**: `oetwebsite_` prefix (migrated from old `oetwebapp_` prefix). Never delete postgres volume without backup.
- **TypeScript 5.9**: Removed deprecated `baseUrl`; uses `ignoreDeprecations: '5.0'` only.
- **Test regex**: Avoid fuzzy selectors like `/welcome/i` when multiple DOM elements match — use exact strings.
- **Vitest globals**: `describe`/`it`/`expect`/`vi` are globally available via tsconfig `types` array.

---

## PR & Commit Guidelines

- **Commit format**: `type(scope): description` (e.g., `feat(admin): add user import page`).
- **Types**: `feat`, `fix`, `refactor`, `test`, `docs`, `style`, `chore`.
- **Before commit**: Run `npx tsc --noEmit`, `npm run lint`, `npm test`.
- **Before merge**: All checks green. No `// @ts-ignore` or `any` without justification.

---

## Refreshing Skills

The repo keeps a vendored copy of `autoskills` at `.tools/autoskills/`.

```powershell
# Dry run
powershell -ExecutionPolicy Bypass -File .\scripts\refresh-autoskills.ps1 -DryRun

# Install/update
powershell -ExecutionPolicy Bypass -File .\scripts\refresh-autoskills.ps1
```

Default agents: `universal`, `codex`, `claude-code`.

---

## MCP Servers

- **`jcodemunch-mcp`**: Preferred for code navigation. See `.claude/mcp/jcodemunch/CLAUDE.md`.
- **`context-mode`**: Installed globally. Use `ctx stats`, `ctx doctor`, `ctx upgrade`, `ctx purge` for context management.
- **`cc_token_saver_mcp`**: Route short tasks to local LLM first. Launches from `.claude/mcp/cc_token_saver_mcp/launch.py`.
- VS Code routing lives in `.vscode/mcp.json` and `.github/hooks/context-mode.json`.
