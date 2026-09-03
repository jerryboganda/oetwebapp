# Play Data Safety — working basis (complete in Play Console from the ACTUAL build)

Do NOT copy answers from another app. Verify against the submitted AAB + backend + every SDK.

## Permissions in `android/app/src/main/AndroidManifest.xml`
- INTERNET — course content, practice, sync, media.
- RECORD_AUDIO — Speaking / conversation practice recordings.
- CAMERA — profile photos / document scanning (disclosed in iOS strings; declare only if the submitted Android build actually requests it at runtime).
- READ_MEDIA_AUDIO — audio playback / practice media where applicable.
- POST_NOTIFICATIONS — study reminders / updates (runtime permission).
- VIBRATE — haptics/feedback.
- USE_BIOMETRIC / USE_FINGERPRINT — secure login where enabled.

## Data collected (mirror `/privacy`: What we collect)
- Account: name, email, mobile, country, password hash, MFA secrets, profession, target exam date.
- Learning: writing submissions, speaking/conversation audio, mocks, AI feedback, expert notes, progress, predicted scores.
- Billing metadata: Stripe customer/subscription IDs, plan, invoice history, last4/brand (full card stays with Stripe).
- Device & technical: IP, user-agent, model, OS, app version, language, timezone, crash/performance traces.
- Communications: support emails, chat, surveys.

## Sharing / SDKs / processors
Stripe (payments), Brevo (transactional + opt-in marketing email), AI providers per feature (Azure OpenAI, OpenAI, Whisper, ElevenLabs, Deepgram — minimum data, no training where supported), Sentry (error reporting, no audio/submissions), UK/EU cloud hosting, advisers/authorities when legally required. International transfers under UK IDTA / EU SCCs.

## Security / retention (mirror `/privacy`)
TLS 1.2+ in transit + at rest; MFA for admin/expert (offered learners); RBAC (16 admin permissions, reviewers only assigned); refresh rotation, short-lived tokens, anomaly detection. Retention: account active + up to 24 months post-closure; speaking audio default 30 days (configurable Settings → Privacy), transcripts/scores 1 year; writing/tutor feedback while active; billing 7 years (UK accounting); security logs up to 90 days.

## Deletion
In-app `Settings > Delete Account` (`POST /v1/auth/account/delete`, 30-day grace, cancel via support) + public `https://app.oetwithdrhesham.co.uk/account-deletion` (same-address email request, verification, acknowledgement within 7 days). Enter the deletion URL in Play Console.

## Ads / tracking
No third-party advertising SDK. No advertising ID. No cross-site tracking. No third-party advertising cookies. Declare Ads = No. Purchases/subscriptions are NOT third-party ads.
