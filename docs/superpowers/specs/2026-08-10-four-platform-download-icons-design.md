# Four-platform app-download icon system

## Goal

Replace every app-download control's current generic or grouped icon treatment with the supplied four-platform visual system across all web-rendered surfaces: Windows, Mac, Google Play, and App Store.

## Design

- Centralize the four brand marks in `components/marketing/store-badges.tsx` as reusable inline SVG glyphs.
- Use one shared black button shell with a tall fixed height, rounded corners, white text, centered icon/text group, and consistent icon scale.
- Use the Windows four-pane mark, Apple mark for Mac, colored Google Play triangle, and App Store mark respectively. App-download surfaces must not use generic `Monitor`, `Laptop`, `Smartphone`, or plain `Apple` icons.
- Expose a shared four-platform group for the compact banner, modal, and card variants in `AppDownloadPromo`, and use the same platform badges in `/get-app`.
- Keep current destination behavior: `/get-app` retains its direct Windows, Mac, Android-install, and iOS resolver/store links; existing grouped promo destinations remain unchanged when split into their four visual buttons.
- Keep the four buttons responsive: a two-column grid where space allows, one column on narrow screens, with equal widths and no hidden platform.

## Scope and boundaries

The desktop Tauri shell and Capacitor Android/iOS shells render this frontend's download surfaces; no separate download-button implementation exists in their native trees. Native launcher icons and splash assets are outside this change and must not be altered.

## Verification

- Update focused component/page tests to assert four platform links and shared badge geometry.
- Run the focused Vitest tests for the affected marketing/auth/get-app surfaces.
- Audit the repository for app-download surfaces that still render the replaced generic or grouped icons, and run `git diff --check`.
