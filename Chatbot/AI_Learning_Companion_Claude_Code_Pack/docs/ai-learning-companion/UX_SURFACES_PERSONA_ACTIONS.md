# UX, Surfaces, Persona, Context and Action Layer

## 1. Experience goal

The companion should feel like one persistent mentor embedded into the existing OET product, not disconnected AI widgets. Reuse the current design system, navigation and account state.

## 2. Persona

Use “Talk to Jana” or “Talk to Sami” only as working persona configuration. Keep master/legal brand independent. Persona text/voice/branding changes must not change core logic. Public copy should prefer AI Learning Companion/Tutor/Mentor rather than “chatbot”.

## 3. Global companion shell

Where supported, provide compact floating launcher, expandable panel, full tutor route, persistent conversation continuity according to tier/privacy, current plan/next action, allowance/credit status, source/citation display, quick actions and clear mode indicator (Tutor/Practice/Examiner/Coach etc.). Different surfaces can have different layouts while sharing session/context services.

## 4. Progressive onboarding

Show value quickly. Recommended sequence: profession + exam; exam date/target; urgent problem/baseline; study availability; language/teaching style; optional result import after confirmation. Learner can edit later; exam-date changes explain replanning impact.

## 5. Surface-specific behaviors

### Public site
Limited demo, lead capture after ~3 interactions, public exam/navigation knowledge only, no paid content leakage, no persistent academic profile before registration.

### Dashboard
“What should I do next?” card, countdown, plan, weakness/risk signals, progress summary and Start action.

### Reading/Listening
Context side panel knows current part/question/selected answer/attempt state. Exam mode cannot reveal prohibited hints/answers before submission.

### Writing
Copilot can use selected sentence/paragraph/whole letter + task metadata. Modes: guided, hint, rewrite, compare and examiner/teaching. Feedback links to rules/sources where available.

### Speaking
Remain in role-play context until exit. Practice can pause-and-coach; strict exam cannot. Transcript/replay appears only when allowed. Voice cost/credit state is clear.

### Video
Know video ID/timestamp. Support current-window summary, quiz, save rule and source-linked example.

### Results/Analytics
Explain score pattern/change drivers, Error DNA/mastery updates and exact next action. Never show guaranteed pass probability.

### Support
Resolve deterministic help where possible; create contextual support request when unresolved.

## 6. Context Envelope

Frontend sends bounded identifiers/context, not entire page HTML. Example:

```json
{
  "surface": "writing_editor",
  "route_id": "...",
  "exam_id": "...",
  "attempt_id": "...",
  "question_id": null,
  "resource_id": "...",
  "video_time_seconds": null,
  "selection": {"start": 120, "end": 244},
  "mode": "tutor"
}
```

Server resolves allowed details by ID and entitlement. Arbitrary client text cannot grant protected source access.

## 7. Quick actions

Relevant buttons can include Explain simpler, Explain in Arabic, Another example, Quiz me, Save this, Add to plan, Open lesson, Ask tutor/human, Start practice and Retry my mistakes. Actions come from server allowlist, not model-generated raw HTML/URLs.

## 8. Platform Action Catalogue

Typed actions:

- `OPEN_RESOURCE`;
- `OPEN_VIDEO_AT_TIMESTAMP`;
- `START_PRACTICE`;
- `CONTINUE_LAST_ACTIVITY`;
- `SAVE_NOTE`;
- `SAVE_VOCABULARY`;
- `ADD_PLAN_ITEM`;
- `MARK_PLAN_ITEM_COMPLETE`;
- `SET_REMINDER`;
- `OPEN_UPGRADE`;
- `START_CHECKOUT`;
- `CREATE_SUPPORT_REQUEST`;
- `PREPARE_TUTOR_HANDOFF`;
- `OPEN_WORKSHOP`;
- `REPORT_CONTENT_ISSUE`;
- `SHOW_ALLOWANCE`.

Each has auth, entitlement, confirmation, audit and idempotency rules.

## 9. Paywall UX

Never use a dead access error as conversion UX. Show what was requested, a value preview, exact required tier/current price, 3–5 relevant benefits, one CTA and why it is useful from learner context when appropriate. Preserve conversation/pending action through checkout; after payment re-evaluate entitlement and resume without forcing restart.

## 10. Usage and AI Credit UX

Show base allowance separately from AI Credit balance. Before chargeable action show exact credit charge and require confirmation. Explain charge. Show technical-failure restoration where relevant. User-facing UI may show one credit total while backend keeps provenance buckets. Top-up must not force tier change.

## 11. Memory controls

Learner can see meaningful learning profile/memory, correct/edit applicable fields, delete/reset learning memory where supported, clear chat and use platform export/delete workflow. Copy distinguishes chat history from durable learning profile.

## 12. Arabic/English/RTL

Support English, Arabic and natural code-switching while retaining English medical/exam terminology where useful. RTL must handle mixed-direction terms, numbers, timers and score tables. Use locale-aware formatting; screen-reader labels remain meaningful; per-message language override is allowed.

## 13. Accessibility

Target WCAG 2.2 AA: keyboard navigation, focus visibility, contrast, scalable text, captions/transcripts, alternatives to speech, accessible modal/paywall/error messages, semantic controls and usable touch targets. Voice-only flows need text alternatives.

## 14. Notifications and proactive coaching

Respect channel consent, quiet hours and frequency. Notifications should be actionable/evidence-based: exam countdown, neglected high-value subtest, relevant new content, weekly summary or changed plan. Avoid spam/punitive tone.

## 15. Positive-moment growth actions

Review/testimonial/referral prompts only after genuine positive milestones/results, never during frustration, failure handling or support escalation.

## 16. Native/mobile reporting

Where store policy requires, provide visible in-app reporting of problematic AI output with category/context and moderation/support workflow.
