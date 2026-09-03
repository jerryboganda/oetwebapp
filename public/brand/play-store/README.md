# Play Store graphic assets — OET with Dr Ahmed Hesham

Exported from original source artwork (`../oet-square-logo.png` 2000×2000), never from a phone screenshot.

| File | Spec | Status |
|---|---|---|
| `play-store-icon-512.png` | 512×512, 32-bit PNG with alpha, 189 KB (limit 1024 KB) | Upload in Play Console > Main store listing (JOB 1 of 2) |
| `feature-graphic-1024x500.png` / `.jpg` | 1024×500, RGB without alpha (JPG 57 KB) | Upload in Play Console > Main store listing |
| Android launcher (`android/app/src/main/res/mipmap-*/ic_launcher*.png`) | mdpi 48, hdpi 72, xhdpi 96, xxhdpi 144, xxxhdpi 192 + adaptive foreground (108/162/216/324/432) + round, white background | Embedded in the AAB (JOB 2 of 2 — Play Store icon does NOT replace this) |
| `public/icon-192.png`, `public/icon-512.png`, `public/icon-maskable-512.png` | PWA icons refreshed from the same source | Web/PWA consistency |

Feature graphic: navy→purple gradient, white card with crest, text `OET / with Dr Ahmed Hesham / OET preparation for healthcare professionals`. Minimal text, NO #1/best/guaranteed-pass/discount/price/rank claims (policy-safe).

Screenshots are NOT stored here — capture 6–8 from the final corrected release build per `docs/play-console/screenshot-plan.md` (`01_Dashboard…08_Progress`, 1080×1920 portrait). Do NOT reuse pre-fix shots.
