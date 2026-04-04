# Learner Mobile Audit Report

## Status

- Audit date: 2026-04-02
- Scope: Learner mobile shell only
- Outcome: implemented and build-validated locally

## Audit Summary

- The mobile implementation uses Capacitor as a thin shell around the shared Next.js Learner UI.
- Production loads the deployed origin; local mobile development loads the local Next.js server.
- Native code is limited to platform-specific bridges where browser APIs are not reliable enough.
- Speaking capture is the primary native bridge because the mobile webview path is not dependable enough for consistent audio recording.

## Architecture Decisions

- Keep Next.js as the single source of truth for UI and page logic.
- Centralize mobile lifecycle behavior in `lib/mobile/runtime.ts` and `components/mobile/mobile-runtime-bridge.tsx`.
- Persist auth and session data with Capacitor Preferences through `lib/mobile/native-storage.ts`.
- Use request timeouts in `lib/network/fetch-with-timeout.ts` so stalled requests do not hang the shell.
- Keep browser `MediaRecorder` for web while using the native speaking recorder bridge on Capacitor devices.

## Implemented Layers

- Capacitor bootstrap and native projects: `capacitor.config.ts`, `capacitor-web/index.html`, `android/`, `ios/`.
- Mobile runtime bridge: `lib/mobile/runtime.ts`, `components/mobile/mobile-runtime-bridge.tsx`, `app/providers.tsx`.
- Native-safe persistence: `lib/mobile/native-storage.ts`, `lib/auth-storage.ts`, `lib/auth-client.ts`.
- Network hardening: `lib/network/fetch-with-timeout.ts` and the auth/API callers that use it.
- Shell hardening: `app/layout.tsx`, `app/globals.css`, `components/layout/app-shell.tsx`, `components/layout/sidebar.tsx`, `components/layout/top-nav.tsx`.
- Native speaking recorder bridge: `lib/mobile/speaking-recorder.ts` plus the Android and iOS plugin implementations.
- Speaking surfaces updated for native fallback: `app/speaking/task/[id]/page.tsx`, `app/diagnostic/speaking/page.tsx`, and `app/speaking/check/page.tsx`.

## Validation Evidence

- `cmd /c npm run build` passed.
- Capacitor add/sync completed successfully for Android and iOS.
- Previous focused Vitest and backend verification completed during the implementation pass.
- No build errors remain in the updated speaking pages.

## Residual Risks

- Physical Android/iOS device validation still needs to be run on real hardware or emulators.
- Signed release packaging remains an external release step.
- Browser QA is still needed because the same pages continue to run on web.

## Relevant Files

- [app/speaking/task/[id]/page.tsx](../../app/speaking/task/[id]/page.tsx)
- [app/diagnostic/speaking/page.tsx](../../app/diagnostic/speaking/page.tsx)
- [app/speaking/check/page.tsx](../../app/speaking/check/page.tsx)
- [lib/mobile/speaking-recorder.ts](../../lib/mobile/speaking-recorder.ts)
- [lib/mobile/runtime.ts](../../lib/mobile/runtime.ts)
- [lib/mobile/native-storage.ts](../../lib/mobile/native-storage.ts)
- [lib/network/fetch-with-timeout.ts](../../lib/network/fetch-with-timeout.ts) is the shared timeout wrapper.
