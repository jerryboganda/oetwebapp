# Real-Mac acceptance test — desktop 0.7.9

For the person running the test on a physical Mac. Takes about 30 minutes.
Record the **whole session on a phone pointed at the Mac screen**. Screen
recordings made on the Mac itself are part of what's being tested, so they can't
serve as the reference for what the learner actually sees.

## What you need

- A Mac (Apple silicon or Intel) on macOS 13 or later. Note the exact version:
   Apple menu → About This Mac.
- The 0.7.9 DMG link you were sent.
- A learner account with Video Library access. Every sign-in logs the account
  out everywhere else, so don't use an account someone is using right now.
- A phone on a stand filming the Mac screen, with sound.
- Optional, for the screen-sharing part: Zoom and/or Google Meet (in Chrome), plus
  a second device to join the call and watch the share.

## 0. Install and identify the build (keep filming from here on)

1. Download the DMG, open it, and drag **OET with Dr. Hesham** into Applications.
   The build is not yet notarized by Apple, so the first launch may be blocked.
   If it is, go to **System Settings → Privacy & Security** and click **Open Anyway**.
2. Open the app from Applications (not Safari, not Chrome).
3. Show the version on camera: menu bar **OET with Dr. Hesham → About OET with
   Dr. Hesham**. It must say **0.7.9**. Say the macOS version out loud as well.
4. Sign in with the learner account.

## A. Video playback (3 videos from 3 different sections)

Open **Video Library**. For each of these three videos, run steps 1–8 below:

- **Video 1:** Reading → *Reading - Computer Based Exam Approach*
- **Video 2:** Listening → *Listening Workshop*
- **Video 3:** Speaking → *Speaking Workshop*

1. Open the video. It must start inside the app window. **Fail** if Safari or any
   other browser opens, if you see "403" or an error panel, or if the player stays
   black.
2. **Play**, wait 10 seconds. **Pause**. Resume.
3. **Seek forward** with the progress bar (jump roughly half-way). Playback continues from there.
4. **Seek backward** (jump back near the start). Playback continues from there.
5. **Fullscreen:** click the **Fullscreen** button at the top-right of the player.
   The video fills the whole screen, **inside the same app**, and the viewer
   watermark stays visible. Only the app's own button is used for fullscreen.
   Bunny's own fullscreen control is intentionally inactive.
6. **Exit fullscreen:** click **Exit fullscreen** at the top-right, or press
   Escape after clicking outside the video. The app returns to its normal window.
7. Go back to **Video Library**.
8. Open the next video.

After the three videos: Safari/Chrome must never have opened. You can check with
Cmd-Tab.

**Security still on:** on the web (Safari → app.oetwithdrhesham.co.uk), a video
page must still say playback is only available in the official apps.

## B. Capture protection (video 1 playing)

Start playing a video and keep it playing through every step below. Film the Mac screen with the phone the whole time.

1. **Screenshot:** press **Shift-Command-3**, then **Shift-Command-4** and drag
   over the video. Open both screenshots from the Desktop and show them on camera.
2. **Screen recording:** press **Shift-Command-5** → Record Entire Screen →
   Record, let the video play **20 seconds**, then stop from the menu bar. Play
   the recording back on camera.
3. **QuickTime:** QuickTime Player → File → New Screen Recording → record 20
   seconds while the video plays. Play it back on camera.
4. **Fullscreen:** repeat step 1 with the video in fullscreen.
5. **Zoom / Google Meet (if possible):** share the **entire screen** while the
   video plays. On the second device, film what the other participant sees. Then
   repeat with Zoom's **Share → Screen**, choosing the app window if offered.

For every capture, write down one of:
- **Hidden:** the app window or video is black, blank, or missing.
- **Visible:** the video or app content can be seen.

## What to send back

- The phone video of the whole session.
- The screenshot files and the screen-recording files (step B1–B3).
- macOS version and Mac model; app version (0.7.9).
- The result table:

| Check | Video 1 | Video 2 | Video 3 |
|---|---|---|---|
| Plays in the app (no Safari, no 403, not black) | | | |
| Play / pause | | | |
| Seek forward / back | | | |
| Fullscreen in / out | | | |
| Back to library | | | |

| Capture | Hidden / Visible |
|---|---|
| Shift-Cmd-3 screenshot | |
| Shift-Cmd-4 screenshot | |
| Shift-Cmd-5 recording | |
| QuickTime recording | |
| Screenshot in fullscreen | |
| Zoom share | |
| Google Meet share | |

**Expected capture result, stated up front:** 0.7.9 uses the macOS window
setting Apple provides (`NSWindow.sharingType = none`). Apple documents it as a
legacy hint that modern recorders ignore. Screen recordings (Shift-Cmd-5,
QuickTime) and some screen shares are therefore **expected to still show the
video**. Report exactly what you see: that result decides the next stage
(FairPlay DRM).
