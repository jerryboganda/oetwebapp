# Original User Request

## Initial Request — 2026-09-01T03:23:06Z

Build and scale a production-grade, world-class Occupational English Test (OET) preparation platform designed for global healthcare professionals and learners, architected to seamlessly support multi-exam expansion (IELTS, PTE, TOEFL) in subsequent phases.

Working directory: d:\Projects\OET with Dr Hesham\OET Project Web App
Integrity mode: development

## Requirements

### R1. Complete 4-Skill OET Exam & Practice Engine
Deliver full exam and practice test flows across all four OET sub-tests:
- **Reading**: Part A (timed 15-min lookup, 20 questions), Part B (6 workplace extracts), and Part C (2 long texts, 16 questions).
- **Listening**: Part A (2 consultations, note-completion), Part B (6 short workplace extracts), and Part C (2 presentations/interviews).
- **Writing**: Profession-specific case notes, letter drafting interface with word count tracking, and structured evaluation.
- **Speaking**: Interactive clinical role-play scenarios with audio recording, prompt cards, and structured performance assessment.

### R2. Server-Authoritative Scoring & Analytics Engine
Implement deterministic, server-authoritative scoring adhering to official OET standards:
- Objective scoring for Reading and Listening mapped to the 0–500 scale (350/500 pass threshold, 30/42 anchor).
- Criterion-based rubric assessment for Writing and Speaking.
- Generation of detailed performance breakdowns and Statement of Results.

### R3. Multi-Exam Extensible Domain Architecture
Structure core test-taking, content delivery, scoring, and user attempt data models with modular exam-type abstractions so that additional language exams (IELTS Academic/General, PTE, TOEFL iBT) can be introduced without altering core infrastructure.

### R4. Candidate Experience, Entitlements & Admin Management
- **Candidate Hub**: Timed exam mode, untimed practice mode, attempt history, progress analytics, and credit/entitlement enforcement.
- **Admin Management**: Secure tools for importing, validating, and publishing official content papers, media assets, and answer keys.

### R5. Controlled Infrastructure & AI Gateway
- All external AI scoring, evaluation, transcription, and speech synthesis must route through a centralized server-side gateway with structured audit records and usage tracking.
- Media assets and papers must persist in dedicated storage volumes through abstract storage interfaces.

## Acceptance Criteria

### Core Exam Flows & Scoring
- [ ] Candidate can complete end-to-end exam and practice sessions for Reading (Parts A, B, C) and Listening (Parts A, B, C) with automated instant server-side grading and detailed answer review.
- [ ] Writing submission flow captures candidate letters, evaluates against OET criteria, and produces actionable feedback with band scores.
- [ ] Speaking session flow delivers role-play prompt cards, records audio submissions, and provides evaluation metrics.
- [ ] Reading and Listening scoring accurately implements the 500-point scale with the 30/42 (350) benchmark.

### Architecture & Extensibility
- [ ] Domain models cleanly decouple exam-specific configuration (sections, timing, question types) from generic attempt/session handling.
- [ ] Schema and API contracts accommodate multi-exam identifiers without breaking existing OET test assets.

### Verification & Quality Bar
- [ ] Automated unit and integration tests verify scoring accuracy, exam session lifecycle, and grading calculations.
- [ ] Project compiles cleanly across frontend and backend (	sc --noEmit, backend build) with all test suites passing.
