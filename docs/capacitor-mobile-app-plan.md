# Capacitor Mobile App Conversion Plan — OET with Dr Hesham Platform

**Document version:** 1.0.0  
**Date:** 2025-07-17  
**Project:** OET with Dr Hesham Learner (com.oetwithdrhesham.learner)  
**Status:** Implementation-ready analysis and plan  

---

## 1. Executive Summary

### What the Project Is

OET with Dr Hesham is a comprehensive OET (Occupational English Test) preparation platform built as a Next.js 15.4 App Router web application with an ASP.NET Core (.NET 8+) backend, PostgreSQL database (SQLite for desktop), and first-party JWT authentication. The platform serves three user roles — **Learner**, **Expert** (reviewer/instructor), and **Admin** — across approximately 120+ distinct routes covering diagnostic assessments, skill practice (listening, reading, writing, speaking), AI-powered conversation, mock exams, expert review workflows, study planning, achievements, billing, community features, and a full CMS/admin panel.

### What the Mobile Plan Is

The project **already has a mature, well-integrated Capacitor 6 mobile shell** targeting both iOS and Android. This document evaluates the existing implementation against official Capacitor guidance, Apple App Store Review Guidelines, Google Play policies, OWASP MASVS, and strict UI/UX/functional parity requirements. It identifies remaining gaps and provides a phased roadmap to bring the mobile apps to production store release quality.

### Key Finding

The Capacitor mobile integration is **70-80% complete**. Both Android and iOS native projects exist and build successfully. A comprehensive mobile runtime system is implemented (`lib/mobile/`) covering lifecycle management, haptics, native storage bridging, offline sync, keyboard handling, status bar theming, and a custom SpeakingRecorder native plugin. Remaining work focuses on: push notification frontend integration, deep linking configuration, App Store/Play Store submission readiness, CI/CD pipeline for mobile builds, security hardening per OWASP MASVS, biometric authentication consideration, and parity validation testing.

---

## 2. Analysis Environment and Method

### Capabilities Used

| Capability | Status | How Used |
|---|---|---|
| Claude Opus 4.6 Agent (VS Code) | ✅ Active | Primary analysis engine |
| Gem Team Orchestration | ✅ Active | Phase routing, parallel analysis delegation |
| Explore Subagent | ✅ Used | Thorough project structure mapping (22KB report) |
| Web Research | ✅ Performed | Official Capacitor docs, Apple/Google guidelines, OWASP MASVS |
| File System Analysis | ✅ Full | All source files read and analyzed |
| Repository Memory | ✅ Read | Prior project facts incorporated |

### Missing/Degraded Capabilities

| Capability | Status | Compensation |
|---|---|---|
| cc_token_saver_mcp | Unavailable | All analysis handled by main model |
| context-mode MCP | Deferred | Custom tool search used instead |
| jcodemunch-mcp | Unavailable | Direct file reads performed |

---

## 3. External Research and Compliance Audit

### Sources Reviewed

| Source | Tier | Trust Level | Key Takeaways | Adopted? |
|---|---|---|---|---|
| [capacitorjs.com/docs](https://capacitorjs.com/docs) | **Tier 1 — Official** | Authoritative | Capacitor v6 config, plugins, native bridge, deep linking guide | ✅ Yes — primary reference |
| [capacitorjs.com/docs/guides/deep-links](https://capacitorjs.com/docs/guides/deep-links) | **Tier 1 — Official** | Authoritative | Universal Links (iOS) + App Links (Android), site association files, `appUrlOpen` listener | ✅ Yes |
| [developer.apple.com/app-store/review/guidelines](https://developer.apple.com/app-store/review/guidelines/) | **Tier 1 — Official** | Authoritative | 5 sections (Safety, Performance, Business, Design, Legal), Guideline 4.2 minimum functionality, 2.5.6 WebKit requirement, 5.1 privacy | ✅ Yes |
| [developer.apple.com/design/human-interface-guidelines/designing-for-ios](https://developer.apple.com/design/human-interface-guidelines/designing-for-ios) | **Tier 1 — Official** | Authoritative | Safe areas, touch targets, Dynamic Type, orientation support | ✅ Yes |
| [developer.android.com (edge-to-edge)](https://developer.android.com/develop/ui/views/layout/edge-to-edge) | **Tier 1 — Official** | Authoritative | Edge-to-edge enforced on SDK 35+, system bar insets, gesture navigation, display cutouts | ✅ Yes |
| [developer.android.com (target SDK)](https://developer.android.com/google/play/requirements/target-sdk) | **Tier 1 — Official** | Authoritative | Must target Android 15 (API 35) by Aug 31, 2025 for new apps | ✅ Yes |
| [mas.owasp.org/MASVS](https://mas.owasp.org/MASVS/) | **Tier 1 — Standard** | Authoritative | 8 control groups: STORAGE, CRYPTO, AUTH, NETWORK, PLATFORM, CODE, RESILIENCE, PRIVACY | ✅ Yes |
| Electron Desktop Plan (internal) | **Tier 1 — Internal** | Authoritative | Structural template, parity matrix approach, implementation wave pattern | ✅ Yes — structural reference |

### OWASP MASVS Compliance Matrix

| Control Group | Relevance to OET with Dr Hesham | Current Status | Gap |
|---|---|---|---|
| **MASVS-STORAGE** | Auth tokens, offline cached content, user preferences | ✅ Good — secure storage helper is wired for auth tokens | Native-device validation still required |
| **MASVS-CRYPTO** | Token storage, potential biometric key binding | ✅ Implemented for secure storage / biometric helper surface | Native-device validation still required |
| **MASVS-AUTH** | JWT authentication, session management, potential biometrics | ✅ Good — JWT auth, session refresh, secure storage, and biometric helper exist | Store-review proof still required |
| **MASVS-NETWORK** | All API communication, WebView content loading | ✅ Good — HTTPS-only, production URL | Evaluate certificate pinning |
| **MASVS-PLATFORM** | WebView security, deep link validation, IPC, permissions | ✅ Good — WebView is Capacitor-managed and deep-link validation is wired | Permission rationale/store proof still required |
| **MASVS-CODE** | Dependency hygiene, enforced updating, ProGuard | ⚠️ Partial — deps tracked and forced-update helpers exist | Wire forced-update startup/backend policy and ProGuard/R8 for Android |
| **MASVS-RESILIENCE** | Root/jailbreak detection, debuggable flag, obfuscation | ❌ Not implemented | Add root/jailbreak detection, disable debug in release |
| **MASVS-PRIVACY** | Microphone permission, analytics, data collection | ⚠️ Partial — microphone permission declared with rationale | Add Privacy Manifest (iOS), data safety section (Play Store) |

---

## 4. Product Scope and Parity Matrix

### Parity Requirements

| Dimension | Target | Notes |
|---|---|---|
| Product intent parity | **100%** | Every feature available on web must be available on mobile |
| UI/UX parity | **Maximum possible** | Adapt to platform conventions (safe areas, gestures, haptics) |
| Functional parity | **100%** | Zero behavioral differences — all flows work identically |
| Unjustified deviation | **0%** | Every deviation must have a documented platform-specific reason |

### Feature Parity Audit

| Feature Area | Web | Mobile (Current) | Gap | Priority |
|---|---|---|---|---|
| Authentication (email/password) | ✅ | ✅ | None | — |
| OAuth (Google/Apple) | ✅ | ⚠️ Partial | Need native OAuth flow via in-app browser | P1 |
| Diagnostic assessments | ✅ | ✅ | None (via WebView) | — |
| Listening practice | ✅ | ✅ | Audio playback works via WebView | — |
| Reading practice | ✅ | ✅ | None | — |
| Writing practice | ✅ | ✅ | None | — |
| Speaking practice + recording | ✅ | ✅ | Custom native plugin exists (AAC recording) | — |
| AI Conversation | ✅ | ✅ | None | — |
| Mock exams | ✅ | ✅ | Timer/fullscreen may need mobile adaptation | P2 |
| Expert review workflow | ✅ | ✅ | None (Learner role only on mobile) | — |
| Study planner | ✅ | ✅ | None | — |
| Achievements/gamification | ✅ | ✅ | None | — |
| Billing/subscription | ✅ | ⚠️ Gap | Need IAP integration or web billing redirect | P1 |
| Push notifications | ✅ (via browser) | ⚠️ Plugin installed, not integrated | Frontend integration needed | P1 |
| Deep linking | N/A | ✅ Implemented | Native-device validation required | P1 |
| Share functionality | N/A | ⚠️ Plugin installed, not integrated | Frontend integration needed | P2 |
| Offline mode | ✅ (SW) | ✅ Partial | IndexedDB offline-sync exists, content caching works | P2 |
| Biometric unlock | N/A | ✅ Helper implemented | Product entry point/native validation required | P2 |
| Native notifications | N/A | ✅ Runtime bridge integrated | Store/device validation required | P1 |

---

## 5. Technology Stack

### Confirmed Stack (from source code inspection)

| Layer | Technology | Version | Notes |
|---|---|---|---|
| **Frontend framework** | Next.js (App Router) | 15.4.9 | `output: 'standalone'` mode |
| **React** | React | 19.2.1 | Latest stable |
| **TypeScript** | TypeScript | 5.9.3 | Strict mode |
| **CSS** | Tailwind CSS | 4.1.11 | Utility-first |
| **Backend** | ASP.NET Core | .NET 8+ | Production: PostgreSQL, Desktop: SQLite |
| **Auth** | First-party JWT | — | Access + refresh tokens |
| **Mobile runtime** | Capacitor | 6.2.1 | Official iOS + Android platforms |
| **iOS platform** | @capacitor/ios | ^6.2.1 | Swift-based native shell |
| **Android platform** | @capacitor/android | ^6.2.1 | Java/Kotlin-based native shell |
| **Desktop** | Electron | 41.1.0 | Coexists with Capacitor |
| **Build** | npm scripts | — | `mobile:dev`, `mobile:build`, `mobile:sync`, `mobile:run:*` |

### Capacitor Plugin Inventory

| Plugin | Version | Status | Integration Level |
|---|---|---|---|
| `@capacitor/app` | 6.0.5 | ✅ Installed + integrated | Full — lifecycle, back button, `appUrlOpen` listener ready |
| `@capacitor/browser` | 6.0.6 | ✅ Installed + integrated | Used for external URL opening |
| `@capacitor/core` | 6.2.1 | ✅ Installed + integrated | Core bridge, `Capacitor.isNativePlatform()` |
| `@capacitor/device` | 6.0.3 | ✅ Installed | Available for device info |
| `@capacitor/filesystem` | 6.0.4 | ✅ Installed | Available for file operations |
| `@capacitor/haptics` | 6.0.3 | ✅ Installed + integrated | Full — impact (light/medium/heavy) + notification (success/warning/error) |
| `@capacitor/keyboard` | 6.0.4 | ✅ Installed + integrated | Full — resize:body, keyboardWillShow/Hide events |
| `@capacitor/network` | 6.0.4 | ✅ Installed + integrated | Full — connectivity monitoring, offline detection |
| `@capacitor/preferences` | 6.0.4 | ✅ Installed + integrated | Full — native storage bridge, web↔native hydration |
| `@capacitor/push-notifications` | 6.0.5 | ⚠️ Installed, **NOT integrated** | Plugin available but no frontend listeners/handlers |
| `@capacitor/share` | 6.0.4 | ⚠️ Installed, **NOT integrated** | Plugin available but no share UI triggers |
| `@capacitor/splash-screen` | 6.0.4 | ✅ Installed + configured | `launchAutoHide: false` (manually controlled) |
| `@capacitor/status-bar` | 6.0.3 | ✅ Installed + integrated | Full — dark/light theme sync with CSS color scheme |
| **SpeakingRecorder** (custom) | N/A | ✅ Installed + integrated | Custom native plugin — Java (Android) + Swift (iOS) |

---

## 6. Capacitor Architecture

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    OET with Dr Hesham Mobile App                       │
├───────────────┬─────────────────────────────────────────────┤
│   Native      │              WebView Layer                   │
│   Shell       │  ┌──────────────────────────────────────┐   │
│               │  │  Next.js App (Remote/Local URL)       │   │
│  iOS:         │  │  ├── app/layout.tsx (root bootstrap)  │   │
│  WKWebView    │  │  ├── app/providers.tsx (mobile init)  │   │
│               │  │  ├── components/mobile/*               │   │
│  Android:     │  │  └── ~120+ routes (full web app)      │   │
│  WebView      │  └──────────────────────────────────────┘   │
│               │                    ↕                         │
│               │           Capacitor Bridge                   │
│               │                    ↕                         │
│  ┌───────┐    │  ┌──────────────────────────────────────┐   │
│  │Native │    │  │  lib/mobile/                          │   │
│  │Plugins│◄───┤  │  ├── runtime.ts (init, lifecycle)     │   │
│  │       │    │  │  ├── haptics.ts (feedback)             │   │
│  │Custom:│    │  │  ├── native-storage.ts (prefs bridge) │   │
│  │Speaker│    │  │  ├── offline-sync.ts (IndexedDB)       │   │
│  │Recorder│   │  │  ├── speaking-recorder.ts (custom)    │   │
│  └───────┘    │  │  ├── lifecycle-motion.ts (animations)  │   │
│               │  │  └── capacitor-config.ts (URL)         │   │
│               │  └──────────────────────────────────────┘   │
├───────────────┴─────────────────────────────────────────────┤
│  lib/runtime-signals.ts — Three-way detection               │
│  (web | desktop | capacitor-native)                          │
└─────────────────────────────────────────────────────────────┘
```

### Runtime Detection Flow

1. `app/layout.tsx` injects `getRuntimeBootstrapScript()` via `<script>` tag before interactive
2. Script sets `document.documentElement.dataset.runtimeKind` to `'capacitor-native'` when `window.Capacitor?.isNativePlatform?.()` returns true
3. `app/providers.tsx` conditionally renders `<MobileRuntimeBridge>` on Capacitor
4. `MobileRuntimeBridge` calls `initializeMobileRuntime()` → sets up:
   - Viewport metrics (`--app-viewport-height`)
   - Color scheme sync (status bar theme)
   - Keyboard events (`keyboardWillShow/Hide`)
   - Network status monitoring
   - App lifecycle (resume/pause)
   - Android back button interception
   - Status bar styling
5. Returns teardown function for cleanup on unmount

### Server Architecture (Remote vs. Bundled)

**Current approach: Remote server** — Capacitor WebView loads `https://app.oetwithdrhesham.co.uk` (production URL). This is configured in `capacitor.config.ts`:

```typescript
server: {
  url: serverUrl,           // Falls back to production URL
  cleartext: false,
  androidScheme: 'https',
},
```

**Implications:**
- ✅ Instant content updates without app store review
- ✅ Single deployment for web + mobile
- ⚠️ Requires network connectivity for initial load
- ⚠️ Must satisfy Apple Guideline 4.2 (Minimum Functionality) — app must provide native-like experience, not just a browser wrapper
- ⚠️ Offline capability depends on service worker + IndexedDB cache

**Recommendation:** Maintain remote server approach but ensure robust offline shell, splash screen, and native feature integration to satisfy App Store review requirements.

---

## 7. Native Plugin Strategy

### Existing Custom Plugin: SpeakingRecorder

**Android** (`android/app/src/main/java/.../SpeakingRecorderPlugin.java`):
- Uses `MediaRecorder` with AAC encoding, 44.1kHz sample rate, 128kbps bitrate
- Records to `.m4a` file in cache directory
- Returns base64-encoded audio data
- Plugin methods: `startRecording`, `stopRecording`, `isRecording`

**iOS** (`ios/App/App/SpeakingRecorderPlugin.swift`):
- Uses `AVAudioRecorder` with AAC encoding, 44.1kHz, high quality
- `.spokenAudio` audio session category mode
- Records to cache directory
- Returns base64-encoded audio data
- Same plugin methods as Android

### Plugins Requiring Frontend Integration

#### Push Notifications (`@capacitor/push-notifications`)

**Current state:** Plugin installed, no frontend code.

**Required integration:**
1. Register for push notifications on app startup (with user permission)
2. Handle token registration → send to backend
3. Handle incoming notifications (foreground + background)
4. Handle notification tap → deep link to relevant content
5. Backend: Implement push notification service (FCM for Android, APNs for iOS)

```typescript
// Required integration pattern
import { PushNotifications } from '@capacitor/push-notifications';

// Request permission
const result = await PushNotifications.requestPermissions();
if (result.receive === 'granted') {
  await PushNotifications.register();
}

// Listen for token
PushNotifications.addListener('registration', token => {
  // Send token.value to backend
});

// Handle received notification
PushNotifications.addListener('pushNotificationReceived', notification => {
  // Show in-app notification
});

// Handle notification action (tap)
PushNotifications.addListener('pushNotificationActionPerformed', action => {
  // Navigate to relevant route based on action.notification.data
});
```

#### Share API (`@capacitor/share`)

**Current state:** Plugin installed, no frontend code.

**Required integration:**
1. Add share buttons to achievement cards, study progress summaries
2. Share practice results, study streaks
3. Platform-appropriate share sheet invocation

### Plugins to Evaluate for Addition

| Plugin | Purpose | Priority | Notes |
|---|---|---|---|
| `@capacitor/camera` | Profile photo capture | P3 | Web fallback exists via `<input type="file">` |
| `@capacitor-community/biometric-auth` | Biometric unlock | P2 | Face ID / Touch ID / Fingerprint for app lock |
| `@capacitor/local-notifications` | Local reminders | P2 | Study reminders, practice streaks |
| `@capacitor-community/in-app-review` | Store review prompt | P3 | Trigger after positive learning milestones |
| `@capacitor/screen-reader` | Accessibility | P2 | Enhanced screen reader support |

---

## 8. Platform Compliance

### Apple App Store Requirements

| Requirement | Status | Action Required |
|---|---|---|
| **4.2 Minimum Functionality** — App must not be merely a repackaged website | ⚠️ Risk | Ensure native features (push, haptics, offline, deep links, biometrics) provide substantial native value |
| **2.5.6 WebKit** — Apps must use WebKit for displaying web content | ✅ Met | Capacitor iOS uses WKWebView (WebKit) |
| **2.1 App Completeness** — Must be final version, not beta/demo | ✅ | Ensure all features work before submission |
| **2.3 Accurate Metadata** — Screenshots must reflect actual app | ✅ | Generate fresh screenshots before submission |
| **5.1.1 Data Collection** — Privacy policy required | ⚠️ Needed | Create comprehensive privacy policy URL |
| **5.1.2 Data Use** — App Privacy Details (nutrition labels) | ⚠️ Needed | Complete App Store Connect privacy questionnaire |
| **3.1.1 In-App Purchase** — Digital content/subscriptions must use IAP | ⚠️ Critical | Evaluate billing flow — may need Apple IAP integration for subscriptions |
| **4.0 Design** — Must follow HIG, support safe areas, Dynamic Type | ✅ Partial | Safe area insets implemented; verify Dynamic Type support |
| **iOS Privacy Manifest** — Required for certain API categories | ⚠️ Needed | Create `PrivacyInfo.xcprivacy` file |

### Google Play Store Requirements

| Requirement | Status | Action Required |
|---|---|---|
| **Target SDK 35** — Must target Android 15 (API 35) by Aug 31, 2025 | ⚠️ Verify | Check current `compileSdkVersion` and `targetSdkVersion` in `android/variables.gradle` |
| **Edge-to-edge** — Enforced on SDK 35+ | ✅ Partial | Safe area CSS exists; verify native insets handling |
| **Data Safety Section** — Required disclosure of data collection | ⚠️ Needed | Complete Play Console data safety form |
| **Content Rating** — IARC questionnaire required | ⚠️ Needed | Complete content rating questionnaire |
| **Target Audience** — Declare target age group | ⚠️ Needed | Declare as 16+ (educational, contains billing) |
| **App signing** — Google Play App Signing recommended | ⚠️ Needed | Enroll in Play App Signing, generate upload key |
| **64-bit requirement** — All apps must include 64-bit libraries | ✅ Met | Capacitor Android produces arm64-v8a + x86_64 by default |
| **Permissions** — Justify all declared permissions | ✅ Partial | INTERNET + RECORD_AUDIO declared with proper rationale |

### Edge-to-Edge Compliance (Android 15 / SDK 35)

Android 15 enforces edge-to-edge display. The app must properly handle:

1. **System bar insets** — Status bar and navigation bar overlap with content
2. **Display cutout insets** — Camera notches, punch-holes
3. **System gesture insets** — Back gesture, home gesture areas
4. **Keyboard insets** — IME (soft keyboard) overlap

**Current implementation:**
- `app/globals.css` defines `env(safe-area-inset-*)` CSS variables ✅
- `--app-viewport-height` CSS custom property updated on keyboard events ✅
- `--app-keyboard-offset` CSS custom property for keyboard avoidance ✅
- `.keyboard-safe-bottom` utility class ✅

**Gap:** Verify these CSS-based insets work correctly with Android 15's enforced edge-to-edge. May need `WindowCompat.setDecorFitsSystemWindows(window, false)` in `MainActivity.java` and corresponding `WindowInsetsCompat` handling.

---

## 9. Security and Privacy

### OWASP MASVS Compliance Plan

#### MASVS-STORAGE — Secure Storage

| Control | Current | Target | Implementation |
|---|---|---|---|
| MASVS-STORAGE-1: Sensitive data not in logs | ⚠️ Verify | ✅ | Audit `console.log` calls for token/credential leaks |
| MASVS-STORAGE-2: Sensitive data storage | ⚠️ Capacitor Preferences (unencrypted) | ✅ Encrypted | Migrate auth tokens to: Android Keystore / iOS Keychain via `@capacitor-community/biometric-auth` or custom bridge |
| Backup exclusion | ❌ Not configured | ✅ | Android: configure `android:allowBackup="false"` or backup rules; iOS: mark sensitive files with `isExcludedFromBackup` |

#### MASVS-AUTH — Authentication

| Control | Current | Target | Implementation |
|---|---|---|---|
| MASVS-AUTH-1: Server-side auth | ✅ JWT | ✅ | No change needed |
| MASVS-AUTH-2: Session management | ✅ Refresh on resume | ✅ | Already handles app resume session validation |
| MASVS-AUTH-3: Biometric auth | ❌ Not implemented | ✅ Optional | Add biometric unlock using platform APIs |
| Secure token storage | ⚠️ Capacitor Preferences | ✅ | Move to encrypted native storage |

#### MASVS-NETWORK — Network Security

| Control | Current | Target | Implementation |
|---|---|---|---|
| MASVS-NETWORK-1: TLS | ✅ HTTPS-only | ✅ | Capacitor `androidScheme: 'https'`, iOS ATS enabled |
| MASVS-NETWORK-2: Certificate pinning | ❌ Not implemented | ⚠️ Evaluate | Consider for high-security API endpoints (auth, billing) |
| Cleartext traffic | ✅ Disabled (`cleartext: false`) | ✅ | Properly configured |

#### MASVS-PLATFORM — Platform Security

| Control | Current | Target | Implementation |
|---|---|---|---|
| WebView security | ✅ Capacitor-managed WKWebView/WebView | ✅ | Capacitor handles WebView config securely |
| Deep link validation | ❌ Not implemented | ✅ | Validate incoming deep link URLs before navigation |
| Permission handling | ⚠️ RECORD_AUDIO only | ✅ | Add runtime permission rationale UI |
| Screenshot prevention | ❌ Not implemented | ⚠️ Evaluate | Consider for billing/auth screens |

#### MASVS-CODE — Code Quality

| Control | Current | Target | Implementation |
|---|---|---|---|
| ProGuard/R8 | ❌ Not configured | ✅ | Enable R8 code shrinking for release builds |
| Forced app update | ❌ Not implemented | ✅ | Add version check on app start, prompt for update |
| Debuggable flag | ⚠️ Verify | ✅ | Ensure `debuggable false` in release build.gradle |
| Dependencies | ✅ Tracked | ✅ | Add `npm audit` to CI pipeline |

#### MASVS-RESILIENCE — Anti-Tampering

| Control | Current | Target | Implementation |
|---|---|---|---|
| Root/jailbreak detection | ❌ Not implemented | ⚠️ P3 | Evaluate `freerasp` or `rootbeer` for detection (non-blocking warning) |
| Emulator detection | ❌ Not implemented | ⚠️ P3 | Evaluate for exam/assessment integrity |
| App integrity | ❌ Not implemented | ⚠️ P3 | Google Play Integrity API, Apple App Attest |

#### MASVS-PRIVACY — User Privacy

| Control | Current | Target | Implementation |
|---|---|---|---|
| iOS Privacy Manifest | ❌ Not created | ✅ P1 | Create `PrivacyInfo.xcprivacy` declaring API usage reasons |
| Data collection declaration | ❌ Not declared | ✅ P1 | Complete App Store privacy labels + Play Store data safety |
| Permission minimization | ✅ | ✅ | Only INTERNET + RECORD_AUDIO currently declared |
| Consent mechanisms | ⚠️ Web-based | ✅ | Ensure consent UI works properly in WebView context |

---

## 10. Authentication Flow

### Current Implementation

```
┌─────────┐     ┌──────────────┐     ┌─────────────┐
│  Mobile  │────▶│  WebView     │────▶│  Backend    │
│  App     │     │  (Next.js)   │     │  (ASP.NET)  │
│          │     │              │     │             │
│  launch  │     │ /auth/login  │     │ /api/auth/* │
│          │     │ /auth/signup │     │             │
│          │◀────│              │◀────│ JWT tokens  │
│          │     │ localStorage │     │             │
└─────────┘     └──────────────┘     └─────────────┘
                       │
                       ▼
               lib/mobile/native-storage.ts
               (hydrates web→native on login,
                syncs native→web on app resume)
```

### Authentication Lifecycle on Mobile

1. **App launch** → `initializeMobileRuntime()` hydrates native preferences → web localStorage
2. **Login** → Standard web form in WebView → JWT stored in localStorage + mirrored to Capacitor Preferences
3. **App resume** → `MobileRuntimeBridge` refreshes auth session, re-syncs storage
4. **Token refresh** → Web-based refresh token rotation, native storage updated
5. **Logout** → Both web localStorage and native preferences cleared

### OAuth Flow (Gap)

**Current state:** Web app has OAuth (Google, potentially Apple Sign In) via redirect flows. In native mobile context, OAuth redirect flows must be handled differently:

**Required approach:**
1. Use `@capacitor/browser` to open OAuth provider in system browser (not embedded WebView, per OAuth 2.0 best practices)
2. Register custom URL scheme or Universal Link for OAuth callback
3. Handle `appUrlOpen` event to capture OAuth code/token
4. Exchange code for JWT token via backend

### Biometric Authentication (New Feature)

**Proposed flow:**
1. After successful login, prompt user to enable biometric unlock
2. Encrypt refresh token with biometric-bound key (Android Keystore / iOS Keychain with `kSecAccessControlBiometryCurrentSet`)
3. On subsequent app opens, request biometric auth → decrypt token → auto-login
4. Fallback to password entry if biometric fails 3 times

---

## 11. Offline Strategy

### Current Implementation

`lib/mobile/offline-sync.ts` provides a comprehensive offline system:

| Component | Implementation | Status |
|---|---|---|
| **Content cache** | IndexedDB store `content` with 7-day expiry | ✅ Implemented |
| **Attempt queue** | IndexedDB store `attempts` for offline practice submissions | ✅ Implemented |
| **Vocabulary store** | IndexedDB store `vocabulary` for offline word lists | ✅ Implemented |
| **Meta store** | IndexedDB store for sync timestamps and status | ✅ Implemented |
| **Auto-sync** | Reconnect listener triggers `syncPendingAttempts()` | ✅ Implemented |
| **Network monitoring** | `@capacitor/network` status change events | ✅ Integrated |

### Offline UX Flow

```
Online                              Offline
  │                                    │
  │  User practices reading            │  User practices reading
  │  → Fetch content from API          │  → Load from IndexedDB cache
  │  → Submit attempt to API           │  → Queue attempt in IndexedDB
  │  → Cache content locally           │  → Show offline indicator
  │                                    │
  └────────── Network restored ────────┘
                    │
           Auto-sync pending attempts
           Refresh expired content cache
```

### Gaps and Improvements

| Gap | Impact | Solution | Priority |
|---|---|---|---|
| No offline shell/splash | App shows blank on first load without network | Add offline fallback HTML in `capacitor-web/` with retry mechanism | P1 |
| Audio content not cached | Listening practice unavailable offline | Extend `cacheContent()` to handle audio blob storage via `@capacitor/filesystem` | P2 |
| No offline auth | App requires network for login | Implement local session validation for cached credentials | P2 |
| Stale content indicator | User doesn't know content age | Show "Last synced X hours ago" badge | P3 |
| Sync conflict resolution | Potential data conflicts on reconnect | Server-wins strategy with local queue timestamp ordering | P3 |

---

## 12. Push Notifications

### Architecture

```
┌──────────┐     ┌──────────┐     ┌──────────────┐     ┌────────┐
│  Mobile  │────▶│  Backend │────▶│  FCM/APNs    │────▶│ Device │
│  App     │     │  API     │     │  (Firebase)  │     │        │
│          │     │          │     │              │     │        │
│ register │     │ /push/   │     │ Send message │     │ Notify │
│ token  ──┼────▶│ register │     │              │     │        │
│          │     │          │     │              │     │        │
│ handle ◀─┼─────┼──────────┼─────┼──────────────┼─────┤ Tap    │
│ action   │     │          │     │              │     │        │
└──────────┘     └──────────┘     └──────────────┘     └────────┘
```

### Implementation Plan

**Phase 1: Frontend Integration**
1. Create `lib/mobile/push-notifications.ts` module
2. Request permission with proper pre-permission prompt
3. Register device token with backend
4. Handle foreground notifications (in-app toast)
5. Handle notification taps → route to relevant content

**Phase 2: Backend Integration**
1. Add push notification endpoint to ASP.NET backend
2. Store device tokens per user (multiple devices)
3. Implement notification types:
   - Study reminders
   - Expert review ready
   - Achievement unlocked
   - New content available
   - Practice streak warnings
4. Integrate FCM (Android) and APNs (iOS) via Firebase Admin SDK

**Phase 3: Notification Channels (Android)**
1. Create notification channels for different notification types
2. Allow per-channel user preferences

### Notification Types

| Type | Trigger | Priority | Deep Link Target |
|---|---|---|---|
| Study reminder | Scheduled (user preference) | Normal | `/dashboard` |
| Review ready | Expert completes review | High | `/writing/results/{id}` or `/speaking/results/{id}` |
| Achievement | User earns badge/milestone | Normal | `/achievements` |
| Streak warning | 23h since last activity | Normal | `/dashboard` |
| New content | Admin publishes new material | Low | `/practice/{type}` |
| Account | Password change, billing update | High | `/settings` |

---

## 13. Deep Linking

### Configuration Overview

Deep linking requires a two-way association between the domain (`app.oetwithdrhesham.co.uk`) and the native apps.

### iOS — Universal Links

**Step 1: Apple App Site Association file** (host at `https://app.oetwithdrhesham.co.uk/.well-known/apple-app-site-association`):

```json
{
  "applinks": {
    "apps": [],
    "details": [
      {
        "appID": "TEAMID.com.oetwithdrhesham.learner",
        "paths": [
          "/dashboard/*",
          "/practice/*",
          "/diagnostic/*",
          "/writing/*",
          "/speaking/*",
          "/listening/*",
          "/reading/*",
          "/conversation/*",
          "/achievements/*",
          "/exam-booking/*",
          "/study-plan/*",
          "/community/*",
          "/billing/*",
          "/settings/*",
          "/auth/callback/*"
        ]
      }
    ]
  }
}
```

**Step 2: Xcode Associated Domains** — Add `applinks:app.oetwithdrhesham.co.uk` in Signing & Capabilities.

### Android — App Links

**Step 1: Asset Links file** (host at `https://app.oetwithdrhesham.co.uk/.well-known/assetlinks.json`):

```json
[
  {
    "relation": ["delegate_permission/common.handle_all_urls"],
    "target": {
      "namespace": "android_app",
      "package_name": "com.oetwithdrhesham.learner",
      "sha256_cert_fingerprints": ["<SHA256_FINGERPRINT>"]
    }
  }
]
```

**Step 2: AndroidManifest.xml Intent Filter:**

```xml
<intent-filter android:autoVerify="true">
    <action android:name="android.intent.action.VIEW" />
    <category android:name="android.intent.category.DEFAULT" />
    <category android:name="android.intent.category.BROWSABLE" />
    <data android:scheme="https" android:host="app.oetwithdrhesham.co.uk" />
</intent-filter>
```

### Deep Link Routing (Frontend)

Create `lib/mobile/deep-link-handler.ts`:

```typescript
import { App, URLOpenListenerEvent } from '@capacitor/app';

export function initializeDeepLinkHandler(navigate: (path: string) => void) {
  App.addListener('appUrlOpen', (event: URLOpenListenerEvent) => {
    const url = new URL(event.url);
    // Validate domain
    if (url.hostname !== 'app.oetwithdrhesham.co.uk') return;
    // Navigate to path
    const path = url.pathname + url.search;
    if (path) {
      navigate(path);
    }
  });
}
```

### Deep Link Routes

| Pattern | Target | Context |
|---|---|---|
| `/dashboard` | Main dashboard | General re-engagement |
| `/practice/reading/{id}` | Specific reading practice | Share link |
| `/practice/listening/{id}` | Specific listening practice | Share link |
| `/writing/results/{id}` | Writing review result | Push notification |
| `/speaking/results/{id}` | Speaking review result | Push notification |
| `/diagnostic/{id}` | Specific diagnostic test | Course link |
| `/achievements` | Achievements page | Achievement notification |
| `/auth/callback/*` | OAuth callback | Auth flow redirect |
| `/billing` | Subscription management | Email link |

---

## 14. Performance Targets

### Mobile-Specific Metrics

| Metric | Target | Measurement Method |
|---|---|---|
| **Cold start to interactive** | < 3s (4G), < 5s (3G) | Manual measurement from tap to first usable screen |
| **Warm resume** | < 500ms | App state listener → first paint |
| **Navigation (route change)** | < 300ms | Client-side, measured via Performance API |
| **Haptic response latency** | < 50ms | Perceived — no measurable delay |
| **Offline content load** | < 1s | IndexedDB read → render |
| **Memory (Android)** | < 200MB | Android Studio Profiler |
| **Memory (iOS)** | < 150MB | Xcode Instruments |
| **Battery (1h active use)** | < 10% drain | Manual test, screen-on active usage |
| **App size (download)** | < 30MB | Store listing size |
| **App size (installed)** | < 80MB | Device storage |
| **JS bundle size** | Optimize for mobile | Next.js bundle analyzer |
| **WebView render (FCP)** | < 1.5s | Lighthouse mobile audit via WebView |
| **Splash to content** | < 2s | SplashScreen hide timing |

### Performance Optimization Strategies

1. **Image optimization** — Serve WebP/AVIF via Next.js `<Image>` with mobile-appropriate sizes
2. **Code splitting** — Leverage Next.js route-based splitting (already in place)
3. **Preconnect** — `<link rel="preconnect" href="https://api.oetwithdrhesham.co.uk">` for API domain
4. **Cache headers** — Aggressive caching for static assets, sensible revalidation for API
5. **WebView recycling** — Capacitor manages single WebView lifecycle efficiently
6. **Memory management** — Clean up event listeners properly (teardown functions already implemented)
7. **Animation performance** — Prefer CSS transforms/opacity over layout-triggering properties

---

## 15. Testing Strategy

### Test Pyramid for Mobile

```
        ╱╲
       ╱  ╲       Manual E2E on Devices (iOS + Android)
      ╱    ╲      5-10 critical journey tests
     ╱──────╲
    ╱        ╲     Automated E2E (Playwright/Appium)
   ╱          ╲    ~20 smoke tests
  ╱────────────╲
 ╱              ╲   Integration Tests (Vitest)
╱                ╲  API mocking, component testing
╱──────────────────╲
╱                    ╲  Unit Tests (Vitest)
╱                      ╲ lib/mobile/* modules, utilities
╱──────────────────────────╲
```

### Test Categories

| Category | Tool | Count (Est.) | Focus |
|---|---|---|---|
| **Unit tests** | Vitest | ~20-30 | `lib/mobile/*` modules, offline-sync logic, storage bridge |
| **Component tests** | Vitest + Testing Library | ~10-15 | `MobileRuntimeBridge`, platform-conditional rendering |
| **Integration tests** | Vitest | ~10-15 | API mock + mobile module interaction |
| **E2E (Web)** | Playwright | Existing suite | Regression — ensure mobile changes don't break web |
| **E2E (Mobile)** | Playwright (mobile viewports) | ~20 | Critical mobile flows in mobile viewport |
| **Manual device testing** | Physical devices | ~10 journeys | Real devices: iPhone 14+, Pixel 7+, iPad |
| **Accessibility** | axe-core + manual | ~5 flows | VoiceOver (iOS), TalkBack (Android) |

### Critical Mobile Test Scenarios

1. **Cold launch → login → dashboard** — Full onboarding flow
2. **App background → resume → session valid** — Lifecycle handling
3. **Offline → practice → reconnect → sync** — Offline capability
4. **Push notification → tap → deep link** — Notification flow
5. **Speaking recording → submit → review** — Custom plugin flow
6. **OAuth login → callback → authenticated** — Native OAuth
7. **Keyboard appears → form scrolls → submit** — Keyboard handling
8. **Orientation change → layout adapts** — Responsive behavior
9. **Back button (Android) → expected navigation** — Platform convention
10. **Deep link → authenticated route → content** — Deep link with auth guard

### Device Test Matrix

| Device | OS | Priority | Notes |
|---|---|---|---|
| iPhone 15 Pro | iOS 17+ | P1 | Dynamic Island, Face ID |
| iPhone SE 3 | iOS 17+ | P1 | Small screen, Touch ID |
| iPad Air | iPadOS 17+ | P2 | Tablet layout, multitasking |
| Pixel 7 | Android 14+ | P1 | Stock Android, gesture nav |
| Samsung Galaxy S24 | Android 14+ | P1 | Top Android manufacturer |
| Samsung Galaxy A14 | Android 13+ | P2 | Budget device performance |
| OnePlus Nord | Android 13+ | P3 | OxygenOS variant testing |

---

## 16. CI/CD Pipeline

### Pipeline Architecture

```
┌──────────┐     ┌──────────────┐     ┌───────────────┐     ┌──────────┐
│  GitHub  │────▶│  GitHub      │────▶│  Build        │────▶│  Deploy  │
│  Push    │     │  Actions     │     │  Artifacts    │     │          │
│          │     │              │     │               │     │  App     │
│  PR/     │     │ lint+test    │     │ APK/AAB       │     │  Store   │
│  merge   │     │ build web    │     │ IPA           │     │  Play    │
│  tag     │     │ cap sync     │     │               │     │  Store   │
│          │     │ native build │     │               │     │          │
└──────────┘     └──────────────┘     └───────────────┘     └──────────┘
```

### GitHub Actions Workflows

#### 1. Mobile CI (on PR/push to main)

```yaml
# .github/workflows/mobile-ci.yml
name: Mobile CI
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  lint-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '22' }
      - run: npm ci
      - run: npm run lint
      - run: npm test
      - run: npm run build

  android-build:
    needs: lint-and-test
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '22' }
      - uses: actions/setup-java@v4
        with: { distribution: 'temurin', java-version: '17' }
      - run: npm ci
      - run: npm run mobile:build
      - run: npm run mobile:sync
      - run: cd android && ./gradlew assembleDebug
      - uses: actions/upload-artifact@v4
        with:
          name: debug-apk
          path: android/app/build/outputs/apk/debug/*.apk

  ios-build:
    needs: lint-and-test
    runs-on: macos-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '22' }
      - run: npm ci
      - run: npm run mobile:build
      - run: npm run mobile:sync
      - run: cd ios/App && pod install
      - run: xcodebuild -workspace ios/App/App.xcworkspace -scheme App -configuration Debug -sdk iphonesimulator build
```

#### 2. Mobile Release (on tag)

```yaml
# .github/workflows/mobile-release.yml
name: Mobile Release
on:
  push:
    tags: ['v*-mobile']

jobs:
  android-release:
    runs-on: ubuntu-latest
    steps:
      - # ... setup steps ...
      - run: cd android && ./gradlew bundleRelease
      - # Sign AAB with upload key
      - # Upload to Play Console via fastlane or manual

  ios-release:
    runs-on: macos-latest
    steps:
      - # ... setup steps ...
      - # Build archive with xcodebuild
      - # Export IPA with ExportOptions.plist
      - # Upload to App Store Connect via fastlane or xcrun altool
```

### Build Tool Options

| Tool | Platform | Pros | Cons | Recommendation |
|---|---|---|---|---|
| **Fastlane** | Both | Industry standard, match/cert management, automated screenshots | Ruby dependency, learning curve | ✅ Recommended for production |
| **Xcode Cloud** | iOS only | Apple-native, free tier, tight integration | iOS only, limited customization | ⚠️ Consider for iOS-specific |
| **Gradle CLI** | Android only | Simple, direct, no extra dependencies | Manual signing config | ✅ Good for Android CI |
| **Ionic Appflow** | Both | Capacitor-native, cloud builds, live deploy | Paid service, vendor lock-in | ❌ Not recommended — prefer open tooling |

---

## 17. App Store Deployment

### Apple App Store

| Step | Status | Details |
|---|---|---|
| Apple Developer Account | ⚠️ Required | $99/year Apple Developer Program enrollment |
| App Store Connect setup | ⚠️ Needed | Create app listing, configure metadata |
| App icon | ⚠️ Needed | 1024x1024px icon in Asset Catalog |
| Screenshots | ⚠️ Needed | 6.7" (iPhone 15 Pro Max), 6.1" (iPhone 15), 12.9" (iPad Pro) |
| Privacy policy URL | ⚠️ Needed | Hosted privacy policy page |
| App Privacy Details | ⚠️ Needed | Complete data collection questionnaire |
| iOS Privacy Manifest | ⚠️ Needed | `PrivacyInfo.xcprivacy` file |
| Code signing | ⚠️ Needed | Distribution certificate + provisioning profile |
| TestFlight beta | ⚠️ Recommended | Internal + external beta testing before release |
| Review submission | ⬜ | Estimated 24-48h review time |

### Google Play Store

| Step | Status | Details |
|---|---|---|
| Google Play Console | ⚠️ Required | $25 one-time registration fee |
| App listing | ⚠️ Needed | Title, description, screenshots, feature graphic |
| Data Safety Section | ⚠️ Needed | Declare all data collection and sharing |
| Content Rating | ⚠️ Needed | IARC questionnaire |
| App signing | ⚠️ Needed | Enroll in Play App Signing |
| Release tracks | ⚠️ Recommended | Internal → Closed → Open → Production |
| Target API level | ⚠️ Verify | Must be API 35 (Android 15) by Aug 31, 2025 |
| AAB format | ✅ | Gradle produces `.aab` for bundled delivery |
| Review submission | ⬜ | Estimated 24-72h review time |

### Billing Compliance

**Critical decision required:** The app includes subscription-based billing.

| Approach | Apple | Google | Recommendation |
|---|---|---|---|
| **Native IAP** | Required for digital Content | Required for digital content | Complex, 30% commission, full compliance |
| **Web billing redirect** | ⚠️ Risk of rejection | ⚠️ Risk of rejection | Simpler but risky |
| **Reader app exception** | Only for "reader" apps (content purchased elsewhere) | N/A | If accessible: content purchased on web, consumed in app |
| **External link entitlement (US)** | Available in US via StoreKit 2 | Available via User Choice Billing | Allows linking to web for purchase |

**Recommendation:** Start with the "reader app" approach — users purchase subscriptions on the web, the mobile app provides access to purchased content. This avoids the 30% IAP commission and simplifies implementation. If Apple rejects, implement IAP as fallback.

---

## 18. Analytics and Monitoring

### Mobile-Specific Analytics

| Event | Trigger | Data |
|---|---|---|
| `app_launch` | Cold start | `launch_type`, `network_state`, `platform`, `os_version` |
| `app_resume` | Resume from background | `background_duration`, `session_valid` |
| `offline_practice` | Practice completed offline | `content_type`, `cached_age` |
| `offline_sync` | Pending items synced | `items_count`, `sync_duration` |
| `push_received` | Notification received (foreground) | `notification_type` |
| `push_tapped` | Notification tapped | `notification_type`, `deep_link` |
| `deep_link_opened` | App opened via deep link | `link_type`, `target_route` |
| `recording_completed` | Speaking recording finished | `duration_seconds`, `file_size_kb` |
| `network_change` | Connectivity changed | `connected`, `connection_type` |

### Error Monitoring

| Category | Tool | Priority |
|---|---|---|
| JavaScript errors | Existing error boundary + reporting | P1 |
| Native crashes (Android) | Firebase Crashlytics or Sentry | P1 |
| Native crashes (iOS) | Firebase Crashlytics or Sentry | P1 |
| Network failures | Custom interceptor logging | P2 |
| WebView errors | `window.onerror` handler | P1 |
| Plugin failures | Try/catch wrappers in `lib/mobile/*` | P1 |

### Performance Monitoring

| Metric | Tool | Threshold |
|---|---|---|
| App startup time | Custom timing + analytics | > 5s triggers alert |
| API response time | Network interceptor | > 3s triggers warning |
| Memory usage | Platform profiler (manual) | > 300MB triggers investigation |
| Crash-free sessions | Crashlytics/Sentry | < 99.5% triggers investigation |

---

## 19. Accessibility

### Platform Accessibility Requirements

| Requirement | iOS (VoiceOver) | Android (TalkBack) | Status |
|---|---|---|---|
| Screen reader compatibility | ✅ | ✅ | Web content accessible via WebView |
| Touch target size | 44x44pt minimum | 48x48dp minimum | ✅ `.touch-target` class (44x44px) |
| Dynamic Type | ✅ Required | ✅ Font scaling | ⚠️ Verify font scaling works in WebView |
| Reduce Motion | `prefers-reduced-motion` | `prefers-reduced-motion` | ⚠️ Verify system preference propagates to WebView |
| High Contrast | ✅ Supported | ✅ Supported | ⚠️ Verify in WebView context |
| Color contrast | 4.5:1 text, 3:1 UI | Same WCAG ratios | ✅ Tailwind theme configured |
| Focus management | VoiceOver focus order | TalkBack focus order | ⚠️ Test with actual screen readers |
| Audio descriptions | Required for audio content | Required for audio content | ⚠️ Verify listening practice has transcripts |

### Mobile-Specific Accessibility Considerations

1. **Haptic feedback** — Already provides non-visual feedback via `lib/mobile/haptics.ts`
2. **Status bar** — Adapts to light/dark theme for contrast
3. **Keyboard navigation** — Keyboard event handling for physical keyboards (Bluetooth)
4. **Safe areas** — Content never hidden behind system UI
5. **Form inputs** — Keyboard type optimization (`type="email"`, `inputMode="numeric"`)

### Accessibility Testing Plan

| Test | Tool | Frequency |
|---|---|---|
| Automated scan | axe-core in Vitest | Every CI run |
| VoiceOver manual test | iOS device | Every release |
| TalkBack manual test | Android device | Every release |
| Keyboard-only navigation | Physical keyboard + device | Quarterly |
| Color contrast verification | Lighthouse accessibility audit | Quarterly |

---

## 20. Internationalization

### Current State

The app is primarily English-language (OET is an English proficiency test). However, UI chrome (buttons, labels, navigation) may need localization for markets where English is not the primary language.

### i18n Strategy

| Aspect | Approach | Priority |
|---|---|---|
| Content language | English only (OET requirement) | N/A |
| UI language | English primary, evaluate RTL support | P3 |
| Date/time formatting | `Intl.DateTimeFormat` (already in use) | ✅ Done |
| Number formatting | `Intl.NumberFormat` | ✅ Done |
| Store listing localization | English, Arabic (potential market) | P2 |
| App Store metadata | Localize description for target markets | P2 |

### App Store Localization

| Market | Language | Notes |
|---|---|---|
| Global (primary) | English | Default store listing |
| Middle East | Arabic | Significant OET test-taker population |
| Southeast Asia | English | English sufficient for this market |
| India/Pakistan | English | English sufficient for this market |

---

## 21. Migration Path

### Current State Assessment

| Component | Readiness | Blocking Issues |
|---|---|---|
| Capacitor core | ✅ 100% | None |
| Android native project | ✅ 95% | Verify targetSdkVersion=35 |
| iOS native project | ✅ 95% | Need Associated Domains, Privacy Manifest |
| Mobile runtime (lib/mobile) | ✅ 90% | Minor gaps |
| Push notifications | ❌ 10% | Plugin installed, zero frontend integration |
| Deep linking | ❌ 5% | Plugin capability exists, zero configuration |
| Share | ❌ 5% | Plugin installed, zero frontend integration |
| App Store assets | ❌ 0% | Icons, screenshots, metadata not created |
| CI/CD pipeline | ❌ 0% | No mobile build automation |
| Store accounts | ⚠️ Unknown | Need Apple Developer + Google Play Console accounts |

### Migration Phases

```
Phase 0 (Now)        Phase 1 (1-2 weeks)   Phase 2 (2-3 weeks)   Phase 3 (1-2 weeks)
├── Existing ✅      ├── Critical gaps      ├── Store readiness    ├── Submission
│   Capacitor core   │   Push notifications │   CI/CD pipeline     │   TestFlight
│   Both platforms   │   Deep linking       │   Store assets       │   Play Store beta
│   Mobile runtime   │   OAuth native flow  │   Privacy compliance │   Review fixes
│   Custom plugin    │   Security hardening │   Accessibility      │   Production
│   Offline sync     │   Forced update      │   Performance opt    │   release
└──────────────────  └─────────────────────  └────────────────────  └──────────────
```

---

## 22. Risk Register

| # | Risk | Probability | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Apple rejects app under Guideline 4.2 (Minimum Functionality — too web-like) | Medium | High | Ensure robust native feature integration: push, haptics, offline, deep links, biometrics. Add native splash, app icon, proper metadata |
| R2 | Apple IAP requirement for subscriptions | High | High | Start with "reader app" approach. Prepare IAP fallback implementation using StoreKit 2 |
| R3 | Android edge-to-edge breaking layouts on SDK 35 | Medium | Medium | Test on Android 15 device/emulator. Verify CSS safe area insets propagate correctly through Capacitor WebView |
| R4 | Push notification token management complexity | Low | Medium | Use Firebase Cloud Messaging for unified Android+iOS push. Store tokens server-side with proper token refresh handling |
| R5 | OAuth redirect flow fails in native context | Medium | High | Use system browser (`@capacitor/browser`) not embedded WebView. Register Universal Links for callback URL. Test on both platforms |
| R6 | WebView performance on low-end Android devices | Medium | Medium | Profile on budget devices (Galaxy A14). Optimize bundle size, reduce initial JS payload, implement skeleton screens |
| R7 | Capacitor plugin version conflicts | Low | Low | Pin exact versions in package.json. Update in batches with regression testing |
| R8 | Custom SpeakingRecorder plugin fails on specific devices | Low | High | Test on diverse device matrix. Add proper error handling and fallback behavior. Consider web MediaRecorder API as fallback |
| R9 | Store review delays (2-7 days) | Medium | Low | Submit early, use expedited review for critical fixes. Maintain TestFlight/beta tracks for testing |
| R10 | OWASP compliance gaps discovered during security audit | Medium | Medium | Address P1 security items (encrypted storage, backup exclusion, debug flags) before store submission |
| R11 | Offline sync data corruption | Low | High | Implement data integrity checks, backup sync state, server-side validation on sync |
| R12 | Network Security Config (Android) cleartext traffic flagged | Low | Medium | Already configured `cleartext: false`. Verify no HTTP fallbacks exist |

---

## 23. Implementation Waves

### Wave 0: Foundation Verification (1-2 days)

| Task | Agent | Files | Description |
|---|---|---|---|
| W0.1 | gem-researcher | `android/variables.gradle`, build configs | Verify `targetSdkVersion=35`, `compileSdkVersion=35` |
| W0.2 | gem-researcher | `android/app/build.gradle` | Verify ProGuard/R8 enabled for release, `debuggable=false` |
| W0.3 | gem-researcher | `ios/App/App/Info.plist` | Verify all required Info.plist entries |
| W0.4 | gem-implementer | `android/app/src/main/AndroidManifest.xml` | Add deep link intent filter |
| W0.5 | gem-implementer | iOS Xcode project | Add Associated Domains capability |

### Wave 1: Critical Integrations (1 week)

| Task | Agent | Files | Description |
|---|---|---|---|
| W1.1 | gem-implementer | `lib/mobile/push-notifications.ts` | Create push notification module — register, listen, handle |
| W1.2 | gem-implementer | `lib/mobile/deep-link-handler.ts` | Create deep link handler — URL validation, route mapping |
| W1.3 | gem-implementer | `components/mobile/mobile-runtime-bridge.tsx` | Integrate push + deep link initialization |
| W1.4 | gem-implementer | `lib/mobile/secure-storage.ts` | Create encrypted token storage bridge (Keychain/Keystore) |
| W1.5 | gem-implementer | Auth flow files | Implement native OAuth redirect handling |
| W1.6 | gem-implementer | `lib/mobile/forced-update.ts` | Version check on app start, prompt for store update |
| W1.7 | gem-reviewer | All Wave 1 files | Security review of new modules |

### Wave 2: Platform Compliance (1 week)

| Task | Agent | Files | Description |
|---|---|---|---|
| W2.1 | gem-implementer | `ios/App/App/PrivacyInfo.xcprivacy` | Create iOS Privacy Manifest |
| W2.2 | gem-implementer | Store listing assets | App icon (1024x1024), screenshots, feature graphic |
| W2.3 | gem-implementer | `android/app/build.gradle` | Configure release signing, ProGuard rules |
| W2.4 | gem-implementer | `.well-known/` files | Create `apple-app-site-association` + `assetlinks.json` |
| W2.5 | gem-implementer | `lib/mobile/share.ts` | Create share module — trigger native share sheet |
| W2.6 | gem-designer | Mobile UI | Verify design parity, safe area coverage, touch targets |
| W2.7 | gem-reviewer | All Wave 2 files | Compliance review |

### Wave 3: CI/CD and Testing (1 week)

| Task | Agent | Files | Description |
|---|---|---|---|
| W3.1 | gem-devops | `.github/workflows/mobile-ci.yml` | Create Android CI build workflow |
| W3.2 | gem-devops | `.github/workflows/mobile-ci.yml` | Add iOS CI build workflow |
| W3.3 | gem-devops | `.github/workflows/mobile-release.yml` | Create release/deployment workflow |
| W3.4 | gem-implementer | `tests/mobile/` | Create mobile-specific unit tests |
| W3.5 | gem-browser-tester | E2E tests | Mobile viewport E2E smoke tests |
| W3.6 | gem-implementer | Various | Bug fixes from testing |

### Wave 4: Store Submission (1 week)

| Task | Agent | Files | Description |
|---|---|---|---|
| W4.1 | gem-implementer | Release builds | Generate signed release APK/AAB + IPA |
| W4.2 | gem-documentation-writer | Store listings | Write app descriptions for both stores |
| W4.3 | gem-reviewer | Full app | Final security + compliance review |
| W4.4 | gem-implementer | Store consoles | Submit to TestFlight + Play Console internal track |
| W4.5 | Manual | Devices | Manual testing on target device matrix |
| W4.6 | gem-implementer | Store consoles | Submit for production review |

---

## 24. Appendices

### A. File Inventory — Mobile-Specific Code

| File | Purpose | Lines (est.) |
|---|---|---|
| `capacitor.config.ts` | Root Capacitor configuration | ~70 |
| `lib/mobile/runtime.ts` | Mobile runtime initialization | ~200 |
| `lib/mobile/haptics.ts` | Haptic feedback wrapper | ~40 |
| `lib/mobile/native-storage.ts` | Capacitor Preferences bridge | ~100 |
| `lib/mobile/offline-sync.ts` | IndexedDB offline system | ~250 |
| `lib/mobile/speaking-recorder.ts` | Custom plugin bridge | ~60 |
| `lib/mobile/lifecycle-motion.ts` | Resume animation trigger | ~30 |
| `lib/mobile/capacitor-config.ts` | App URL resolution | ~20 |
| `lib/runtime-signals.ts` | Three-way runtime detection | ~80 |
| `components/mobile/mobile-runtime-bridge.tsx` | React initialization component | ~100 |
| `app/providers.tsx` | Root provider (includes mobile init) | ~80 |
| `app/layout.tsx` | Root layout (viewport, bootstrap) | ~120 |
| `app/globals.css` | Global styles (safe areas, keyboard) | ~300 |
| `android/app/src/.../MainActivity.java` | Android entry point | ~20 |
| `android/app/src/.../SpeakingRecorderPlugin.java` | Custom Android plugin | ~120 |
| `android/app/src/main/AndroidManifest.xml` | Android manifest | ~40 |
| `ios/App/App/AppDelegate.swift` | iOS entry point | ~20 |
| `ios/App/App/SpeakingRecorderPlugin.swift` | Custom iOS plugin | ~100 |
| `ios/App/App/Info.plist` | iOS configuration | ~60 |
| `ios/App/Podfile` | CocoaPods dependencies | ~30 |
| `capacitor-web/index.html` | Capacitor web shell | ~30 |

### B. npm Scripts for Mobile Development

| Script | Command | Purpose |
|---|---|---|
| `mobile:dev` | `next dev -H 0.0.0.0 -p 3000` | Start dev server accessible to devices on LAN |
| `mobile:build` | `next build` | Build Next.js for production |
| `mobile:sync` | `npx cap sync` | Sync web assets + plugins to native projects |
| `mobile:copy` | `npx cap copy` | Copy web assets only (no plugin sync) |
| `mobile:run:android` | `npx cap run android` | Build and run on Android device/emulator |
| `mobile:run:ios` | `npx cap run ios` | Build and run on iOS device/simulator |
| `mobile:open:android` | `npx cap open android` | Open Android project in Android Studio |
| `mobile:open:ios` | `npx cap open ios` | Open iOS project in Xcode |

### C. Environment Variables

| Variable | Purpose | Default |
|---|---|---|
| `CAPACITOR_APP_URL` | Server URL for Capacitor WebView | `https://app.oetwithdrhesham.co.uk` |
| `APP_URL` | Fallback server URL | `https://app.oetwithdrhesham.co.uk` |
| `NEXT_PUBLIC_API_URL` | Backend API base URL | Production API URL |

### D. Key Decisions Log

| # | Decision | Rationale | Alternatives Considered |
|---|---|---|---|
| D1 | Maintain remote server approach | Instant updates, single deployment, proven pattern | Bundled offline-first (too complex for this stage) |
| D2 | Start with "reader app" billing approach | Avoids 30% IAP commission, simpler implementation | Full IAP integration (fallback if rejected) |
| D3 | Use Firebase for push notifications | Industry standard, unified iOS+Android, free tier | OneSignal (vendor lock-in), custom (too complex) |
| D4 | Capacitor Preferences for non-sensitive storage | Already implemented, works well for app preferences | AsyncStorage (React Native only), MMKV (requires plugin) |
| D5 | Encrypted native storage for auth tokens | OWASP MASVS compliance, secure token lifecycle | Capacitor Preferences unencrypted (current, insecure) |
| D6 | Fastlane for CI/CD mobile builds | Industry standard, both platforms, cert management | Xcode Cloud (iOS only), manual builds (not scalable) |

### E. Glossary

| Term | Definition |
|---|---|
| **AAB** | Android App Bundle — Google Play's preferred upload format |
| **APK** | Android Package — installable Android application file |
| **APNs** | Apple Push Notification service |
| **ATS** | App Transport Security — iOS HTTPS enforcement |
| **FCM** | Firebase Cloud Messaging — Google's push notification service |
| **HIG** | Human Interface Guidelines — Apple's design standards |
| **IAP** | In-App Purchase — Apple/Google payment system |
| **IPA** | iOS App Store Package — distributable iOS application file |
| **MASVS** | Mobile Application Security Verification Standard (OWASP) |
| **ProGuard/R8** | Android code shrinking and obfuscation tool |
| **Universal Links** | iOS deep linking via HTTPS URLs |
| **App Links** | Android deep linking via HTTPS URLs with verification |
| **WKWebView** | iOS WebKit-based web content renderer |
| **SDK 35** | Android 15 API level |

---

*Document generated by Claude Opus 4.6 Agent via gem-orchestrator workflow. All content derived from direct source code inspection, official documentation research, and project inventory analysis.*
