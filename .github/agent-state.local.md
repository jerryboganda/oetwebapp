# Agent State - Writing I18n Standalone Bundle Fix

Last updated: 2026-06-14

## Goal

Prevent the Writing module from rendering raw `writing.*` keys or `Writing copy unavailable` in the Next standalone production bundle.

## Implemented

- Replaced runtime dynamic JSON imports in `i18n.ts` with static imports for `messages/en/writing.json` and `messages/ar/writing.json`.
- Kept English as the baseline bundle and overlays Arabic messages on top so missing Arabic copy falls back to readable English.
- Kept the Writing provider fallback guard in place, but production should no longer hit it because Writing messages are now bundled into the server artifact.
- Extended `tests/unit/i18n-writing-messages.test.ts` with a standalone-build regression assertion that rejects dynamic `import(\`./messages/...json\`)` loading.

## Validation

- `pnpm exec vitest run tests/unit/i18n-writing-messages.test.ts --reporter=dot`: passed, 3 tests.
- `pnpm exec tsc --noEmit`: passed.
- `pnpm exec eslint i18n.ts app/providers.tsx tests/unit/i18n-writing-messages.test.ts`: passed.
- `pnpm run build`: passed.
- Standalone artifact check: `.next/standalone/messages/en/writing.json` contains `writing.hub.hero.title` with `Practise OET Writing your way`.
- Production deploy: GitHub Actions `Build & Deploy (web + API)` run `27509057100` passed for commit `bdfd09d77`.
- Production health: `https://app.oetwithdrhesham.co.uk/api/health` returned HTTP 200 with `{"status":"ok","service":"oet-web"}`.

## Not Run / Blocked

- Authenticated production `/writing` DOM verification could not be completed from Codex because Chrome is not exposing a local CDP endpoint and unauthenticated curl correctly redirects `/writing` to `/sign-in?next=%2Fwriting`.
- Full `pnpm run check:encoding` was not rerun in this follow-up; this change touched only TypeScript source/test files, not translation JSON content.

## Next Step

Open production in an authenticated browser and verify `/writing` renders `Practise OET Writing your way` and does not contain `writing.hub.` or `Writing copy unavailable`.
