# macOS 0.7.10 — Real-Mac QA Checklist (owner-executed)

Prereq: a signed, notarized, stapled 0.7.10 DMG published to the download URL
(this checklist gates its release — until every box here passes, the public
Mac download stays disabled and 0.7.10 stays on hold).

Fill every evidence slot; a gate without evidence is not passed.

## A. Install from the public channel (Gatekeeper)

1. [ ] Download the DMG from `https://app.oetwithdrhesham.co.uk/api/download/mac` in Safari on a clean Mac (no prior OET install; quarantine intact — do not unzip/copy with tools that strip it).
   - Evidence: filename, size, `shasum -a 256` output:
2. [ ] Open the DMG → drag to /Applications → first launch shows no Gatekeeper block (no "cannot verify developer", no right-click dance).
   - Evidence: screenshot of first launch.
3. [ ] `codesign -dv --verbose=4 /Applications/OET\ with\ Dr.\ Hesham.app` and `spctl -a -vvv -t exec` output pasted:
   - Expected: `Authority=Developer ID Application: …`, `TeamIdentifier=…`, `satisfies its Designated Requirement`.
4. [ ] `xcrun stapler validate /Applications/OET\ with\ Dr.\ Hesham.app` → "The validate action worked!".
5. [ ] Apple Silicon Mac (M1+): app launches and runs natively. If an Intel Mac is available: repeat 1–4 there.
   - Hardware used:

## B. Protected video / DRM acceptance (handover 3.A)

On the production backend with a candidate account that has course access:

1. [ ] Play **3 different protected course videos from 3 different sections**.
2. [ ] For each: play, pause, seek forward, seek back, enter fullscreen, exit fullscreen, return to the course page normally.
3. [ ] No Safari/browser redirection, no black player, no HTTP 403 in network activity, no external-browser workaround needed.
4. [ ] Videos refused to load when opened outside the app (protection intact — no weakening of Bunny referrer/token rules).
   - Evidence: per-video section names + screenshots/screen recording.

## C. Screen capture / PiP (handover 3.B)

1. [ ] While a protected video plays: take a macOS screenshot (⇧⌘3/⇧⌘4) → player content is excluded/protected.
2. [ ] Start a screen recording (QuickTime or ⇧⌘5) → video content not captured cleanly.
3. [ ] Fullscreen playback then screenshot — same protection.
4. [ ] Picture-in-Picture: if PiP allows capture of protected content, PiP must be disabled for protected content (report; engineering disables).
   - Evidence: screenshots of captured output showing protection.

## D. Microphone / Speaking

1. [ ] First speaking task triggers the macOS microphone permission prompt; Allow → recording works.
2. [ ] Deny → app shows a recoverable message (not a crash); enabling in System Settings recovers the flow.

## E. Auto-update (handover 3.D)

1. [ ] Install the previous **signed** build (e.g. 0.7.9-signed or 0.7.10-rc), then let the updater detect the newer 0.7.10: it detects, downloads, verifies the minisign signature, installs and relaunches without manual uninstall.
2. [ ] Candidate login survives the update; version in-app reports the new version.
3. [ ] Tamper test (engineering-supplied corrupt update package) is rejected and the app keeps running.
   - Evidence: versions before/after, updater log/screenshot.

## F. Session & relaunch parity

1. [ ] Full quit (⌘Q) → reopen → fresh sign-in lands on Dashboard (never an old Reading/Listening/video route).
2. [ ] Logout clears protected state; reopening the app without signing in shows no previous candidate content (check Recents/restore).

## G. Post-notarization re-test (handover 3.A note)

- [ ] Repeat section B (3 videos) on the exact final notarized DMG installed in section A/E.

**Verdict:** all boxes → 0.7.10 is release-approved; re-enable the Mac download by removing `NEXT_PUBLIC_MAC_DOWNLOAD_DISABLED=1` from deploy.yml build args (and compose env) and record the flip in the release ledger.
