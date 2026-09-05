# OET with Dr Hesham — Web App

OET exam-preparation platform (medicine-led, multi-profession) selling structured prep products with AI-graded practice and human-tutor surfaces.

## Language

### Commerce

**Product**:
The canonical sellable unit identified by stable `product_id`, driving catalogue seed, routing, checkout, and entitlements.
_Avoid_: Course, package, SKU (as standalone nouns)

**Bundle**:
A multi-SKU purchase grouping several Products under one checkout.
_Avoid_: Package (as bundle synonym)

**Enrolment**:
Exactly one row per purchased SKU recording that a learner owns that Product.
_Avoid_: Registration, purchase record

**Entitlement**:
What an Enrolment unlocks, per the Product's template (credits, attempts, flags, durations), enforced server-side.
_Avoid_: Unlock, access (as noun for the grant itself)

**Access**:
Time-bound enforcement of an Entitlement for a learner (course-access window).
_Avoid_: Entitlement (when meaning the enforced window)

**CheckoutSession**:
The payment state-machine row tracking a single checkout attempt to completion or abandonment.
_Avoid_: Session (bare), order, transaction

**Pending Verification**:
Manual-approval grant state for Products 1–29; grant occurs only after admin approval, unlike instant grant on confirmed payment (Products 30–47).
_Avoid_: Pending payment, on hold

### Credits and Attempts

**Shared Credits**:
Universal candidate-facing allowance pool; Reading/Listening cost 1, Writing/Speaking cost 2 per graded submission.
_Avoid_: Tokens, points

**Flexible W/S Credits**:
Restricted pool spendable only on Quick Check / Exam Prep Pro, exactly 1 per graded submission.
_Avoid_: Shared Credits, bonus credits

**Subtest Credits**:
Per-subtest allowance pools (e.g. W3/8/15, S3/8/15, R/L variants or Unlimited) granting a fixed count of AI-graded submissions in that subtest.
_Avoid_: Attempts (for these), tokens

**Full Mock Attempt**:
A single whole-mock allowance consumed per completed mock; never consumes AI credits.
_Avoid_: Credit, submission

**Mock**:
Bare word reserved for the Full Mock product and its Full Mock Attempts only.
_Avoid_: mock.full_grade (feature flag), WritingMock / SpeakingMockSet (content-set entities, always qualified)

**5-credit gift**:
One-time Shared Credits grant per qualifying Full Course enrolment; inherited not doubled inside bundles.
_Avoid_: Bonus, promo credits

**Course-access expiry**:
End of the Enrolment's `access_duration_days` window (clamped to 180 days).
_Avoid_: Expiry (bare), credit expiry

**Credit validity**:
Independent lifespan of a credit pool (30/90/180 days per package); distinct from course-access expiry.
_Avoid_: Expiry (bare), course expiry

**Tutor Book fulfilment cap**:
180-day reader + updates/audio-scripts window for the Tutor Book product.
_Avoid_: Expiry (bare), subscription

### Content

**ContentPaper**:
The canonical curatorial selectable unit (e.g. Listening Sample 1, Speaking Card 4).
_Avoid_: Paper (bare), exam, test

**QuestionPaper**:
An asset role only (the question PDF/image role within a ContentPaper), never the paper itself.
_Avoid_: ContentPaper, booklet

**Booklet**:
The source PDF artefact (part-only A/B/C crops); never attached as the combined book.
_Avoid_: Paper, QuestionPaper

**ContentPaperAsset**:
A typed file role binding a ContentPaper to a physical file for one purpose.
_Avoid_: Attachment, file

**MediaAsset**:
The physical stored file behind a ContentPaperAsset.
_Avoid_: Upload, blob

**CandidateVisible**:
Explicit publish gate on ContentPaper; candidate surfaces show only Published + Visible papers.
_Avoid_: Published (alone), live, enabled

### Attempts and Scoring

**Attempt**:
A unique backend-created try that reserves allowance up front; reopen/resume must not double-charge.
_Avoid_: Submission (for the try itself), try, session

**Submission**:
The learner artefact uploaded for grading (letter, transcript, answers); an Attempt wraps one Submission.
_Avoid_: Attempt, assessment

**Writing Assessment**:
Add-on product count (3/5/7/10) granting that many graded Writing Attempts.
_Avoid_: Submission, attempt (bare), correction

**Grading**:
The AI-plus-rulebook evaluation output for a Submission (e.g. WritingAssessmentReport); never the Attempt itself.
_Avoid_: Marking (bare), score (as process)

**SubmitGrading**:
The deep module owning key-normalise, hash-guard, claim, credit-reserve, and preflight behind `Submit(Attempt)`; callers pass one idempotency key and never re-implement retry/dedupe. Credit-reserve is internal with a delegate-ready seam for a future CreditLedger.
_Avoid_: Submission service (bare), pipeline (bare), preflight (as caller-side step)

**Scoring anchor 30/42 = 350/500**:
Canonical Listening/Reading pass mapping; scoring only via `lib/scoring.ts` / backend `OetScoring`, never inline thresholds.
_Avoid_: Inline cut-score, 350 (bare, without anchor)

**Writing country-aware pass**:
Writing grade gated on mandatory candidate country (350/B vs 300/C+ paths with `country_required` state).
_Avoid_: Universal writing pass, fixed writing threshold

**Speaking 350**:
Universal Speaking pass mark, never country-varying.
_Avoid_: Country-adjusted speaking score

### AI and Rulebooks

**Rulebook**:
Grounded JSON guidance per skill × profession × version driving lint, audit, and AI prompts.
_Avoid_: Prompt pack, guidelines (bare)

**AiUsageRecord**:
Exactly one row per physical provider call (success, error, or refusal), the basis for billing and audit.
_Avoid_: Log, usage event (bare)

**Tutor Book**:
Standalone (£45) or add-on (£32) fulfilment-capped reader product surfacing recalls, updates, and audio scripts.
_Avoid_: E-book (bare), course, recall set

**Recall**:
Exam-recall content unit surfaced through the Tutor Book reader.
_Avoid_: Past paper, leak, update (as content type)

**RecallSet**:
A coded content collection grouping related Recalls.
_Avoid_: Recall (single unit), update

**Tutor Book update**:
A fulfilment delivery of new Recalls into the Tutor Book reader window.
_Avoid_: Recall, RecallSet

### Learning units

**Speaking Session**:
A live/private booked tutoring interaction (£18 add-on); exam and mock variants are Session types, always qualified.
_Avoid_: Session (bare), lesson, card, drill

**Card**:
A role-play prompt unit, always a ContentPaper; never a session.
_Avoid_: Session, drill, scenario (bare)

**Drill**:
A micro-exercise (SpeakingDrillItem / WritingDrill) with its own attempts; consumes credits only when AI-graded.
_Avoid_: Lesson, session, practice (as type)

**Lesson**:
Video or curated teaching content, progress-tracked, never consuming credits.
_Avoid_: Drill, session, practice (as type)

**Practice**:
Generic word for learner activity; never a type or code name.
_Avoid_: (do not use in identifiers or glossary references)

### Classification axes

**Profession**:
One of 13 exam professions (medicine, nursing, dentistry, pharmacy, physiotherapy, veterinary, optometry, radiography, occupational therapy, speech pathology, podiatry, dietetics, other-allied-health).
_Avoid_: Subtest, skill, specialty (bare)

**Subtest**:
One of R/L/W/S (Reading, Listening, Writing, Speaking).
_Avoid_: Profession, skill, module (bare)

**Rulebook kind**:
One of writing, speaking, listening, reading, grammar, vocabulary, pronunciation, conversation, remediation (plus exam-mode variants and drills).
_Avoid_: Profession, subtest
