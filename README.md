# OET Prep Platform

OET preparation platform with a Next.js 15 web app, ASP.NET Core 10 API, Electron desktop shell, and Capacitor 6 mobile shell.

## Stack

- Frontend: Next.js App Router, React 19, TypeScript 5.9, Tailwind CSS 4, motion v12
- Backend: ASP.NET Core 10, EF Core, PostgreSQL 17, SignalR
- Desktop: Electron 41 with electron-builder
- Mobile: Capacitor 6 for iOS and Android

## Local Baseline

- Frontend URL: `http://localhost:3000`
- Backend URL: `http://localhost:5198`
- .NET SDK: `10.0.201`

## Quick Start

```bash
npm install
npm run dev
npm run backend:run
```

Optional workflow commands:

```bash
npm run backend:watch
npm run desktop:dev
npm run mobile:sync
npm run mobile:run:android
npm run mobile:run:ios
```

If PowerShell blocks scripts, run:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

## Verification

```bash
npx tsc --noEmit
npm run lint
npm test
npm run build
npm run backend:build
npm run backend:test
```

E2E coverage:

```bash
npm run test:e2e:install
npm run test:e2e:auth
npm run test:e2e
npm run test:e2e:smoke
npm run test:e2e:desktop
npm run test:e2e:report
```

## Local API Truth

- The backend development host is `http://localhost:5198`
- `backend/src/OetLearner.Api/Properties/launchSettings.json` and `backend/src/OetLearner.Api/appsettings.Development.json` both agree on port `5198`
- `NEXT_PUBLIC_API_BASE_URL` should point to `http://localhost:5198` for direct local development

## Key Docs

- [AGENTS.md](./AGENTS.md)
- [Agent Operating Model](docs/agent-operating-model.md)
- [Agentic Bootstrap Plan](docs/superpowers/plans/2026-04-19-agentic-bootstrap.md)
- [Scoring](docs/SCORING.md)
- [Rulebooks](docs/RULEBOOKS.md)
- [AI Usage Policy](docs/AI-USAGE-POLICY.md)
- [Content Upload Plan](docs/CONTENT-UPLOAD-PLAN.md)
- [Result Card Spec](docs/OET-RESULT-CARD-SPEC.md)

## Working Model

- Use the root AGENTS file and the operating model doc as the first stop for any agentic work.
- Keep unrelated edits intact.
- Prefer isolated worktrees for multi-file changes.
- Keep shared-contract surfaces tight: scoring, rulebooks, auth, backend bootstrap, and the statement-of-results card.
