# Antigravity Parity Rubric — Golden-Set Gate (Phase 3c)

Before any AI feature route is promoted to the Antigravity agent (10% → 100%
per `docs/speaking/ai-providers.md`), the golden-set harness must pass.

## Harness

```powershell
agent-gateway\.venv\Scripts\python.exe scripts\antigravity\parity\run_parity.py `
    --gateway-url http://localhost:8305 --gateway-token <internal-token> `
    --baseline-url <openai-compatible-url> --baseline-key <key> `
    --baseline-model claude-sonnet-4-5 --report scripts\antigravity\parity\parity-report.md
```

- Golden sets (static, versioned): `scripts/antigravity/parity/golden/`
  - `writing.json` — **50 items**: professions × letter types × slot-varied
    clinical facts; each item carries expected criterion codes, max-score map
    (`purpose=3`, others `7`), must-cover clinical facts, minimum plausible total.
  - `speaking.json` — **30 items**: patient-turn scenarios with expected
    severity, role-play elements, length bounds.
- Regenerate deterministically: `_generate_golden.py` (edit bases there; never
  hand-edit the JSON).

## Per-item scoring

| Dimension | Weight | Definition |
|---|---|---|
| Structural conformance | 60% | Writing: exact six-criterion array, valid score ranges vs maxScore map, total ≥ plausible floor. Speaking: `{text, severity}` JSON, severity enum, in-character text, length bounds. |
| Content coverage | 40% | Fraction of the item's must-cover facts / role-play elements whose informative tokens appear (≥60% token hit). |

## Gate (exit code decides)

1. **Structural pass rate == 100%** for every suite on the gateway. A single
   malformed scoring payload fails the gate — this is the product's parse
   contract (`WritingDualAssessmentService.ParseAiCriteria`).
2. **Parity vs baseline** (when a baseline provider is configured):
   `mean(gateway composite) >= mean(baseline composite) − 0.05`.
3. Latency is reported (p95) but not gated here — latency gating happens at
   Phase 0 baseline comparison and in production dashboards.

## Promotion tie-in

| Result | Action |
|---|---|
| Gate PASS | Proceed with the 10% route flip; watch Grafana + cost dashboard; promote to 100% per swap procedure. |
| Gate FAIL | Route stays on the incumbent provider. Fix persona/skill or gateway issue, rerun harness. Attach report to the rollout PR. |

Run after every change to a persona/SKILL.md, gateway upgrade (SDK pin bump),
or before the Mode C flip-day switch.
