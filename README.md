# OET Prep Platform

Next.js learner frontend plus ASP.NET Core backend for OET preparation, readiness tracking, practice workflows, expert review, and admin operations.

## Stack

- Frontend: Next.js App Router, React 19, TypeScript, Vitest, Playwright
- Backend: ASP.NET Core, Entity Framework Core, PostgreSQL, xUnit
- Auth: first-party JWT auth with development-auth shortcuts in local development
- Delivery: standalone Next.js build plus Docker Compose manifests for backend and production deployments

## Project Structure

- `app/`: Next.js routes and route handlers
- `components/`: shared UI, auth, domain, and layout components
- `lib/`: API clients, hooks, auth utilities, and shared frontend logic
- `backend/src/OetLearner.Api/`: ASP.NET Core API
- `backend/tests/`: backend test projects
- `tests/e2e/`: Playwright coverage for end-to-end browser flows

## Local Development

### Prerequisites

- Node.js 20+
- npm
- .NET SDK 8+
- PostgreSQL if you want the backend to use a real local database

### Install Frontend Dependencies

```bash
npm install
```

### Run the Backend

```bash
npm run backend:run
```

Backend defaults in development:

- API URL: `http://localhost:5198`
- default local connection string: `Host=localhost;Port=5432;Database=oet_learner_dev;Username=postgres;Password=postgres`
- `Auth:UseDevelopmentAuth=true` in [`backend/src/OetLearner.Api/appsettings.Development.json`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/backend/src/OetLearner.Api/appsettings.Development.json)
- auto-migrations and demo seeding enabled in development

### Run the Frontend

```bash
npm run dev
```

Frontend default local URL:

- `http://localhost:3000`

## Environment Variables

### Frontend

Copy from [`.env.example`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/.env.example) and adjust as needed.

Common values:

- `NEXT_PUBLIC_API_BASE_URL`
  Development default is `http://localhost:5198`
  Production is required by [`lib/env.ts`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/lib/env.ts)
- `GEMINI_API_KEY`
  Required only for Gemini-backed frontend features
- `APP_URL`
  Useful for hosted environments and external integrations

### Production / Full-stack

See [`.env.production.example`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/.env.production.example) for the full production surface, including:

- `NEXT_PUBLIC_API_BASE_URL`
- `PUBLIC_API_BASE_URL`
- `CHECKOUT_BASE_URL`
- `CORS_ALLOWED_ORIGINS`
- Postgres credentials
- `AUTHTOKENS__*` signing keys and lifetimes
- SMTP or Brevo email configuration

## Auth and API Proxying

There are two important frontend API paths in this repo:

1. Frontend data client

- [`lib/api.ts`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/lib/api.ts) reads `NEXT_PUBLIC_API_BASE_URL`
- In development it falls back to `http://localhost:5198`
- In production it requires `NEXT_PUBLIC_API_BASE_URL`

2. Auth client / backend proxy path

- [`lib/auth-client.ts`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/lib/auth-client.ts) falls back to `/api/backend` when `NEXT_PUBLIC_API_BASE_URL` is not set
- [`app/api/backend/[...path]/route.ts`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/app/api/backend/[...path]/route.ts) proxies same-origin requests to the backend
- [`lib/backend-proxy.ts`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/lib/backend-proxy.ts) uses `API_PROXY_TARGET_URL` when set, otherwise `http://localhost:5198`

Practical rule:

- For straightforward local development, point `NEXT_PUBLIC_API_BASE_URL` at the backend directly
- For same-origin deployments or proxy-based setups, the `/api/backend/*` route is available

## Useful Commands

### Frontend

```bash
npm run dev
npm run build
npm run start
npm run lint
npm test
```

### Backend

```bash
npm run backend:run
npm run backend:watch
npm run backend:build
npm run backend:test
```

### End-to-End

```bash
npm run test:e2e:install
npm run test:e2e
npm run test:e2e:smoke
```

The E2E commands expect the local stack to be up and use `scripts/qa/assert-local-stack.mjs` as a gate.

## Quality Baseline

Verified during the March 30, 2026 polish pass:

- `npm run lint`
- `npm test`
- `dotnet test backend/OetLearner.sln`
- `npm run build`

Targeted frontend tests were also added or updated for:

- protected shell auth ownership
- auth form labels and field attributes
- semantic card-link navigation
- dashboard data-loading hook behavior

## Docker and Deployment Notes

This repo includes multiple compose manifests:

- [`docker-compose.backend.yml`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/docker-compose.backend.yml)
- [`docker-compose.desktop.yml`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/docker-compose.desktop.yml)
- [`docker-compose.production.yml`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/docker-compose.production.yml)
- [`docker-compose.production.hostports.yml`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/docker-compose.production.hostports.yml)
- [`docker-compose.production.prebuilt-web.yml`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/docker-compose.production.prebuilt-web.yml)

There is also an existing [DEPLOYMENT.md](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/DEPLOYMENT.md) for VPS-oriented deployment details.

## Notes for Contributors

- Avoid touching the wallet-concurrency backend work unless you are explicitly working on that slice
- Prefer `npm run lint`, `npm test`, `dotnet test backend/OetLearner.sln`, and `npm run build` before closing a frontend/backend change
- Keep URLs and backend contracts stable unless the task explicitly calls for contract changes
