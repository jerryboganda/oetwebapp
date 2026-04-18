# OET Rulebooks — Canonical Specification

> **Mission-critical.** This document is the single source of truth for the
> two Dr. Ahmed Hesham rulebooks and the grounded-AI system built around
> them. All Writing / Speaking rule enforcement and every AI call in the
> platform routes through this stack. Deviation breaks the business
> model — the rulebooks ARE the product differentiator.

---

## 1. Authority

- **Author:** Dr. Ahmed Hesham · The Tutor Book · @DrAhmedHesham
- **Source files (gitignored, kept local):**
  - `Project Real Content/Writing_/Writing RuleBook ( Medicine only )/OET_Writing_Rulebook_FINAL.pdf` — 100+ rules, 16 sections (R01.1 – R16.8).
  - `Project Real Content/Speaking_/Speaking Rulebook ( Medicine Only )/OET_Speaking_Rulebook_v2 (1).pdf` — 55 rules, 7 sections (RULE 01 – RULE 55).
  - `Project Real Content/Speaking_/Speaking Assessment Criteria ( ... ).pdf` — public OET rubric.
- **Canonical extraction:** committed JSON under `rulebooks/` — every rule
  with `severity` (`critical` / `major` / `minor` / `info`), `appliesTo`
  (letter type or card type), deterministic detectors via `checkId`, and
  exemplar / forbidden phrases.

---

## 2. Canonical JSON files

```
rulebooks/
├── schema/
│   └── rulebook.schema.json          # JSON Schema draft-2020-12
├── writing/
│   ├── common/assessment-criteria.json   # 6-criteria marking rubric (Purpose 0–3, others 0–7)
│   └── medicine/rulebook.v1.json         # full writing rulebook (medicine)
└── speaking/
    ├── common/assessment-criteria.json   # linguistic bands 0–6 + clinical A–E indicators 0–3
    └── medicine/rulebook.v1.json         # full speaking rulebook (medicine)
```

Versioned ("v1.0.0"). New professions drop in as
`rulebooks/<kind>/<profession>/rulebook.v1.json` with no engine changes.

---

## 3. Rule engines

Both runtimes ship identical, deterministic detectors.

### TypeScript — `lib/rulebook/*`

| File | Responsibility |
|---|---|
| `types.ts` | Canonical types (Rule, Rulebook, LintFinding, SpeakingTurn, AiGroundingContext, AiGroundedPrompt). Mirror of the JSON schema. |
| `loader.ts` | Static import of JSON rulebooks. `loadRulebook(kind, profession)` / `findRule(...)` / `rulesApplicableTo(...)`. Throws `RulebookNotFoundError` for unregistered professions. |
| `writing-rules.ts` | `lintWritingLetter(input)` — 40+ deterministic detectors keyed by `checkId`. Returns severity-sorted `LintFinding[]` plus a `writingCoverageSummary()`. |
| `speaking-rules.ts` | `auditSpeakingTranscript(input)` — jargon, monologue, weight-sensitivity, smoking-ladder order, over-diagnosis, stage coverage, and Breaking Bad News 7-step protocol detectors. |
| `ai-prompt.ts` | **`buildAiGroundedPrompt(ctx)`** — the only supported way to generate an AI system prompt (see §4). |
| `index.ts` | Barrel: `import { ... } from '@/lib/rulebook'`. |

Tests: `lib/rulebook/*.test.ts` — **152 assertions**, all green.

### .NET — `OetLearner.Api.Services.Rulebook`

| File | Responsibility |
|---|---|
| `RulebookLoader.cs` | `IRulebookLoader` — loads JSON from embedded resources (assembly-internal; no filesystem dependency on the server). |
| `WritingRuleEngine.cs` | Mirror of `writing-rules.ts`. |
| `SpeakingRuleEngine.cs` | Mirror of `speaking-rules.ts`. |
| `AiGatewayService.cs` | **The only path to any AI model.** Holds `RulebookPromptBuilder`, the grounded-prompt contract, provider registry, and the refusal logic that blocks ungrounded prompts. |

Tests: `backend/tests/OetLearner.Api.Tests/RulebookEngineTests.cs` — **55 assertions**, all green.

---

## 4. AI grounding — mission-critical contract

**Every AI call in the platform — regardless of provider — MUST go through
the gateway.** The gateway refuses requests unless the system prompt
carries the rulebook header string `"OET AI — Rulebook-Grounded System Prompt"`,
so accidental raw-prompt calls are impossible.

### System prompt anatomy

Built by `buildAiGroundedPrompt(ctx)` (TS) / `AiGatewayService.BuildGroundedPrompt(ctx)` (.NET). Each prompt contains:

1. **Header** — rulebook kind + profession + version + task + candidate country + applied pass mark.
2. **Canonical OET Scoring section** — the full country-aware scoring spec from `lib/scoring.ts` / `OetScoring.cs`.
3. **Active rulebook** — all CRITICAL rules (always) and top 60 MAJOR rules, each with code, severity, title, body, and exemplar phrase.
4. **Guardrails** — 8 strict rules (cite rule IDs, no invented rules, no contradicting scoring, advisory-only output, tone, state-machine respect).
5. **Reply format** — task-specific JSON contract (`score`, `coach`, `correct`, `generate_feedback`, `generate_content`, `summarise`).

### Country-aware scoring inside the prompt

| Kind | Country | Pass mark | Grade |
|---|---|---|---|
| Writing | UK, IE, AU, NZ, CA | 350/500 | B |
| Writing | US, QA            | 300/500 | C+ |
| Writing | unknown           | default 350/500 | B |
| Speaking | any              | 350/500 | B (universal) |

### Gateway refusal

```csharp
if (!request.Prompt.SystemPrompt.Contains("OET AI — Rulebook-Grounded System Prompt"))
    throw new PromptNotGroundedException(
        "SystemPrompt does not carry the rulebook grounding header. " +
        "Build it via AiGatewayService.BuildGroundedPrompt.");
```

Equivalent guard on the TS side is enforced by only exposing the
grounded builder as the public path to prompt construction.

### Providers

`IAiModelProvider` is the vendor abstraction. The backend now ships with:

- `MockAiProvider` — local / test fallback, no external model call.
- `OpenAiCompatibleProvider` — real provider for DigitalOcean Serverless
  Inference and any other OpenAI-compatible `/chat/completions` API.

### Usage accounting (Slice 1, AI Usage Management)

`AiGatewayService` is now paired with an optional `IAiUsageRecorder`
(default: `AiUsageRecorder`). Every call — success, provider error, or
gateway refusal (ungrounded prompt / missing provider) — produces exactly
one `AiUsageRecord` row in the `AiUsageRecords` table.

Admin read-only surface:

- `GET /v1/admin/ai/usage` — paginated, filterable log.
- `GET /v1/admin/ai/usage/summary` — monthly roll-ups by feature / provider
  / outcome / user.

Policy options that govern the rest of the subsystem (quotas, BYOK,
fallback, kill-switch, overage, custody) live in
[`docs/AI-USAGE-POLICY.md`](AI-USAGE-POLICY.md). Slice 1 touches none of
those policies — it only establishes the audit trail they will share.


Configuration comes from the `AI` section in `appsettings.json` (or env vars):

```json
"AI": {
  "ProviderId": "digitalocean",
  "BaseUrl": "https://inference.do-ai.run/v1",
  "ApiKey": "<secret>",
  "DefaultModel": "anthropic-claude-opus-4.7"
}
```

Environment-variable form (recommended for real deployments):

```text
AI__ProviderId=digitalocean
AI__BaseUrl=https://inference.do-ai.run/v1
AI__ApiKey=<secret>
AI__DefaultModel=anthropic-claude-opus-4.7
```

Adding a new vendor remains a single-class change: implement
`IAiModelProvider`, register it in DI, and keep all grounding code in
`AiGatewayService` / `RulebookPromptBuilder` untouched.

---

## 5. Backend endpoints (all require auth)

| Method | Path | Purpose |
|---|---|---|
| GET  | `/v1/rulebooks` | List registered rulebooks (kind, profession, version). |
| GET  | `/v1/rulebooks/writing/{profession}`  | Full Writing rulebook JSON. |
| GET  | `/v1/rulebooks/speaking/{profession}` | Full Speaking rulebook JSON. |
| GET  | `/v1/rulebooks/writing/{profession}/rule/{ruleId}`  | Single rule lookup. |
| GET  | `/v1/rulebooks/speaking/{profession}/rule/{ruleId}` | Single rule lookup. |
| GET  | `/v1/rulebooks/assessment/{kind}` | Assessment criteria JSON. |
| POST | `/v1/writing/lint` | Run the Writing rule engine on a letter. |
| POST | `/v1/speaking/audit` | Run the Speaking rule engine on a transcript. |
| POST | `/v1/ai/complete` | Grounded AI call — builds the prompt, forwards to provider, returns completion + `appliedRuleIds`. |

All inputs / outputs are JSON and version-stamped.

---

## 6. Rule engine capabilities — what gets auto-flagged

### Writing (Dr. Hesham R-codes, all `critical` flagged auto-fail)

- Content rules: `R03.2`, `R03.4` (smoking/drinking), `R03.6` (allergy for atopic).
- Layout rules: `R04.2` (no blank before Re:), `R05.8` (no "Date:"), `R05.9`, `R04.1` structure order.
- Naming rules: `R06.1`, `R06.7`, `R06.10` (minors no title, first name in body), `R06.11` (sincerely vs faithfully).
- Intro rules: `R07.3` (purpose required), `R07.6` (urgent must say "urgent"), `R07.7` (discharge no identity), `R14.2` (discharge template).
- Body rules: `R08.1` (≥2 paragraphs), `R08.7` (no "next visit"), `R08.8` (no today's date in body), `R08.14` (no "the patient"), `R08.5` (urgent starts today).
- Tense rules: `R10.2`, `R10.5`, `R10.6`, `R10.8` (surgery past simple), `R10.10` ("X ago" past simple), `R10.14`.
- Medication rules: `R11.1` (translate Latin abbreviations), `R11.8` (always include values).
- Grammar rules: `R12.1` (no contractions), `R12.2`, `R12.5` (conditions lowercase), `R12.9` (`however` punctuation), `R12.19` (sentence length), `R12.20` (linker density).
- Urgent-specific: `R13.2`, `R13.3`, `R13.4`, `R13.6`, `R13.10` (no "ASAP").
- Discharge-specific: `R14.3`, `R14.4` (omit known-to-GP), `R14.6` (admitted with, not presented), `R14.7`, `R14.9`, `R14.10`, `R14.12`.
- Non-medical: `R15.2` (translate jargon), `R15.7`.
- Assessment: `R16.2`, `R16.3`, `R16.5`, `R16.7`.

### Speaking (Dr. Hesham RULE_NN)

- Jargon (RULE 06–12) — uses the 17-entry layman glossary embedded in the rulebook.
- Consultation stage coverage (RULE 13–21) — 13-stage state machine; `RULE 15` (empathy), `RULE 20` (recap), `RULE 21` (closure) enforced.
- Ping-pong monologue detector (RULE 22).
- Weight-sensitivity detector (RULE 23).
- Smoking-ladder ordering (RULE 27): cessation → NRT → reduction, never reverse.
- Over-diagnosis detector (RULE 32): "you have hypertension" / "you have diabetes" from a single reading.
- Breaking Bad News protocol (RULE 40–47) — 7 steps, checked only for `breaking_bad_news` cards. Silence detector (RULE 44) uses `silenceAfterDiagnosisMs` from the audio timeline.
- Warm-up / preparation rules (RULE 49–54) — informational in the prompt.

---

## 7. Versioning & drift control

- Every in-flight submission records the `rulebookVersion` it was graded against.
- Publishing a new rulebook version is an explicit admin action (future CMS flow) — existing submissions remain pinned to their grading version.
- JSON schema (`rulebooks/schema/rulebook.schema.json`) is the contract; schema changes require engine + test updates.

---

## 8. Adding a new profession

1. Author `rulebooks/<kind>/<new-profession>/rulebook.v1.json` using the schema.
2. Register it in `lib/rulebook/loader.ts` (one `import` + one map entry).
3. The .NET loader picks it up automatically via embedded resources on next build.
4. Add xUnit + Vitest coverage for any new detectors introduced for the profession.

---

## 9. Strictly forbidden

- **Reading or importing rulebook JSON anywhere outside the engine layer.** All consumers go through `lib/rulebook` (TS) or the `Rulebook` services (.NET).
- **Calling any AI model without the grounded prompt builder.** The `.NET` gateway physically refuses; the TS side enforces via code review.
- **Hard-coding rule text** in components, endpoints, or emails. Look it up via `findRule(...)` or query the API.
- **Grading without routing through the canonical scoring module** (`lib/scoring.ts` / `OetScoring.cs`).

---

## 10. Quick reference — TypeScript call sites

```ts
import {
  loadRulebook,
  lintWritingLetter,
  auditSpeakingTranscript,
  buildAiGroundedPrompt,
} from '@/lib/rulebook';

// Writing lint
const findings = lintWritingLetter({
  letterText, letterType: 'urgent_referral', profession: 'medicine',
  patientAge: 45, recipientSpecialty: 'Cardiologist',
});

// Speaking audit
const issues = auditSpeakingTranscript({
  transcript, cardType: 'breaking_bad_news', profession: 'medicine',
  silenceAfterDiagnosisMs: 4000,
});

// Grounded AI prompt
const prompt = buildAiGroundedPrompt({
  kind: 'writing', profession: 'medicine', task: 'score',
  candidateCountry: 'UK', letterType: 'routine_referral',
});
// Pass `prompt.system` and `prompt.taskInstruction` to any AI SDK call.
```

---

## 11. Quick reference — .NET call sites

```csharp
public sealed class MyService(IAiGatewayService gateway, WritingRuleEngine writing, SpeakingRuleEngine speaking)
{
    public async Task ExampleAsync()
    {
        // Writing lint
        var findings = writing.Lint(new WritingLintInput(
            LetterText: letterBody,
            LetterType: "urgent_referral",
            RecipientSpecialty: "Cardiologist",
            PatientAge: 45));

        // Speaking audit
        var issues = speaking.Audit(new SpeakingAuditInput(
            Transcript: turns, CardType: "breaking_bad_news",
            SilenceAfterDiagnosisMs: 4000));

        // Grounded AI completion (mandatory path)
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Score,
            CandidateCountry = "UK",
            LetterType = "routine_referral",
        });
        var result = await gateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            UserInput = letterBody,
            Provider = "mock",  // swap for "openai" / "anthropic" etc. in prod
            Model = "gpt-4o",
        });
    }
}
```
