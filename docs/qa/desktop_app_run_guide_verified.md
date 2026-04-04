# Desktop App Run Guide Verified

## Verified Development Run Path

### 1. Start the local desktop baseline

```powershell
docker compose -f docker-compose.desktop.yml up -d --build
```

Verified local services:

- renderer on `http://localhost:3000`
- backend on `http://localhost:5198`

### 2. Verify health before starting Electron

```powershell
Invoke-WebRequest http://localhost:3000/api/health | Select-Object StatusCode
Invoke-WebRequest http://localhost:5198/health/ready | Select-Object StatusCode
```

Expected result for both endpoints: `200`

### 3. Launch the unpackaged desktop shell

```powershell
npm run desktop:dev
```

Verified behavior:

- The script asserts the local stack before launching Electron.
- Electron starts against the renderer at `http://localhost:3000`.
- Electron uses the local API at `http://localhost:5198`.

## Verified Packaging Path

### 1. Build a local Windows package

```powershell
$env:ELECTRON_ALLOW_UNSIGNED_WINDOWS_BUILD='true'
npm run desktop:dist
```

Expected output:

- unpacked executable at [dist/desktop/win-unpacked/OET Prep.exe](C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\dist\desktop\win-unpacked\OET Prep.exe)

### 1a. Build packaged desktop against the same API used by the web app

If you want packaged desktop logins to use the same account store as the web deployment, provide the shared API URL before packaging:

```powershell
$env:PUBLIC_API_BASE_URL='https://api.example.com'
$env:ELECTRON_ALLOW_UNSIGNED_WINDOWS_BUILD='true'
npm run desktop:dist
```

Verified behavior after the auth fix:

- the packaged shell persists the shared API target into `desktop-runtime-config.json` inside the app resources
- the standalone renderer proxies auth and API requests to that shared API
- Electron only starts the bundled SQLite backend when no shared API target is configured

### 2. Refresh the installed QA folder if needed

The verified local installed test folder is:

- `C:\Users\Public\OETPrepTest8`

Refresh it from `dist\desktop\win-unpacked` before validating the installed shell if you need the latest package contents reflected there.

### 3. Validate packaged backend readiness

```powershell
Invoke-WebRequest http://127.0.0.1:5199/health/ready
```

Expected result: `200`

## Verified Desktop QA Commands

### Full desktop regression suite

```powershell
npx playwright test -c playwright.desktop.config.ts tests/e2e/desktop --reporter=line
```

Verified result in this audit:

- `14 passed (2.2m)`

### Dev-shell smoke only

```powershell
npx playwright test -c playwright.desktop.config.ts tests/e2e/desktop/electron-smoke.spec.ts --reporter=line
```

### Packaged smoke only

```powershell
npx playwright test -c playwright.desktop.config.ts tests/e2e/desktop/electron-packaged-smoke.spec.ts --reporter=line
```

## Required Services and Runtime Assumptions

### Development

- Docker Desktop must be available.
- `docker-compose.desktop.yml` services must be healthy before `desktop:dev`.

### Packaged mode

- The packaged app always starts its standalone renderer.
- The packaged app starts its bundled backend only when no shared API target is configured.
- When `PUBLIC_API_BASE_URL`, `API_PROXY_TARGET_URL`, or an absolute `NEXT_PUBLIC_API_BASE_URL` is configured, packaged desktop uses that shared API instead of the bundled SQLite auth store.
- Docker is not required for packaged startup validation.

## Common Failure Points

### Startup dialog says the app timed out waiting for the backend

Check:

- packaged backend logs
- `http://127.0.0.1:<backend-port>/health/ready`
- recent packaged-backend EF Core changes, especially SQLite provider translation differences

### Packaged desktop says `Invalid email or password` while web login succeeds

Check:

- whether the packaged app was built with a shared API target
- whether `PUBLIC_API_BASE_URL`, `API_PROXY_TARGET_URL`, or an absolute `NEXT_PUBLIC_API_BASE_URL` points at the same backend used by the web app
- whether the packaged build is unintentionally falling back to the bundled SQLite demo backend

### `desktop:dev` fails immediately

Check:

- `http://localhost:3000/api/health`
- `http://localhost:5198/health/ready`
- `docker compose -f docker-compose.desktop.yml ps`

### Local Windows package build fails during QA

Check:

- `ELECTRON_ALLOW_UNSIGNED_WINDOWS_BUILD=true`
- whether you are doing local QA or a real signed release build

## Seeded Local Accounts Used in Verification

- learner: `learner@oet-prep.dev`
- expert: `expert@oet-prep.dev`
- admin: `admin@oet-prep.dev`
- password: `Password123!`

## Verified Recovery Commands

### Restart the local Docker-backed baseline

```powershell
docker compose -f docker-compose.desktop.yml down -v
docker compose -f docker-compose.desktop.yml up -d --build
```

### Rebuild the packaged desktop after backend or renderer changes

```powershell
$env:ELECTRON_ALLOW_UNSIGNED_WINDOWS_BUILD='true'
npm run desktop:dist
```
