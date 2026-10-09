#!/usr/bin/env python3
"""Apply the 2026-10-09 register-audit verdicts to features.json.

The audit verified 45 PARTIAL features against the code. Its result: 9 are now fully
built, 2 are owner-ruled out of scope, and several `repo_gap` strings have become
inaccurate even though the feature is still genuinely partial.

Only statuses and gap text change here; requirements are untouched. Run
render_traceability.py afterwards so the CSV and Markdown matrix cannot contradict the
JSON, then validate_traceability.py.

Stdlib only, per the pack's rules.
"""
import json
from pathlib import Path

TRACE = Path(__file__).resolve().parents[2] / 'docs' / 'ai-learning-companion' / 'traceability'

# Audit verdict: the gap is closed and the capability is genuinely learner-reachable.
NOW_EXISTS = {
    'F-020': "None. CompanionVocabularyIndexer publishes the vocabulary recall sets as "
             "entitlement-scoped CompanionSources (free preview unscoped, paid behind "
             "ModuleKeys.Recalls), chunked per category and wired into CompanionCorpusBuilder.",
    'F-033': "None. SamiPlanTemplateSeeder ships a distinct sami-30day / sami-60day / sami-90day "
             "set (4-5 / 8-9 / 12-13 weeks) selected by the computed weeks-to-exam window, with a "
             "different phase arc per horizon rather than one plan at three lengths.",
    'F-056': "None. companion_train_mistakes builds a drill from the learner's own evidenced "
             "ErrorDnaEntry rows, with per-category prompts and an honest empty case, granted and "
             "reachable on the learner turn.",
    'F-078': "None. An official result recorded through companion_confirm_scores is compared against "
             "the learner's own target and opens a CompanionJourney. Residual note: "
             "CompanionJourney.Outcome is not written automatically.",
    'F-113': "None. NextBestActionService switches to rehearsal and error review inside three days of "
             "the exam, the exam date and day count are in the prompt, and the plan template is "
             "selected from weeks-to-exam.",
    'F-114': "None. A 7-day inactivity rule and a factor-based ChurnRiskSnapshot both drive nudges. "
             "Residual note: LearnerInactiveNudge email is disabled by admin override "
             "(migration 20261212090000); in-app and push remain.",
    'F-115': "None. StudyPlanReminderWorker fires LearnerWeakSkillReminder against plan.WeakSkillFocus. "
             "Residual note: email is disabled by the same override; in-app and push remain.",
    'F-157': "None. CompanionMemoryPanel renders full companion memory controls (view, per-item delete, "
             "reset, download) on the companion page.",
    'F-161': "None. MESSAGE_MODULES includes companion and both messages/{en,ar}/companion.json bundles "
             "ship. Residual note: the i18n.ts header comment is stale and still claims only Writing "
             "is internationalised.",
    'F-046': "None outstanding for scheduling itself. Corrected: the previous gap text claimed "
             "\"never a parallel scheduler\", but ErrorDnaService runs its own 1/3/7/14/30-day "
             "expanding-interval ladder alongside Sm2Scheduler. The status/gap pair was internally "
             "inconsistent and the claim was inaccurate.",
}

# Owner has ruled these out; the vocabulary has no OUT_OF_SCOPE value, so DEFERRED_BY_SOURCE.
OUT_OF_SCOPE = {
    'F-022': "Out of scope by owner decision: the course video library, its transcripts and timestamp "
             "deep links are not companion knowledge sources. Recorded in code by "
             "CompanionKnowledgeGovernance.NotIngested, and video/audio/transcript/recording source "
             "types are refused at retrieval by CompanionContentBoundary.",
    'F-165': "Out of scope by owner decision: dedicated desktop apps are a non-goal where an equivalent "
             "web/PWA surface suffices (\"EQUIVALENT ACCEPTABLE\"). Tauri still bundles nsis/dmg/app and a "
             "macOS kill-switch is armed, and validateDesktopFeed requires only windows-x86_64. "
             "repo_stage stays Deferred.",
}

# Still genuinely partial, but the recorded gap no longer describes what is missing.
CORRECTED_GAP = {
    'F-014': "Session transcripts are still not CompanionSource rows -- live-class transcripts are "
             "chunked only into ClassRecordingEmbedding -- and the transcript/recording source types "
             "are now deliberately refused at retrieval. The vector(1536) half of the old gap is closed: "
             "CompanionChunk.Embedding is a pgvector Vector?.",
    'F-021': "search_recall_set is still granted to no feature (it was absent from CompanionToolCodes) "
             "-- now fixed in this change; recall-set CONTENT was already citable via "
             "CompanionVocabularyIndexer.",
    'F-070': "The confidence column, the optional API field and the analysing tool all exist, but no "
             "learner-facing picker sends a value, so ReadingAnswer.Confidence stays NULL and the "
             "confidence-vs-accuracy signal cannot yet produce data. The answer-change, pace and "
             "distractor signals work immediately from existing data.",
    'F-090': "The vision path works end to end through the floating assistant widget, but the dedicated "
             "companion page discarded the attachment argument in its AiAssistantInput onSend, so an "
             "image attached there was silently dropped -- now fixed in this change.",
    'F-120': "Ten seeded achievements tied to demonstrated learning value cannot be awarded yet: "
             "GamificationService.MeetsCriteria receives only XP, streak, attempt count and "
             "vocabulary counts, while ach-020..ach-024 (first_grade, all_subtests_grade, "
             "consecutive_improvement), ach-033/034/035/036 (review/pronunciation/conversation/"
             "grammar sessions), ach-040/041 (forum posts) and ach-042/043 (referrals) need data "
             "it is never handed. The API now reports `evaluable: false` for these and the "
             "achievements page labels them \"Not tracked\" instead of leaving them looking "
             "permanently locked. Remaining work to close this properly is plumbing: emit "
             "CheckAndAwardAchievementsAsync from each event source (grading, mock completion, "
             "drill and review sessions, forum posts, referral conversion, leaderboard refresh) "
             "and extend the evaluator with per-criterion queries for grades, session counts and "
             "consecutive score improvement.",
    'F-153': "The unknown-answer contract now exists across the prompt and the honest-empty tool paths, "
             "but there is still no unsupported-claim detection: CompanionLeakDetector checks canaries, "
             "pack scaffolding, credential shapes and verbatim reuse, never whether a claim is "
             "supported by the retrieved evidence.",
    'F-158': "Export now covers derived data, but the bulk reset deleted only notes and bookmarks -- "
             "companion memory entries and Error DNA survived it and Error DNA had no delete endpoint "
             "-- and the Documentation Center claimed the endpoint reset all of it. Fixed in this "
             "change; this row should be re-audited after the deploy.",
}


def main() -> int:
    doc = json.loads((TRACE / 'features.json').read_text(encoding='utf-8'))
    changed = []

    for feat in doc['features']:
        fid = feat['id']
        if fid in NOW_EXISTS:
            if feat['implementation_status'] != 'EXISTS':
                feat['implementation_status'] = 'EXISTS'
                changed.append(f"{fid} -> EXISTS")
            feat['repo_gap'] = NOW_EXISTS[fid]
        elif fid in OUT_OF_SCOPE:
            if feat['implementation_status'] != 'DEFERRED_BY_SOURCE':
                feat['implementation_status'] = 'DEFERRED_BY_SOURCE'
                changed.append(f"{fid} -> DEFERRED_BY_SOURCE")
            feat['repo_gap'] = OUT_OF_SCOPE[fid]
        elif fid in CORRECTED_GAP:
            feat['repo_gap'] = CORRECTED_GAP[fid]
            changed.append(f"{fid} gap text corrected")

    (TRACE / 'features.json').write_text(
        json.dumps(doc, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')

    print(f"applied {len(changed)} change(s):")
    for c in changed:
        print(f"  {c}")
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
