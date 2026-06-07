# PROGRESS - Active Agent Continuity

Last updated: 2026-06-07

## Current Operating Goal

Recall vocabulary ElevenLabs audio generation fix is ready for commit/push to main, followed by GitHub Actions deploy and production verification.

## Current State

- Recall import audio backfill now includes recall-set rows and forces ElevenLabs.
- Missing ElevenLabs key returns a clear elevenlabs_api_key_required validation error.
- Recall import audio batches expose queued/pending progress and can be cancelled without deleting generated audio.
- Admin vocabulary import UI shows generated, queued, remaining, ETA, status, and Cancel generation controls.
- Local focused validation passed; next step is commit/push, CI watch, and production verification via CI-built GHCR images.
- Unrelated pre-existing feature-branch work was preserved in a git stash before moving the recall fix onto main.

## Next-Step Protocol For New Agent Runs

1. Read AGENTS.md, .github/copilot-instructions.md, this file, and .github/agent-state.local.md.
2. Continue from .github/agent-state.local.md when it matches the newest request.
3. For production deploy, use GitHub Actions + GHCR images; do not build on the VPS.
4. Before handoff, update .github/agent-state.local.md with validation, blockers, and next concrete step.

## Active Risks

- Do not drop stash codex-preserve-before-recall-audio-main-push; it contains unrelated feature-branch dirty work preserved before the clean main push.
- Production verification must check more than homepage 200: containers, restart counts, logs, direct API/web health, migrations, and backup posture.
