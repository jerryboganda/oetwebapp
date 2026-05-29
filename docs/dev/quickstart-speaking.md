# Speaking Module — Local Docker Quickstart

This guide gets a developer to a working local Speaking stack using the repo's Docker-first workflow.

## Prereqs

- Docker Desktop
- Node.js only for lightweight repo scripts if needed
- Git
- (Optional) Anthropic API key — without it, the local stack runs against mock AI

## 1. Clone + install

```bash
git clone https://github.com/<org>/oet-web-app.git
cd oet-web-app
```

Do not run dependency installs on the Windows host. Validation dependencies are managed through the Docker-backed `node_modules` volume.

## 2. Configure env

```bash
cp .env.example .env.local
# Open .env.local and uncomment the Speaking-module block at the bottom.
# For the AI self-practice path you only need ANTHROPIC__APIKEY (or leave the
# mock provider in place by leaving it empty).
```

## 3. Start the local stack

```powershell
docker compose -f docker-compose.local.yml --env-file .env.docker-local up -d --build
```

This starts the local web, API, and PostgreSQL containers using the repo-standard Docker configuration.

## 4. Seed Speaking data

Local compose applies migrations and demo seed data through the API container configuration. Do not run legacy seed scripts on the host unless they are rewritten to execute through `docker exec oet-local-api`.

## 5. Follow logs

```powershell
docker compose -f docker-compose.local.yml --env-file .env.docker-local logs -f learner-api web
```

## 6. Sign in + try Speaking

1. Open <http://localhost:3000>
2. Sign in with the seeded learner (`e2e-learner@example.com` / `please-change-me`).
3. Visit `/speaking`. If you have never set a profession, you land on the profession picker — choose **Nursing**.
4. Pick a role-play card from `/speaking/selection`.
5. Walk through warm-up → preparation → role-play → results.

## 7. Run the smoke

```bash
docker exec oet-local-web ./scripts/speaking-smoke.sh
```

You should see `[smoke] PASS — readiness band: <band>` within 60 seconds.

## Where to next

- **Module docs**: `docs/speaking/README.md`
- **Architecture**: `docs/speaking/architecture.md`
- **Runbook**: `docs/speaking-module-runbook.md`
- **CI**: `docs/ci/speaking.md`
- **Plan**: `~/.claude/plans/1-oet-speaking-module-sequential-candy.md`

## Troubleshooting

- **Container is not healthy** — inspect `docker compose -f docker-compose.local.yml --env-file .env.docker-local ps` and `docker compose -f docker-compose.local.yml --env-file .env.docker-local logs learner-api web postgres`.
- **AI assessment never lands** — check `ANTHROPIC__APIKEY`; without it, the mock provider returns synthetic scores.
- **Microphone not detected** — `chrome://settings/content/microphone` → allow `localhost:3000`.
- **LiveKit not wired** — local dev defaults to the stub gateway; live-tutor flow requires the cloud keys in `.env.local`.
