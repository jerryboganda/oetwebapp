# TESTER_SETUP.md — Installing & Testing OET Prep Learner

> **Status:** skeleton — finalized in Phase 8–9 with real download links, version, and known issues.

## What you're testing
The **OET Prep Learner** mobile app. It loads the OET Prep web experience inside a native app shell and adds mobile features (microphone recording for Speaking, biometric login, notifications, deep links). This build points at: **`<STAGING/TEST URL — TBD>`**.

## System requirements
- **Android:** **5.1 (Lollipop) or newer** (minSdk 22). A current device (Android 12–15) is recommended. ~60–100 MB free storage. A network connection is required (the app loads the live site).
- **iOS:** _not available this round_ — see "iOS status" below.

## Android — install the APK (direct sideload)
1. Download the APK: **`<LINK — TBD (CI artifact or shared file)>`**.
2. On your phone, open the downloaded file. Android will warn that it's from an unknown source.
3. Allow installation: **Settings → Apps → Special access → Install unknown apps → [your browser/file manager] → Allow from this source** (wording varies by device/Android version).
4. Tap the APK again → **Install** → **Open**.
5. On first launch, grant permissions when prompted (microphone is required for Speaking practice).

### Optional managed channel — Firebase App Distribution
If invited by email: accept the invite → install the **App Tester** app → install OET Prep Learner from there. (Decided in Phase 8; instructions finalized if used.)

## iOS status (this round)
iOS testing is **not available yet**: putting a signed build on a tester's iPhone requires a paid **Apple Developer Program** account ($99/yr) for TestFlight or ad-hoc distribution. Free Apple-ID signing only runs on the developer's own device and expires after 7 days — it cannot be shared. **To unblock iOS testing:** enroll in the Apple Developer Program, then we distribute via TestFlight. (See `QA_REPORT.md` Phase 8.)

## How to report bugs
Please include, for every issue:
- **Device + OS version** (e.g., "Pixel 6a, Android 14").
- **What you did** (steps), **what you expected**, **what happened**.
- A **screenshot or screen recording** if possible.
- Time it happened (helps match server logs).
- Report to: **`<channel — TBD (email / form / chat)>`**.

## Known issues
- _Populated from `BUGLOG.md` (Medium/Low items shipped with the test build) before release._
