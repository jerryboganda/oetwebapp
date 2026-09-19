# Placement Test — Real-Device Production Checklist

Owner-run manual checks of the production Placement Test on real devices, before
broad student access. Wording in the **Expected** columns is the text the runner
(`components/placement/placement-test-runner.tsx`) and the result card
(`components/placement/result-report-card.tsx`) actually show.

- `Placement.BetaOnly` **stays enabled** until you finish this checklist and give
  final approval. The website PR (oetwebsite #2) stays unmerged until then.
- This is internal production approval, not psychometric validation.
- Context and admin steps: `docs/PLACEMENT-ROLLOUT-HANDOFF.md`.

## Before you start

| Need | Detail |
|---|---|
| Build under test | Production web app (`https://app.oetwithdrhesham.co.uk`). Copy the GEPA and oetwebapp commit ids from the verification log in the handoff (§8) into the sign-off block at the end. |
| Account A | Standard learner listed in `Placement.BetaEmails`. |
| Account B | Learner with an admin-approved extra-time grant (handoff §6). Only needed for the timer rows marked **B**. |
| Account C | Optional: a learner outside the beta allowlist (row 10.2). |
| Kit | Headphones, working microphone, normal (not private / incognito) window, a way to go offline (airplane mode, or Chrome DevTools > Network > Offline). |
| Time | A full attempt takes about 60–85 minutes. Each attempt is a fresh attempt; history keeps them all. |

Devices (the columns of the results grid):

| Code | Device |
|---|---|
| D1 | Chrome, desktop/laptop (Windows or macOS) |
| D2 | Safari, macOS |
| D3 | Safari, iPhone (iOS) |
| D4 | Chrome, Android |
| D5 | Samsung Internet, Android |

Suggested order: one full attempt per device, ticking rows as you go; then the
extra-time rows (**B**) on one desktop and one phone; then the failure-injection
rows (3.9, 5.10, 6.5) on desktop. Tick a group in the grid (section 12) only when
every row in it passed on that device; otherwise note the row numbers.

## 1. Language Systems (Part 1 of 5)

| # | Check | Expected |
|---|---|---|
| 1.1 | Start Free Placement Test, then Start Part 1 | Question 1 appears. |
| 1.2 | Question layout | The prompt sits inside its own card. Each option is a full-width row at least 44 px tall; tapping anywhere on the row selects it and highlights it. No horizontal scroll, in portrait and landscape. |
| 1.3 | Press Next with nothing selected | "Choose an answer to continue." Stays on the question. |
| 1.4 | Select an answer, press Next | The next question loads. The counter reads "Question N" and goes up by one. |
| 1.5 | Run to the end of the part | About 12–18 questions (adaptive, so it varies), then the "Language Systems complete" screen. |
| 1.6 | Reload the page mid-part | You return to a Language Systems question, not the overview, and answered questions are not asked again. Note whether the question number restarts. |

## 2. Reading (Part 2 of 5)

| # | Check | Expected |
|---|---|---|
| 2.1 | Open a Reading unit | A "Read the text" card above the question cards. Text wraps, long words break, and it reads comfortably at phone width. |
| 2.2 | Question numbering | A text with several questions shows "Questions N–M". |
| 2.3 | Submit with one question unanswered | Button reads "Submit answers". Message: "Answer every question in this set before continuing." |
| 2.4 | Answer all, submit | The next text loads. |
| 2.5 | Finish the part | "Reading complete" screen (see section 7). |

## 3. Listening: audio check, autoplay, replay, failure

| # | Check | Expected |
|---|---|---|
| 3.1 | Audio Check screen ("Check Audio" from the Reading transition) | Heading "Part 3 of 5 — Listening". Text: "The audio will start automatically after you begin. Questions are visible while you listen." An "Audio check" box with **Play sample**, and "Each recording can be played twice. A replay never lowers your result." |
| 3.2 | Press Play sample | You hear a spoken sound-check sentence (or a tone) and can set a comfortable volume. If the browser cannot play it: "This browser could not play the sample. You can still continue." Note it. |
| 3.3 | Press Start Listening | The first unit shows "Loading audio…" then "Playing — listen carefully". The audio **starts by itself**, with no extra tap. Questions are visible under the audio card. Record per device: autoplay worked (yes/no). |
| 3.4 | Tap-to-start fallback | If the browser blocks autoplay, the audio card says "Tap to start the audio" with a large **Tap to start audio** button. Tapping it plays the clip. Not a failure by itself, but record device, OS and browser: after the Start Listening tap autoplay is expected to work. |
| 3.5 | Playback quality | Audio is audible and clear. The "Audio progress" bar moves from left to right. Never silent with a frozen bar. |
| 3.6 | Replay counter | At the end: "Audio finished" and **Play again (1 left)**. It replays from the start. After the second play there is no replay button. |
| 3.7 | Later units | Units 2, 3 and on start automatically, with no tap (Start Listening unlocked audio for the whole part). |
| 3.8 | Timer starts only when audio plays | While the audio card says "Loading audio…" the timer area reads "Timer starts when the audio begins". The countdown appears only once playback starts. Desktop: DevTools > Network > Slow 3G makes this easy to see. |
| 3.9 | Failed audio becomes a replacement item (desktop, throwaway attempt) | Chrome DevTools > Network > Network request blocking: add `*placement/audio*`, then start a Listening unit. You see "Technical audio problem — loading a replacement item. This item will not be scored." and the app moves on to another unit without hanging, marking nothing wrong. While the block stays on, each replacement fails too, and the third failure ends Listening with no level by design ("Not enough evidence yet" on the result). Remove the block afterwards. |
| 3.10 | Reload mid-Listening | You return to the Audio Check screen (autoplay needs a fresh tap), not into a half-played unit. Start Listening carries on. |
| 3.11 | Offline as a Listening unit loads (any device; airplane mode is enough) | "The audio could not be loaded. Check your connection, then reload the audio." with **Reload audio**. Back online, press it: the audio loads and plays. |

## 4. Dynamic timers

Timer format is m:ss next to "Question N". It turns amber at 60 s or less and red
and bold at 10 s or less. Values come from the engine's timing table (GEPA
`docs/DECISIONS.md` D-028). Allow about 2 s difference at the start.

| Module | Start value for a standard account |
|---|---|
| Language Systems | 45 s per question, so 0:45 for a single question. It is under 60 s, so it shows amber from the start; that is expected. |
| Reading | Per two-question text, by level: Pre-A1 1:15, A1 1:30, A2 2:15, B1 2:45, B2 3:30, C1 4:00, C2 4:30. The level moves as you answer, so values differ between texts. |
| Listening | 2 x clip length + 30 s per question + 15 s, minimum 1:30 (a 60 s clip with two questions gives 3:15). Time the clip with a stopwatch. |

| # | Check | Expected |
|---|---|---|
| 4.1 | Language Systems questions | Each shows about 0:45 and counts down. |
| 4.2 | Reading texts | Each start value matches a value in the table. |
| 4.3 | Listening units | No countdown until the audio plays, then a start value near the formula. |
| 4.4 | Let one Language Systems timer reach 0:00 | The unit submits itself and the next question appears. No error banner. Only try this on Language Systems. |
| 4.5 | Reload with about 30 s left on a question | The timer resumes from the time left, not from the full allowance. |
| 4.6 | Safari (D2, D3) | Every question shows a real m:ss timer, never blank or "NaN". |
| 4.7 **B** | Repeat 4.1–4.3 on Account B | The test overview first shows "Extra time has been approved for your account (+N% on timed sections)." Start values are longer than Account A's for the same module and level: the standard value x (1 + N/100). Record N and the values. Speaking "Planning time" and Writing time limits should also be longer by N%; Speaking response caps stay the same. If the screen does not show those, record it. |
| 4.8 | Account A: look for any way to turn extra time on | None. No extra-time or accommodation control on the overview or any test screen. |

## 5. Speaking: recording and upload

| # | Check | Expected |
|---|---|---|
| 5.1 | Microphone check ("Check Microphone", then the "Part 4 of 5 — Speaking" screen) | Text: "8 responses. You will see planning/response time for each task." Press **Check microphone**, allow access, speak. The level bar moves for about 4 s, then "Your microphone is working." |
| 5.2 | Stay silent during the check | "We could not hear you. Check your microphone and try again — you can still continue." |
| 5.3 | Block microphone access, then check | "Microphone access was blocked. Allow it in your browser settings to record your responses." After allowing it in site settings, the check passes. |
| 5.4 | First task screen | "Response 1 of 8" and a task card with the prompt and "Planning time: N s · Response time: up to N s". The engine sets planning of 10–30 s and response caps of 20–90 s depending on the task type (D-028). If planning time is missing, or every task says "up to 60 s", record it: the engine's timing did not reach the screen. |
| 5.5 | Press Start planning time | "Planning time — recording starts automatically in N s." counting down; recording starts by itself at zero. **Start recording now** skips the wait. If a microphone permission prompt appears again, note the device. |
| 5.6 | Recording | "Recording — N s left" with a pulsing dot. **Stop recording** ends it early; at zero it stops on its own. |
| 5.7 | Review | A playback control plays your voice, complete and audible. **Submit response** and **Record again** are both offered; Record again replaces the recording. |
| 5.8 | Submit | "Saving your response…", then "Response 2 of N". After the last one: "Speaking complete". No error banner. Where you can see the network: the upload (`POST .../v1/placement/upload`) and the submit (`.../speaking/.../submit`) return 2xx. Any 415, 4xx or 5xx fails the row. |
| 5.9 | Browser formats | Chromium browsers (Chrome, Android Chrome, Samsung Internet) normally record webm (Opus). Safari normally records mp4 (uploaded as .m4a); a newer Safari may pick webm. Every format must submit cleanly. Note the browser and version. |
| 5.10 | Retry state (failure injection) | Record, go offline, press Submit response. You see "We could not upload your recording. Your recording is still saved on this device. Retry upload." Playback still works and the button now reads **Retry upload**. Go online, press it: the response submits without re-recording. |
| 5.11 | Reload mid-Speaking | You return to the Microphone check screen (access may be asked again). Note which response comes next: the app asks the server for the full task list again, so it may start at Response 1. |

## 6. Writing: autosave

| # | Check | Expected |
|---|---|---|
| 6.1 | Continue to Writing | "Task 1 of N". The overview and the transition say 3 tasks, but the engine's task list for a route has three scored tasks plus an unscored "Integrated accuracy diagnostic", so N may be 4. Record N and the order. Each task card shows the prompt and "Suggested time: about N minutes · Aim for at least N words". If that line is empty, record it. |
| 6.2 | Type in the editor | The word count updates ("N words", plus "· X more suggested" until the minimum). On a phone the keyboard does not hide the editor, and you can scroll to Submit. |
| 6.3 | Pause typing for 1–2 s | "Saving draft…", then "Draft saved HH:MM". |
| 6.4 | Reload after "Draft saved" (normal window) | You come back to Writing and your text is in the editor. The copy is kept on the device; a private window may not keep it. |
| 6.5 | Offline (failure injection) | Go offline and keep typing. After 1–2 s: "Not saved to the server yet — kept on this device, retrying as you type". Text is not lost. Go online and type one more character: "Draft saved HH:MM". |
| 6.6 | Submit response | Disabled until there is at least one word. Submitting opens "Task 2 of N" with an empty editor; the last task goes to "Assessment complete". Submitting offline shows "Could not submit your response — your text is still here. Please try again." and keeps the text. |
| 6.7 | Reload after submitting Task 1 | Record which task appears. The app asks the server for the full task list again, so it may show Task 1. |

## 7. Section transitions and the "Part X of 5" header

Tick these while you work through sections 1–6.

| # | Check | Expected |
|---|---|---|
| 7.1 | Overview screen | "Free General English Placement Test", the five parts with counts and times, "Approximate full-profile time: 60–85 minutes.", headphone and microphone notes, one **Start Free Placement Test** button. |
| 7.2 | Header on every part screen | "Part X of 5 — Name", "N of 5 parts complete" and a progress bar. At 640 px wide and up a five-step list shows a check mark on finished parts; on a narrow phone it is hidden (expected). No header on the overview or the final profile. |
| 7.3 | Every transition button works on the first tap, and the new part opens at its top. | See the table below. |
| 7.4 | Rotate the phone and back on any screen | The screen and your progress are unchanged. No horizontal scroll anywhere. |

| After | Heading | Text | Button | Header reads |
|---|---|---|---|---|
| Overview | Part 1 of 5 — Language Systems | Grammar and vocabulary in context. Approx. 8–12 minutes. | Start Part 1 | Part 1, 0 of 5 complete |
| Language Systems | Language Systems complete | Next: Reading — approx. 10–15 minutes. | Continue to Reading | Part 1, 1 of 5 complete |
| Reading | Reading complete | Next: Listening. Please use headphones if available. | Check Audio | Part 2, 2 of 5 complete |
| Audio Check | Part 3 of 5 — Listening | (see 3.1) | Start Listening | Part 3, 2 of 5 complete |
| Listening | Foundation section complete | Next: Speaking. Check microphone before continuing. Includes a "Your foundation profile" card. | Check Microphone | Part 3, 3 of 5 complete |
| Mic check | Part 4 of 5 — Speaking | (see 5.1) | Start Speaking | Part 4, 3 of 5 complete |
| Speaking | Speaking complete | Next: Writing — 3 tasks. Desktop/laptop recommended for longer upper-level responses. | Continue to Writing | Part 4, 4 of 5 complete |
| Writing | Assessment complete | We are preparing your indicative English profile. | View Results | Part 5, 5 of 5 complete |

The foundation profile card (after Listening) shows Reading and Listening levels;
Speaking and Writing read "Not taken yet".

## 8. Final result and profile

Press **View Results**. If it says "Your profile is still being prepared — please try again in a moment.", wait and press it again.

| # | Check | Expected |
|---|---|---|
| 8.1 | Card header | "Indicative CEFR Placement Estimate · Diagnostic English Profile", title "Your placement profile", and "Confidence: Low" or "Confidence: Moderate". Never "High". No percentages in the confidence text. |
| 8.2 | Skill tiles | Four tiles only: Reading, Listening, Speaking, Writing. Language Systems is not a fifth tile. |
| 8.3 | A measured skill | A level such as "B1", or a range such as "B1–B2", with short "You can" and "Focus next" lists (three items at most each). |
| 8.4 | Speaking or Writing awaiting a reviewer | The tile says "Being reviewed by Dr Hesham's team" with "Your level appears here once it has been reviewed." No level, no score, no error. A "Partial profile" badge shows while any of the four is not measured, and the headline reads "Overall profile shown once all four skills are measured". |
| 8.5 | Missing evidence | "Not enough evidence yet" with a reason, or "Not taken yet" for a part that was skipped. Never a default level. |
| 8.6 | Grammar and Vocabulary | A separate section titled "Grammar & Vocabulary diagnostics": "A diagnostic from Part 1. It guides your study plan and is not part of the skill levels above." Grammar and Vocabulary each show "(X of Y correct)" with Strengths and Priorities. |
| 8.7 | Wording | None of these anywhere: "validated", "certified", "CEFR-aligned", "your level is", "High confidence", a predicted exam score, an IELTS band or OET grade. A "Next step" card shows the readiness text with its disclaimer and the retest advice. |
| 8.8 | Buttons | **View result history** opens the history page. **Start a new attempt** returns to the overview; the old attempt stays in history. |
| 8.9 | Layout | Skill tiles stack in one column on a narrow phone and sit two across at 640 px wide and up. |

## 9. Test history and result page

| # | Check | Expected |
|---|---|---|
| 9.1 | Open `/placement-test/history` (or **View result history**) | The attempt you just finished is listed: "Attempt of <date>", then "All four skills measured" or "Partial profile" and "ruleset <version>", with a **View result** button. |
| 9.2 | New account with no attempts | "No placement attempts yet. Start the free placement test and your results will be saved here." |
| 9.3 | Press View result | `/placement-test/results/<id>` shows the same report as 8.x under the title "Your placement result", with "Back to your placement history". |
| 9.4 | Compare with the profile you saw at the end of the test | Same levels, badges and diagnostics. |
| 9.5 | Overview when history exists | A link "N previous result(s)" to the history page. |
| 9.6 | Open a result URL while signed in as a different learner | "That result does not exist or belongs to another account." with a link back to the history. |

## 10. Dashboard card and sidebar item

| # | Check | Expected |
|---|---|---|
| 10.1 | Account A, desktop sidebar and phone menu | A "Placement Test" item opens `/placement-test` and is highlighted while you are on it. |
| 10.2 | Account C (outside the allowlist) | No sidebar item and no dashboard card. `/placement-test` says "Placement test unavailable". |
| 10.3 | Card, no attempt yet | Status "Not started", title "Find your English level — free", "About 60–85 minutes", "Headphones", "Microphone", button **Start Placement Test**. |
| 10.4 | Card, attempt in progress | Status "In progress", title "Continue your placement test", "Your answers are saved. Pick up where you left off.", button **Continue** (and **View Placement Test Results** if an earlier result exists). |
| 10.5 | Card, finished | Status "Completed" or "Partial result", title "Your placement test results", **View Placement Test Results** and **Retake**. |
| 10.6 | "In progress" in another browser | The card remembers an open attempt per browser, so a different browser or device shows results or Start, not Continue. Expected, not a failure. |
| 10.7 | Phone bottom navigation | Each slot opens the page it is labelled with (the new sidebar item did not shift any slot). |

## 11. If something fails: what to capture

| Capture | How |
|---|---|
| Row number and device code | For example "5.10, D3". |
| Device | Model and OS version (for example "iPhone 15, iOS 18.x"). |
| Browser and version | Chrome: chrome://version. Safari: Safari > About Safari (macOS) or the iOS version. Samsung Internet: Settings > About. |
| Screenshot or screen recording | The whole screen, including the clock. |
| Time | Local time and time zone, within a minute. |
| Account | The email used and whether it was Account A, B or C. |
| Session id | Desktop: DevTools > Application > Local Storage > `oet_placement_active_session` (kept until you start a new attempt). On a phone without DevTools, the account email and the time are enough to trace it. |
| Console | Screenshot of red errors (Chrome DevTools; Safari > Develop > Show Web Inspector; iPhone via a Mac with Settings > Safari > Advanced > Web Inspector on; Android via chrome://inspect over USB). |
| Network status | Method, path and status code of the failing request (for example `POST /v1/placement/upload 415`). **Do not share HAR files or screenshots that show the Authorization header or a token.** |

## 12. Results grid and sign-off

| Group | D1 Chrome | D2 Safari Mac | D3 Safari iPhone | D4 Chrome Android | D5 Samsung |
|---|---|---|---|---|---|
| 1 Language Systems | ☐ | ☐ | ☐ | ☐ | ☐ |
| 2 Reading | ☐ | ☐ | ☐ | ☐ | ☐ |
| 3 Listening audio | ☐ | ☐ | ☐ | ☐ | ☐ |
| 4 Timers (4.7 with Account B) | ☐ | ☐ | ☐ | ☐ | ☐ |
| 5 Speaking | ☐ | ☐ | ☐ | ☐ | ☐ |
| 6 Writing | ☐ | ☐ | ☐ | ☐ | ☐ |
| 7 Transitions and header | ☐ | ☐ | ☐ | ☐ | ☐ |
| 8 Final result | ☐ | ☐ | ☐ | ☐ | ☐ |
| 9 History and result page | ☐ | ☐ | ☐ | ☐ | ☐ |
| 10 Dashboard card and sidebar | ☐ | ☐ | ☐ | ☐ | ☐ |

| Sign-off | |
|---|---|
| GEPA commit / oetwebapp commit tested | |
| Tester and date | |
| Failures (row, device, note) | |
| Final approval for broad student access (yes / no) | |
| Approved by and date | |

Until "yes": `Placement.BetaOnly` stays on and oetwebsite #2 stays unmerged.
