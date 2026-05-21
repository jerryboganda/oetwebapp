# Speaking Module — k6 Load Tests

## Run locally

```bash
brew install k6   # or apt / choco / scoop
export K6_TARGET_URL=http://localhost:5199
export OET_TEST_LEARNER_EMAIL=e2e-learner@example.com
export OET_TEST_LEARNER_PASSWORD=please-change-me
k6 run tests/load/speaking-session-create.k6.js
```

## SLOs

See `docs/load-testing/speaking-budgets.md` and `docs/speaking/sla.md`. Summary:

| Endpoint | p95 | p99 |
|----------|-----|-----|
| Session create | 800 ms | 2000 ms |
| End session | 1500 ms | — |
| AI assess | 10000 ms | — |
| Drill score | 6000 ms | — |
| LiveKit token | 200 ms | — |

`http_req_failed` < 2%.

## CI

Weekly on Mondays via `.github/workflows/speaking-load.yml`.
