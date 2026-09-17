# iOS Purchase Compliance — No In-App Purchase Model

**Decision (owner, 17 Sep 2026):** the iOS app sells nothing. This document is
the compliance record for App Store guideline 3.1.1 and is referenced from the
App Review notes.

## The model

| Surface | iOS behaviour |
| --- | --- |
| Enrolments / course purchases | Happen on the marketing website (`oetwithdrhesham.co.uk`) via Stripe. Never inside the app. |
| Existing subscribers | Sign in with their existing account and access already-entitled content — no re-purchase, no IAP bridge. |
| Cart / pricing / checkout routes in the iOS shell | Replaced by an "enrol on our website" notice (see below). |
| Account/billing management (`/account/billing`, payment methods) | Remains available: managing an existing subscription is account management, not a digital purchase. |

## Enforcement in the app

`components/compliance/ios-purchase-gate.tsx` detects the iOS shell
(`getAppRuntimeKind() === 'capacitor-native'` + `getCapacitorPlatform() === 'ios'`,
from `lib/runtime-signals.ts`) and swaps the wrapped UI for a notice that
points candidates at the website. Wrapped via route layouts:

- `app/cart/layout.tsx`
- `app/pricing/layout.tsx`
- `app/catalog/layout.tsx`
- `app/ai-packages/layout.tsx`
- `app/checkout/review/layout.tsx`

Post-purchase informational routes (`/checkout/success`, `/checkout/cancel`)
and account-billing management are deliberately **not** gated.

Android, web and desktop render the real UI — this gate never touches the
live Android checkout-in-browser flow. The preflight validator
(`scripts/qa/validate-mobile-release-inputs.mjs`) also fails the release if a
RevenueCat/native-IAP dependency ever appears.

## Review answers

- **Guideline 3.1.1 (In-App Purchase):** no digital content is purchased
  inside the app; there are no buy buttons, price tags or checkout flows in
  the iOS binary.
- **Guideline 3.1.2 / external links:** the notice opens the marketing site in
  the system browser via `openURL` — standard behaviour, no StoreKit, no
  external-purchase entitlement needed.
- **Guideline 5.1.1(v):** account deletion is available from within the app
  (Settings → Account Deletion, OTP-confirmed).

## Related evidence

- App Privacy answers and age rating: `fastlane/metadata/ios/APP-STORE-LISTING-NOTES.md`
- Review notes template: `fastlane/metadata/ios/en-US/review_information/review_notes.txt`
