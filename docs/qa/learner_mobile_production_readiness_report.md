# Learner Mobile Production Readiness Report

## Status

- Assessment date: 2026-04-02
- Readiness level: ready for internal QA and device verification
- Release level: not yet ready for final store release because signing and native hardware validation remain external steps

## Readiness Gates

| Gate | Status | Evidence |
| --- | --- | --- |
| Shared UI remains the source of truth | Pass | The mobile shell loads the same Next.js Learner UI used on web. |
| Frontend build stability | Pass | `cmd /c npm run build` completed successfully. |
| Capacitor project sync | Pass | Android and iOS add/sync completed successfully. |
| Auth/session persistence | Pass | Native-safe storage and auth hydration are implemented. |
| Speaking capture | Pass in code | The native speaking recorder bridge is wired into the speaking task and supporting flows. |
| Safe-area and keyboard handling | Pass | Shell hardening was applied in the layout and app shell. |
| Device/emulator microphone validation | Pending | Real hardware validation has not been run in this workspace. |
| Signed release packaging | Pending | Release signing is external to the local implementation pass. |

## Assessment

- The mobile codebase is in a strong handoff state for internal QA.
- The main user risk, speaking capture, now has a native Capacitor path instead of relying only on browser APIs.
- The remaining blockers are operational rather than code-level: device/emulator verification and signed release packaging.

## Recommendation

- Proceed to Android Studio and Xcode verification next.
- Treat microphone permission, speaking recording, and resume-from-background as the first device-level checks.
- After device validation, run a signed release build before any production rollout.

## Relevant Files

- [app/speaking/task/[id]/page.tsx](../../app/speaking/task/[id]/page.tsx)
- [app/diagnostic/speaking/page.tsx](../../app/diagnostic/speaking/page.tsx)
- [app/speaking/check/page.tsx](../../app/speaking/check/page.tsx)
- [lib/mobile/runtime.ts](../../lib/mobile/runtime.ts)
- [lib/mobile/speaking-recorder.ts](../../lib/mobile/speaking-recorder.ts)
- [capacitor.config.ts](../../capacitor.config.ts) remains the mobile config entry.
