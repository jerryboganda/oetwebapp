# OET Prep Quick Fixes v2 Design

**Date:** 2026-08-08

**Source:** `C:\Users\Dr Faisal Maqsood PC\Downloads\oet_prep_quick_fixes_v2.pdf`

## Goal

Deliver the PDF's three requested user-visible fixes: mobile-safe video fullscreen, a working bottom-right video stretch/widen control, consistent Windows/macOS/Android/iPhone download buttons, and a temporary direct iOS download path before App Store publication.

## Requirements

1. The protected course player must fill the complete mobile viewport when fullscreen is entered. The first-party player container remains the fullscreen element so the identifying watermark continues to cover the provider iframe.
2. The video stretch/widen control must work on mobile and laptop, be available for secure-embed and legacy direct-HLS playback, and remain clearly positioned at the bottom right.
3. The Windows, macOS, Android, and iPhone/iPad download CTAs on `/get-app` must share the same badge height, shape, spacing, width treatment, and alignment. The Google Play badge geometry is the visual reference.
4. iOS download behavior must use the configured App Store URL when one exists. Until then it must use a direct iOS release resolver that selects only a trusted published GitHub `.ipa` asset. If no IPA has been published yet, the resolver may fall back to the public releases page; it must never point users back to the generic `/get-app` page while claiming to be a direct download.

## Existing boundaries

- `components/videos/video-player.tsx` owns the first-party fullscreen container, media controls, and watermark layering.
- `components/videos/secure-embed-player.tsx` owns the Bunny iframe and must continue to render the provider inside the first-party container.
- `app/api/download/[platform]/route.ts` already resolves versioned desktop/Android release assets and validates the GitHub download source.
- `lib/app-downloads.ts` is the canonical client-side download destination module.
- `components/marketing/store-badges.tsx`, `components/marketing/app-download-promo.tsx`, and `app/get-app/page.tsx` are the shared and public download surfaces.
- No secrets, `.env` files, certificates, or production credentials are changed.

## Design

### Video display and controls

The player adds a scoped `oet-video-player` class and uses fullscreen CSS on that element: `width: 100vw`, `height: 100dvh`, and zero radius/flow constraints. The media element and iframe remain `height: 100%` and `width: 100%`, with `object-contain` as the default presentation and `object-cover` when the learner activates the stretch control. The fullscreen and fit state are tracked from standard and WebKit/Mozilla fullscreen events. The request/exit helpers attempt the corresponding vendor methods when a browser or WebView does not expose the standard method.

The stretch control is an explicit button with an accessible label, title, pressed state, and `StretchHorizontal`/`Minimize2` icon. Secure embeds receive it as an absolutely positioned bottom-right overlay. Legacy direct-HLS controls render it next to fullscreen in the bottom-right control cluster. The watermark stays above both media modes and above the controls.

The secure iframe also receives `allowFullScreen` and `fullscreen` in its `allow` policy so supported provider/WebView implementations can honor presentation requests without exposing raw media URLs.

### Download badge consistency

The public `/get-app` four-card grid uses the same non-compact badge geometry for all platforms and applies one shared full-width/max-width alignment class. The banner uses the same compact geometry and a shared fixed width at larger breakpoints. Labels and destinations remain platform-specific, but the outer button dimensions and spacing are identical.

### Temporary iOS delivery

The existing trusted GitHub release resolver is extended with an iOS `.ipa` matcher. `/api/download/ios` redirects only to an HTTPS GitHub release asset under `jerryboganda/oetwebapp/releases/download/`. `IOS_DOWNLOAD_URL` prefers `NEXT_PUBLIC_IOS_APP_STORE_URL`, then uses `/api/download/ios`; it no longer falls back to `/get-app`. The badge copy distinguishes an App Store destination from a temporary direct iOS download. If the current release inventory has no IPA, the resolver's controlled releases-page fallback is the explicit external boundary until a signed IPA is published.

## Error handling and security

- Fullscreen API failures remain non-fatal and leave the player in its current state.
- Stretch state is purely presentation state and cannot bypass entitlement, attestation, session, watermark, iframe sandbox, or playback-token checks.
- The iOS resolver rejects untrusted hosts and untrusted paths and skips draft/prerelease releases.
- Direct download fallback never embeds a token, credential, or customer data in client code.

## Verification

- Unit-test the secure/legacy player controls and fullscreen state transitions with mocked fullscreen APIs.
- Test that the app-download banner exposes all three shared actionable badge links and that the iOS destination is the direct resolver when no App Store URL is configured.
- Test the iOS release resolver's trusted `.ipa` selection and rejection/fallback behavior.
- Run the smallest relevant Vitest command and `git diff --check`; report any platform/device or missing-IPA boundary separately.
