# iOS TestFlight → App Store — Device QA Checklist (owner-executed)

Prereq: the IPA reached TestFlight (requires working ASC API keys + Team ID —
currently owner-blocked). Install via the TestFlight app, never a raw .ipa.
Minimum supported: iOS 16.4. Test at least one small iPhone (SE-class) and one
large iPhone; iPad if it installs.

Fill every evidence slot.

## 1. Install / first launch

- [ ] TestFlight install completes; app launches with no crash, no blank/black screen.
- [ ] Splash screen is centered (not stretched/offset).
- Evidence: device model + iOS version:

## 2. Safe areas & layout

- [ ] Notch/Dynamic Island: status bar content is not overlapped.
- [ ] Bottom home indicator: bottom navigation and buttons sit above it.
- [ ] No clipped controls, no desktop-style fallback overlays.
- [ ] Small iPhone: Recalls → Practice Spelling layout stays usable **with the keyboard open**; spacebar types a space and does not replay audio; multi-word answers work; audio control works.
- [ ] iPad (if supported): same checks in both orientations.

## 3. Login / OTP / session

- [ ] Sign-up flow, sign-in flow.
- [ ] OTP: manual typing, paste, delete/backspace; invalid + expired OTP show recoverable errors.
- [ ] One-time-code AutoFill appears above the keyboard (SMS OTP) — digits enter the correct field.
- [ ] English and Arabic keyboards both type digits into the OTP fields (regression: keyboard opens but digits ignored on iPad/Safari).
- [ ] Device policy: enrolling a 3rd device follows the approved OTP/replacement flow; only 1 active session at a time.
- [ ] Full app kill → sign-in again → **lands on Dashboard**, never an old Reading/Listening/Writing/Speaking/video/material route.

## 4. Protected video

- [ ] 3 different protected course videos play in-app with approved controls (play/pause/seek/fullscreen); no external browser, no 403, no black player.

## 5. Reading / Listening / Writing / Speaking + credits

- [ ] Reading: full exam flow, submit, review screens; **1 credit** deducted once; reopening a completed paper does not double-charge.
- [ ] Listening: A/B/C headings and numbering correct, C1→C2 transition works; post-submit full transcript + A/B/C review tabs render; text selection/copy where approved; **1 credit** once.
- [ ] Writing: submit letter/case note → feedback + model answer + reopen works; Past Submissions inside Writing shows Writing letters only; visible cost **2 AI credits** per letter/case note, deducted once.
- [ ] Speaking: mic allow/deny recoverable; record/stop/playback/submit works; **1 card = 2 credits; full A+B exam = 4**; refresh/retry never double-charges.
- [ ] Qualifying Full/Crash gift = **5 shared credits granted once** (no duplicates across re-login).

## 6. Purchase compliance surface

- [ ] Inside the app: /cart, /pricing, /catalog, /ai-packages, /checkout/review show the "Enrol on our website" notice (no purchase UI anywhere).
- [ ] A website-purchased account signs in and sees entitled content immediately.

## 7. Account deletion

- [ ] Settings → Account Deletion initiates full deletion with OTP confirmation; after confirmation the account can no longer sign in.

## 8. Network / recovery

- [ ] Kill network mid-exam → recoverable state, no corrupted attempt; reconnect and continue safely.
- [ ] Airplane-mode cold start shows the offline recovery screen, not a blank screen.

## 9. Update test (handover 4.E)

- [ ] Older TestFlight build → newer TestFlight/App Store build upgrades in place: access, entitlements, credits and local state survive; no reinstall needed.

**Verdict:** all boxes → iOS device QA passed; proceed to App Store submission with metadata from `fastlane/metadata/ios/` and the demo account from the review notes.
