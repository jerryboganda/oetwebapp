# Learner Mobile Fix Summary

## What Changed

- Added a Capacitor shell that loads the shared Learner UI instead of introducing a separate mobile app surface.
- Added mobile-safe auth/session persistence, request timeout handling, and runtime initialization.
- Hardened the shell for safe-area, viewport, keyboard, and lifecycle behavior.
- Added a native speaking recorder bridge for Android and iOS.
- Updated the speaking task, diagnostic speaking, and speaking check flows to use the native recorder on Capacitor devices while preserving the browser path on web.

## Defects Fixed

- Capacitor could not scaffold cleanly until the web root was split into a dedicated native entry page.
- The TypeScript build was blocked by a deprecation-setting mismatch.
- The main speaking task depended on browser-only recording behavior.
- Diagnostic speaking and the speaking readiness check were not ready for native microphone flows.
- The speaking task cleanup logic triggered a hook stability warning during the final pass.

## Validation Completed

- `cmd /c npm run build` passed after the final cleanup.
- Capacitor add/sync completed successfully for Android and iOS.
- The updated speaking pages no longer report build-time errors.
- Previous focused unit and backend verification completed successfully during the implementation pass.

## Remaining Follow-Up

- Run Android Studio and Xcode device or emulator verification when the native toolchains are available.
- Confirm microphone permission prompts and audio capture on a physical Android device and an iPhone.
- Perform signed release packaging once release credentials are ready.
