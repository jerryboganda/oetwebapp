# Product

## Register

product

## Users

Internationally-trained healthcare professionals (doctors, nurses, dentists,
pharmacists, and allied health workers) preparing for the OET (Occupational English
Test), the English-proficiency exam they must pass to register and practice in
English-speaking countries.

Their context:
- **High stakes.** Passing gates their career and ability to work abroad. Failure is
  expensive (re-sit fees, delayed registration) and stressful.
- **Time-poor and often anxious.** They study around demanding clinical shifts, in
  short sessions, frequently on a phone. Many carry real test anxiety.
- **Non-native English speakers (ESL).** Comprehension load matters; plain, precise
  language beats clever wording.
- **Multi-device.** Web, mobile (Capacitor iOS/Android), and desktop (Electron). The
  experience must hold up on a small screen during a commute and on a large screen
  during a focused study block.

The job to be done: build the English proficiency and exam technique to pass all four
OET sub-tests (Listening, Reading, Writing, Speaking) at the required grade, and walk
into the real exam feeling prepared and calm.

## Product Purpose

A guided OET exam-prep platform covering all four sub-tests end to end: diagnostics that
locate weak spots, structured learning pathways and lessons, realistic mock exams,
AI-assisted practice and feedback, expert human review, and progress analytics that show
whether a learner is on track.

It exists because generic English courses do not prepare candidates for the OET's
specific format, healthcare context, and scoring. Success looks like: learners improve
measurably across sub-tests, trust the feedback they receive, and pass the OET, having
felt supported rather than overwhelmed throughout.

## Brand Personality

A premium academic study companion that is also warm and reassuring. It treats users as
the capable professionals they are, never as children or as data points.

- **Three-word core:** calm, supportive, trustworthy, expressed through a premium,
  academic, focused surface with a warm, encouraging, human voice.
- **Voice and tone:** professional and precise about scores and feedback; warm and
  encouraging about effort and progress; reassuring but never patronizing. Speaks plainly
  for an ESL audience.
- **Emotional goals:** lower test anxiety, build justified confidence, and sustain
  momentum between study sessions.

## Anti-references

The interface should explicitly NOT feel like:

- **A cluttered cram tool (the primary thing to avoid).** No overwhelming walls of
  content, no dense everything-at-once screens. Density must never create the anxiety the
  product is meant to relieve. Favor calm, airy, one-clear-next-step layouts.
- **A sterile corporate dashboard.** Not cold enterprise KPI grids or gray-on-gray
  admin chrome. (DESIGN.md: "not sterile, not dense dark chrome.")
- **A loud marketing landing page.** No hero-metric templates, gradient hype, or salesy
  CTAs scattered through the study experience. (DESIGN.md: "not a marketing landing page.")
- **A gamified, childish app.** No confetti, cartoon mascots, or streak-spam that
  trivializes a high-stakes professional exam. Encouragement is earned and adult.
- **Generic AI-generated SaaS.** Avoid the tells: default-violet/indigo gradients used
  decoratively, side-stripe accent borders on cards and alerts, a rounded icon tile above
  every heading, and identical card grids. (The brand color stays violet by decision; the
  cliché *patterns* around it do not.)

## Design Principles

1. **Calm under pressure.** Every screen should lower cognitive load, not add to it. When
   in doubt, show less and make the next action obvious. The product's job is to reduce
   exam anxiety, and the UI is part of that job.
2. **Guided, not gamified.** Structure, pathways, and honest coaching, aimed at serious
   adults. Motivation comes from visible, trustworthy progress, not from points or prizes.
3. **Content is the product.** Exam material, practice, and feedback take center stage;
   chrome and decoration recede. Containers and ornament must earn their place.
4. **Earn trust through precision.** Accurate scoring, honest and specific feedback, and
   dependable, predictable behavior. Healthcare professionals demand rigor, and trust in
   the feedback is what makes the product worth using.
5. **Meet learners everywhere.** Equal care for phone, desktop, and tablet, for ESL
   readers, and for assistive-technology users. The experience should not degrade on the
   device or in the language a learner actually uses.

## Accessibility & Inclusion

- **Target:** WCAG 2.1 AA conformance, enforced by the existing axe-core Playwright suite.
- **Reduced motion:** Full `prefers-reduced-motion` support is built in (`lib/motion.ts`)
  and must be preserved in all new motion work.
- **Internationalization:** Right-to-left (Arabic) correctness is a committed requirement,
  not an afterthought. Layouts, icons, and motion must be direction-aware.
- **ESL-first clarity:** Plain language, strong contrast, generous touch targets (44 to
  48px), and unambiguous labels and error messages, because many users are reading in a
  second language under stress.
