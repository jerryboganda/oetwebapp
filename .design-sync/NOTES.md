# design-sync NOTES — oet-with-dr-hesham → "OET with Dr. Hesham — UI"

Repo-specific gotchas for future re-syncs. Read this first.

## Build facts

- **`oet-with-dr-hesham` is the Next.js app at repo root**, not a published component library. The
  "design system" is `components/ui/*.tsx`. There is **no `dist/`**.
- A **synthesized entry** at `.design-sync/build/ui-entry.ts` re-exports the 40 DS
  components (mirrors `components/ui/index.ts` EXCEPT `CardLink`, which wraps `next/link`
  and can't bundle into a standalone IIFE — deliberately excluded; `componentSrcMap.CardLink: null`).
- CSS is compiled **Tailwind v4**: `.design-sync/build/tw-input.css` →
  `.design-sync/build/styles.compiled.css` (≈458 KB) via `.design-sync/build/compile-css.mjs`,
  wired as `cfg.cssEntry`. The build copies it to `_ds_bundle.css` and `styles.css` just
  `@import`s it. (A prior session left `styles.css` empty — a clean rebuild fixes it.)
- Build command (run from repo root):
  ```sh
  node .ds-sync/package-build.mjs --config .design-sync/config.json \
    --node-modules ./node_modules --entry ./.design-sync/build/ui-entry.ts --out ./ds-bundle
  ```
  `--node-modules ./node_modules` = repo root (where `react@19` resolves). No `buildCmd` in
  config because the synth entry + precompiled CSS already exist; if `components/ui/*` changes,
  rebuild is just the command above (deterministic).
- Render check needs Playwright; installed `playwright@1.61.1` into `.ds-sync` + chromium
  (cached in `%LOCALAPPDATA%/ms-playwright`).

## Known render warns (triaged — NOT new on re-sync)

- **[FONT_MISSING] "Cambria"** — it's only a member of Tailwind's default
  `--font-serif: ui-serif, Georgia, Cambria, "Times New Roman", Times, serif` stack, i.e. a
  generic fallback, **not a brand font**. The brand face is the sans stack. No action; accepted.
- **[TOKENS_MISSING] 14 vars** — `--radix-*` (radix injects these at runtime on portaled
  elements; expected absent in static CSS) and a few app-shell tokens (`--header-height`,
  `--admin-*`) that ARE defined in `_ds_bundle.css` but under scoped selectors. Components
  render correctly (verified on contact sheets). Non-blocking; accepted.

## Motion / preview-render fix (CRITICAL — already wired)

- DS components animate in from `opacity:0` via `motion/react` (`getSurfaceMotion` /
  `getCelebrateMotion`; even the reduced-motion branch starts at `opacity:0`). Static
  screenshots capture them **blank** because the enter animation hasn't run.
- Fixed with a **preview-only provider**: `.design-sync/build/ds-preview-provider.tsx`
  exports `DsPreviewProvider`, wired via `cfg.extraEntries` + `cfg.provider`. Its render
  body sets `MotionGlobalConfig.skipAnimations = true` so motion renders at final state.
  Set in the render body (not module load) so it fires **only** for preview cards — the
  shipped `_ds_bundle.js` that designs import is untouched (production motion intact).
- `DsPreviewProvider` is an extraEntry export, NOT counted as a component (still 40).
- Without this, ANY motion surface (alerts, Toast, Modal, Drawer, Motion* primitives,
  Stepper transitions, etc.) captures blank. It is already in place; don't remove it.

## Preview authoring conventions (for `.design-sync/previews/<Name>.tsx`)

- One file per component. **Each named export = one card cell** and must be a
  **function component** (PascalCase) returning JSX (rendered via `React.createElement`).
- Import DS components from `'oet-with-dr-hesham'` (the build redirects this to the `window.OetUI`
  bundle global). JSX automatic runtime — **no `import React`**.
- Layout glue between components uses **inline `style={{}}`** (Tailwind utility classes are
  NOT available to the preview's own wrapper markup — only the DS components carry their
  classes). 2–6 cells per component; realistic OET / healthcare-English-exam copy, never foo/bar.
- Numeric/required props need real values: `CircularProgress`/`ProgressBar` show `NaN%` with
  defaults — pass `value`. `StatCard`, `DataTable`, `Stepper`, `Tabs` need real data arrays.
- Compose context-required sub-parts inside their parent (e.g. `CardHeader`/`CardTitle`/
  `CardContent`/`CardFooter` shown inside a `<Card>`; `TabPanel` inside `<Tabs>`).
- Overlay / `position:fixed` components (`Toast`, `Modal`, `Drawer`, `MobileFilterSheet`)
  need `cfg.overrides.<Name> = {cardMode:'single', viewport:'WxH'}`; wide ones (`DataTable`,
  `FilterBar`) need `{cardMode:'column'}`. These are **orchestrator-only config edits** —
  subagents record the need in their learnings file, they don't touch config.

## Per-component authoring learnings (from the all-40 wave)

- **Overlay components use `position:fixed`** → wrap them in a sized, transformed
  "Stage" in the preview so they have a measured containing block:
  `<div style={{position:'relative', transform:'translateZ(0)', height:H, width:'100%', overflow:'hidden'}}>`.
  See `previews/Toast.tsx` (H=150) and `previews/Drawer.tsx` (H=520). Without it they
  capture blank/0px. Modal does NOT need a Stage (its own centered layout sits in the
  single-card transform fine).
- **`Toast`** is `cardMode:single` + `primaryStory:"Success"` — in column/grid the fixed
  toast trips `[GRID_OVERFLOW]` (single is exempt). All 3 variants still capture for grading.
- **`MobileFilterSheet`** owns its open state internally; statically it renders ONLY the
  "Filters" trigger button (the sheet opens on click, which a screenshot can't trigger).
  No override; the trigger is the honest static preview.
- **`RadioGroup`, `Tabs`, `FilterBar`** are controlled → previews use `import { useState }`.
- **`ErrorState`** props are `message` + `onRetry` (NOT description/action).
- **`Stepper`** horizontal labels are `hidden sm:block` (only number circles show below 640px);
  `cardMode:column`. `StatCard` is also `cardMode:column` (4-up dashboard grid overflows a cell).
- **Numeric props are mandatory**: `ProgressBar`/`CircularProgress`/`Timer` render NaN/blank
  without real `value`/time props.
- The bundle `<Name>.d.ts` files are generic stubs (`[key:string]:unknown`) — author from the
  real `components/ui/*.tsx` sources, which are authoritative.

## Grading / re-sync gotcha

- Grade-file cell keys MUST equal the export names in `previews/<Name>.tsx` **exactly**
  (PascalCase, case-sensitive) — they're matched against `.design-sync/.cache/review/<Name>.json`
  `cells` (an array of name strings). camelCase/UPPERCASE keys silently fail to match and read
  as ungraded. (Grades live in the gitignored `.cache/`; durable verified-state is the uploaded
  `_ds_sync.json`.)

## Re-sync risks (watch-list)

- `styles.compiled.css` is **precompiled and committed-as-build-output (gitignored)**. If
  `components/ui/*` Tailwind classes change, re-run `.design-sync/build/compile-css.mjs` before
  the bundle build, or new utility classes won't be in `_ds_bundle.css`.
- Previews under `.design-sync/previews/*.tsx` are authored against the **current** component
  APIs; if a component's props change upstream, its preview may need updating (check the
  `.d.ts` after rebuild).
