# Contributing

Read [AGENTS.md](AGENTS.md) before changing or shipping this project. Its mandatory
accelerated baseline applies to people, coding agents and delegated workers.

Commit only explicit owned paths, then release with:

```powershell
pnpm run ship
```

The command owns the shared lock, static gate, visibility lease, main push,
successful-descendant-aware deploy watch, exact live proof and evidence recording.
Do not push and walk away, use release-bypass flags, manually roll out on the VPS,
or add automated QA anywhere in CI (owner directive 2026-10-06, permanent: the owner tests
manually and reports bugs, the agent fixes them). Builds stay on GitHub Actions; no tests run there.
Missing provenance rebuilds conservatively; no safety gate is removed for speed.

The verified 8m30.24s release is a measured architecture baseline, not a timing
guarantee. Owner-console contributors remain `agent/*` + PR-only and use the
isolated Ship button; they never run the workstation shipping command.
