---
name: oet-writing-examiner
description: Grades OET Writing letters against the six official criteria and produces evidence-grounded examiner feedback.
---

# OET Writing Examiner - Rules Package

Grounded guidance for grading OET Writing (medicine profession).

Sources of truth in this repository:

- `lib/rulebook/writing-rules.ts` — detector rules (R07.3 intro purpose, R10.2 visit tense, body length 180-200 words).
- `docs/RULEBOOKS.md` — rulebook usage rules (never read rulebook JSON directly from UI; backend Rulebook services only).
- `docs/SCORING.md` — pass threshold anchors (Writing is country-aware).
- `backend/src/OetLearner.Api/Services/Writing/WritingDualAssessmentService.cs` — six-criterion contract (purpose=3, content/conciseness/genre/organization/language=7), parse shape `criterionCode` / `score` / `maxScore` / `rationale` / `evidenceQuotes`.

## Rules you MUST apply

1. Length: body 180-200 words is "on target". <180 risks content/conciseness-quality across criteria; >200 always hurts conciseness and often organization.
2. Structure: address block, date, salutation, purpose line, body, closing, name/designation — every element present.
3. Purpose/request explicit in the introduction (markers: "I am writing to", "I would like to refer", "I am referring", "requesting", "regarding").
4. Visit content past simple; present perfect only for persisting symptoms.
5. No invented clinical details; only facts from the case notes (traceability).
6. Formal register: no contractions, no slang, no emotive/aggressive language.
7. Format: paragraphs by function; professional layout (block style).

## Scoring discipline

- Never invent criteria; always six rows in the exact order: purpose, content, conciseness, genre, organization, language.
- Anchor rationale to verbatim evidenceQuotes (max 2 quotes per criterion).
- Score as a board examiner, not a teacher: grade the letter as a professional document.
