# Speaking Module — STRIDE Threat Model

One STRIDE row per (asset × threat-category). Likelihood + impact rated **Low / Medium / High**. Residual risk reflects what remains after listed mitigations.

## SpeakingSession

| Threat | Vector | Impact | Likelihood | Mitigation | Residual |
|--------|--------|--------|------------|------------|----------|
| Spoofing | Forged user id in URL | H | L | Owner check in `SpeakingSessionService`; 404 on mismatch | Low |
| Tampering | State-machine skip (Prep→Active without going through WarmUp) | M | L | State guards in service; xUnit tests | Low |
| Repudiation | "I never started this session" | M | M | Consent stamp + audit event on create + transitions | Low |
| Information disclosure | Cross-tenant read | H | L | Owner check; integration test for IDOR | Low |
| Denial of service | Session-create flood | M | M | `PerUser` rate limit; create-session SLO budget | Low |
| Elevation | Learner triggers tutor-only transitions | H | L | Endpoint-level policy; service-layer role checks | Low |

## SpeakingRecording

| Threat | Vector | Impact | Likelihood | Mitigation | Residual |
|--------|--------|--------|------------|------------|----------|
| Spoofing | Upload a recording as someone else | H | L | Session ownership check; SHA-256 manifest | Low |
| Tampering | Tamper with stored audio | H | L | SHA-256 stored, verified on read | Low |
| Repudiation | "Not my voice" | M | M | Voice biometric stored; consent versioning | Low |
| Information disclosure | Unauthorized listen | H | M | Restricted access; admin access logged | Medium |
| Denial of service | Storage exhaustion | M | M | Per-session chunk + total size cap; retention worker | Low |
| Elevation | Use signed URL to read others' audio | H | L | Pre-signed URL bound to recording id + caller; short TTL | Low |

## SpeakingTranscript

| Threat | Vector | Impact | Likelihood | Mitigation | Residual |
|--------|--------|--------|------------|------------|----------|
| Spoofing | Fabricated transcript injected into AI assessment | H | L | Evidence-quote verification (verbatim substring) | Low |
| Tampering | Mid-flight edit | H | L | `IsLatest` invariant; new version row per re-transcription | Low |
| Repudiation | n/a | — | — | — | — |
| Information disclosure | Cross-tenant read | H | M | Restricted access; owner-only learner endpoint | Medium |
| Denial of service | Huge transcript size | L | L | Provider-side caps | Low |
| Elevation | Tutor reads pre-publication transcript | M | L | Tutor allowed (queue claim required); audited | Low |

## InterlocutorScript

| Threat | Vector | Impact | Likelihood | Mitigation | Residual |
|--------|--------|--------|------------|------------|----------|
| Information disclosure | Leak to learner | **H** | M | Backend never projects script fields to learner endpoints; integration test asserts no leakage | Low |
| Tampering | Unauthorized edit | M | L | `AdminContentWrite` policy; audit | Low |
| Spoofing / Repudiation / DoS / Elevation | as above | — | — | — | — |

## SpeakingAiAssessment

| Threat | Vector | Impact | Likelihood | Mitigation | Residual |
|--------|--------|--------|------------|------------|----------|
| Spoofing | Provider impersonation | M | L | Provider id pinned in record; provider key + endpoint controlled | Low |
| Tampering | Forge a high score | M | L | Stored quote evidence verified verbatim against transcript | Low |
| Repudiation | "Score is unfair" | M | M | `IsAdvisory = true`; tutor assessment is authoritative | Low |
| Information disclosure | Cross-tenant read | M | L | Owner-only learner endpoint | Low |
| Denial of service | Provider rate-limit exhaustion | M | M | Backoff + secondary provider failover | Medium |
| Elevation | Bypass `IsAdvisory` semantics | M | L | Frontend disclaimer + backend never returns AI as final | Low |

## SpeakingTutorAssessment

| Threat | Vector | Impact | Likelihood | Mitigation | Residual |
|--------|--------|--------|------------|------------|----------|
| Spoofing | Tutor impersonation | H | L | `ExpertOnly` policy; session binding; MFA gap noted in README | **Medium** |
| Tampering | Score edit after submit | H | L | `IsFinal` flag + audit row per change | Low |
| Repudiation | "I didn't submit this score" | M | M | Authored claims captured in JWT; submit time recorded | Low |
| Information disclosure | Reveal scores before learner sees them | L | M | Standard delivery pattern; no risk to assessment integrity | Low |
| Denial of service | Queue claim hoarding | M | M | TTL on idle claims | Low |
| Elevation | Tutor edits another tutor's assessment | H | L | Owner check on assessment id | Low |

## SpeakingComplianceConsent

| Threat | Vector | Impact | Likelihood | Mitigation | Residual |
|--------|--------|--------|------------|------------|----------|
| Spoofing | Forge consent record | H | L | Caller bound to session id; recorded from auth context | Low |
| Tampering | Modify accepted-at timestamp | H | L | Append-only audit trail; row not editable | Low |
| Repudiation | "I never consented" | H | L | IP + user agent + version stored; audit | Low |
| Information disclosure | n/a | — | — | — | — |
| Denial of service | Spam consents | L | L | Idempotent insert | Low |
| Elevation | n/a | — | — | — | — |

## SpeakingLiveRoom

| Threat | Vector | Impact | Likelihood | Mitigation | Residual |
|--------|--------|--------|------------|------------|----------|
| Spoofing | Joining a room without booking | H | L | JWT capability scoping; room name uniqueness; participant identity bound to user | Low |
| Tampering | Forged webhook payload | H | L | HMAC verification + constant-time comparison | Low |
| Repudiation | "Room never ended" | L | L | Webhook + audit | Low |
| Information disclosure | Eavesdropping on tutor session | H | L | TLS over WebRTC; recording consent gates egress | Low |
| Denial of service | Room provisioning flood | M | L | `MockBooking` schedule constraints + rate limit on provisioning | Low |
| Elevation | Learner gains `roomAdmin` | H | L | JWT minted server-side; capability scoping enforced | Low |
