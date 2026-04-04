# Learner Mobile Test Matrix

## Verified Locally

| Area | Command / Surface | Status | Notes |
| --- | --- | --- | --- |
| Frontend build | `cmd /c npm run build` | Passed | Next production build completed successfully after the mobile changes. |
| Mobile build and sync | `cmd /c npm run mobile:build` | Passed | Runs the frontend build and Capacitor sync together. |
| Capacitor scaffold | `cmd /c npm run mobile:sync` | Passed | Android and iOS native projects were generated and synced successfully. |
| Frontend unit tests | Focused Vitest suite from the implementation pass | Passed | Previous focused unit verification completed successfully. |
| Backend tests | Backend test suite from the implementation pass | Passed | Previous backend verification completed successfully. |
| Speaking task flow | `app/speaking/task/[id]/page.tsx` | Implemented | Native recorder path now uses the Capacitor speaking bridge. |
| Diagnostic speaking flow | `app/diagnostic/speaking/page.tsx` | Implemented | Native recorder fallback added for the diagnostic speaking path. |
| Speaking readiness check | `app/speaking/check/page.tsx` | Implemented | Native permission and sample recording fallback added. |
| Mobile runtime bootstrap | `components/mobile/mobile-runtime-bridge.tsx`, `lib/mobile/runtime.ts` | Implemented | Lifecycle, device, keyboard, network, and status-bar handling are centralized. |
| Shell safe-area handling | `app/layout.tsx`, `app/globals.css`, `components/layout/app-shell.tsx` | Implemented | Safe-area and viewport behavior were hardened for mobile. |

## Not Run Locally

| Area | Expected Validation | Status | Notes |
| --- | --- | --- | --- |
| Android Studio build | Native Android compile | Pending | The scaffold exists, but Android Studio was not used on this machine. |
| Xcode build | Native iOS compile | Pending | Xcode and CocoaPods tooling were not available on this machine. |
| Device/emulator microphone flow | Real microphone permission and capture | Pending | Needed for final device confidence. |
| Release-store packaging | Signed app build | Pending | Out of scope for the local implementation pass. |

## Coverage Notes

- The mobile shell is intentionally thin: most validation still happens in the shared Next.js UI.
- Native code is isolated to the speaking recorder plugin and the mobile runtime bridge.
- Browser smoke remains important because the same pages still run on web.
- The highest-risk user path is speaking capture, and that path now has both native and browser coverage.
