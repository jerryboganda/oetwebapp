# App Store Listing Notes — iOS (`com.oetprep.learner`)

Answers for the App Store Connect submission forms, kept next to the
`fastlane/metadata/ios/en-US/` copy so the listing, privacy answers and review
materials stay in one place. Apply manually in App Store Connect or via
`fastlane deliver` once ASC access works (currently blocked on the owner's
fresh Team Keys — see `.tools-state/apple-api-keys/README.md`).

## App Information

| Field | Value |
| --- | --- |
| Name | OET with Dr Ahmed Hesham |
| Subtitle | OET prep for healthcare pros |
| Primary category | Education |
| Secondary category | Medical (reference/education, not medical advice) |
| Bundle ID | `com.oetprep.learner` |
| Minimum iOS | 16.4 (see `apple-compatibility.json`; enforced by CI guards) |
| Copyright | © 2026 OET with Dr Ahmed Hesham |

## Age rating questionnaire (target: 4+)

| Question | Answer |
| --- | --- |
| Cartoon/fantasy/realistic violence, medical treatment info, profanity, horror, gambling, user-generated content with sharing, adult content | No / None |
| Unrestricted web access | Yes (the app renders our website in a system WebView; there is no third-party browsing promoted) |
| Gambling | No |
| Contests | No |

Notes: the app is an education product. It contains healthcare *language*
scenarios for exam preparation, not medical advice or treatment information.

## App Privacy (matches `ios/App/App/PrivacyInfo.xcprivacy`)

| Data type | Collected | Linked to identity | Tracking | Purpose |
| --- | --- | --- | --- | --- |
| Email address | Yes | Yes | No | App functionality (account sign-in) |
| Device ID | Yes | No | No | App functionality (device-limit enforcement, 2 devices / 1 active session) |
| Audio data | Yes | Yes | No | App functionality (Speaking practice recordings, processed for assessment) |

No data is used for tracking; no third-party analytics/ads SDKs ship in the
binary; `NSPrivacyTracking` is `false`.

## Export / encryption compliance

`ITSAppUsesNonExemptEncryption` is already `false` in `Info.plist`: the app
uses only exempt standard encryption (HTTPS/ATS to our own servers, no
proprietary crypto).

## Account deletion (guideline 5.1.1(v))

Available inside the app: **Settings → Account Deletion**, confirmed by OTP.
Flow lives at `app/(auth)/account-deletion`; backend deletion is soft-delete
(`DeletedAt` on the account) with GDPR-compliant purge on the server.

## Purchase model (guideline 3.1.1)

No in-app purchases. Enrolments happen on the website via Stripe; existing
subscribers sign in and access entitled content. See
`docs/IOS-PURCHASE-COMPLIANCE.md`.

## Screenshots (to produce at submission)

Required sizes: 6.7" (1290×2796) and 5.5" (1242×2208) iPhone; 12.9" iPad
(2048×2732) if iPad listing desired (`TARGETED_DEVICE_FAMILY` includes 2).
Suggested set: Dashboard, Listening practice player, Writing submission +
feedback, Speaking recorder, Course Materials video player, Recalls spelling.

## Review account

`review_information/review_notes.txt` holds the review notes; the demo
account credentials must be filled in by the owner immediately before
submission (and must be a real, entitled account — video lessons and AI
feedback are login-gated).
