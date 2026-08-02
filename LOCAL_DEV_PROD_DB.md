---
name: "Local Development Against Production DB"
description: "Instructions for setting up and running the local web app, desktop app (Tauri), and mobile view against the live production database with fast hot-reloading."
applyTo: ".env.development.local,scripts/start-local-prod.ps1,app/**,components/**,backend/**"
---

# Local Development Stack with Direct Production DB Access

This instruction guide defines the mandatory procedure for launching local development environments (Web, Desktop, and Mobile) connected directly to the live production database.

---

## 🚀 Architecture Overview

1. **Native Next.js Frontend (`http://localhost:3000`)**:
   - Runs natively on Windows for **~1s hot-reloading**.
   - Server-side proxy target configured in `.env.development.local`.

2. **Native .NET API Backend (`http://localhost:5198`)**:
   - Runs natively via `dotnet run` on port **5198**.
   - Connects to live Production PostgreSQL (`185.252.233.186`) via a self-healing SSH tunnel (`127.0.0.1:15433`).

3. **Desktop App (Tauri)**:
   - Launches via `pnpm desktop:dev` with `OET_DESKTOP_WEB_URL=http://localhost:3000`.

4. **Mobile View**:
   - Accessible at `http://localhost:3001` or via local IP `http://<local-ip>:3000`.

---

## ⚙️ Key Configuration File: `.env.development.local`

To ensure Next.js proxies API calls (e.g. `/api/backend/v1/auth/sign-in`) correctly to the local .NET API:

```env
# Point Next.js proxy at the local .NET API bound to port 5198
API_PROXY_TARGET_URL=http://localhost:5198
NEXT_PUBLIC_API_BASE_URL=http://localhost:5198
```

> **CRITICAL GOTCHA**: Never set `API_PROXY_TARGET_URL` to `http://localhost:8080` (Podman) or an unresolved domain (`api.oetprep.com`) when running Option B. That causes `ECONNREFUSED` / `Auth request failed with status 500`.

---

## 🛠️ One-Command Execution Procedure

When launching the environment for local development:

```powershell
# Run the automated helper script in PowerShell
powershell -ExecutionPolicy Bypass -File .\scripts\start-local-prod.ps1
```

### What `start-local-prod.ps1` does automatically:
1. Verifies/fetches the production DB password securely via SSH (`root@185.252.233.186`).
2. Starts the self-healing SSH tunnel: `127.0.0.1:15433 -> 172.20.0.3:5432`.
3. Starts the .NET API on `http://localhost:5198` connected to the SSH tunnel.
4. Starts Next.js on `http://localhost:3000` with `API_PROXY_TARGET_URL=http://localhost:5198`.
5. Launches Tauri desktop shell pointing to `http://localhost:3000`.
6. Launches phone-frame mobile view on `http://localhost:3001`.

---

## 🧪 Verification & Troubleshooting Ladder

1. **Verify SSH Tunnel**:
   ```powershell
   Get-NetTCPConnection -LocalPort 15433 -ErrorAction SilentlyContinue
   ```
2. **Verify .NET API Health**:
   ```powershell
   Invoke-WebRequest -Uri 'http://localhost:5198/health' -UseBasicParsing
   ```
3. **Verify Next.js Frontend**:
   ```powershell
   Invoke-WebRequest -Uri 'http://localhost:3000' -UseBasicParsing
   ```
4. **If Port Conflicts / Stale Processes Occur**:
   ```powershell
   # Kill stale node and dotnet processes
   taskkill /F /IM node.exe
   taskkill /F /IM dotnet.exe
   ```
