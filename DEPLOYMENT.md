# Linux VPS Deployment

This repository now ships with a production Docker stack for:

- `web`: Next.js learner app
- `learner-api`: ASP.NET Core 10 API
- `postgres`: PostgreSQL 17

It is designed for a Linux VPS where Nginx Proxy Manager runs in Docker and proxies to the app over a shared Docker network.

## 0. Local production smoke test

Before deploying, you can verify that the backend release build boots in `Production` mode with safe local overrides:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\probe-production.ps1
```

Expected output includes:

```text
READY_STATUS=200
```

## 1. Prepare the VPS

Install:

- Docker Engine
- Docker Compose plugin

Create the shared network that Nginx Proxy Manager and this stack will both join:

```bash
docker network create npm_proxy
```

If your Nginx Proxy Manager stack already uses a different external network name, reuse that name and set `NPM_PROXY_NETWORK` in your env file to match.

## 2. Create the production env file

Copy the template and fill in every value:

```bash
cp .env.production.example .env.production
```

Minimum values you must set correctly:

- `APP_URL`
- `NEXT_PUBLIC_API_BASE_URL`
- `PUBLIC_API_BASE_URL`
- `CHECKOUT_BASE_URL`
- `CORS_ALLOWED_ORIGINS`
- `POSTGRES_PASSWORD`
- Either `AUTH_AUTHORITY`, or all of `AUTH_ISSUER`, `AUTH_AUDIENCE`, and `AUTH_SIGNING_KEY`

Notes:

- `CHECKOUT_BASE_URL` should point at your frontend billing route or external payment handoff page.
- `PUBLIC_API_BASE_URL` must be the final public HTTPS API URL because the backend returns absolute upload/audio links.
- `SEED_DEMO_DATA` should stay `false` in production.
- `NEXT_PUBLIC_ENABLE_MOCK_AUTH=true` is only for local development. Do not enable it in production.
- For the built-in .NET auth flow, configure `BOOTSTRAP_EXPERT_EMAIL`, `BOOTSTRAP_EXPERT_PASSWORD`, and `BOOTSTRAP_EXPERT_DISPLAY_NAME` so the initial expert account can sign in and receive JWTs from `/v1/auth/login`.

## 3. Build and start the stack

```bash
docker compose --env-file .env.production -f docker-compose.production.yml up -d --build
```

The API runs database migrations automatically on startup when `AUTO_MIGRATE=true`.

If the first frontend image build is slow because `npm ci` is downloading packages inside Docker, you can use the faster prebuilt-web path:

```bash
npm ci
npm run build
docker compose --env-file .env.production -f docker-compose.production.yml -f docker-compose.production.prebuilt-web.yml up -d --build
```

That override keeps the same production runtime image but copies the already-built Next.js standalone output instead of rebuilding it inside Docker.

## 4. Configure Nginx Proxy Manager

Attach your Nginx Proxy Manager container to the same external Docker network.

Create these proxy hosts:

1. `app.example.com` -> forward host `oet-web`, port `3000`
2. `api.example.com` -> forward host `oet-api`, port `8080`

Recommended Nginx Proxy Manager settings:

- Enable WebSocket support for both hosts
- Request a LetsEncrypt certificate for both hosts
- Force SSL
- Enable HTTP/2

## 5. Verify after first deploy

Check containers:

```bash
docker compose --env-file .env.production -f docker-compose.production.yml ps
```

Check API health:

```bash
curl https://api.example.com/health/live
curl https://api.example.com/health/ready
```

Check frontend:

```bash
curl https://app.example.com/api/health
```

## 6. Persistent data and backups

The stack persists:

- PostgreSQL data in `oet_postgres_data`
- Uploaded learner audio in `oet_learner_storage`

Back up both named volumes before upgrades or VPS maintenance.

## 7. Updating the deployment

```bash
git pull
docker compose --env-file .env.production -f docker-compose.production.yml up -d --build
```

## Troubleshooting

- If the API exits on startup, check JWT configuration first. The app now fails fast when production auth settings are incomplete.
- If browser uploads fail, confirm `PUBLIC_API_BASE_URL` is correct and that Nginx Proxy Manager can reach `oet-api:8080`.
- If the frontend cannot call the API, confirm `NEXT_PUBLIC_API_BASE_URL` matches the public API host and `CORS_ALLOWED_ORIGINS` includes the frontend host.
