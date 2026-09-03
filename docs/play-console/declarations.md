# Play Policy declarations — how to answer (verify against the submitted build)

## Support / website
- Store listing contact details: website `https://www.oetwithdrhesham.co.uk`, support email `support@oetwithdrhesham.co.uk` (actively monitored inbox — do NOT use an unmonitored or personal address).

## Privacy Policy
- Play Console Privacy Policy URL + in-app access (`/privacy` linked from `/support`, Settings, Terms). Must be a PUBLIC WEB PAGE, not a PDF.
- Must identify the app/developer (`OET with Dr Ahmed Hesham`), data collected, use/sharing, security, retention/deletion, and a privacy contact (`dpo@oetwithdrhesham.co.uk`, `support@oetwithdrhesham.co.uk`, ICO reference). In-app route: `app/(auth)/privacy/page.tsx` (UK GDPR, last updated — bump to submission date).

## Account deletion
- In-app: `Settings > Delete Account` → confirm password + optional reason → 30-day grace → `POST /v1/auth/account/delete` → sign out. During grace contact support to cancel.
- Web: public `/account-deletion` page (same-address email request, verification, acknowledgement within 7 days, billing 2 business days). Enter that URL in Play Console.
- Warning: merely disabling/freezing an account does NOT satisfy Google. The flow must allow requesting deletion of the account AND associated data, subject only to clearly disclosed legitimate retention (e.g. 7-year billing records, 24-month post-closure account window).

## Ads
- No third-party advertising SDK in the app (mobile launch uses web checkout only; `validate-mobile-release-inputs.mjs` fails if RevenueCat native IAP is installed). Declare Ads = No. Purchases/subscriptions are not third-party ads.

## Target audience / content
- Designed for healthcare professionals / adult exam candidates (Terms: 16+, Privacy: not for under-16s, website: adult candidates). Do NOT select child audiences unless genuinely designed for them AND Families requirements are met.

## Content rating
- Complete the IARC questionnaire with accurate answers for the CURRENT build (Education, no child targeting, audio recording with consent, no ads).

## Other declarations (complete any that appear)
- Permissions: justify RECORD_AUDIO / CAMERA / READ_MEDIA_AUDIO / POST_NOTIFICATIONS / biometric from actual runtime use.
- Financial: Stripe/PayPal/Whop/Fawaterak via secure WEBSITE checkout (no native IAP in this launch) — answer subscriptions/purchases truthfully, distinct from ads.
- Health-related data: learning/audio/progress data as described in Privacy + Data Safety — disclose accurately.
