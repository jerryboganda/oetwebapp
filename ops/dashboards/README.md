# Speaking Module — Grafana Dashboards

Three dashboards: funnel, quality, LiveKit health.

## Import

Grafana → Dashboards → Import → upload JSON file.

If your destination is PostHog (rather than Prometheus), use the panel `targets` as a starting point — they will need vendor-specific queries.

## Files

| File | What it shows |
|------|---------------|
| `speaking-funnel.json` | module_entry → warmup_finished → roleplay_ended → assessment_viewed |
| `speaking-quality.json` | AI vs tutor delta histograms, calibration drift |
| `speaking-livekit.json` | room connects, disconnect reasons, cue volume |

## Variables

Each dashboard expects:
- `$env` (production / staging)
- `$profession` (nursing, medicine, ...)
- `$tenant_id` (optional)
