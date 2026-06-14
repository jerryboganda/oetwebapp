# Agent State - Writing I18n Raw-Key Hardening

Last updated: 2026-06-14

## Goal

Prevent the Writing module from rendering raw `writing.*` translation keys in production and make QA fail if that regression returns.

## Implemented

- Hardened `i18n.ts` so translated modules load English as the baseline and overlay the requested locale, giving Arabic a readable English fallback for missing strings instead of raw keys.
- Hardened `AppProviders` so missing `writing.*` messages no longer fall back to the raw key; they emit a console error and render `Writing copy unavailable`.
- Added `tests/unit/i18n-writing-messages.test.ts` to verify loaded English/Arabic Writing hub copy and statically cover `app/writing/**` plus `components/domain/writing/**` static `t('writing...')` keys.
- Removed raw `writing.*` fallback acceptance from learner smoke and Writing V2 E2E/a11y smoke specs.
- Added a `/writing` learner-smoke assertion that page body text must not contain raw `writing.*` keys.

## Validation

- `pnpm exec vitest run tests/unit/i18n-writing-messages.test.ts --reporter=dot`: passed, 2 tests.
- `pnpm exec tsc --noEmit`: passed.
- `pnpm exec eslint app/providers.tsx i18n.ts tests/unit/i18n-writing-messages.test.ts tests/e2e/learner/learner-smoke.spec.ts tests/e2e/writing-v2/a11y.spec.ts tests/e2e/writing-v2/mocks.spec.ts tests/e2e/writing-v2/stats.spec.ts tests/e2e/writing-v2/diagnostic.spec.ts tests/e2e/writing-v2/canon-library.spec.ts`: passed.
- `git diff --check`: passed.
- Scoped mojibake scan over changed files with `rg -n "â|ð|�|Ã|Â" <changed files>`: passed with no matches.

## Not Run / Blocked

- Focused Playwright writing-route smokes were not run because the local frontend/API stack was not reachable.
- `scripts/OET-Local-Launch.ps1 -SkipBrowser` timed out after 3 minutes; afterward `http://localhost:3000` and `http://localhost:5198/health` were still unreachable, while PostgreSQL alone was listening on `localhost:5432`.
- `pnpm run check:encoding` was attempted and failed on pre-existing `.codex/skills/...` mojibake noise, not on changed files; changed files passed the scoped scan above.

## Next Step

Start a healthy local stack or run the writing Playwright smoke shards in CI, then verify:

- learner `/writing`
- `tests/e2e/writing-v2/mocks.spec.ts`
- `tests/e2e/writing-v2/diagnostic.spec.ts`
- `tests/e2e/writing-v2/stats.spec.ts`
- `tests/e2e/writing-v2/canon-library.spec.ts`
- `tests/e2e/writing-v2/a11y.spec.ts`
