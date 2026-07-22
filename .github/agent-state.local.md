# Agent State - Native App Update Hardening

Last updated: 2026-07-22

## Goal

Make desktop and mobile update discovery, download, and installation reliable and fail-safe while preserving the product name `OET with Dr. Hesham`.

## Implemented

- Added a stable, validated desktop updater feed backed by signed GitHub releases, with a GitHub fallback endpoint.
- Deduplicated desktop checks/installs so automatic and forced-update flows cannot race.
- Added automatic mobile checks on launch, resume, reconnect, and a four-hour interval.
- Added trusted native-release discovery and a signed Android APK fallback when Google Play in-app update is unavailable.
- Wired the Capacitor app-update plugin into Android and iOS native projects.
- Made missing update targets visible errors instead of silent no-ops.
- Updated desktop and Android release workflows to publish and verify their updater artifacts.

## Validation

- Focused updater tests: 14 passed.
- `pnpm exec tsc --noEmit`: passed.
- `git diff --check`: passed.

## External Requirement

iOS App Store distribution still requires Apple signing/App Store Connect credentials and a live listing; those repository secrets are not configured. The iOS app now contains the update plugin and automatic detection path, but an installable iOS release cannot be truthfully published until those credentials exist.

## Next Step

Push `main`, publish fresh signed Windows and Android releases, then verify the production updater endpoints and release workflows.
