# Remaining `eslint-disable` / `@ts-expect-error` Inventory

**Date:** 2026-04-24
**Scope:** After Wave 4 (`d6f5b75`) removed every `react-hooks/exhaustive-deps` disable, exactly **three** suppression sites remain across `app/`, `components/`, `lib/`, and `hooks/`. This doc catalogs each one with justification, risk, and fix path. No source changes in this doc.

**Verification (at HEAD `40feb09`):**

```powershell
rg -n "eslint-disable|@ts-ignore|@ts-expect-error" app/ components/ lib/ hooks/
```

Returns three hits, all listed below. Backend (`backend/`), tests (`tests/`), E2E (`tests/e2e/`), and generated (`.next/`) output are excluded by scope.

---

## Site 1 — `components/domain/audio-player-waveform.tsx:104`

**Directive:** `// eslint-disable-next-line react-hooks/exhaustive-deps`
**Rule suppressed:** `react-hooks/exhaustive-deps`
**In‑file justification:**

> WaveSurfer instance lifecycle: re‑creating on callback changes would destroy/rebuild the waveform

**Context:** `useEffect` that spins up a WaveSurfer audio‑visualisation instance. Effect deps are `[resolvedAudioUrl]`. Handler callbacks (`onTimeUpdate`, play/pause listeners) are intentionally **not** in the dep array — if they were, every parent rerender that produced a new callback identity would tear down and rebuild the waveform DOM.

**Risk class:** **Low.** The pattern is a documented escape hatch for third‑party imperative instances. Callbacks are read via closures created at mount; stale‑closure risk exists only if the parent swaps in a callback that depends on rapidly changing parent state — which it currently does not.

**Fix paths (ordered by invasiveness):**

1. **Preferred — `useEffectEvent`** (React 19.2+, now stable in this project after Wave 4). Wrap each external callback in `useEffectEvent(onTimeUpdate)` and call the stable wrapper inside the effect. This removes the suppression and matches the pattern Wave 4 used in `lib/hooks/use-dashboard-home.ts`.
2. **Acceptable — `useRef` latch.** Store callbacks in a ref, update via `useEffect(() => { ref.current = onTimeUpdate })`, read `ref.current(...)` inside handlers. Mechanical, no React‑version dependency.
3. **Not recommended** — adding callbacks to the dep array and gating instance rebuild with a manual diff. Adds complexity for no gain.

**Recommended action:** convert to `useEffectEvent` in the next FE hygiene pass. Pattern is identical to `d6f5b75`; the change stays within the file, requires one test addition, and is autonomous‑safe.

**Autonomous‑safe?** Yes, once the pattern is signed off.

---

## Site 2 — `components/domain/OetStatementOfResultsCard.tsx:441`

**Directive:** `{/* eslint-disable-next-line @next/next/no-img-element */}`
**Rule suppressed:** `@next/next/no-img-element`
**In‑file justification:**

> fixed-size document asset, next/image would add layout cost without benefit

**Context:** The card renders the CEO signature PNG at a hard‑coded 160×54px inside the certification block. The asset lives at `/oet/signature-sujata-stead.png`; it is a document‑fidelity element whose size never changes regardless of viewport.

**MISSION‑CRITICAL marker (from AGENTS.md):**

> The learner‑facing result card in `components/domain/OetStatementOfResultsCard.tsx` is a pixel‑faithful reproduction of the CBLA official Statement of Results. … **Do not restyle, do not "improve", and never remove the practice disclaimer.** Any change requires a pixel diff against the reference screenshots in `Project Real Content/Create Similar Table Formats for Results to show to Candidates/`.

**Risk class:** **None that this audit can action unilaterally.** Switching `<img>` to `next/image` would:

- Inject Next's default `srcSet` / `sizes` behaviour into a design‑locked region.
- Change DOM structure (next/image wraps in a `<span>` with padding‑based intrinsic sizing).
- Require a pixel diff against the reference screenshots before merge — i.e., a human gate.

**Fix paths:**

1. **Keep as‑is.** Current state is compliant with the AGENTS.md lock. No action.
2. **If a design‑system pass ever opens the SoR card for revision:** migrate to `next/image` with `priority` + explicit `width`/`height` + a pixel‑diff CI job against the reference screenshots. Not autonomous‑safe.

**Recommended action:** **do not touch.** This suppression is load‑bearing documentation of the lock. Removing it silently would invite a future contributor to swap the `<img>` without understanding the constraint.

**Autonomous‑safe?** **No — locked by AGENTS.md.**

---

## Site 3 — `lib/mobile/pronunciation-recorder.ts:44`

**Directive:** `// @ts-expect-error — optional peer dep: @capacitor-community/voice-recorder`
**Rule suppressed:** TypeScript module‑resolution error
**In‑file justification:**

> optional peer dep: `@capacitor-community/voice-recorder`

**Context:** `getPlugin()` dynamically imports the recorder plugin **only** on native platforms (`Capacitor.isNativePlatform()`). The package is declared an *optional peer dependency* precisely so web builds can ship without it. When `tsc` runs on a web‑only install, it cannot resolve the module — which is the whole point: the code path is unreachable on web.

Surrounding annotations reinforce the intent:

```ts
const mod = (await import(
  /* webpackIgnore: true */
  /* @vite-ignore */
  // @ts-expect-error — optional peer dep: @capacitor-community/voice-recorder
  '@capacitor-community/voice-recorder'
)) as { VoiceRecorder: VoiceRecorderPlugin };
```

**Risk class:** **Low.** `@ts-expect-error` (not `@ts-ignore`) fails the build if the error ever disappears, so the suppression self‑heals the moment the dep becomes resolvable. The `try { … } catch { pluginCache = null }` wrapper guarantees web fallback even if resolution ever changes shape.

**Fix paths:**

1. **Preferred — ambient module declaration.** Add a `declare module '@capacitor-community/voice-recorder';` in a native‑only `.d.ts` guarded by `/// <reference types="…"/>`, or ship a lightweight typings shim. Removes the suppression cleanly.
2. **Stronger — install the plugin as a regular optional dep** and let TypeScript resolve it. Requires confirming web bundlers still tree‑shake it out (both `webpackIgnore` and `@vite-ignore` comments remain in place, so this should be safe but needs a bundle‑size diff).
3. **Status quo.** The current pattern is battle‑tested and the suppression is the narrowest possible.

**Recommended action:** defer to the next mobile module pass. Not blocking; the `@ts-expect-error` form is self‑healing.

**Autonomous‑safe?** Option 1 (ambient `.d.ts`) is autonomous‑safe. Option 2 needs a bundle‑size check.

---

## Summary Table

| Site | File:Line | Rule | Risk | AGENTS.md lock | Fix autonomous‑safe? | Recommended |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `components/domain/audio-player-waveform.tsx:104` | `react-hooks/exhaustive-deps` | Low | No | Yes | Convert to `useEffectEvent` next FE pass |
| 2 | `components/domain/OetStatementOfResultsCard.tsx:441` | `@next/next/no-img-element` | None | **Yes — locked** | No | **Do not touch** |
| 3 | `lib/mobile/pronunciation-recorder.ts:44` | `@ts-expect-error` (module resolution) | Low | No | Yes (ambient `.d.ts`) | Defer to next mobile pass |

**Overall suppression budget in app code: 3 sites / 3 justified.** Zero unjustified suppressions, zero `@ts-ignore`, zero `any`‑casts flagged by this audit.

---

## Verification Recipe

To reproduce this inventory:

```powershell
rg -n "eslint-disable|@ts-ignore|@ts-expect-error" app/ components/ lib/ hooks/
```

Expected output at HEAD `40feb09`:

```text
components/domain/audio-player-waveform.tsx:104: …
components/domain/OetStatementOfResultsCard.tsx:441: …
lib/mobile/pronunciation-recorder.ts:44: …
```

If this count ever rises, the delta must be added here with the same six‑field profile (site, directive, rule, justification, risk, fix path).

---

## Change Log

- **2026-04-24** — Initial inventory written against tree at `40feb09`. Follows the Wave 4 `exhaustive-deps` cleanup (`d6f5b75`) and the pronunciation test cleanup (`59a5a5f`).
