# Verified Desktop Run Instructions

## 1. Start the Docker Desktop Baseline

```powershell
docker compose -f docker-compose.desktop.yml up -d --build
```

This brings up:

- `postgres`
- `learner-api` on `http://localhost:5198`
- `web` on `http://localhost:3000`

## 2. Verify Health

```powershell
Invoke-WebRequest http://localhost:3000/api/health | Select-Object StatusCode
Invoke-WebRequest http://localhost:5198/health/ready | Select-Object StatusCode
```

Expected result for both commands: `200`

## 3. Launch Desktop Dev Mode

```powershell
npm run desktop:dev
```

What this now does:

- asserts the Docker-backed frontend and API are healthy
- launches Electron against `http://localhost:3000`
- keeps the renderer and API contracts aligned with the validated Docker baseline

Optional overrides:

- `ELECTRON_RENDERER_URL` to point Electron at a different local renderer
- `ELECTRON_API_URL` to point readiness checks at a different local API

## 4. Run Desktop Smoke QA

```powershell
npx playwright test -c playwright.desktop.config.ts --reporter=line
```

This covers:

- shared Electron shell boot
- learner reload and relaunch persistence
- learner reading workflow inside the shell
- expert protected routes
- admin protected routes
- packaged shell smoke

## 5. Build a Local Unsigned Desktop Package

```powershell
$env:ELECTRON_ALLOW_UNSIGNED_WINDOWS_BUILD='true'
npm run desktop:dist
```

Expected artifact:

- [`dist/desktop/win-unpacked/OET Prep.exe`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/dist/desktop/win-unpacked/OET Prep.exe)

## 6. Run Packaged Smoke Validation Directly

```powershell
npx playwright test -c playwright.desktop.config.ts tests/e2e/desktop/electron-packaged-smoke.spec.ts --reporter=line
```

## 7. Seeded Accounts

- learner: `learner@oet-prep.dev`
- expert: `expert@oet-prep.dev`
- admin: `admin@oet-prep.dev`
- password: `Password123!`

## Troubleshooting

### Docker health fails

- Rebuild and restart:

```powershell
docker compose -f docker-compose.desktop.yml down -v
docker compose -f docker-compose.desktop.yml up -d --build
```

### Privileged desktop auth fails with stale MFA state

- The desktop QA harness will automatically clear its cached bootstrap state, restart `learner-api`, wait for readiness, and retry once.

### Packaged build fails immediately on Windows

- Confirm the unsigned local-build override is set.
- Confirm the packaged executable exists under `dist/desktop/win-unpacked`.
- Remember that signed-release validation still needs real signing credentials and update infrastructure.
