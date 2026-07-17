# Security Policy

## Supported versions

Only the latest commit on the `main` branch is actively supported with security updates. Production deployments are built from `main` via GitHub Actions.

## Reporting a vulnerability

Please report security vulnerabilities privately by emailing the maintainers or opening a GitHub Security Advisory. Do not disclose vulnerabilities in public issues.

## Security practices

- Secrets and credentials are read through `IRuntimeSettingsProvider` / env vars; they are never committed to source control.
- Authorization is enforced server-side for every sensitive action.
- User input is validated at system boundaries with Zod and EF Core safe patterns.
- AI invocations are grounded and audited; every call records one `AiUsageRecord`.
- File and audio I/O goes through domain storage services, never ad-hoc filesystem access.
- Production VPS, database, and Docker volume operations require explicit approval and verified backups.

## Dependencies

Dependabot monitors dependencies and opens pull requests for security updates. Review and merge dependency updates promptly.
