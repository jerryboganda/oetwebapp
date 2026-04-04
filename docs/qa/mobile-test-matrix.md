# Mobile Test Matrix

## Verified Locally

| Area | Command / Surface | Status | Notes |
| --- | --- | --- | --- |
| Frontend build | `cmd /c npm run build` | Passed | Next production build completed successfully after the speaking changes. |
| Frontend unit tests | Focused Vitest suite | Passed | Previous focused suite completed successfully during implementation. |
| Backend tests | `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj` | Passed | Previous backend verification completed successfully during implementation. |
| Capacitor scaffold | `npm.cmd exec -- cap add android`, `npm.cmd exec -- cap add ios`, `npm.cmd exec -- cap sync` | Passed | Android and iOS native projects were generated/synced successfully. |
| Speaking task flow | `app/speaking/task/[id]/page.tsx` | Implemented | Native recorder path now uses the Capacitor speaking bridge. |
| Diagnostic speaking flow | `app/diagnostic/speaking/page.tsx` | Implemented | Native recorder fallback added for the diagnostic speaking path. |
| Speaking readiness check | `app/speaking/check/page.tsx` | Implemented | Native permission and sample recording fallback added. |

## Not Run Locally

| Area | Expected Validation | Status | Notes |
| --- | --- | --- | --- |
| Android Studio build | Native Android compile | Pending | The scaffold exists, but Android Studio was not used on this machine. |
| Xcode build | Native iOS compile | Pending | Xcode/CocoaPods tooling was not available on this machine. |
| Device/emulator microphone flow | Real microphone permission and capture | Pending | Needed for final device confidence. |
| Release-store packaging | Signed app build | Pending | Out of scope for the local implementation pass. |

## Coverage Notes

- The mobile shell is intentionally thin: most validation still happens in the shared Next.js UI.
- Native code is isolated to the speaking recorder plugin and the mobile runtime bridge.
- Browser smoke remains important because the same pages still run on web.
- The highest-risk user path is speaking capture, and that path is now covered by both native and browser implementations.
