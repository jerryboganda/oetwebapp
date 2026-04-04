# Mobile Capacitor Architecture Report

## Status

- Audit date: 2026-04-03
- Final state: implemented and build-validated locally
- Scope: Learner mobile shell only

## Architecture

- Keep the existing Next.js App Router UI as the single source of truth.
- Use Capacitor as a thin shell that loads the deployed Next.js origin in production and the local dev server during development.
- Resolve the production mobile origin from `APP_URL` or `CAPACITOR_APP_URL` during Capacitor packaging, and fall back to the documented production host when the workspace env is still a template.
- Limit native code to device-specific bridges where the browser/webview path is not reliable enough.
- Treat speaking recording as the primary native bridge because MediaRecorder/Web Audio is not robust enough for the mobile webview path.

## Implemented Mobile Layers

- Capacitor bootstrap and native projects: `capacitor.config.ts`, `android/`, `ios/`, `capacitor-web/index.html`.
- Mobile runtime bridge: `lib/mobile/runtime.ts`, `components/mobile/mobile-runtime-bridge.tsx`, `app/providers.tsx`.
- Native-safe persistence: `lib/mobile/native-storage.ts`, `lib/auth-storage.ts`, `lib/auth-client.ts`.
- Network hardening: `lib/network/fetch-with-timeout.ts` and the auth/API callers that use it.
- Shell hardening: `app/layout.tsx`, `app/globals.css`, `components/layout/app-shell.tsx`, `components/layout/sidebar.tsx`, `components/layout/top-nav.tsx`.
- Native speaking recorder bridge: `lib/mobile/speaking-recorder.ts` plus the Android and iOS plugin implementations.
- Speaking surfaces updated for native fallback: `app/speaking/task/[id]/page.tsx`, `app/diagnostic/speaking/page.tsx`, and `app/speaking/check/page.tsx`.

## Speaking Flow Decision

- Web continues to use `MediaRecorder` for browser compatibility.
- Capacitor/native uses the custom speaking recorder bridge for permission, start, pause, resume, stop, and cancel.
- Diagnostic speaking and mic-check use the same native fallback so the readiness flow matches the main speaking task path.

## Risks Addressed

- Session hydration now survives native storage quirks.
- Speaking recording no longer depends on browser-only APIs on device.
- Safe-area and keyboard handling are centralized in the shared shell.
- Request timeout handling prevents stalled uploads and auth requests from hanging indefinitely.

## Residual Risks

- Native emulator/device validation is still the next best assurance step for microphone permission behavior.
- iOS/macOS build tooling was not available on this machine, so Xcode/CocoaPods verification remains a follow-up step.
- The web path still needs ordinary browser QA for speaking and diagnostic recording.

## Relevant Files

- [app/speaking/task/[id]/page.tsx](../../app/speaking/task/[id]/page.tsx)
- [app/diagnostic/speaking/page.tsx](../../app/diagnostic/speaking/page.tsx)
- [app/speaking/check/page.tsx](../../app/speaking/check/page.tsx)
- [lib/mobile/speaking-recorder.ts](../../lib/mobile/speaking-recorder.ts)
- [lib/mobile/runtime.ts](../../lib/mobile/runtime.ts)
- [lib/mobile/native-storage.ts](../../lib/mobile/native-storage.ts)
- [lib/network/fetch-with-timeout.ts](../../lib/network/fetch-with-timeout.ts)
