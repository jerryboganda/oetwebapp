# OET with Dr Hesham — Master Product & Platform Document

**The complete reference: scope, every feature, how it works, and why it matters.**

| | |
|---|---|
| **Product** | OET with Dr Hesham (internal codename: *Ruflo*) |
| **Type** | Guided exam-preparation platform for the Occupational English Test |
| **Owner / academic authority** | Dr Ahmed Hesham |
| **Production** | `app.oetwithdrhesham.co.uk` (web) · `api.oetwithdrhesham.co.uk` (API) |
| **Surfaces** | Web · Android (Capacitor) · Windows & macOS desktop (Tauri 2) · iOS *(shell not yet scaffolded)* |
| **Document version** | 1.0 — 20 July 2026 |
| **Audience** | Everyone: learners & marketing, partners & investors, tutors & operations, engineering |

---

## 0. How to read this document

This is a single, self-contained master document. It is written in four concentric layers, and you can stop at whichever layer you need:

| If you are… | Read |
|---|---|
| A learner, marketer or partner | §1–§7 and §15 |
| An investor or commercial stakeholder | §1, §2, §10, §15, §16, §17 |
| A tutor, examiner or operations lead | §7, §8, §11, §13 |
| An engineer or technical reviewer | §8–§14 and the appendices |

**Honesty convention.** This document distinguishes what is *shipped and reachable* from what is *built but not yet wired* and what is *planned*. Anything not fully live is tagged:

- 🟢 **Shipped** — live in production and reachable by the intended user.
- 🟡 **Built, not surfaced** — backend/logic complete, but no user-facing entry point yet.
- 🔵 **Planned** — designed and specified, not yet built.

Section 16 consolidates every 🟡 and 🔵 item in one place.

---

## 1. Executive summary

### 1.1 What it is

**OET with Dr Hesham is an end-to-end preparation platform for the Occupational English Test — the English exam that internationally-trained healthcare professionals must pass to register and practise in English-speaking countries.**

It covers all four OET sub-tests (Listening, Reading, Writing, Speaking) from first diagnostic to exam day, combining four things that are normally bought separately:

1. **Recorded course content** authored by Dr Ahmed Hesham, per profession.
2. **A full-fidelity exam simulator** that reproduces the real test's timing, locks, navigation rules and marking.
3. **AI assessment grounded in a written rulebook** — every piece of feedback cites a specific, human-authored rule.
4. **Human expert review** — real examiners marking Writing letters and playing the patient in one-to-one Speaking sessions.

Around that core sit a personalisation engine, a full commerce and entitlement system, an operations console, and native mobile and desktop shells.

### 1.2 The problem it solves

OET failure is expensive and career-blocking. Candidates re-sit at their own cost, delay registration, and lose income. Yet the market they buy from is fragmented and generic:

| Learner problem | How this platform answers it |
|---|---|
| Generic English courses don't teach the OET's *format* | Every module is built to the canonical OET structure — item counts, timings and question types are enforced in code, not approximated |
| Practice tests don't behave like the real exam | Server-authoritative timers, one-way section locks, single-play audio and confirm-token gates make cheating-yourself impossible |
| Feedback is vague ("improve your grammar") | Every finding cites a numbered rule from a 172-rule Writing rulebook and quotes the exact words in your letter |
| Writing and Speaking need a human, and humans are expensive | Grounded AI handles unlimited practice; human examiners are reserved for the moments that need them, and are calibrated against Dr Hesham's own gold standards |
| Candidates study around clinical shifts, on a phone, at odd hours | Web, Android and desktop apps, short-session drills, spaced repetition and a daily plan sized to the study budget you declare |
| Nobody tells you when you're actually ready | A readiness engine blends mock averages, trajectory, error-clean rate, time management and consistency into a single predicted band |

### 1.3 Who it's for

**Primary users — OET candidates.** Internationally-trained doctors, nurses, dentists, pharmacists, physiotherapists and allied-health professionals. High-stakes, time-poor, often anxious, studying in a second language, frequently on mobile.

**Secondary users:**

- **Tutors and expert examiners** — mark Writing letters, run live Speaking sessions, manage learner cohorts.
- **Administrators and content authors** — author and publish exam material, run the business, monitor quality.
- **Sponsors and institutions** — hospitals, agencies and training providers buying seats in bulk 🟡.
- **Affiliates** — partners earning commission on referred enrolments.

### 1.4 What makes it different

1. **Rulebook-grounded AI, not free-form AI.** Every AI call is built from an embedded, versioned rulebook. Rule IDs the model invents are filtered out before they reach a learner. Ungrounded prompts throw an exception rather than proceeding.
2. **Fail loud, never fabricate.** When AI grading fails, the system marks the attempt failed and *refunds the credit* rather than returning a plausible-looking fake score. This is written into the code as an explicit rule.
3. **Exam fidelity enforced server-side.** Timers, locks and section transitions are computed from persisted timestamps on the server, so refreshing the page, closing the laptop or editing the DOM changes nothing.
4. **One scoring anchor, everywhere.** Listening and Reading are pinned to the OET conversion `30/42 ≡ 350/500`. Inline scoring arithmetic anywhere in the Listening codebase *fails the build*.
5. **Mock exams are guaranteed AI-free.** Four independent layers — coach refusal, pipeline hard-branch, gateway backstop, and neutral pre-assessment on the tutor screen — ensure no hint, estimate or AI grade can leak into a mock.
6. **The academic authority is a named person.** The rulebooks cite *"Dr. Ahmed Hesham — The Tutor Book — OET Writing Comprehensive Rulebook (Sessions 1–10)"*. The AI calibration harness literally scores itself against a field called `DrAhmedGrade`.

### 1.5 The platform at a glance

| Dimension | Scale |
|---|---|
| Learner-facing pages | 317 routes |
| Admin console pages | 251 routes |
| Backend API endpoint files | 155 |
| Backend services | 40+ namespaced service areas |
| Database migrations | 160+ (hand-authored) |
| Real-time hubs | 9 SignalR hubs + 1 raw WebSocket |
| Background workers | ~40 hosted services |
| Writing rulebook | 172 rules × 13 professions, 16 sections |
| Listening rulebooks | 19 authoring rules/profession + 80 exam-mode rules |
| Reading rulebooks | 8 authoring rules/profession + 81 exam-mode rules |
| Product catalogue | 24 course SKUs + 29 add-ons/packs |
| Professions supported | Medicine, Nursing, Pharmacy, Dentistry, Physiotherapy, Allied Health (+ more in rulebooks) |
| Third-party integrations | 25+ (payments, AI, speech, video, comms, scheduling) |
| E2E test specs | ~72 across 14 browser/role projects |

---

## 2. Product scope

### 2.1 The exam being prepared for

OET has four sub-tests. The platform models each one canonically — these shapes are enforced in code and validated at publish time, not left to author discretion.

| Sub-test | Structure the platform enforces | Timing | Raw → scaled |
|---|---|---|---|
| **Listening** | Part A: 24 short-answer note-completion items (A1 Q1–12, A2 Q13–24) · Part B: 6 three-option MCQs (B1–B6, one each) · Part C: 12 three-option MCQs (C1 Q31–36, C2 Q37–42). **Total 42** | 40 min (API/test-rules) with per-section preview windows: A1 30 s, A2 30 s, B 30 s, C1 90 s, C2 90 s | `30/42 ≡ 350/500`, piecewise linear, 42 → 500 |
| **Reading** | Part A: 4 texts, 20 items (matching / short answer / sentence completion) · Part B: 6 extracts, 6 MCQs · Part C: 2 long articles, 16 MCQs. **Total 42 across 12 texts** | Part A **15 min hard-locked**; optional 10-min break; Parts B+C share **45 min**. Total 60 min | Same `30/42 ≡ 350/500` anchor |
| **Writing** | One profession-specific letter from case notes. Six official criteria: Purpose /3, Content /7, Conciseness & Clarity /7, Genre & Style /7, Organisation & Layout /7, Language /7. **Raw max 38** | 5 min reading (locked) + 40 min writing = **45 min** | `raw × 500 / 38`; **country-aware pass mark** (see §8.3) |
| **Speaking** | Unscored warm-up, then **two role-plays**, each 3 min preparation + 5 min discussion, against an interlocutor playing patient/carer/relative/colleague | ~20 min total | 4 linguistic criteria (0–6) + 5 clinical-communication criteria (0–3), weighted 55/45 → 0–500 |

### 2.2 In scope

- All four OET sub-tests, end to end, for both **computer-based (CBT)** and **paper** delivery formats.
- Profession-specific content: Medicine, Nursing, Pharmacy, Dentistry, Physiotherapy, Allied Health — with rulebooks additionally covering Occupational Therapy, Speech Pathology, Radiography, Optometry, Podiatry, Veterinary Science and Dietetics.
- Diagnostic → learning → drilling → mock → readiness → exam-day pathway.
- AI assessment, human expert assessment, and reconciliation between them.
- Full commerce: catalogue, cart, checkout, entitlements, add-ons, credits, refunds.
- Content authoring, review workflow and publishing.
- Tutor operations: queues, calibration, compensation, scheduling.
- Learner community, peer review and social features.

### 2.3 Adjacent scope (present but secondary)

- **Basic English foundation course** — a pre-OET A1–B1 bridge for candidates not yet ready for OET-specific training.
- **IELTS and PTE scoring helpers** — `lib/ielts-scoring.ts`, `lib/pte-scoring.ts`, `IeltsMockEngine.cs` and an `/ielts-guide` page exist for comparison and cross-exam guidance. Full IELTS/PTE preparation is **explicitly out of scope**.

### 2.4 Out of scope

- Non-healthcare English exams as a primary product.
- Live proctoring of the *real* OET exam.
- Issuing official OET results. Every score surface carries a practice/estimate disclaimer.
- Clinical or medical training. This is a language and exam-technique product.

### 2.5 Delivery surfaces

| Surface | Technology | Status |
|---|---|---|
| **Web app** | Next.js 16 App Router, React 19, Tailwind v4 | 🟢 Primary surface |
| **Android** | Capacitor 7 remote shell (`com.oetprep.learner`) | 🟢 Shipped, Play listing live |
| **Windows / macOS desktop** | Tauri 2 remote-only thin client (`com.oetprep.desktop`) with OS-level screen-capture blocking during video playback | 🟢 Shipped via GitHub Releases + auto-updater |
| **iOS** | Capacitor config exists; Xcode project not committed | 🔵 Planned |

All shells load the same production web app, so a feature ships everywhere at once. The desktop shell adds native credential storage (Keychain / Credential Manager), an offline splash, deep links (`oet-prep://`), a system tray, and anti-piracy screen-capture exclusion.

### 2.6 Design principles

The product deliberately rejects the "cram tool" aesthetic. From `PRODUCT.md`:

1. **Calm under pressure** — every screen lowers cognitive load. The product's job is to *reduce* exam anxiety, and the UI is part of that job.
2. **Guided, not gamified** — structure and honest coaching for serious adults. No confetti, no mascots, no streak-spam.
3. **Content is the product** — exam material and feedback take centre stage; chrome recedes.
4. **Earn trust through precision** — accurate scoring, specific feedback, predictable behaviour.
5. **Meet learners everywhere** — equal care for phone, desktop and tablet; for ESL readers; for assistive technology.

**Accessibility target: WCAG 2.1 AA**, enforced by an axe-core Playwright suite. Full `prefers-reduced-motion` support. Right-to-left (Arabic) correctness is a committed requirement.

---

## 3. The learner journey, end to end

```
  Sign up ──► Onboarding ──► Diagnostic ──► Daily plan
     │           (profession,      (find weak    (sized to your
     │            goals, exam       spots)        declared budget)
     │            date, country)
     │                                    │
     ▼                                    ▼
  Buy a course ◄──── Catalogue ────►  LEARN ──► DRILL ──► PRACTISE ──► MOCK
  (or start free tier)                   │        │           │          │
                                         │        │           │          ▼
                                   lessons,   targeted    full items   READINESS
                                   strategies  micro-      under exam   (predicted
                                   rulebooks   drills      conditions    band)
                                                               │          │
                                                               ▼          ▼
                                                          FEEDBACK ──► REMEDIATION
                                                          (AI +          (auto 7-day
                                                          human)          fix plan)
                                                               │
                                                               ▼
                                                        REVISE / APPEAL ──► EXAM DAY
```

### Stage-by-stage

| Stage | What happens | Key routes |
|---|---|---|
| **1. Register** | Email + password or social sign-in; email OTP verification; optional TOTP MFA with recovery codes | `/register`, `/verify-email`, `/mfa/setup` |
| **2. Onboard** | Choose profession, target band, exam date, target country, weekly study budget, focus areas. An interactive product tour is available | `/onboarding`, `/onboarding-tour`, `/writing/profile-setup/*` |
| **3. Buy** | Browse the catalogue, compare SKUs, add add-ons, check out via Stripe or PayPal | `/catalog`, `/pricing`, `/cart`, `/checkout/review` |
| **4. Diagnose** | Sit a diagnostic paper per sub-test to seed the pathway and identify weak sub-skills | `/listening/pathway` stage 1, `/readiness` |
| **5. Plan** | A daily plan (max 4 tasks/day, carry-over cap 6) and a 10-week Writing roadmap generate from your goals | `/study-plan`, `/next-actions`, `/writing/today` |
| **6. Learn** | Sub-skill lessons, strategy guides, searchable rulebooks, video library, materials | `/listening/lessons`, `/writing/lessons`, `/strategies`, `/videos` |
| **7. Drill** | Short targeted drills: dictation, Part A scanning, distractor recognition, case-note triage, pronunciation, vocabulary | `/listening/dictation`, `/reading/practice`, `/writing/drills`, `/speaking/drills` |
| **8. Practise** | Full items under exam conditions, with AI or auto marking | `/writing/practice/session/[id]`, `/reading/paper/[id]`, `/speaking/exam` |
| **9. Mock** | Full 4-subtest mock exams and per-subtest mocks, strict mode, with reports | `/mocks`, `/mocks/player/[id]`, `/mocks/report/[id]` |
| **10. Review** | Transcript evidence, distractor analysis, miss reasons, annotated letters, waveform-anchored speaking markers | `/listening/review/[id]`, `/writing/submissions/[id]/results` |
| **11. Remediate** | Auto-generated fix plans, error banks, spaced-repetition review queues | `/remediation`, `/review`, `/practice/interleaved` |
| **12. Get human review** | Request expert Writing marking; book a 1:1 Speaking session with a tutor as your patient | `/writing/expert-request`, `/private-speaking` |
| **13. Track** | Readiness score, predicted band, progress dashboards, comparative cohort view | `/progress`, `/readiness`, `/predictions`, `/dashboard` |
| **14. Exam day** | Exam-day guide, booking helper, test-day checklist, score calculator | `/test-day`, `/exam-booking`, `/score-calculator`, `/exam-guide` |

---

## 4. Core learning modules

Each module below documents: what it does, every significant feature, and the concrete benefit.

---

### 4.1 Listening 🟢

**26 learner routes.** The most mechanically strict module in the product.

#### Exam engine

- **Canonical 42-item structure** enforced by `ListeningStructureService`: Part A 24 · Part B 6 · Part C 12. Publishing a paper that doesn't match is blocked with a specific error code (`listening_part_a_count`, `listening_part_a_split`, and 13 others).
- **Five attempt modes** with distinct rule sets: Exam (CBT), OET@Home, Paper, Learning, Diagnostic. Free navigation, one-way locks, timers and confirm-tokens vary by mode.
- **Server-authoritative finite state machine**: `intro → a1_preview → a1_audio → a1_review → a2_… → b_intro → b_audio → c1_… → c2_final_review → submitted`. Mirrored in TypeScript and parity-tested against the C# implementation.
- **Two-step HMAC confirm-token** in strict modes: the first submit returns HTTP 412 with a token; the second must echo it. Prevents accidental and scripted submission.
- **Audio integrity**: pause, seek and replay are blocked in *every* mode by owner directive. Backward seeks snap back and log `audio_seek_blocked`; replays after end log `audio_replay_blocked`. Audio is fetched as an authorised blob URL (a bare `<audio>` tag cannot carry a bearer token) with `controlsList="nodownload nofullscreen noremoteplayback"`.
- **Tech-readiness gate** before strict attempts: an audio probe must be confirmed and is persisted to the attempt. A dead headset is caught *before* Part A, not after.
- **Timers**: whole-attempt countdown, warning at ≤120 s, danger at ≤30 s, auto-submit at zero with `reason: attempt_timer_expired`. Heartbeat every 15 s.

#### Learner tools

| Feature | What it does |
|---|---|
| **Part B/C highlighting** | Highlight question stems, strike through ruled-out options, flag questions for review — autosaved, 64 KB cap |
| **Part A annotation lock** | Highlighting is deliberately *disabled* on Part A (exam rule L-R08.1) |
| **Zoom** | 90–130% in 10% steps |
| **Question jumper** | Pills for direct navigation where the mode allows it |
| **Extract chips** | Show accent code and `mm:ss–mm:ss` cue window per audio extract |
| **PDF question-paper viewer** | Renders the real question paper via pdf.js |
| **Paper-mode booklet simulation** | Pencil, highlighter, eraser and print — for candidates sitting OET on Paper |

#### Practice, drills and curriculum

- **Dictation drills** — 8 clips per set (8–10 minutes), hint after 60 s, per-clip diff against the canonical transcript. Simplified spaced schedule: wrong → +1 day, first correct → +3, second consecutive → +7. Round-robins across difficulty bands.
- **12-stage pathway** — `diagnostic → foundation A/B/C → drill A/B/C → mini-test A → mini-test B+C → full paper → full CBT → exam simulation`. Stages 10–12 gate on scaled ≥ 350; stages 2–9 on ≥ 300.
- **12-stage curriculum catalogue** — numbers & units → names & spelling → … → listening loop → full mock, 15–50 min per stage.
- **8 sub-skill lessons (L1–L8)** — Detail capture · Note-taking speed · Spelling accuracy · Gist · Distractor recognition · Inference · Speaker stance · Accent adaptation. Each ~30 min with a 6-step ladder and a 4/5 quiz pass gate. *(Lesson pages are currently display-only — no embedded video player or quiz runner 🟡.)*
- **7 strategy categories** — note-taking, gist, inference, time management, accent handling, exam day — with mark-as-read and favourites.

#### Review and analytics

- **Evidence Player** — the review screen plays the exact seconds of audio that contained each answer. Click a time-coded transcript segment and it seeks and plays.
- **Extract Map** — accent, speakers and cue window per extract.
- **Per-item analysis** — your answer vs the key, a miss-reason chip, speaker-attitude chip, transcript clue, distractor explanation, and a 3-up option analysis with a written rationale.
- **Structured miss reasons** — `Empty`, `WrongNumber`, `SpellingError`, `ExtraInfo`, `WrongSection`, `Paraphrase`, computed at grade time and persisted so analytics never recompute them.
- **Analytics** — per-part accuracy, ranked weaknesses and a numbered action plan derived from your last 20 submitted attempts.
- **Stats** — L1–L8 skill radar, per-accent bar chart, 12-week roadmap grid.
- **Pronunciation deck** — a genuine SM-2 spaced-repetition deck reachable from Listening, with British and Australian audio, mastery bars, and 4-button quality rating.

#### Grading

Listening is **fully auto-graded** and deliberately **strict**:

- Part A: normalised exact match against the author key plus explicitly listed accepted synonyms. No partial credit.
- Parts B/C: option-key match, case-insensitive, with a legacy fallback for option-text submissions.
- **No fuzzy acceptance anywhere.** A stale `fuzzy_levenshtein_1` policy setting degrades to exact match by design. Levenshtein distance is used *only* to classify why you missed an item, never to award a mark.
- **Version-pinned grading** — each attempt snapshots the question-version map at first navigation. Drift is flagged and audited, never silently corrected.
- **Advisory AI layer** 🟢 — an additive, non-blocking Claude pass over Part A gaps emits `correct | acceptable | incorrect` plus a rationale. It **never** changes the score.

#### Content pipeline

Source material arrives as a quartet — question-paper PDF, audio-script PDF, answer-key PDF and an MP3. Text extraction uses PdfPig with an Azure Document Intelligence OCR fallback for scanned keys. AI extraction produces **drafts only**; an administrator must approve, and approval routes through the validated import path. Text-to-speech uses ElevenLabs; production startup *fails* if the TTS provider resolves to the silent development stub.

#### Benefits

1. **Nothing on exam day is structurally unfamiliar** — the item mix, counts and question types are the real ones.
2. **Single-pass listening is genuinely trained** — you cannot rewind your way to a false score.
3. **Pre-reading is rehearsed** — per-section preview windows train the habit that wins Part C marks.
4. **Equipment failure is caught early** — the tech-readiness probe runs before the clock starts.
5. **You hear exactly what you missed** — evidence replay of the specific 4 seconds beats guessing.
6. **"18/24" becomes actionable** — miss-reason classification tells you *you lose marks to over-answering*, not just that you lost them.
7. **Spelling and numbers get their own weapon** — dictation drills attack the highest-frequency Part A loss category in 8–10 minute doses.
8. **Accent exposure is measured** — the accent chart shows which of the exam's accents is dragging you down.
9. **Paper candidates rehearse on paper** — booklet simulation with print.
10. **Tutors see the cohort, not 30 attempts** — teacher class analytics roll up per-item weakness.

---

### 4.2 Reading 🟢

**22 learner routes.** The strictest timing model in the product.

#### Exam engine

- **Canonical structure**: Part A 4 texts / 20 items · Part B 6 extracts / 6 items · Part C 2 articles / 16 items = **42 items across 12 texts**, every question worth exactly 1 point.
- **Part A is expeditious reading and hard-locked at 15 minutes.** Writes are rejected server-side after the deadline (`part_a_locked`); Part B/C writes are rejected before it (`part_bc_not_open`).
- **Optional 10-minute break** between Part A and B/C, exam mode, once per attempt. Submit is blocked while a break is pending.
- **Parts B+C share a 45-minute window.** Total 60 minutes plus a 10-second grace period.
- **Extra-time entitlement** (0–100%) scales Part A and B/C independently for accommodations.
- **Auto-expire worker** clears attempts idle for 180 minutes.
- Practice modes bypass all locks by design.

#### Learner tools

| Feature | What it does |
|---|---|
| **Split view** | Real question-paper PDF on the left, question panel on the right |
| **PDF annotation** | Hand / Select / Text highlight / Rectangle / Freehand marker / Delete / Undo / Redo / Clear — percentage-based overlay so marks survive resize; PDF zoom 50–200% |
| **Rule-out strikethrough** | Every MCQ option has a strikethrough toggle *and* right-click support, with an `aria-live` "N options ruled out" announcement |
| **Break screen** | "Part A collected" badge with a large countdown and Resume |
| **Transition alert** | A one-time `alertdialog`: "Part A has been submitted and locked — you cannot return to it. You now have N minutes for Parts B & C." |
| **Countdown warnings** | Toasts at 120 s and 60 s; auto-submit fires once at zero; the confirm modal lists unanswered items |
| **Accessibility profile** | Font scale 90/100/110/125%, high-contrast palette, extra screen-reader hints — persisted per paper to local storage |
| **Autosave** | Debounced 400 ms, forced within 5 s of a deadline; submit is idempotent via an idempotency key |

#### Practice system

- **Learning Mode** — untimed per-paper study with the Part A lock suppressed.
- **Error Bank** — every wrong answer is upserted automatically with a `TimesWrong` counter and resolved only when you answer it correctly later. **Error Bank Retest** turns up to 10 open misses into a timed session (~30 s/question, clamped 3–30 min).
- **Six skill drills** — Part A scan (8 q / 6 min) · Part B distractor (6 q / 8 min) · Part C inference (6 q / 10 min) · Part C attitude · Part C vocabulary · Part C reference.
- **Mini-tests** — 5/10/15 minutes → 6/12/18 questions, sampled on the real 20:6:16 ratio.
- **Second practice player** with highlight / underline / strikethrough / sticky-note annotation, progress dots and mark-for-review.

#### Skill tree and pathway

- **8 skills S1–S8**, each scored /10: Scanning · Skimming for gist · Paraphrase recognition · Distractor pattern recognition · Inference & implied meaning · Reference resolution · Vocabulary in context · Time management.
- **Pathway ladder**: `not_started → foundation → drilling → mini_tests → mock_ready → exam_ready`. Drilling triggers on ≥5 open error-bank entries or best scaled < 300; exam-ready requires a recent mock ≥ 350.

#### Vocabulary sub-module

A genuine **SM-2 spaced-repetition** system: daily cap 30 cards, easiness seeded 2.5 and floored 1.3, intervals `1 → 6 → interval × easiness`. Adding a word generates a master card through the grounded AI gateway. Four curated lists ship: **Top 200 OET Medical Terms**, Medicine & Surgery, Nursing & Allied Health, Pharmacy & Pharmacology, each bulk-subscribable. Buckets: mastered ≥90, learning 50–89, struggling <50.

#### Grading

- Same `30/42 ≡ 350/500` anchor as Listening. Subset scopes (drills, mini-tests, error-bank retests) grade only their own items, set `MaxRawScore` to the subset total, and deliberately return **no scaled score** — the OET anchor only applies to a full 42-item paper.
- **Part A is the strictest path in the product**: exact text match, no synonyms, no fuzzy matching — because real OET Part A answers are copied word-for-word from the text. The publish gate emits a *blocking* error if an author tries to enable fuzzy matching.
- Optimistic concurrency via row versions; grade-at-submit is idempotent, first submit wins.
- **Post-submit disclosure is guaranteed by owner directive** — the correct answer and explanation always return once an attempt is submitted, in every mode, overriding admin toggles. Pre-submit disclosure is hard-blocked.

#### Content pipeline

Three authoring modes: hand-authored, AI-assisted extract-from-PDF (draft only), and JSON manifest import (idempotent, atomic). Questions move through a **linear review chain**: `Draft → AcademicReview → MedicalReview → LanguageReview → Pilot → Published → Retired`, with one-step rollback at each gate and a full audit log per transition. **The publish gate blocks unless all 42 questions have cleared the chain.** Distractor QC throws if an author tags the correct answer as a distractor.

**Paper-quality analytics** use classic upper/lower-27% item discrimination with risk codes `too_hard` (<0.20 facility), `too_easy` (>0.95) and `low_discrimination` (<0.20).

#### Benefits

1. **The 15-minute Part A pressure is real** — the lock is enforced on the server and survives a bypassed browser.
2. **The real sitting rhythm is reproduced** — Part A, break, then a shared B/C window, not one undifferentiated hour.
3. **You work from the actual question paper**, not a re-typed approximation.
4. **Elimination becomes visible and reviewable** — strikethroughs persist and are replayed in review.
5. **Word-for-word copying is trained** — strict Part A grading teaches exactly what OET marks.
6. **Missed questions can't be forgotten** — the error bank captures them and only clears on a correct answer.
7. **Short sessions still count** — 5/10/15-minute mini-tests are exam-shaped and ratio-balanced.
8. **You always learn why** — guaranteed post-submit explanations plus miss reasons and distractor categories.
9. **Pacing problems surface** — per-question timing is captured on every autosave.
10. **Accommodations persist** — font scale, contrast and extra time are remembered per paper.

---

### 4.3 Writing 🟢

**46 learner routes** — the largest module. This is where the platform's academic authority is most concentrated.

#### The rulebook

**172 rules × 13 professions, across 16 sections** — Letter Types · Exam Structure & Case Notes · Content & Data Selection · Page Layout · Address & Date · Salutation & Re: Line · Introduction · Body Paragraphs · Closure · Tenses · Medications & Investigations · Grammar/Vocabulary/Linkers · Urgent Referral · Discharge Letter · Referral to Non-Medical Professionals · Assessment Criteria.

Severity distribution: **56 critical · 46 major · 37 minor · 33 info**. Authority string: *"Dr. Ahmed Hesham — The Tutor Book — OET Writing Comprehensive Rulebook (Sessions 1–10)"*, v1.0.0. Rules carry examples, exemplar phrases, forbidden regex patterns and an enforcement mode (`deterministic` | `ai-grounded` | `human-review-only`).

One rule worth quoting to learners verbatim — **R16.5 (critical)**: *"Language does not need to be perfect. Most OET failures result from poor content selection, not grammar errors."*

#### Journey

| Stage | Route | What happens |
|---|---|---|
| **Onboard** | `/writing/welcome` → `/writing/profile-setup/*` | 4-step wizard: profession & experience → target band, exam date, target country, study budget → ≥2 focus letter types → confirm |
| **Plan** | `/writing/pathway`, `/writing/today` | 10-week roadmap with phases, themes and mock markers; a daily plan with Start / Complete / Skip and quota-limited regeneration |
| **Learn** | `/writing/skill-tree`, `/lessons`, `/canon`, `/common-mistakes` | 8 sub-skills W1–W8; markdown lessons with drills and quizzes; a searchable rule library; your personal top-5 recurring errors |
| **Drill** | `/writing/drills/*`, `/case-notes-drills` | Six drill categories (relevance, opening, ordering, expansion, tone, abbreviation) shipped for all 12 professions, plus case-note triage against gold-standard labels |
| **Practise** | `/writing/practice/session/[id]` | The core AI-graded loop |
| **Mock** | `/writing/mocks/*`, `/writing/paper/session/[id]` | Computer or paper, strict conditions, human-marked |
| **Feedback** | `/writing/submissions/[id]/results` | Scores, criteria, highlights, tutor notes, rule violations |
| **Revise** | `/writing/submissions/[id]/revise` | Rewrite with violations as inline annotations |
| **Appeal** | `/writing/submissions/[id]/appeal` | Independent second-opinion regrade |

#### The practice session — the most mechanically dense screen in the product

- **Credit gate fires first.** Eligibility (2 AI grading credits) is checked *before* the case notes load, so you never burn the clock on a session you cannot submit.
- **Strict 45-minute clock**: 5 minutes forced reading behind a non-skippable full-screen overlay, then 40 minutes writing, then hard auto-submit. Deadlines persist to session storage — **refreshing does not buy you extra time**.
- **Case-notes PDF highlighter** — yellow highlights persist across the reading→writing boundary, autosave every 800 ms, pre-load on future attempts, and are **snapshotted onto the submission** so they replay on your results page.
- **Draft autosave every 5 seconds. Paste is blocked. Word counter targets 180–220.**
- On insufficient credits the draft is preserved and you're routed to buy more.

#### Mock exams — guaranteed AI-free

- Mode matrix: **Computer × Paper** by **Strict × Practice** conditions.
- Computer mode uses server-anchored phases, a `beforeunload` guard, spellcheck off, and an exam-fidelity strip: *"No spellcheck · no hints · no AI · no model answer."*
- Paper mode renders a booklet simulation with writer role, date, instruction bullets and a 180–200 word guide.
- **Mocks are never AI-graded.** The pipeline hard-branches to `AwaitingReview` before the idempotency cache and before any AI call. Results poll for the human examiner's mark.
- 11 attempt-event types are captured as telemetry (`attempt_started`, `reading_started`, `paste`, `focus_lost`, `timer_expired`, …).

#### AI grading

- **Six official criteria**, raw max 38, temperature 0.2, model `claude-sonnet-5` with a `gpt-4o` fallback, prompt caching on.
- **Grounding is mandatory and checked four ways before any credit is debited**: mock-context calls are refused outright; a null prompt throws; an empty system prompt throws; and the system prompt must literally contain `"OET AI — Rulebook-Grounded System Prompt"`.
- **Rules are injected verbatim** — all CRITICAL rules first ("violations are auto-mark-deductions; flag them first"), then up to 60 MAJOR rules.
- **Citation validation is real.** Returned rule IDs are intersected against the grounded prompt's allowlist and anything else is dropped. A hallucinated `R99.9` never reaches a learner.
- **Fail loud** — malformed AI output produces status `failed` + a retryable 503, with the code comment: *"Never fabricate a grade — fail loud and retryable so the learner can re-run rather than receive a fake 'all 3s' score."* Every failure path refunds the credit.
- **24-hour idempotency cache** — resubmitting identical content within 24 hours clones the previous grade at no cost and no AI call.
- **Canon engine** — a second, DB-backed rule layer with three detection types: regex (150 ms timeout, context-whitelisted), structural (5 named matchers), and LLM (one call per rule). LLM failure is non-fatal; regex and structural violations still apply.
- **Result visibility** is admin-controlled across 10 booleans (show submission received, AI estimate, tutor score, full criteria, annotated response, missing content, model answer, content checklist, allow rewrite), globally and per scenario.

#### Feedback surfaces

- **Score gauge** with band label and raw `/38`.
- **Criteria radar** with expandable per-criterion rows carrying AI feedback and an `exemplarFix`.
- **Your own highlighted case notes replayed read-only** — see what you noticed versus what mattered.
- **The answer-sheet PDF**, revealed only post-submission.
- **Tutor free-text plus a voice note** (up to 600 s).
- **Canon violation cards** with character offsets, line numbers, a 240-char snippet, and a per-violation **dispute** button.
- **Top-3 priorities**, severity-ranked.

#### Revise and appeal

- **Revise** pre-fills the editor with your original letter and converts every violation into an **inline annotation** with `charStart`/`charEnd`/`ruleId`/`suggestedFix`. Submit is disabled until the text actually differs. A new submission row is created, linked to the original.
- **Appeal** runs an independent second AI examiner (`writing.appeal.v1`, temperature 0.2). If the two scores differ by more than 3 raw points they are **averaged**; otherwise no change. The result renders as a 3-up comparison: Original / Second opinion (with Δ) / Final on record.

#### Tools and community

| Feature | What it does |
|---|---|
| **Ask about your letter** | Chat-style Q&A grounded in what you actually wrote |
| **Paraphraser** | Alternatives with formality badges and copy buttons |
| **Phrase suggestions** | Before→after diffs by type with confidence %, Accept/Dismiss |
| **Showcase** | Anonymised, PII-redacted A-grade letters filtered to your profession and letter type |
| **Writing buddy** | Anonymous study-partner pairing with chat and weekly check-ins |
| **Compare / Model** | Side-by-side against a model answer with paragraph-level include/exclude rationale |

#### Stats

Ten learner-facing endpoints: dashboard (latest band, trend delta, days to exam, streak, top weakness) · 30-point band history vs target · criteria current-vs-target · per-letter-type averages · top 15 violated rules · time distribution and % finishing within 40 min · W1–W8 mastery · **readiness** · 90-day heatmap.

**Readiness blend**: `0.50 × mock average + 0.20 × trajectory slope + 0.15 × canon-clean rate + 0.10 × time management + 0.05 × cross-type consistency`. Crossing 80 upward publishes a green-light event.

**Weakness dashboard** maps violations to 8 canonical error tags — `missing_key_content`, `irrelevant_content`, `unclear_purpose`, `informal_tone`, `abbreviation_issue`, `poor_paragraphing`, `inaccurate_transfer`, `grammar_articles` — and routes the top tag to a recommended drill.

#### Benefits

1. **Real exam conditions** — a 45-minute clock with a forced reading window that survives refresh.
2. **Scored on the actual OET rubric** — all six criteria, not a generic writing score.
3. **Every claim is traceable** — findings are filtered against the active rulebook's allowlist.
4. **172 profession-specific rules** behind the feedback, across 13 professions.
5. **Errors are located, not described** — line numbers, character offsets and the offending snippet.
6. **Fix in place** — the revise flow reopens your letter with each violation as an actionable annotation.
7. **No fabricated grades** — malformed AI output fails loudly and retryably.
8. **Never pay twice** — a 24-hour idempotency cache reuses the prior grade for identical content.
9. **Credits are refunded on failure** — automatically, on every failure path.
10. **You know before you start** whether you can afford the session.
11. **Highlight like on paper** — and see your highlights replayed against what actually mattered.
12. **Mocks are genuinely AI-free** — four independent guarantees.
13. **Mocks are human-marked** with per-criterion comments and a voice note.
14. **Paper or screen** — practise in whichever format your real exam uses.
15. **Challenge a score** — an independent second examiner, with automatic averaging on wide divergence.
16. **Challenge an individual flag** — dispute any rule violation.
17. **Track trajectory, not just the last score.**
18. **Know your personal top-5 recurring mistakes**, each linked to the rule that explains it.
19. **Learn in the right order** — 8 sub-skills from case-note triage to layout.
20. **Attack the real cause of failure** — case-note triage drills target relevance judgement, the single biggest cause of OET Writing failure.
21. **Learn from real A-grade letters**, anonymised and filtered to your profession.
22. **Country-aware pass mark** — Writing is the only sub-test where the threshold differs by destination.
23. **Watch grading happen live** instead of staring at a spinner.
24. **Work survives everything** — 5-second autosave through credit errors, timeouts and refreshes.

---

### 4.4 Speaking 🟢

The most technically ambitious module: a real-time voice conversation with an AI patient, or a live video session with a human tutor playing the patient.

#### The two-card exam engine

- **State machine**: `intro → prep_a → active_a → prep_b → active_b → completed` (+ `cancelled`, `expired`). No bridge step, no manual advance.
- **Timing**: 180 s preparation + 300 s discussion per card, overridable per card.
- **Card A auto-closes and Card B auto-reveals** with no learner action, enforced in three independent layers: a 20-second background sweeper, a lazy re-check on every exam API read, and a real-time SignalR `TimeUp` callback.
- **Restart-safe** — every transition is recomputed from persisted timestamps, never from in-memory timers. Idle exams expire after 2 hours.
- **The intro is unscored and free.** Credits are debited only at card reveal.

#### Exam realism

- The prep screen instructs you to bring blank paper and a pen for rough work, matching the real exam.
- **The candidate opens the consultation** — the AI patient stays silent until spoken to, exactly as in the real test. (The warm-up keeps interlocutor-first.)
- **Barge-in is allowed** — you may talk over the patient; playback stops and the turn is flagged for the grader.
- **You are never cut off** mid-turn; the 210-second cap sends rather than discards.

#### The AI patient

Always-on microphone → client-side voice-activity detection → automatic segmentation → Whisper transcription → Claude in-character reply → ElevenLabs speech → autoplay → auto-resume. The result feels like a conversation, not a form. Patient voices are **cast per scenario** — gender, age, accent and emotional tone are properties of the role-play card.

#### Content model

`RolePlayCard` (candidate-facing) pairs 1:1 with a hidden `InterlocutorScript` (the patient persona). Cards are scoped to your profession. There are **6 internal card types** visible only to admins, tutors and the AI — tested to never reach a learner payload, with separate learner and admin DTOs.

#### Learner features

| Feature | What it does |
|---|---|
| **Speaking hub** | Take a full two-card AI exam (2 credits) or book a tutor as your patient |
| **Profession gate** | Mandatory selection; content is scoped to it |
| **Role-play card library** | Filterable catalogue of published cards |
| **Self-practice recorder** | Native-capable recorder for unscored rehearsal |
| **Drills** | Single-criterion micro-practice, filterable by drill kind and criterion, with AI feedback per criterion (ungraded by design) |
| **Better Phrasing** | Up to 5 segments of "your original phrase → why it hurt you → stronger alternative → drill prompt" |
| **Transcript review** | Timestamped transcript with inline markers over a **real waveform** of your own recording, role-card context, and a rulebook audit |
| **Fluency timeline** | Average WPM vs an ideal band, filler count and ratio, long-pause count, per-segment good/fair/poor rating 🟡 *(currently requires pasting an attempt ID)* |
| **Results** | Performance summary, criterion breakdown, tone-of-voice card, key strengths, top improvements, pronunciation insight |
| **Rulebook browser** | Every speaking rule grouped by section and severity, with exemplar phrasing |
| **16-stage course pathway** | Intro to format → understanding the card → using the 3-minute prep → opening naturally → rapport → open/closed questions → exploring ICE → explaining simply → signposting → checking understanding → handling angry patients → time management → profession-specific role-play → recorded mock #1 → feedback & drills → final simulation. Stages unlock from real activity 🟡 *(catalogue is in-memory, not yet persisted)* |
| **Recordings** | Self-management with a retention countdown and GDPR self-delete |
| **PDF report** | Watermarked, rate-limited, forensically tokenised |

#### Live tutor sessions

Real-time rooms over **LiveKit**, with capability-scoped JWTs (learner: join + publish own + subscribe; tutor: + room admin), track-composite recording to S3, HMAC-verified webhooks with an IP allow-list, and an append-only event log. Tutors can raise cues that appear live on the learner's screen.

#### Assessment

**Nine criteria** — 4 linguistic scored 0–6 (Intelligibility · Fluency · Appropriateness of Language · Resources of Grammar and Expression) and 5 clinical-communication scored 0–3 (Relationship building · Understanding patient perspective · Providing structure · Information gathering · Information giving).

**Projection**: `((linguistic_avg/6 × 0.55) + (clinical_avg/3 × 0.45)) × 500`, rounded to the nearest 10. Readiness bands: ≥400 Strong · 350–399 Exam-ready · 300–349 Borderline · 200–299 Developing · <200 Not ready.

**Evidence verification** — every AI criterion rationale must include a verbatim transcript quote, **verified server-side as an actual substring of the transcript**. Non-verifiable quotes downgrade the confidence band and tag the assessment for human review.

**Tone assessment (RULE_40)** — a dedicated assessor derives proxy acoustic metrics from Whisper segment timings and confidence (mean confidence ≈ clarity, p90 pause, mean segment length) to judge whether a "Breaking Bad News" card was delivered softly and empathetically — something a transcript alone cannot capture. Bands are deliberately generous at the top because tone is hard to score from text; the tutor can downgrade.

**Fail loud** — AI provider errors mark the evaluation failed with a retryable code and **refund the credit**. Quota exhaustion refunds too. An ungrounded prompt throws.

**Human marking** requires all three of: all 9 criteria explicitly scored and in range, non-empty overall feedback, and **at least one timestamped comment**. Calibration deltas versus the AI are computed and stored on every submission.

**Double marking and moderation** — variance threshold 30 scaled points. Within threshold, the two markers auto-average; beyond it, senior moderation is required. Separation of duties is enforced in-service: the second marker cannot be the first, and the moderator cannot be either.

**Which score is official?** For **AI exams**, the AI score is the released result and those exams are excluded from the human queue. For **live-tutor exams**, the human mark is authoritative and the AI evaluation parks as `awaiting_human_review`.

#### Benefits

1. **Practise 24/7** — an AI patient plays the role any time, in character, with a voice cast to the scenario.
2. **It feels like a conversation** — always-on mic, auto-segmentation, autoplay replies, permitted barge-in.
3. **Marked on the real 9-criterion rubric.**
4. **A 0–500 score anchored to 350/B**, plus a plain-English readiness band.
5. **Every claim is quoted and verified** — no unfalsifiable feedback.
6. **Find the exact moment it went wrong** — timestamped markers over your own waveform.
7. **Told what to say instead** — Better Phrasing gives you a rehearsable alternative.
8. **Fix one weakness at a time** — single-criterion drills.
9. **See pace, pauses and fillers** laid out numerically.
10. **Book a real tutor as your patient** — practice or full two-card exam format.
11. **Your tutor is calibrated** — tutors drifting more than 40/100 from gold standards are hidden from booking until re-calibrated.
12. **Disagreement triggers a second opinion**, with strict separation of duties.
13. **Follow a route** — a 16-stage pathway, not a pile of exercises.
14. **Walk away with a shareable report.**
15. **Your voice data stays yours** — versioned consent, retention countdown, one-click deletion, erasure pre-flight.
16. **Never charged for a broken run.**

---

### 4.5 Conversation — AI role-play trainer 🟢

A real-time voice role-play trainer, distinct from the Speaking exam engine and aimed at conversational fluency rather than exam scoring.

- **Two task types**: `oet-roleplay` (AI plays the patient) and `oet-handover` (AI plays a receiving colleague).
- **13 seeded scenarios**, all 300 seconds: Medicine ×6 (hip-replacement discharge, T2 diabetes lifestyle, breaking bad news / abnormal mammogram, statin counselling, ED chest-pain handover, post-op sepsis ward handover), Nursing ×3 (falls prevention, anxious-parent vaccination, acute confusion handover), Pharmacy ×2 (warfarin start, asthma inhaler technique), Physiotherapy ×1 (post-ACL rehab), Dentistry ×1 (root canal consent).
- **Scenario data** carries objectives, expected red flags, key vocabulary, patient voice casting and a time limit. Publishing requires title, scenario, role, patient context, ≥3 objectives and a valid duration.
- **The learner opens** the conversation; the AI replies in 1–3 sentences with an emotion hint (`neutral | worried | frustrated | calm | in-pain`).
- **Barge-in is first-class** — interruptions are flagged and factored into the appropriateness criterion.
- **Two mandatory consent checkboxes** before recording — recording consent and vendor speech-processing consent, with retention disclosure.
- **Evaluation**: 4 criteria (intelligibility, fluency, appropriateness, grammar & expression) scored 0–6, each with prose evidence *and* verbatim quotes. Aggregated and projected on a piecewise-linear curve anchored at **4.2 → 350 (Grade B pass)**.
- **Results**: grade disc, rubric bars with up to 3 quotes each, strengths and improvements, full transcript with per-turn ASR confidence and inline audio, annotation badges, practice suggestions, rules-applied list.
- **Every error and improvement annotation automatically seeds a Review Module item** — mistakes become revision without you filing them.
- **Transcript export** as TXT or PDF.
- **Entitlement**: paid/trial unlimited; free tier 3 sessions per rolling 7 days.

---

### 4.6 Pronunciation 🟡

A technically complete backend with a currently limited learner surface.

**What exists and works:**

- **Scope**: phoneme accuracy, consonant clusters, word stress in medical vocabulary, sentence stress and rhythm, clinical intonation.
- **Four scoring dimensions 0–100**: accuracy, fluency, completeness, prosody.
- **OET projection** anchored at **70 → 350 (Grade B)** — described in code as "sacrosanct" — through 80→400, 90→450, 100→500.
- **Three real ASR providers**, auto-ordered azure → gemini → whisper (mock is refused in production):
  - **Azure** is the only true phoneme engine — per-word accuracy with error type (Mispronunciation / Omission / Insertion) *and* a nested per-phoneme accuracy array, plus speech rate and pause statistics. Surfaces your **bottom-5 worst phonemes** with occurrence counts and rule IDs.
  - **Gemini** sends raw audio through the grounded gateway; if it cannot produce phoneme data it returns null and rejects the response rather than emitting nothing.
  - **Whisper** has no native phoneme data and therefore emits a single honest synthetic summary. The code comment is explicit: *"never silently emit fake per-phoneme scores."*
- **Per-phoneme progress rollup** with an all-time cumulative mean and the last 20 score-history entries.
- **Spaced repetition** ("SM-2-lite"): quality ≥85 raises ease by 0.10, <70 lowers it by 0.15, clamped 1.3–2.9; quality <60 repeats same day; intervals cap at 30 days. Due drills are ordered **weakest-first**, with cold-start top-up from never-attempted phonemes.
- **Rulebooks** per profession, embedded at build time so a running instance cannot diverge from its shipped snapshot.
- **Speaking linkage** — expert-reviewed speaking attempts create advisory pronunciation rows, surfaced as the "Pronunciation Insight" card on speaking results.
- **Full admin CMS** with a publish gate (label, target phoneme, tips, ≥3 example words, ≥1 practice sentence) and AI drafting that strips unknown rule IDs.
- **Entitlement**: paid/trial unlimited; free tier 20 attempts per rolling 7 days, checked *before* recording so the paywall never appears after you've spoken.

**Current limitation 🟡:** the learner-facing UI is read-only. There is no recording button in the pronunciation section, and the minimal-pair discrimination drill is a static stub. The Listening module's SM-2 pronunciation deck (§4.1) is the shipped learner-facing pronunciation experience today. Wiring the recorder is the single highest-leverage item on the roadmap (§17).

---

### 4.7 Vocabulary 🟢

- **Browse, flashcards, quiz, quiz history, per-term detail** (`/vocabulary/*`), plus the Reading module's own vocabulary sub-module (§4.2).
- Genuine **SM-2 spaced repetition** with a 30-card daily cap.
- **AI-generated medical glosses** through the grounded gateway when you add a word.
- **Curated lists** with bulk subscribe, led by *Top 200 OET Medical Terms*.
- **ElevenLabs audio** generated for vocabulary terms via a background worker.
- Admin CMS with AI drafting, bulk import and recall-set tagging.

---

### 4.8 Grammar 🟢

Topic-based grammar lessons (`/grammar`, `/grammar/topics/[slug]`, `/grammar/[lessonId]`) with a seeded catalogue split across four specification files. Admin CMS at `/admin/content/grammar` with AI drafting, topic management and lesson authoring. HTML explanations are sanitised at persist time — raw admin HTML never renders unsanitised.

---

### 4.9 Recalls 🟢

Recall-based preparation is a signature of the brand: **real exam content patterns reported by recent candidates**, curated into study sets.

- `/recalls`, `/recalls/words`, `/recalls/favourites` for learners.
- Admin recall management with **bulk upload** and recall-set tagging.
- Recall data ships as copy-to-output seed files.
- **`recallUpdatesEnabled` is a commercial property of each SKU** — most courses include continuing recall updates for the life of the access window, which is a recurring reason to buy the higher tiers.

---

### 4.10 Video library and materials 🟢

- **Bunny.net Stream** backs the video library: TUS resumable upload, HLS playback over CDN, token-authenticated URLs (default TTL 4 hours) and encode-status webhooks.
- **Anti-piracy**: the desktop app applies OS-level screen-capture exclusion (`WDA_EXCLUDEFROMCAPTURE` on Windows, `NSWindow.sharingType = None` on macOS) while video plays, and playback is gated by an HMAC attestation key baked into each app build.
- Learner surfaces: `/videos`, `/videos/[id]`, `/materials`.
- Admin: full video CRUD with categories, collections, per-video access rules, review workflow, extras and per-video analytics; plus a materials library and a media manager.

---

### 4.11 Strategies, guides and exam-day tools 🟢

| Surface | What it gives you |
|---|---|
| `/strategies`, `/strategies/[id]` | Cross-module strategy guides |
| `/listening/strategies`, `/reading/strategies` | Sub-test-specific strategy libraries with mark-as-read and favourites |
| `/exam-guide` | How the OET exam works end to end |
| `/test-day` | Test-day checklist and preparation |
| `/exam-booking` | Guidance on booking the real exam |
| `/score-calculator`, `/dashboard/score-calculator` | Convert raw scores to OET scaled scores and grades |
| `/feedback-guide` | How to read the feedback the platform gives you |
| `/ielts-guide` | Comparative guidance for candidates weighing IELTS |
| `/speaking/rulebook`, `/writing/canon` | The rulebooks themselves, browsable and searchable |

---

## 5. Mock exams and readiness 🟢

The **Mock Center** (`/mocks`) is the single place all mock exams live — the per-subtest mock routes (`/listening/mocks`, `/reading/mocks`, `/speaking/mocks`, `/writing/mocks`) all redirect here by owner directive.

### What's in it

| Feature | Detail |
|---|---|
| **Full mocks** | All 4 sub-tests in one bundle: Listening and Reading auto-marked, Writing and Speaking AI-graded (or human-marked in strict/mock mode) |
| **Sub-test mocks** | Single-sub-test mocks for targeted rehearsal |
| **Strict player** | `/mocks/player/[id]` — full exam enforcement, no hints, no AI |
| **Simulation** | `/mocks/simulation` — end-to-end sitting simulation |
| **Setup & readiness** | `/mocks/setup`, `/mocks/readiness` — pre-flight checks and eligibility |
| **Bookings** | `/mocks/bookings`, `/mocks/bookings/new` — scheduled mocks with reminder notifications at 24 h / 2 h / 30 min for both learner and examiner; reschedule limit is admin-configurable (default 2) |
| **Speaking room** | `/mocks/speaking-room/[bookingId]` — audio-only live room, ~7-second chunked upload, SHA-256 deduplication, consent-gated, SignalR-driven with REST fallback |
| **Writing section** | `/mocks/writing/[sectionAttemptId]` — the 5+40 minute writing section inside a full mock |
| **Reports** | `/mocks/report/[id]` — per-sub-test and overall scores with an OET-style statement-of-results card |
| **Resume** | Resumable attempts surface on the mock home screen |
| **Recommendation** | A "recommended next mock" with rationale, latest score, trend and readiness tier |

### Mock operations (admin)

A full content pipeline: a **bundle wizard** (`/admin/content/mocks/wizard/[bundleId]/*` for bundle → listening → reading → writing → speaking → review), a **review-stage rail**, **item analysis** with distractor histograms, **leak reports**, **randomisation** controls, **entitlement** rules, and **mock analytics**.

**Diagnostic mock gating is not hardcoded** — it is a property of each subscription package, configurable in the billing admin as `unlimited | one_per_lifetime | one_per_renewal_period | paid_per_use | disabled`.

### Readiness engine 🟢

`/readiness` and `/predictions` blend mock performance, trajectory, error-clean rate, time management and consistency into a single predicted band per sub-test, plus a cohort-comparative view at `/progress/comparative`. Admins get `/admin/readiness`, `/admin/readiness/[userId]` and `/admin/readiness/metrics`.

**Benefits:** you find out you're not ready *before* you pay the exam fee; a specific readiness tier tells you what to fix; and the recommendation engine picks your next mock so you don't have to.

---

## 6. Personalisation engine 🟢

| System | What it does |
|---|---|
| **Study plan** | `/study-plan`, `/study-plan/calendar`, `/study-plan/drift` — a generated plan with calendar view and drift detection when you fall behind. Admin study-plan templates and per-learner plans exist |
| **Goals** | `/goals`, `/goals/study-commitment` — declare a target band, exam date and weekly commitment |
| **Daily plan** | Max 4 tasks/day, carry-over cap 6, with per-task time budgets (drill 15 · vocab 10 · wrong-review 10 · strategy 5 · lesson 30 · mock 60 min) |
| **Next actions** | `/next-actions` — the single clearest next step, always |
| **Adaptive difficulty** | `AdaptiveDifficultyService` + `/v1/adaptive` — item selection adjusts to demonstrated ability |
| **Spaced repetition** | `Sm2Scheduler`, `SpacedRepetitionService` — powers vocabulary, pronunciation and review queues |
| **Review module** | `/review`, `/reviews` — a unified queue seeded automatically from conversation annotations, writing violations and wrong answers |
| **Remediation** | `/remediation` — auto-generated 7-day fix plans targeting your specific weaknesses |
| **Predictions** | `/predictions` — predicted band per sub-test from current trajectory |
| **Interleaved practice** | `/practice/interleaved`, `/practice/quick-session` — mixed-skill sessions and short-burst practice |
| **Error banks** | Per-module wrong-answer capture with automatic resolution |
| **Freeze** | `/freeze` — pause your subscription clock for illness, shifts or life, with an admin approval workflow and dedicated notifications |

**Benefit:** the platform decides what you should do today, so a time-poor clinician spends their 30 minutes on the highest-yield activity instead of choosing between 320 pages.

---

## 7. The human layer 🟢

AI does volume; humans do the moments that matter.

### 7.1 Expert / examiner portal (`/expert/*`, ~40 routes)

| Area | Capability |
|---|---|
| **Queue** | `/expert/queue`, `/expert/queue/assigned`, `/expert/queue-priority` — claim-locked assignment (first write wins, 15-minute idle claim TTL, fairness ordering by profession match then age) |
| **Writing review** | Full letter, task, stimulus PDF **with the learner's own highlights**, AI grade, AI pre-assessment, existing annotations, moderation state, marker sequence and voice note |
| **Speaking review** | `/expert/speaking/*` — queue, sessions, assessment, live rooms, moderation |
| **Listening & Reading review** | Paged attempt queues, non-redacted privileged review, score override with mandatory reason, per-scope feedback (test / section / question / skill), cohort RAG analytics |
| **Calibration** | `/expert/calibration`, `/expert/calibration/speaking` — gold-standard samples with per-criterion drift reporting. The gold rubric is hidden from tutors; they see only their own error after submitting |
| **Compensation** | `/expert/compensation` — earnings tracking |
| **Messaging** | `/expert/messages` — learner ↔ expert threads |
| **Metrics & quality** | `/expert/metrics`, `/expert/scoring-quality` |
| **Live classes** | `/expert/live-classes` |
| **Learners** | `/expert/learners`, `/expert/learners/[id]` — cohort management |
| **Mobile review** | `/expert/mobile-review` — mark on a phone |
| **Onboarding** | `/expert/onboarding` — examiner induction |

### 7.2 Tutor portal (`/tutor/*`)

Dashboard, profile, availability, classes (list / detail / create), earnings, and the Writing review queue with calibration.

### 7.3 Private 1:1 Speaking sessions 🟢

Real one-to-one sessions over **Zoom**, with a tutor playing your patient.

- **Lifecycle**: `Reserved → PendingPayment → Confirmed → ZoomPending → ZoomCreated → InProgress → Completed`, plus Cancelled / Refunded / NoShow / Expired / Failed.
- **Two payment rails**: a session credit from your entitlement (booking created directly confirmed, consumed inside a serializable transaction), or PayPal (order created *before* persistence so a gateway failure leaves no dangling reservation).
- **Defaults**: £50, 30-minute slot, 10-minute buffer, 24-hour minimum lead time, 30-day maximum advance, 15-minute reservation timeout — all admin-configurable.
- **Cancellation**: >48 h before start → full refund; ≤48 h → no refund; after start → treated as no-show. Expert or admin cancellation always refunds fully.
- **Reschedule**: free beyond 24 h, or within 24 h if it's a different calendar day in your timezone; otherwise a 50% same-day penalty. The original booking holds the slot until the penalty is paid.
- **No-show**: 15-minute grace. Attendance is verified only when a Zoom `participant_joined` event's email matches the learner's — host and unknown joins never verify.
- **Availability**: recurring rules plus overrides, evaluated dynamically against **Google Calendar free-busy** — and free-busy failure is **fail-closed** (a booking is refused rather than double-booked).
- **Calendar integration**: per-tutor Google OAuth with encrypted refresh tokens; events created on the tutor's primary calendar with 60-minute popup and 24-hour email reminders; ICS download for both sides.
- **Join window** opens 30 minutes before and closes 15 minutes after the end; Zoom renders in-app with a new-tab fallback. After three Zoom creation failures the booking fails and the entitlement is restored.
- **Calibration gates booking** — a tutor whose scoring drift exceeds 40/100 has their slots hidden until an admin posts a time-boxed override.
- **Rating**: 1–5 stars plus feedback after completion, feeding the tutor's average.

### 7.4 Live classes 🟢

`/classes`, `/classes/[id]`, `/me/classes/*` — scheduled group classes over Zoom, with upcoming/past views, session join, transcript, recordings and post-class feedback. Admin manages classes at `/admin/live-classes`.

### 7.5 Community and peer learning 🟢

| Feature | Route |
|---|---|
| **Threads** | `/community/threads/*` — create, edit, browse, my threads |
| **Groups** | `/community/groups` |
| **Ask an Expert** | `/community/ask-an-expert`, `/expert/ask-an-expert` |
| **Peer review** | `/community/peer-review`, `/peer-review` — learners review each other's work |
| **Reading Q&A** | `/reading/community/[questionId]` — per-question discussion with expert badges |
| **Writing showcase** | Anonymised A-grade letters |
| **Writing buddy** | Anonymous study-partner pairing with weekly check-ins |
| **Leaderboard** | `/leaderboard` — opt-in |
| **Achievements** | `/achievements`, `/achievements/certificates` — certificates of completion |
| **Escalations** | `/escalations` — raise an issue and track it |

**Gamification** exists but is deliberately restrained, matching the brand's anti-gamification stance: XP (10/question, 25/correct, 100/lesson, 250/mock), 10 levels, five badges (`foundation_graduate`, `bookworm`, `speed_demon`, `bullseye`, `mock_master`), and streaks that qualify on 8 questions/day.

---

## 8. Scoring and assessment integrity

This is the section that underwrites the product's core claim: *you can trust the number*.

### 8.1 One scoring module, enforced by the build

Listening and Reading share a single conversion implemented identically in C# (`OetScoring.cs`) and TypeScript (`lib/scoring.ts`), verified by 98 and 72 assertions respectively:

```
Raw max 42 · Raw pass 30 · Scaled pass 350 · Scaled max 500

raw  0 → 0
raw 30 → 350   (exact anchor)
raw 42 → 500   (exact anchor)

[0..30]  → [0..350]    slope 350/30
[30..42] → [350..500]  slope 150/12
```

Grade bands: **A** 450–500 · **B** 350–449 · **C+** 300–349 · **C** 200–299 · **D** 100–199 · **E** 0–99.

**Enforcement:** inline scoring arithmetic (`* 350`, `/ 42`, `* 8.33`) anywhere in the Listening service tree **fails the build** via a source-scanning audit test. A paper with no gradable questions throws rather than emitting a misleading 0/500.

### 8.2 Sub-test projections

| Sub-test | Projection |
|---|---|
| Listening / Reading | Piecewise linear, anchored 30/42 ≡ 350/500 |
| Writing | `raw × 500 / 38` (38 ≡ 500) |
| Speaking | `((linguistic/6 × 0.55) + (clinical/3 × 0.45)) × 500`, rounded to 10 |
| Conversation | Piecewise: 0→0, 3.0→250, **4.2→350 (pass)**, 5.0→417, 6.0→500 |
| Pronunciation | Piecewise: 0→0, 60→300, **70→350 (pass)**, 80→400, 90→450, 100→500 |

### 8.3 The country-aware Writing pass mark

Writing is the only sub-test where the destination country changes the threshold:

| Destination | Pass |
|---|---|
| UK, Ireland, Australia, New Zealand, Canada | **350 / Grade B** |
| USA, Qatar | **300 / Grade C+** |

This is why target country is collected during onboarding.

### 8.4 Integrity controls

| Control | Implementation |
|---|---|
| **Server-authoritative timing** | Every deadline is recomputed from persisted timestamps. Refresh, sleep or DOM edits change nothing |
| **One-way section locks** | Enforced on write, not just in the UI |
| **Confirm tokens** | Two-step HMAC gate on strict submissions |
| **Audio integrity** | Pause/seek/replay blocked in all modes, with structured logging of each blocked attempt |
| **Answer-key leak shield** | A source-scanning test asserts no learner-facing DTO contains `IsCorrect`, `CorrectAnswer*`, `AcceptedSynonyms*`, `Explanation*`, `WhyWrong*`, `TranscriptEvidence*` or `DistractorCategory` before submission |
| **Version-pinned grading** | Attempts snapshot the question-version map; drift is flagged and audited |
| **Mock AI-free guarantee** | Four independent layers on the Writing path |
| **Citation validation** | Hallucinated rule IDs are filtered against the grounded prompt's allowlist |
| **Evidence verification** | Speaking AI quotes must be verifiable substrings of the actual transcript |
| **Watermarked reports** | Speaking PDFs carry a diagonal "PRACTICE COPY — NOT OFFICIAL" watermark plus a page-2 forensic HMAC token mirrored into PDF metadata |
| **Idempotent submission** | Submit-and-grade is idempotent; first submit wins |
| **Optimistic concurrency** | Row versions on grading; conflicts reload and return the winner |
| **Content publish gates** | Structural validators with named blocking error codes per module |
| **Legal attestation** | Listening papers cannot publish without one of `original-authoring-attested`, `licensed-content-attested`, `permission-attested`, `copyright-cleared` |
| **Immutable content after attempts** | Listening authoring refuses mutation once learner attempts exist; Reading import throws rather than replacing an attempted paper |

### 8.5 Human–AI reconciliation

| Mechanism | Threshold |
|---|---|
| Writing double marking | 3 raw points → auto-average, else senior moderation |
| Speaking double marking | 30 scaled points → auto-average, else senior moderation |
| Writing appeal | >3 raw points divergence → average the two examiners |
| Tutor calibration drift (Speaking) | >40/100 mean normalised drift → booking blocked until re-calibrated or overridden |
| AI calibration harness (Writing) | 50-letter corpus scored against `DrAhmedGrade`; documented release gate is ≥90% of letters within ≤2 raw points 🟡 *(gate is not yet enforced in code)* |

---

## 9. The AI platform

### 9.1 The grounded gateway

**Every AI call in the product goes through one gateway** (`IAiGatewayService`). There is no path to a raw model call from feature code.

```
Feature code
     │
     ▼
BuildGroundedPrompt(rulebook, letterType, profession, task)
     │  ├─ refuse if mock context           → MockAssessmentForbiddenException
     │  ├─ refuse if prompt null            → PromptNotGroundedException
     │  ├─ refuse if system prompt empty    → "ungrounded"
     │  └─ refuse unless the prompt contains the literal
     │      "OET AI — Rulebook-Grounded System Prompt"
     ▼
CompleteAsync(provider, model, temperature, maxTokens)
     │
     ├─► exactly one AiUsageRecord row per call (multi-turn tool loops aggregated)
     ├─► rule IDs filtered against the prompt's allowlist
     └─► failure → fail loud, refund the credit, never fabricate
```

Rulebook JSON is **embedded at build time** as an assembly resource, so a running instance cannot diverge from the rulebook it shipped with.

### 9.2 Provider matrix

| Task | Default | Fallback |
|---|---|---|
| Writing grade (`writing.score.v1`) | `claude-sonnet-5` @ 0.2 | `gpt-4o` |
| Writing appeal (`writing.appeal.v1`) | `claude-sonnet-5` @ 0.2 | — |
| Speaking score (`speaking.score.v2`) | `claude-sonnet-4-6` | `gpt-4o` |
| Speaking patient turn | `claude-haiku-4-5` | `gpt-4o-mini` |
| Speaking drill score | `claude-haiku-4-5` | `gpt-4o-mini` |
| Card / drill drafting | `claude-sonnet-4-6` | `gpt-4o` |
| Conversation opening & reply | `claude-sonnet-5` @ 0.6 | — |
| Conversation evaluation | `claude-sonnet-5` @ 0.1 | — |
| Listening Part A advisory | `claude-sonnet-5` | — |
| Reading structure extraction | grounded gateway @ 0.1 | — |
| Speech-to-text | OpenAI Whisper (`whisper-1`) | Azure Speech, Deepgram `nova-2-medical` |
| Pronunciation phonemes | Azure Speech | Gemini native audio, Whisper alignment |
| Text-to-speech | ElevenLabs `eleven_multilingual_v2` | — |
| Embeddings | OpenAI `text-embedding-3-small` (1536-dim, pgvector HNSW cosine) | JSON cosine fallback |

### 9.3 Cost, quota and governance

- **One usage record per call**, including direct non-gateway calls (OCR, STT) via a dedicated recorder.
- **Credit ledger** with renewal and reset workers, per-account quotas, and priority-queue flags on premium packages.
- **BYOK (bring your own key)** is permitted for non-scoring features and **refused for scoring features** — you cannot grade yourself with your own key.
- **Kill switches**: module-level feature flags with a 30-second cache TTL and no restart required.
- **Learner transparency**: `/ai-usage` shows your own consumption; `/settings/ai` controls AI preferences; `/escalations` raises AI issues.
- **Admin**: `/admin/ai-usage`, `/admin/ai-analytics`, `/admin/ai-providers`, `/admin/ai-config`, `/admin/ai-assistant/*`, `/admin/escalations` and per-user usage drill-down.
- **Policy** is documented in `docs/AI-USAGE-POLICY.md`.

### 9.4 AI assistant / copilot 🟢

An in-product assistant over its own SignalR hub, with an admin-managed tool registry, feature routing, thread history and analytics.

---

## 10. Commercial model

### 10.1 The 2026 portfolio — 24 course SKUs

All prices in **GBP**. Standard access window is **180 days (6 months)** unless noted.

#### Flagship recorded courses

| SKU | Price | Includes |
|---|---|---|
| **Full Condensed Recorded OET Course — Medicine** | **£100** | All four sub-tests, completable in ~10 days of focused study, unlimited repeats within the window. Bundles **5 Writing assessments + 1 private Speaking session + 5 AI credits**. Advertised content: 160+ Listening exams, 100+ Reading exams with keys and rationales, 90+ Writing tasks, 100+ Speaking cards, recall updates from 2023 onwards, continuous Q&A support |
| **Full Condensed Course + TutorBook — Medicine** | **£135** | The above bundled with The Tutor Book |
| **Full Pharmacy OET Course** | **£100** | Pharmacy-specific Writing (complaint, dosage, drug safety, expiry discrepancy, interactions, counselling) and Speaking (community + hospital) |
| **Full Physiotherapy OET Course** | **£75** | Physiotherapy-specific letters and scenarios |
| **Full Allied Health Profession Course** | **£75** | Allied-health letters and scenarios |
| **Full Nursing OET Course** | **£60** | Nursing-specific letters (referral, discharge, transfer, update, incident) and scenarios |
| **Nursing Course + Assessment Package** | **£70** | Nursing course + 5 Writing assessments + 5 AI credits — the recommended nursing SKU |
| **Nursing Premium Bundle** | **£85** | The above + the Basic English foundation course |

#### Crash courses

| SKU | Price |
|---|---|
| Full Crash Course — General OET | **£60** |
| Full Crash Course + 3 Writing Assessments | **£70** |
| Full Crash Course + 5 Writing Assessments | **£80** |

#### Standalone sub-test courses

| SKU | Price |
|---|---|
| Recorded Writing Crash Course (A–Z) | **£35** |
| Writing Crash + 2 / 3 / 5 / 7 / 10 letter assessments | **£45 / £55 / £70 / £90 / £115** |
| Recorded Speaking Crash Course | **£30** |
| 1 Private Speaking Assessment Session *(60-day access)* | **£18** |
| 2 Private Speaking Assessment Sessions *(60-day access)* | **£34** |

#### Combos, foundation and book

| SKU | Price | Notes |
|---|---|---|
| **Double Special — Writing + Speaking** | **£55** | Both full standalone courses; supports both add-on families |
| **Mega Special Package** | **£80** | Full Writing + full Speaking + 1 private Speaking session + 5 Writing assessments — the flagship combo |
| **Basic English Course** | **£35** | A1/A2/B1 → B1–B2 bridge before OET-specific training |
| **The Tutor Book — First Edition 2026** | **£45** *(was £60)* | Recall-based book: new Listening recalls, recall-based Reading topics, 8 full Writing letters with model answers, 16 recall-based Speaking cards. **Lifetime access** |

### 10.2 Add-ons — 29 stackable items

Every product carries **three independent eligibility flags**, and the rule is strict: **if a flag is false, the entire section is hidden — no card, no button, no upsell.**

| Flag | Gates |
|---|---|
| `writing_addons` | 3 / 5 / 7 / 10 letter assessment add-ons |
| `speaking_addons` | Extra private Speaking sessions |
| `tutor_book_discount` | The discounted £32 Tutor Book |

| Add-on | Price | Grants |
|---|---|---|
| 3 Writing letter assessments | £30 | 3 letters |
| 5 Writing letter assessments | £45 | 5 letters |
| 7 Writing letter assessments | £60 | 7 letters |
| 10 Writing letter assessments | £85 | 10 letters — best per-letter value |
| 1 private Speaking session | £18 | 1 session |
| 2 private Speaking sessions | £34 | 2 sessions |
| **Tutor Book (enrolled-student price)** | **£32** *(was £60)* | The book, for eligible enrolments only |
| **Extend Access — 90 days** | **£15** | Pushes expiry out 90 days from the later of today or current expiry |

### 10.3 AI credit and practice packages

| Package | Price | Grants | Validity |
|---|---|---|---|
| Quick Check | £15 | 5 grading credits, 3 Listening + 3 Reading tests | 30 d |
| Exam Prep Pro | £32 | 15 grading credits, 6 + 6 tests | 90 d |
| OET Mastery | £75 | 40 grading credits + **priority queue** | 180 d |
| Writing Starter / Standard / Pro | £9 / £19 / £32 | 3 / 8 / 15 AI-graded letters | 30 / 90 / 180 d |
| Speaking Starter / Standard / Pro | £12 / £24 / £42 | 3 / 8 / 15 AI-graded speaking cards | 30 / 90 / 180 d |
| AI Speaking Credits — Starter / Standard / Pro | £8 / £18 / £30 | 4 / 10 / 20 credits (1 per card, 2 per full exam) | 30 / 90 / 180 d |
| Listening Starter / Standard / **Pro** | £4 / £9 / **£15** | 5 / 15 / **unlimited** Listening tests | 30 / 90 / 180 d |
| Reading Starter / Standard / **Pro** | £4 / £9 / **£15** | 5 / 15 / **unlimited** Reading tests | 30 / 90 / 180 d |
| 1 / 3 / 5 Full Mocks | £19 / £45 / £67 | Full 4-sub-test mocks | 180 d |

**Design note:** Listening and Reading are *deterministically* marked from an answer key, so they cost no AI credits — the packs curate test access, not grading. Writing and Speaking consume AI credits because they invoke a model. Mock allowances are a **separate currency** from AI credits.

### 10.4 Entitlement model

Access is resolved from four independent sources, checked server-side on every gated action:

1. **Subscription** — an active plan with an expiry date, granting module access (`dashboardModules` per SKU: Listening, Reading, Writing, Speaking, Materials Library, Writing Assessments, Speaking Session, AI Practice, Recalls, Tutor Book, Audio Scripts, Updates, Basic English, Vocabulary, Grammar, Listening Foundations, Study Plan, Booklet, Add-ons, Model Letters, Writing Rules, Speaking Cards, Useful Phrases, Role-Play Practice).
2. **Countable units** — Writing letter assessments, private Speaking sessions, mock entitlements.
3. **AI credits** — a metered ledger with expiry, consumed per grading call (Writing exam = 2, Speaking card = 1, full Speaking exam = 2).
4. **Free-tier quotas** — rolling-window allowances (Conversation 3/7 days, Pronunciation 20 attempts/7 days), DB-overridable at runtime.

Supporting mechanics: **freeze** (pause the clock with approval), **extra time** entitlement (0–100% for accommodations), **score guarantee** claims, **wallet tiers**, **scholarships** and **regional pricing**.

### 10.5 Payments

| Rail | Use |
|---|---|
| **Stripe** | Primary card checkout, billing portal, subscriptions, coupons. **Two checkout systems, one webhook handler** (`/v1/payment/webhooks/stripe`) with **4-layer idempotent fulfilment** |
| **PayPal / Venmo** | Alternative checkout with Smart Buttons and advanced card fields; also the rail for private-speaking bookings |
| **Native IAP** | Apple / Google in-app purchase reconciliation for mobile |
| **Manual payment** | Bank-transfer style flow with admin verification (`/billing/manual-payment`, `/admin/billing/manual-payments`, `/admin/billing/bank-accounts`) |
| **Regional pricing** | Per-region price books and FX rate refresh (`/admin/billing/region-pricing`, `/admin/fx-rates`) |

**Commerce UX:** `/catalog` → `/cart` → `/checkout/review` → `/checkout/success` or `/cancel`. Account billing at `/account/billing`, `/invoices`, `/payment-methods`, `/subscriptions`, plus plan comparison, card update, cancellation and a billing profile.

### 10.6 Growth programmes

| Programme | Mechanics |
|---|---|
| **Affiliates** | `/affiliate` portal. `?ref=CODE` / `?agent=CODE` sets a 30-day, first-click-wins attribution cookie validated against a strict pattern. Admin at `/admin/billing/affiliates` |
| **Referral** | `/referral`, `/billing/referral` — learner-to-learner referral |
| **Sponsors / B2B** | `/sponsor`, `/sponsor/learners`, `/sponsor/billing` — seat packs for institutions 🟡 *(behind a feature flag)*. Admin at `/admin/institutions`, `/admin/enterprise` |
| **Marketplace** | `/marketplace`, `/marketplace/packages` — third-party or partner package listings, with admin review at `/admin/marketplace-review` |
| **Promo codes & scholarships** | `/admin/billing/coupons`, `/admin/billing/scholarships` |
| **Pricing experiments** | `/admin/pricing-experiments` with a conversion-tracking worker |

### 10.7 Why the model is structured this way

- **Low entry price, natural upgrade path.** A £30 Speaking crash course or a £4 Reading Starter pack converts a browser into a customer; add-ons and credits monetise engagement rather than gating the first experience.
- **Eligibility flags prevent nonsense upsells.** A learner who bought a Listening-only pack is never shown Writing assessment add-ons.
- **Human time is priced separately from AI time.** Private Speaking sessions and Writing letter assessments are finite, human-delivered units; AI credits are elastic. This keeps unit economics honest.
- **Recall updates create ongoing value inside a fixed window**, which is a genuine reason to renew or extend rather than an artificial one.
- **The Tutor Book is both a product and an anchor.** It sells standalone at £45, discounts to £32 for enrolled learners, and is the cited authority behind the Writing rulebook — the same asset does commercial and academic work.

---

## 11. Administration and operations 🟢

**251 admin pages.** The console is a full operations product in its own right.

### Content operations

| Area | Capability |
|---|---|
| **Papers** | Create, import, preview, revise and publish Listening and Reading papers with per-module structure, question, PDF, audio and sequence editors |
| **Listening authoring** | Part A notes builder (TipTap over a `____` / `##` / `-` grammar), **PDF overlay editor** (drag blank boxes onto the PDF, auto-numbered in reading order), **waveform cue-point editor** with draggable audio windows, manifest round-trip, bulk validation, AI extraction approval |
| **Reading authoring** | Structure, questions, texts, distractor QC, review-history and the 6-stage question review chain |
| **Mocks** | Bundle wizard across all 5 steps, review pipeline, item analysis, leak reports, randomisation, operations, bookings |
| **Speaking** | Role-play cards (with hidden interlocutor scripts), card types, classification, scoring, tasks, review, mock sets, drills, shared resources, AI drafting and import |
| **Writing** | Tasks, scenarios, canon rules, lessons, drills, common mistakes, calibration letters, result visibility, AI options, audit log |
| **Other content** | Videos (with Bunny upload, categories, collections, access rules, analytics), materials, media, vocabulary, grammar, pronunciation, conversation templates, strategies, recalls, tutor book, result templates |
| **Pipeline tooling** | Content hierarchy, library, imports, deduplication, generation, quality, staleness, publish requests, bulk operations, revisions |

### Business operations

Billing catalogue and products · pricing and region pricing · coupons · subscriptions & packages · wallet tiers · manual payments · bank accounts · refunds · dunning · scholarships · affiliates · gateway routes · eligibility rules · storefront · notification templates · billing metrics and analytics · FX rates · pricing experiments · score-guarantee claims · credit lifecycle · free tier · freeze policy.

### People operations

Learners (with per-learner study plans) · experts and specialties · roles and permissions · institutions and enterprise · review ops · SLA health · expert efficiency analytics · calibration (general and speaking) · private speaking (config, tutors, calibration) · onboarding (including interlocutor training) · live classes · community moderation.

### Platform operations

Runtime settings with per-section connection tests · feature flags · AI providers, config, usage and analytics · AI assistant configuration and threads · alerts · audit logs · launch readiness · conformance · analytics (cohort, content effectiveness, quality, subscription health, per-module) · business intelligence · notifications and campaigns · bulk operations · playbook · rulebook management · scoring system and criteria.

**Runtime settings** deserve a specific note: hot-rotatable secrets and configuration live encrypted in the database and override environment variables, so an operator can rotate an API key or flip a provider from the admin panel without a redeploy. Startup validation fails fast in production if critical settings are missing or if sandbox fallbacks are enabled.

---

## 12. Technical architecture

### 12.1 Stack

**Frontend** — Next.js **16.2** (App Router, Turbopack, standalone output) · React **19.2** · TypeScript **5.9** · Tailwind CSS **4.1** · TanStack Query · Zustand · Zod + React Hook Form · Radix primitives · `motion/react` · next-intl · Recharts · SignalR client · wavesurfer.js · hls.js · pdf.js · TipTap · Sentry.

**Backend** — ASP.NET Core Minimal API on **.NET 10** · EF Core **10** · **PostgreSQL 17 with pgvector** · Npgsql · Stripe.net · MailKit · QuestPDF · UglyToad.PdfPig · Tesseract · Ical.Net · WebPush · HtmlSanitizer · Polly resilience · Sentry.

**Tooling** — pnpm 10.33 · Vitest 4 · Playwright 1.58 with axe-core · k6 · xUnit · ESLint 9.

### 12.2 Frontend structure

```
app/            App Router — 317 learner routes + 251 admin routes + 3 route handlers
components/ui/  Design system — 40 exported primitives, design-sync'd
components/domain/  OET-specific composites (23 barrel exports + sub-packages)
contexts/       auth · ai-assistant · notification-center · accessibility
hooks/          24 hooks (timers, recorders, annotations, feature flags, …)
lib/            ~85 modules — scoring, rulebook, api client, network, observability
messages/       i18n bundles (en, ar)
middleware.ts   CSP+nonce · auth gate · CSRF double-submit · affiliate attribution
```

**All HTTP goes through one `apiClient`** in `lib/api.ts` — with narrow, documented exceptions for route handlers, external URLs and network internals. Native `<audio>`, `<img>` and `<iframe>` cannot carry bearer tokens, so authorised media is fetched to a blob URL.

**Middleware does four things on every request:** generates a per-request CSP nonce (deliberately without `strict-dynamic`, for Turbopack chunk-loader compatibility) with allow-lists for Zoom, PayPal, Bunny and Google Fonts; gates unauthenticated routes against a public allow-list; enforces CSRF double-submit on state-changing proxy calls; and captures affiliate attribution.

### 12.3 Backend structure

```
Endpoints/   155 endpoint files, grouped by domain
Services/    40+ namespaced areas (Billing, Speaking, Writing, Listening, Reading,
             Mocks, Conversation, Pronunciation, Vocabulary, VideoLibrary, Ai,
             Rulebook, Content, Settings, Planner, Auth, Admin, Seeding, Voice…)
Data/        LearnerDbContext + 160+ hand-authored migrations + seeds
Domain/      entities
Hubs/        9 SignalR hubs
Security/    CSRF guard, authz policies, admin permission scopes
Middleware/  cross-cutting request handling
```

**Nine real-time hubs**: notifications · conversations · AI assistant · mock live room · speaking live rooms · writing submissions · writing coach · writing today — plus a raw WebSocket endpoint for the writing coach. All rate-limited on connect.

**Domain invariants** enforced repo-wide:

- Scoring only through `lib/scoring.ts` / `OetScoring` — never inline thresholds.
- Rulebooks only through the rulebook services — never raw JSON from the UI.
- AI only through the grounded gateway — one usage row per call, never bypass grounding.
- User media only through `IFileStorage` — never raw filesystem calls.
- **EF migrations are hand-authored**, future-dated with an inline `[Migration]` attribute, because generated output would recreate live tables and break deployment.

### 12.4 Infrastructure

```
                       Nginx Proxy Manager (shared host)
                                  │
                ┌─────────────────┴─────────────────┐
                ▼                                   ▼
        web  (nginx blue/green)            learner-api (nginx blue/green)
         ├── web-blue   ──┐                 ├── learner-api-blue   ──┐
         └── web-green  ──┤                 └── learner-api-green  ──┤
                          │                                          │
                          └──────────► postgres 17 + pgvector ◄──────┘
                                       clamav (mandatory in prod)
                                       db-backup (nightly, GPG, → S3/R2)
```

- **Blue/green deployment** on a VPS. Each web slot is pinned to its matching API slot. The router flips via an `ACTIVE_SLOT` variable, with a 30-second grace period so nginx can drain long-lived SignalR connections.
- **The VPS only pulls prebuilt images** from GHCR — it never builds, because it's a shared multi-tenant host.
- **Health gate**: `/health/ready` probes the database and storage; a broken commit fails the gate and is not promoted.
- **ClamAV is mandatory** — the API refuses to boot in production with a no-op upload scanner, with limits raised to 512 MB so large course media isn't rejected.
- **17 GitHub Actions workflows**, of which `Build & Deploy (web + API)` on push to `main` is the real production gate.

### 12.5 Performance and scale

- **Monthly range partitioning** on the three append-only high-volume tables (analytics events, audit events, AI usage records), with a worker pre-creating partitions two months ahead.
- **BRIN indexes** on time-ordered append-only tables; hot JSON columns converted to `jsonb`; `xmin`-based optimistic concurrency; `pg_stat_statements` preloaded.
- **Cursor pagination** across the heavy list endpoints.
- **In-memory caching** across 20 services, notably runtime settings, rulebook loaders, profession catalogue and AI quotas.
- **~40 background workers** covering billing (dunning, expiry, churn, forecasting, FX, retention, experiments, metrics), writing crons (daily plan, readiness, batch grading, analytics, queue alerts, cleanup, content audit), listening (TTS jobs, AI scoring, backfill, expiry), speaking/conversation/pronunciation retention, AI credit renewal and quota reset, and content/media jobs.
- **pgvector HNSW cosine** for writing exemplar retrieval 🟡 *(dual-path: a JSON column remains source of truth until a backfill runs)*.

---

## 13. Security, privacy and compliance

### Authentication and authorisation

- First-party JWT access/refresh tokens with **single-use rotation and refresh-token families**, account lockout, and 21 auth endpoints.
- **Email OTP verification**, **TOTP MFA** with recovery codes, external identity (Google / Facebook / LinkedIn), device pairing, and self-service session/device management.
- **Roles**: `learner · expert · admin · sponsor`, layered with granular admin permission scopes (`AdminContentRead`, `AdminContentWrite`, `AdminContentPublish`, `AdminQualityAnalytics`, `AdminAuditLogs`, …).
- Authorisation is **always server-side**; the frontend gate is convenience only.

### Rate limiting

| Policy | Limit |
|---|---|
| Per user (read / write) | 5000/min — deliberately high so admins can run bulk operations |
| Hub connect | 30/min |
| **Auth brute force** | **10/min per IP** in production |
| Auth refresh | 120/min per IP (separated so multi-tab navigation doesn't starve users behind NAT) |
| **OTP send** | **5/hour per email** |
| Device pairing redeem | 5/min |
| AI credential validation | 5/min |

### Data protection

- **Upload virus scanning** via ClamAV, fail-closed by default.
- **HTML sanitisation** at persist time for all admin-authored HTML.
- **CSRF** double-submit on the frontend plus a backend cookie-backed guard, with every `DisableAntiforgery()` tracked in a security audit doc.
- **Sentry PII scrubbing is pinned in code** — `sendDefaultPii: false` is hard-coded on both frontend and backend, sample rates default to 0, and session replay only loads with masking enabled.
- **Secrets** are encrypted at rest in the runtime-settings table and rotatable from the admin panel. `.env*` files are never committed.
- **Media attestation** — video playback requires an HMAC token tied to a key baked into each app build, plus OS-level screen-capture exclusion on desktop.

### GDPR and retention

- **Versioned consent** (`recording.v1`, `live_video_with_tutor.v1`) is required before any recording; recording cannot proceed without it (Article 9 special-category data, lawful basis = explicit consent).
- **Learner rights are self-service**: list my recordings, list my consents, delete a recording, run an erasure pre-flight that tells you exactly what will be removed, and revoke a consent type.
- **Eight retention workers** covering speaking audio (default 365 days), conversation audio (30 days), pronunciation audio (45 days), auth data, webhook PII, admin uploads, writing drafts and general data retention.
- **Every admin access to a learner recording writes an audit row** with actor, IDs, a *required* free-text reason and timestamp, reviewable in the admin console.
- **Audit events** are BRIN-indexed and monthly-partitioned, written from every privileged mutation path.
- A dedicated **Speaking security dossier** documents the threat model, attack surface, abuse cases, data classification, key rotation and penetration-test scope.

---

## 14. Quality assurance

| Layer | Coverage |
|---|---|
| **Unit** | Vitest across `lib/`, `components/`, `app/` — including 72 scoring assertions and TS/C# parity tests for the Listening state machine |
| **Backend** | xUnit, with `EndpointRegistrationTests` as the canonical route-registration guard |
| **E2E** | Playwright, ~72 specs across **14 projects**: chromium/firefox/webkit × learner/expert/admin, unauthenticated, mobile Chromium (Pixel 7), mobile WebKit (iPhone 14), and a **Sydney/en-AU project** for timezone and locale regressions |
| **Production smoke** | A dedicated prod suite: smoke, expert smoke, privileged, exhaustive, drill-down, interaction, performance and mobile-a11y |
| **Accessibility** | axe-core suites with a formal sign-off template and validator |
| **Load** | k6 scenarios for speaking session creation and LiveKit token minting |
| **Static gates** | `tsc --noEmit`, scoped ESLint, encoding/mojibake check, unused-code scan |
| **Conformance** | A dedicated rulebook-conformance workflow |
| **Source-scanning audits** | Inline-scoring-math ban and answer-key leak shield, both enforced as failing tests |
| **QA documentation** | 33 files: master report, test plan, bug log, defect log, release readiness, coverage map, browser/device matrix, accessibility report, plus full desktop and mobile audit sets |

**Service level objectives** (Speaking module, representative): role-play turn round-trip p95 < 2.5 s · AI assessment p95 < 12 s, p99 < 25 s · drill score p95 < 6 s · LiveKit connect p95 < 3 s · module uptime 99.5% · AI mean absolute error vs gold ≤ 0.3 per criterion · prompt-cache hit ≥ 80% · recording loss ≤ 0.1% · tutor queue median time-to-claim < 30 min.

---

## 15. Benefits summary

### 15.1 For learners

| Benefit | Why it's real |
|---|---|
| **Pass faster, at lower total cost** | One platform replaces a course, a mock provider, a writing marker and a speaking tutor |
| **Practise the real exam, not an approximation** | Item counts, timings, locks and marking rules are enforced in code and validated at publish |
| **Feedback you can act on** | Every finding cites a rule, quotes your words, and gives you a stronger alternative |
| **Feedback you can trust** | Hallucinated rule IDs are filtered; speaking quotes are verified against the transcript; failures refund rather than fabricate |
| **Unlimited speaking practice** | An AI patient available at 3 a.m. between shifts |
| **Human expertise where it counts** | Calibrated examiners for Writing letters and 1:1 Speaking, with double marking and moderation |
| **Know when you're ready** | A blended readiness score with a predicted band, before you pay the exam fee |
| **Study in the gaps** | 5–15 minute drills, daily plans capped at 4 tasks, spaced repetition that resurfaces your weakest items first |
| **Nothing gets lost** | Error banks, review queues and autosave every 5 seconds |
| **Your profession, not "healthcare"** | Letters, scenarios, cards and rulebooks are scoped to your discipline |
| **Your destination country** | Writing pass thresholds differ by country and the platform knows it |
| **Accessible by default** | WCAG 2.1 AA target, reduced motion, RTL, font scaling, high contrast, extra-time entitlements |
| **On any device you own** | Web, Android and desktop, all the same app |
| **Your data stays yours** | Versioned consent, retention countdowns, one-click deletion and an erasure pre-flight |

### 15.2 For tutors and examiners

Claim-locked queues that prevent duplicate work · full context on one screen (letter, task, stimulus with the learner's own highlights, AI pre-assessment, prior annotations) · voice-note feedback · calibration against Dr Hesham's gold standards with private drift reporting · double-marking and moderation so no single marker carries a contested score alone · availability, calendar sync and earnings tracking · mobile review for marking on the go.

### 15.3 For administrators and content authors

Publish gates that make it structurally impossible to ship a malformed paper · AI-assisted extraction that always requires human approval · a linear question review chain with full audit history · item-level psychometrics (facility, discrimination, distractor histograms) · runtime settings and secret rotation without redeploy · launch-readiness and conformance dashboards · 251 pages of operational control.

### 15.4 For the business

A 24-SKU, 29-add-on catalogue with clean eligibility rules and a natural upgrade ladder · human-delivered units priced separately from elastic AI capacity · three payment rails plus regional pricing · affiliate, referral, sponsor and marketplace growth channels · pricing experiments with conversion tracking · churn prediction, dunning and usage forecasting workers · blue/green deployment with a health gate so a bad build never reaches learners.

---

## 16. Current status and known gaps

The platform is **in production and serving learners**. This section is the honest register of what isn't finished, consolidated from a full codebase audit.

### 🟡 Built but not surfaced

| Item | Status |
|---|---|
| **Pronunciation recording UI** | Full backend (4 ASR providers, phoneme rollup, spaced repetition, grounded coaching, OET projection) is reachable only by direct API call. No mic button in `/pronunciation`. **Highest-leverage gap** |
| **Minimal-pair discrimination drill** | Static stub; scoring is client-declared and feeds nothing |
| **Writing OCR handwriting upload** | Endpoints and an uploader component exist; the component is imported by nothing |
| **Writing live coach V2** | Three transports registered, no UI consumer; V1 powers the one shipped coach surface |
| **Writing module discoverability** | Roughly two-thirds of 46 writing routes are reachable only by direct URL — there is no writing sub-navigation |
| **Seven admin writing pages** | Working backends and permissions, linked from neither the hub nor the sidebar |
| **Speaking fluency timeline** | Requires manually pasting an attempt ID |
| **16-stage speaking pathway** | Catalogue is in-memory; stage state is derived, not persisted |
| **Realtime ElevenLabs STT** | Gated off behind a 13-condition production authorisation gate; 6 of 8 backlog items are blocked on external evidence |
| **Sponsor portal** | Behind a feature flag; redirects to support when off |
| **Soketi realtime** | Fully wired in config with an admin test probe, but no deployed service — realtime today is SignalR only |
| **Interlocutor training tutor UI** | Backend endpoints wired; no page calls them |
| **Writing calibration table** | Schema and read path exist; nothing writes it, so every tutor shows 0% with a misleading warning |

### 🔵 Planned

- **iOS app** — Capacitor config exists; the Xcode project is not scaffolded and no App Store URL is set.
- **SignalR backplane** — required before any canary split or replica count above one. Currently safe only because blue/green cuts over to exactly one live slot.
- **Full Arabic translation** — chrome and the Writing module are internationalised; the rest renders English inline.
- **Content at scale** — the Phase 8 targets (20–30 full Reading mocks, 50+ Part A tests, 300+ Part B extracts, 100+ Part C texts, 200+ drills, 50 vocabulary sets) are aspirational, not current inventory.
- **Plagiarism / AI-text detection** on Writing submissions — does not exist today.
- **Enforced AI calibration gate** — the harness computes agreement but nothing blocks a model rollout on it, and there is no seeded corpus.

### ⚠️ Known inconsistencies worth fixing

| Issue | Impact |
|---|---|
| Listening duration is stated three ways (45 min on the hub, 40 min in the API, 90 s/sub-section fallback in the paper player) | Learner-visible confusion |
| Writing V2 emits a raw total and a hand-rolled band but **not** a 0–500 scaled score or the country-aware pass/fail — the canonical `lib/scoring.ts` writing path is currently unused | Country is collected in onboarding but not applied on the V2 result surface |
| `"B+"` appears in the Writing band ladder; the official OET set is A, B, C+, C, D, E | Terminology mismatch |
| Two disconnected Writing review systems — the learner request page feeds one, the tutor queue reads the other | Requested reviews may not reach the tutor queue |
| Writing tutor review takes no payment and consumes no entitlement; appeals are free and uncapped | Revenue leakage |
| Two incompatible score-override formats on the same Listening column | The grading service silently ignores one of them |
| Moderated Writing final scores are stored but never propagated to the grade row | Moderation outcome not applied |
| Several dead links: `/reading/lessons/{slug}`, `/listening/diagnostic`, `/writing/paper/session/{id}/results`, `/tutor/calibration` | 404s from live UI |
| Some stale docs contradict shipped code (listening grader tolerance, expert listening surface, speaking "AI never official", speaking TTS fallbacks, reading rulebook existence) | Misleads future contributors |
| `typescript.ignoreBuildErrors: true` — the production build does not typecheck | Typecheck is a separate, non-blocking gate |
| `QA Smoke` is chronically red and is **not** the release gate | Do not read it as a signal |

---

## 17. Roadmap

Ordered by learner impact per unit of effort.

### Near term — close the loops that are already built

1. **Ship the pronunciation recorder.** A complete, high-value subsystem sits behind a missing mic button.
2. **Add Writing sub-navigation.** Two-thirds of the largest module is invisible; this is pure discoverability upside with no new backend work.
3. **Wire the OCR handwriting upload.** The mocks page already advertises "print & handwrite, then upload."
4. **Reconcile the two Writing review systems** and attach entitlement/payment to tutor review and appeals.
5. **Apply the canonical scoring path to Writing V2** so learners see a 0–500 score and the country-aware pass mark they were profiled for.
6. **Fix the dead links and the Listening duration inconsistency.**

### Medium term — depth and trust

7. **Persist the speaking pathway** and add navigation into the fluency timeline.
8. **Enforce the AI calibration gate** and seed the calibration corpus, so model changes are gated on agreement with Dr Hesham's marking.
9. **Write the tutor calibration table** so inter-rater agreement is real rather than a permanent 0%.
10. **Propagate moderated final scores** to the grade of record.
11. **Build the discrimination drill** with server-generated rounds.
12. **Complete Arabic localisation** beyond the chrome and Writing module.

### Longer term — reach and scale

13. **Scaffold and ship the iOS app.**
14. **Add a SignalR backplane** to unlock horizontal scaling and canary deploys.
15. **Launch realtime STT** once the external evidence gates clear.
16. **Scale content to the Phase 8 targets** — this is the single biggest determinant of perceived product depth.
17. **Turn on the sponsor/B2B portal** and pursue institutional sales.
18. **Add originality checking** to Writing submissions.

---

## Appendix A — Glossary

| Term | Meaning |
|---|---|
| **OET** | Occupational English Test — the healthcare-specific English proficiency exam |
| **CBT / CBLA** | Computer-based test / computer-based language assessment |
| **OET@Home** | The remotely-proctored at-home version of the exam |
| **Sub-test** | One of Listening, Reading, Writing, Speaking |
| **Raw score** | Marks out of the sub-test maximum (42 for L/R, 38 for Writing) |
| **Scaled score** | The 0–500 OET scale |
| **Grade B / 350** | The pass standard most regulators require |
| **Rulebook** | A versioned, human-authored JSON rule set that grounds every AI call |
| **Canon** | The database-backed Writing rule engine and its rule library |
| **Recall** | A reported pattern or item from a recent real exam sitting |
| **The Tutor Book** | Dr Hesham's recall-based preparation book and the cited authority behind the Writing rulebook |
| **Interlocutor** | The person (or AI) playing the patient in a Speaking role-play |
| **Role-play card** | The candidate-facing brief for a Speaking task |
| **Entitlement** | A server-checked right to perform an action, from a subscription, unit, credit or quota |
| **AI credit** | The metered currency consumed by an AI grading call |
| **Miss reason** | The classified cause of a wrong answer, used for analytics not marking |
| **Distractor** | A deliberately plausible wrong option, categorised for post-attempt teaching |
| **Blue/green** | A deployment pattern with two identical slots and an instant router flip |
| **Grounded prompt** | A system prompt built from rulebook rules, validated before any model call |

## Appendix B — Integration register

| Category | Services |
|---|---|
| **Payments** | Stripe · PayPal / Venmo · Apple & Google in-app purchase |
| **AI / LLM** | Anthropic Claude · OpenAI · Google Gemini · Azure AI Inference · Mistral (PDF/OCR tier) |
| **Speech** | ElevenLabs (TTS + STT) · OpenAI Whisper · Azure Speech (pronunciation assessment) · Deepgram (`nova-2-medical`) |
| **Real-time media** | LiveKit Cloud (WebRTC rooms + egress recording) · Zoom Meeting SDK |
| **Video** | Bunny.net Stream (TUS upload, HLS CDN, token auth, encode webhooks) |
| **Documents** | Azure Document Intelligence · Google Cloud Vision · Tesseract · QuestPDF · PdfPig |
| **Communications** | Brevo (API + SMTP relay) · Firebase Cloud Messaging · Web Push / VAPID · Soketi 🟡 |
| **Identity** | Google · Facebook · LinkedIn OAuth |
| **Scheduling** | Google Calendar (free-busy + events) |
| **Storage** | S3-compatible object storage / Cloudflare R2 · ClamAV |
| **Observability** | Sentry (frontend + backend) |
| **Delivery** | GitHub Container Registry · GitHub Releases (desktop/Android + Tauri updater) |

## Appendix C — Route map at a glance

**Learner core:** `/dashboard` `/practice` `/listening` `/reading` `/writing` `/speaking` `/conversation` `/pronunciation` `/vocabulary` `/grammar` `/recalls` `/mocks` `/videos` `/materials`

**Progress & planning:** `/progress` `/readiness` `/predictions` `/study-plan` `/goals` `/next-actions` `/remediation` `/review` `/history` `/submissions`

**Human & community:** `/private-speaking` `/classes` `/me/classes` `/tutoring` `/community` `/peer-review` `/leaderboard` `/achievements` `/escalations` `/support`

**Commerce:** `/catalog` `/pricing` `/cart` `/checkout` `/billing` `/account/billing` `/subscriptions` `/ai-packages` `/marketplace` `/affiliate` `/referral` `/sponsor` `/freeze`

**Guides:** `/exam-guide` `/test-day` `/exam-booking` `/score-calculator` `/strategies` `/feedback-guide` `/ielts-guide` `/learning-paths`

**Account:** `/settings` `/settings/ai` `/settings/sessions` `/onboarding` `/onboarding-tour` `/get-app` `/terms`

**Roles:** `/expert/*` (≈40 routes) · `/tutor/*` (≈12) · `/admin/*` (251)

---

*This document reflects the state of the codebase as of 20 July 2026. It was compiled from a full audit of the repository — source code, rulebooks, seed catalogues and internal documentation — rather than from marketing material. Where the code and the internal docs disagreed, the code was treated as the source of truth and the disagreement is recorded in §16.*
