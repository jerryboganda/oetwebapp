# Speaking Module — Load Budgets

| Surface | p95 budget | p99 budget | Error rate |
|---------|-----------|-----------|-----------|
| `POST /v1/speaking/sessions` | 800 ms | 2000 ms | < 2% |
| `POST /sessions/{id}/end` | 1500 ms | — | < 2% |
| `POST /sessions/{id}/ai-assess` (AI roundtrip) | 10000 ms | 25000 ms | < 5% |
| `POST /drills/.../score` | 6000 ms | — | < 5% |
| `GET /live-rooms/{id}/token` | 200 ms | — | < 1% |
| Per-turn AI loop (ASR+LLM+TTS) | 2500 ms | — | < 5% |

## Cache hit rate

Anthropic prompt cache on multi-turn role-plays: **≥ 80%** hit rate (turns 2..N).

## ASR / LLM / TTS latency split (per turn)

- Whisper ASR: p95 < 800 ms (audio chunk dependent).
- Anthropic Haiku 4.5 turn: p95 < 1200 ms with cache hit; < 1800 ms cold.
- ElevenLabs TTS: p95 < 500 ms for ≤ 30-word reply.

## Fail criteria

Two consecutive weekly runs over budget on the same metric → release freeze + incident.
