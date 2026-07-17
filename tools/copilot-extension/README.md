# OET Copilot Extension

A GitHub Copilot Extension that integrates with the OET with Dr Hesham Platform, enabling developers to search code, explain files, run tests, check deployment status, and scaffold components directly from Copilot Chat.

## What It Does

This extension adds OET-specific capabilities to GitHub Copilot Chat:

| Command | Description |
|---------|-------------|
| `/search <query>` | Search the OET codebase |
| `/explain <file>` | Get an explanation of a specific file |
| `/test [scope]` | Trigger a test run (all, backend, frontend, or specific file) |
| `/deploy` | Check deployment status across environments |
| `/create <description>` | Scaffold a new component (page, component, or endpoint) |

You can also use natural language — the extension detects intent automatically.

## Architecture

```
┌─────────────────┐     POST /      ┌─────────────────────┐     HTTP     ┌──────────────┐
│  GitHub Copilot │ ──────────────▶  │  Copilot Extension  │ ──────────▶  │  OET Backend │
│  Chat (VS Code) │ ◀────────────── │  (Node.js :3001)    │ ◀────────── │  (.NET :5000)│
└─────────────────┘     SSE stream   └─────────────────────┘             └──────────────┘
```

## Setup

### Prerequisites

- Node.js 20+
- A GitHub App configured as a Copilot Extension
- Access to the OET backend

### 1. Create a GitHub App

1. Go to **Settings → Developer settings → GitHub Apps → New GitHub App**
2. Set the callback URL to your extension's public URL
3. Under **Copilot**, enable the extension
4. Set the endpoint URL to `https://your-domain.com/` (POST)
5. Generate a webhook secret and note it down

### 2. Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `API_URL` | OET backend base URL | `http://localhost:5000` |
| `WEBHOOK_SECRET` | GitHub webhook secret for signature verification | _(empty = dev mode)_ |
| `COPILOT_BACKEND_TOKEN` | Token for authenticating with OET backend | _(empty)_ |
| `PORT` | Port for this service | `3001` |
| `BACKEND_TIMEOUT_MS` | Timeout for backend API calls (ms) | `30000` |

### 3. Install & Run

```bash
cd tools/copilot-extension
pnpm install
pnpm start
```

For development with auto-reload:

```bash
pnpm run dev
```

### 4. Docker

```bash
docker build -t oet-copilot-extension .
docker run -p 3001:3001 \
  -e API_URL=http://backend:5000 \
  -e WEBHOOK_SECRET=your-secret \
  -e COPILOT_BACKEND_TOKEN=your-token \
  oet-copilot-extension
```

## Testing Locally

### Health Check

```bash
curl http://localhost:3001/health
```

### Simulate a Copilot Request

```bash
curl -X POST http://localhost:3001/ \
  -H "Content-Type: application/json" \
  -H "x-github-token: ghp_your_token" \
  -d '{
    "messages": [
      {"role": "user", "content": "/search authentication endpoint"}
    ]
  }'
```

> **Note:** In dev mode (no `WEBHOOK_SECRET` set), signature verification is skipped.

## Backend Endpoints

The extension calls these OET backend endpoints (see `CopilotExtensionEndpoints.cs`):

- `POST /v1/copilot/search` — Search codebase
- `POST /v1/copilot/explain` — Explain a file
- `POST /v1/copilot/test` — Trigger test run
- `GET /v1/copilot/deploy/status` — Get deployment status

All require the `X-Copilot-Token` header for authentication.

## Security

- GitHub webhook signatures are verified using HMAC-SHA256
- User identity is extracted from the `x-github-token` header via GitHub API
- Backend calls use a separate service token (`X-Copilot-Token`)
- Rate limiting: 60 requests/minute/user on backend endpoints
