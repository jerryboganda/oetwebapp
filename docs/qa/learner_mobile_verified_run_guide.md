# Learner Mobile Verified Run Guide

## Prerequisites

- Install the project dependencies.
- On Windows, use `cmd /c` for npm scripts to avoid PowerShell policy issues with `npm.ps1`.
- Install Android Studio for Android device or emulator runs.
- Install Xcode on macOS for iOS device or simulator runs.

## Local Web Run

1. Install dependencies:

   ```bash
   cmd /c npm install
   ```

2. Start the mobile-friendly dev server:

   ```bash
   cmd /c npm run mobile:dev
   ```

3. Open `http://localhost:3000` in a browser.

## Capacitor Sync

1. Rebuild the web app and sync native projects:

   ```bash
   cmd /c npm run mobile:build
   ```

   Set `APP_URL` to the deployed Next.js origin before packaging. `CAPACITOR_APP_URL` is also accepted. If neither is set, the Capacitor config falls back to `https://app.example.com` instead of shipping the scaffold page, but real deployments should still override the origin explicitly.

2. If you only need to refresh the native project files, run:

   ```bash
   cmd /c npm run mobile:sync
   ```

## Android Run

1. Open the Android project in Android Studio:

   ```bash
   cmd /c npm run mobile:open:android
   ```

2. From Android Studio, choose an emulator or connected device and run the app.

3. Or launch directly from the CLI:

   ```bash
   cmd /c npm run mobile:run:android
   ```

4. Verify the speaking check page, diagnostic speaking page, and live speaking task with microphone permission granted.

## iOS Run

1. Open the iOS project in Xcode:

   ```bash
   cmd /c npm run mobile:open:ios
   ```

2. From Xcode, choose a simulator or connected device and run the app.

3. Or launch directly from the CLI:

   ```bash
   cmd /c npm run mobile:run:ios
   ```

4. Verify microphone permission prompts and audio capture behavior on the device.

## What to Verify

- Sign in and resume after backgrounding the app.
- Dashboard and navigation on a small mobile viewport.
- Reading, listening, writing, and study-plan navigation.
- Speaking check, diagnostic speaking, and live speaking task recording.
- Safe-area behavior, keyboard behavior, and status-bar presentation.

## Suggested Order

1. Run `cmd /c npm run build`.
2. Run `cmd /c npm run mobile:build`.
3. Open the native project.
4. Test speaking capture first.
5. Re-run the core learner routes once the speaking flow is confirmed.
