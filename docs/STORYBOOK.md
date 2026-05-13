# Storybook Setup (Listening V2)

This repo ships with a minimal Storybook 9 scaffold for the **Listening
V2** extracted player components. Storybook is opt-in: it is not part of
`npm test`, `npm run lint`, or `npx tsc --noEmit`. Story files and the
`.storybook/` folder are excluded from lint + typecheck so the
production build stays green whether or not Storybook is installed.

## First-time install

```bash
npm install
```

That pulls the Storybook devDependencies declared in `package.json`:

- `storybook` (CLI)
- `@storybook/nextjs-vite` (framework — Next 15 + Vite + React 19)
- `@storybook/react` (renderer)
- `@storybook/addon-a11y` (axe-core a11y panel)

## Run locally

```bash
npm run storybook         # dev server on http://localhost:6006
npm run build-storybook   # static export to ./storybook-static
```

## Where the stories live

```text
.storybook/
  main.ts                 # framework + addons config
  preview.ts              # global Tailwind import + decorators

components/domain/listening/player/__stories__/
  fixture.ts              # shared ListeningSessionDto fixture
  ListeningIntroCard.stories.tsx
  ListeningAudioTransport.stories.tsx
  ListeningSectionStepper.stories.tsx
  ListeningPhaseBanner.stories.tsx
```

## Adding new stories

1. Co-locate under `components/<area>/__stories__/<Component>.stories.tsx`
   so the lint/tsc excludes catch it automatically.
2. Use the `Meta`/`StoryObj` typed exports — no CSF2 default exports.
3. Keep fixtures lean. Listening-specific fixtures belong in the
   `__stories__/fixture.ts` file next to the stories.

## Excluded from build pipelines

- `tsconfig.json` excludes `.storybook/**`, `**/__stories__/**`,
  and `**/*.stories.tsx`.
- `eslint.config.mjs` ignores the same.
- `vitest.config.ts` only matches `.test.tsx` / `.spec.tsx`, so
  `.stories.tsx` is never picked up by Vitest.

## CI

There is currently no CI job that builds Storybook. To add one later,
run `npm run build-storybook` in a separate workflow step and publish
`storybook-static/` to your preferred preview host (Chromatic, Pages,
S3, etc).
