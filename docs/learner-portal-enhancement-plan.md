# Learner Portal Enhancement Plan — Ultra-High-Level

## Executive Summary

The Learner Portal is the largest surface area with **45+ route groups, ~85 backend endpoint files, and a complete 10-phase user journey** (auth → onboarding → diagnostic → study plan → practice loop → spaced repetition → mock exams → progress monitoring → exam prep → support). All 11 original gaps from the comprehensive audit are closed (100%).

**Critical open gaps**: Complete UX audit undone (70+ routes unscored), Recalls module test desert, `/practice` 404 in production, mobile hardware validation pending, Writing revision coach + Speaking transcript loop on roadmap but unimplemented.

---

## 1. Learner Dashboard Enhancement

### 1.1 Unified Next-Action Engine
- `GET /v1/learner/next-actions` — priority queue across StudyPlan, SpacedRepetition, Readiness, Diagnostics
- Frontend: Single prominent card with clear CTA, animated countdown for time-sensitive items

### 1.2 Readiness Score Breakdown
- Per-criterion readiness radar chart, "What's holding you back" blocker card
- `GET /v1/learner/readiness/blockers` — ordered list with recommendations

### 1.3 Progress Timeline Redesign
- Interactive timeline with attempt markers, score milestones, trend projection
- `GET /v1/learner/progress/trend` — projected score trajectory

---

## 2. Study Plan Enhancement

### 2.1 Adaptive Difficulty
- Tasks auto-adjust based on performance (>80% → harder; <50% → foundational)
- Difficulty badge on each task (Foundational/Standard/Advanced)

### 2.2 Study Plan Calendar View
- `/study-plan/calendar` — weekly/monthly calendar with completed/pending/missed indicators

---

## 3. Skills Practice Enhancements

### 3.1 Writing Revision Coach (ROADMAP)
- Guided revision with AI hints per criterion, draft comparison, improvement score

### 3.2 Speaking Fluency Timeline (ROADMAP)
- Interactive timeline: wpm markers, pause detection, filler word count

### 3.3 Quick Practice Session (FIX)
- `/practice` — restore from 404; implement smart session generator

---

## 4. Recalls & Vocabulary Enhancement

### 4.1 Recalls Dashboard
- Streak, daily goal progress ring, mastery %, weakest words list

### 4.2 Listen and Type Drill
- Audio playback + typed answer verification

---

## 5. Community & Peer Features

### 5.1 Peer Review Exchange
- Match learners by skill/profession for reciprocal writing/speaking review

### 5.2 Study Groups
- Group creation, join, member list, activity feed

---

## 6. Testing Coverage Enhancement

| Priority | Target | Tests |
|---|---|---|
| **P0** | Recalls, Grammar, Learning Paths | 14 unit tests |
| **P1** | Remediation, Peer Review, Quick Session | 12 unit tests |
| **P2** | Calendar, Listen and Type | 7 unit tests |
| **E2E P0** | Recalls, Peer Review, Study Plan Calendar | 3 new specs |

---

## 7. Implementation Sequence

**Phase A** — Dashboard Next-Action Engine + Readiness Blockers + Progress Trend
**Phase B** — Study Plan Calendar + Adaptive Difficulty + Skills Practice
**Phase C** — Recalls Dashboard + Listen & Type + Peer Review + Study Groups
**Phase D** — Testing Backfill + UX Audit + Verification

### Files (12 new, 15 modified)
New: Calendar page, Listen-and-type page, Next-Action hook, Adaptive study hook, Backend endpoints/service, 3 E2E specs
Modified: Dashboard, Readiness, Progress, Recalls, Peer Review, Community, Study Plan, Practice, Writing Revision, Speaking Fluency, API client, Types, Learner data, Layout
