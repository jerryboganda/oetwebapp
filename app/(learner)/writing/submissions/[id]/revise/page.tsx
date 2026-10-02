'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { RefreshCw } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { WritingEditorV2, type WritingEditorAnnotation } from '@/components/domain/writing/WritingEditorV2';
import { WordCounter } from '@/components/domain/writing/WordCounter';
import { SubmitBar } from '@/components/domain/writing/SubmitBar';
import { CanonViolationCard } from '@/components/domain/writing/CanonViolationCard';
import { DraftConflictNotice, DraftSaveStatus } from '@/components/domain/writing/DraftSaveStatus';
import { useWritingDraftSync, type DraftSyncBaseline } from '@/hooks/use-writing-draft-sync';
import { loadStoredSession } from '@/lib/auth-storage';
import {
  getWritingDraftV2,
  getWritingSubmission,
  getWritingSubmissionGrade,
  reviseWritingSubmission,
} from '@/lib/writing/api';
import { clearDraftShadow, draftShadowKey, readDraftShadow, reconcileDraft } from '@/lib/writing/draft-sync';
import { countLetterWords } from '@/lib/writing/letter-text';
import type {
  WritingGradeDto,
  WritingSubmissionDto,
} from '@/lib/writing/types';

export default function WritingReviseSubmissionPage() {
  const t = useTranslations();
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const submissionId = String(params?.id ?? '');
  const [userId] = useState(() => loadStoredSession()?.currentUser?.userId ?? 'anonymous');

  const [original, setOriginal] = useState<WritingSubmissionDto | null>(null);
  const [grade, setGrade] = useState<WritingGradeDto | null>(null);
  const [baseline, setBaseline] = useState<DraftSyncBaseline | null>(null);
  const [editorText, setEditorText] = useState('');
  const [editorKey, setEditorKey] = useState(0);
  const [previousText, setPreviousText] = useState<string | null>(null);
  const [content, setContent] = useState('');
  const [wordCount, setWordCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);
  const [loadAttempt, setLoadAttempt] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [startedAt] = useState(() => Date.now());

  const sync = useWritingDraftSync({
    scenarioId: original?.scenarioId ?? '',
    mode: 'revision',
    userId,
    baseline,
  });

  // The revision draft is restored before the editor mounts (it reads its text
  // once). A draft or device copy saved before this letter was submitted
  // belongs to an older revision of the task and is not restored. A failed
  // draft load is a Retry state — never "original letter + overwrite".
  useEffect(() => {
    if (!submissionId) return;
    let cancelled = false;
    const load = async () => {
      const [s, g] = await Promise.all([getWritingSubmission(submissionId), getWritingSubmissionGrade(submissionId)]);
      const draft = await getWritingDraftV2(s.scenarioId, 'revision');
      if (cancelled) return;
      const shadowKey = draftShadowKey(userId, s.scenarioId, 'revision');
      const submittedAt = Date.parse(s.submittedAt);
      const isThisRevision = (savedAt: number) => !Number.isFinite(submittedAt) || savedAt >= submittedAt;
      const active =
        draft && draft.status !== 'submitted' && isThisRevision(Date.parse(draft.lastSavedAt)) ? draft : null;
      let shadow = readDraftShadow(shadowKey);
      if (shadow && (draft?.status === 'submitted' || !isThisRevision(shadow.savedAt))) {
        clearDraftShadow(shadowKey);
        shadow = null;
      }
      const restored = reconcileDraft(active, shadow);
      const fromOriginal = restored.source === 'none';
      const text = fromOriginal ? s.letterContent : restored.text;
      const words = fromOriginal ? s.wordCount : restored.wordCount || countLetterWords(text);
      setOriginal(s);
      setGrade(g);
      setEditorText(text);
      setContent(text);
      setWordCount(words);
      setBaseline({
        text,
        wordCount: words,
        // Starting from the original letter needs no save until it is edited.
        serverText: fromOriginal ? s.letterContent : restored.serverText,
        // Writes go on top of whatever revision row exists (an older one is replaced).
        version: active ? restored.version : (draft?.version ?? 0),
        conflict: restored.conflict,
      });
    };
    void load().catch((err) => {
      if (cancelled) return;
      setLoadFailed(true);
      setError(err instanceof Error ? err.message : t('writing.submissions.revise.error.load'));
    });
    return () => {
      cancelled = true;
    };
    // `t` is read only for the error copy; a new translator must not reload the letter.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [submissionId, userId, loadAttempt]);

  // Canon offsets point into the ORIGINAL letter: only show them on it.
  const annotations = useMemo<WritingEditorAnnotation[]>(() => {
    if (!grade?.canonViolations || !original || editorText !== original.letterContent) return [];
    return grade.canonViolations.map((v) => ({
      charStart: v.charStart,
      charEnd: v.charEnd,
      type: 'canon',
      note: `${v.ruleId}: ${v.suggestedFix ?? v.ruleText}`,
      ruleId: v.ruleId,
    }));
  }, [grade, original, editorText]);

  const replaceEditorText = (text: string) => {
    setEditorText(text);
    setEditorKey((key) => key + 1);
    setContent(text);
    setWordCount(countLetterWords(text));
  };

  const canSubmit = !submitting && !!original && content !== original.letterContent && sync.online;

  const helperText = !sync.online
    ? t('writing.practice.session.draft.offlineSubmit')
    : content === original?.letterContent
      ? t('writing.submissions.revise.helper.noChanges')
      : t('writing.submissions.revise.helper.ready');

  const onSubmit = useCallback(async () => {
    if (!canSubmit || !original) return;
    setSubmitting(true);
    setError(null);
    try {
      const revised = await reviseWritingSubmission(submissionId, {
        letterContent: content,
        wordCount,
        timeSpentSeconds: Math.round((Date.now() - startedAt) / 1000),
      });
      sync.discard();
      router.push(`/writing/submissions/${encodeURIComponent(revised.id)}/grading`);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('writing.submissions.revise.error.submit'));
      setSubmitting(false);
    }
  }, [canSubmit, content, original, submissionId, wordCount, router, t, startedAt, sync]);

  return (
    <>
      <div className="space-y-4 pb-32" aria-busy={!original}>
        <header className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-surface p-4 shadow-sm">
          <div className="flex items-center gap-3">
            <RefreshCw className="h-5 w-5 text-warning-strong" aria-hidden="true" />
            <div>
              <p className="eyebrow text-muted">{t('writing.submissions.revise.eyebrow')}</p>
              <h1 className="text-base font-bold text-navy">{original ? t('writing.submissions.revise.heroTitle', { mode: original.mode }) : t('writing.submissions.revise.heroTitleLoading')}</h1>
              {grade ? <p className="mt-1 text-xs text-muted">{t('writing.submissions.revise.originalBand')} <Badge variant="muted" size="sm">{grade.bandLabel}</Badge></p> : null}
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-4">
            {baseline ? <DraftSaveStatus state={sync.state} /> : null}
            <WordCounter count={wordCount} target={{ min: 180, max: 220 }} ariaLabelPrefix="Letter length" />
          </div>
        </header>

        {error ? (
          <InlineAlert
            variant="error"
            action={
              loadFailed ? (
                <Button
                  size="sm"
                  onClick={() => {
                    setLoadFailed(false);
                    setError(null);
                    setLoadAttempt((n) => n + 1);
                  }}
                >
                  {t('writing.practice.session.loadError.retry')}
                </Button>
              ) : undefined
            }
          >
            {error}
          </InlineAlert>
        ) : null}

        <DraftConflictNotice
          conflict={sync.conflict !== null}
          previousText={previousText}
          onKeepThis={sync.keepLocal}
          onUseOther={() => {
            setPreviousText(content);
            replaceEditorText(sync.takeServer());
          }}
          onRestorePrevious={() => {
            if (previousText === null) return;
            replaceEditorText(previousText);
            sync.update(previousText, countLetterWords(previousText));
            setPreviousText(null);
          }}
          onDismissPrevious={() => setPreviousText(null)}
        />

        {grade?.canonViolations?.length ? (
          <Card padding="md">
            <CardContent>
              <h2 className="text-sm font-bold text-navy">{t('writing.submissions.revise.issuesHeading')}</h2>
              <div className="mt-2 grid gap-2 md:grid-cols-2">
                {grade.canonViolations.map((v) => (
                  <CanonViolationCard key={v.id} violation={v} />
                ))}
              </div>
            </CardContent>
          </Card>
        ) : null}

        <section aria-label={t('writing.submissions.revise.editorLabel')} className="rounded-2xl border border-border bg-surface p-4">
          {baseline ? (
            <WritingEditorV2
              key={editorKey}
              mode="revision"
              initialContent={editorText}
              annotations={annotations}
              onChange={(text, words) => {
                setContent(text);
                setWordCount(words);
                sync.update(text, words);
              }}
              onBlur={() => sync.flush()}
              placeholder={t('writing.submissions.revise.editorPlaceholder')}
              inputId="revision-editor"
            />
          ) : (
            <p className="p-4 text-sm text-muted">{t('writing.submissions.revise.heroTitleLoading')}</p>
          )}
        </section>

        <SubmitBar
          canSubmit={canSubmit}
          submitLabel={t('writing.submissions.revise.submit')}
          onSubmit={() => void onSubmit()}
          loading={submitting}
          helperText={helperText}
        />
      </div>
    </>
  );
}
