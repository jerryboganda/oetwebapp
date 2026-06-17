# Hardening: Vocabulary / Recall Audio Pipeline

Branch: `harden/vocab-audio-pipeline`

## Root cause (investigation 2026-06-17)
Production DB at time of report: **1822/1822 terms had a Ready audio asset, 0 pending** —
generation was actually complete. The admin panel showed a stale "177 pending / 90.3%"
because the progress poller swallows every error silently and only updates on success
(`catch { /* silent */ }`), so any failed/cached progress fetch freezes the bar. Resume
did nothing because there was nothing left to generate.

Underneath, the pipeline thrashed: **2541 audio files on disk for 1822 terms (~700 orphans)**
from repeated non-deterministic ElevenLabs regeneration (each regen → new content-addressed
key → orphan-sweep churn), an **in-memory queue** lost on every deploy, the worker running
in **all blue/green containers at once**, and a progress denominator counting all 1822 terms
while only 820 are `active`.

## Changes (each committed + tested independently)
1. [x] **Deterministic per-identity storage keys** for recall audio — key derived from
   (recallCode, termId, voiceId, modelVariant), NOT the audio byte sha. Regenerating the
   same term/voice/model overwrites the same key → no orphans, `storage.Exists` stable.
   *(VocabularyAudioWorker.AudioIdentityHash; test RecallRegeneration_SameIdentity_UsesStableKey_NoChurn)*
2. [x] **Honest progress + no-store + resilient UI** — backend `Cache-Control: no-store`,
   progress counts only **Ready** assets + exposes `broken`; frontend surfaces poll errors
   instead of freezing (audioError + Retry) and `cache:'no-store'` on the fetch.
3. [ ] **Resume reuses a running batch** instead of creating a new `AudioRegenerationBatch`
   per click.
4. [ ] **Single-writer leader election** — Postgres advisory lock so only one instance drains
   the queue (no blue/green double-processing). RISK: a bug here halts ALL audio generation —
   validate in staging. NOTE: Change 5 (sweep) depends on this for safety, else it amplifies
   the multi-container double-processing.
5. [ ] **Durable reconciliation sweep** — periodic worker re-enqueues terms with missing/broken
   audio; restart-safe, removes reliance on the in-memory enqueue surviving deploys. Do WITH #4.
6. [ ] **Orphan-file cleanup** — sweep audio files not referenced by any `MediaAsset.StoragePath`.
   Confirm no in-flight references before deleting. ~700 orphans currently under recalls/audio.

## Status
- Committed + tested: #1, #2 (deployable; fixes the visible stuck-panel symptom + the
  regeneration-churn root cause).
- Remaining #3–#6 are a coherent follow-up needing staging validation (esp. the #4 leader
  lock, on which #5 depends). Do not ship #5 without #4.
