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
3. [x] **Resume reuses a running batch** — `ResumeVocabularyAudioAsync` no longer creates a
   second `AudioRegenerationBatch` when one of that type is already running.
4. [x] **Single-writer leader election** — `IAudioWorkerLeaderLock` (Postgres
   `pg_try_advisory_lock`, `AlwaysLeaderLock` for sqlite/in-memory/tests). **FAIL-OPEN**: any
   uncertainty → treated as leader, so it can never halt generation; with deterministic keys
   (#1) double-processing is idempotent anyway. Gates the startup batch-resume + the sweep.
5. [x] **Durable reconciliation sweep** — `VocabularyAudioWorker.EnqueueMissingAudioAsync`
   runs every 5 min (leader-gated), re-enqueues non-archived terms lacking a Ready asset.
   Restart-safe; manual "Resume" is now just a nudge. *(test ReconciliationSweep_…)*
6. [x] **Orphan-file cleanup** — `CleanupOrphanedAudioAsync(dryRun)` +
   `POST /v1/admin/voice-design/cleanup-orphans?dryRun=` (dryRun defaults TRUE). Deletes audio
   objects under recalls/audio + vocabulary/audio not referenced by any `MediaAsset.StoragePath`.
   *(test CleanupOrphanedAudio_…)* — run `?dryRun=true` first in prod to clear the ~700 orphans.

## Status — ALL COMPLETE
All six changes committed + tested (10 worker tests + 125 VoiceDesign/Admin/Endpoint tests green).
Fail-open leader lock means staging is recommended but not gating. Post-deploy: hit
`cleanup-orphans?dryRun=true`, eyeball the count, then `?dryRun=false`.
