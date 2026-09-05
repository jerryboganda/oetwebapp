# Content Operations and Knowledge Ingestion Pipeline

## Critical principle

The assistant cannot credibly be described as grounded in “everything” until every eligible source asset is inventoried, processed, tagged, approved and versioned. Content operations is a permanent production workstream.

## Step 1 — Asset Register

Inventory every Rule Book, PDF/slide/handout/note, Tutor Book asset, Reading/Listening material, recall/practice bank, Writing/Speaking workshop, correction session, recording, transcript, video/audio, platform/support article and official exam source.

Record source ID, type, owner, profession, exam/version, subtest/skill, package/entitlement, confidentiality, approval state, files/video hours/pages and update cadence.

**Owner:** Content Ops + Dr Hesham / named subject owner.  
**Output:** master source-of-truth register. Exact counts/hours are TO INVENTORY.

## Step 2 — Transcription / Extraction

Generate/import searchable text while preserving source file/version/checksum, PDF page/slide, video/audio timestamp, chapter/section and extraction confidence where relevant. Never discard original location metadata.

## Step 3 — Chaptering and Semantic Tagging

Split by lesson/rule/concept and assign exam/version, profession, subtest/skill/task, difficulty where useful, content entitlement, authority class, effective dates and security/visibility tags. Output is retrieval-ready structured corpus, not automatically authoritative rules.

## Step 4 — Rule / FAQ / Example Extraction

AI may propose teaching rules, examples, exceptions, common mistakes, FAQs, vocabulary, quizzes and summaries. All remain drafts with source links.

## Step 5 — Pedagogical Approval

Dr Hesham or named subject owner approves/edits/rejects draft rules before they become authoritative. Record approver, timestamp, effective version, change reason, source refs and profession/task scope. No model-generated rule self-promotes.

## Step 6 — Security Marking

Apply entitlement/package label, confidentiality level, quote policy, optional canary/watermark, public/private class and future tenant namespace.

## Step 7 — Evaluation

Before publish run retrieval recall/precision, authority selection, profession/version filter, entitlement leak suite, golden questions, content diff/regression and canary/exfiltration tests where relevant. Attach release report.

## Step 8 — Publish / Rollback

Build a versioned knowledge release containing source versions, index/checksum, evaluation result, approval, changelog and rollback target. Support rollback without full application deployment.

## Step 9 — Ongoing Cadence

Every new workshop, recall, official-rule change or correction enters the same pipeline. Define content SLA, owner and maintenance cost; values remain TO VERIFY where not operationally known.

## Platform map ingestion

Treat navigation as structured data where possible. For each destination record route/resource ID, title/type, profession/exam/package visibility, required entitlement, deep-link resolver, mobile/web support, timestamp capability and retired/renamed state.

## Official facts workflow

Official exam sources need review/effective dates. Capture source reference where allowed. Never overwrite prior fact without history. Learner exam date resolves applicable version.

## Video pipeline

For videos store transcript, timestamp segments, chapters, rule/example candidates, entitlement, source authority, quiz/notes proposals, approval and index release. Current-video context can retrieve current time window without exposing unrelated locked content.

## Content-gap / teaching-gap loop

Analytics may create proposals when many learners ask a weakly covered question, learners repeatedly miss a concept after completing the lesson or low-confidence answers cluster around a topic. Output is an admin queue, never an auto-published rule/page.

## Content Studio requirements

Admin tools should eventually support source upload/register, extraction status, tags/authority/entitlement, draft proposals, diff from previous version, approve/reject/edit, evaluation result, publish/rollback and audit log.

## Operational metrics

Track inventoried vs processed vs approved assets, extraction failures, index lag, evaluation pass/fail, source conflicts, content flags, correction/publish time, content gaps and maintenance hours/cost.
