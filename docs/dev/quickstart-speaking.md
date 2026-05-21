# Speaking Module — 10-Minute Local Quickstart

This guide gets a new developer from a fresh clone to a working AI self-practice role-play in under ten minutes.

## Prereqs

- .NET SDK 10
- Node.js 20
- Postgres 16 (Docker is fine)
- Git
- (Optional) Anthropic API key — without it, the local stack runs against mock AI

## 1. Clone + install

```bash
git clone https://github.com/<org>/oet-web-app.git
cd oet-web-app
npm ci
```

## 2. Configure env

```bash
cp .env.example .env.local
# Open .env.local and uncomment the Speaking-module block at the bottom.
# For the AI self-practice path you only need ANTHROPIC__APIKEY (or leave the
# mock provider in place by leaving it empty).
```

## 3. Start Postgres

```bash
docker compose up -d postgres
```

(If you don't have a docker-compose file yet, run `docker run -d --name oet-pg -p 5432:5432 -e POSTGRES_PASSWORD=postgres postgres:16`.)

## 4. Seed Speaking data

```bash
./scripts/seed-speaking-dev.sh        # bash / WSL / mac / linux
pwsh ./scripts/seed-speaking-dev.ps1  # Windows PowerShell
```

This applies EF Core migrations and runs the seeders (warm-up questions, role-play cards, drills, training modules).

## 5. Run the stack

In two terminals:

```bash
# terminal 1 — backend
dotnet run --project backend/src/OetLearner.Api
```

```bash
# terminal 2 — frontend
npm run dev
```

## 6. Sign in + try Speaking

1. Open <http://localhost:3000>
2. Sign in with the seeded learner (`e2e-learner@example.com` / `please-change-me`).
3. Visit `/speaking`. If you have never set a profession, you land on the profession picker — choose **Nursing**.
4. Pick a role-play card from `/speaking/selection`.
5. Walk through warm-up → preparation → role-play → results.

## 7. Run the smoke

```bash
./scripts/speaking-smoke.sh
```

You should see `[smoke] PASS — readiness band: <band>` within 60 seconds.

## Where to next

- **Module docs**: `docs/speaking/README.md`
- **Architecture**: `docs/speaking/architecture.md`
- **Runbook**: `docs/speaking-module-runbook.md`
- **CI**: `docs/ci/speaking.md`
- **Plan**: `~/.claude/plans/1-oet-speaking-module-sequential-candy.md`

## Troubleshooting

- **`pg_isready` not found** — install the Postgres client (`sudo apt install postgresql-client` or `brew install libpq`).
- **AI assessment never lands** — check `ANTHROPIC__APIKEY`; without it, the mock provider returns synthetic scores.
- **Microphone not detected** — `chrome://settings/content/microphone` → allow `localhost:3000`.
- **LiveKit not wired** — local dev defaults to the stub gateway; live-tutor flow requires the cloud keys in `.env.local`.
