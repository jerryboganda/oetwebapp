'use client';

import { Suspense, useEffect, useRef, useState } from 'react';
import { MotionSection } from '@/components/ui/motion-primitives';
import { HelpCircle, CheckCircle2, XCircle, ArrowLeft, RotateCcw, Volume2 } from 'lucide-react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { PageSkeleton, Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchRecallsAudio, fetchVocabQuiz, submitVocabQuiz } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { useRecallsAudioUpgrade } from '@/components/domain/recalls/audio-upgrade-modal';
import { playTransientAudio } from '@/lib/recalls-audio';
import type { VocabularyQuizQuestion, VocabularyQuizResult } from '@/lib/types/vocabulary';

const QUIZ_FORMATS = [
  { id: 'definition_match', label: 'Definition Match', free: true },
  { id: 'fill_blank', label: 'Fill the Blank', free: false },
  { id: 'synonym_match', label: 'Synonym Match', free: false },
  { id: 'context_usage', label: 'Context Usage', free: false },
  { id: 'audio_recognition', label: 'Audio Recognition', free: false },
] as const;

type QuizFormatId = typeof QUIZ_FORMATS[number]['id'];

function VocabQuizContent() {
  const searchParams = useSearchParams();
  const initialFormat = (searchParams?.get('format') as QuizFormatId) ?? 'definition_match';

  const [format, setFormat] = useState<QuizFormatId>(initialFormat);
  const [questions, setQuestions] = useState<VocabularyQuizQuestion[]>([]);
  const [answers, setAnswers] = useState<Record<string, { selectedIndex?: number; userAnswer?: string; correct: boolean }>>({});
  const [current, setCurrent] = useState(0);
  const [selectedOption, setSelectedOption] = useState<number | null>(null);
  const [textAnswer, setTextAnswer] = useState('');
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<VocabularyQuizResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [premiumBlock, setPremiumBlock] = useState<string | null>(null);
  const [revealed, setRevealed] = useState(false);
  const { guardAudio, modal: audioUpgradeModal } = useRecallsAudioUpgrade();

  const startedAtRef = useRef<number>(0);

  useEffect(() => {
    analytics.track('vocab_quiz_viewed', { format });
    void loadQuiz(format);
  }, [format]);

  async function loadQuiz(fmt: QuizFormatId) {
    setLoading(true);
    setError(null);
    setPremiumBlock(null);
    setResult(null);
    setAnswers({});
    setCurrent(0);
    setSelectedOption(null);
    setTextAnswer('');
    setRevealed(false);
    try {
      const data = await fetchVocabQuiz(10, fmt);
      const loadedQuestions = Array.isArray(data)
        ? data
        : ((data as { questions?: VocabularyQuizQuestion[] }).questions ?? []);
      setQuestions(loadedQuestions as VocabularyQuizQuestion[]);
      startedAtRef.current = performance.now();
      analytics.track('vocab_quiz_started', { format: fmt, count: loadedQuestions.length });
    } catch (e) {
      const err = e as { code?: string; errorCode?: string; status?: number; message?: string };
      if (err?.code === 'VOCAB_PREMIUM_REQUIRED' || err?.errorCode === 'VOCAB_PREMIUM_REQUIRED' || err?.status === 402) {
        setPremiumBlock(`"${fmt.replace(/_/g, ' ')}" is a premium quiz format. Upgrade to unlock all 5 formats.`);
      } else {
        setError('Could not load quiz.');
      }
    } finally {
      setLoading(false);
    }
  }

  const q = questions[current];

  function handleSelect(idx: number) {
    if (!q || revealed) return;
    setSelectedOption(idx);
    setRevealed(true);
    const correct = idx === q.correctIndex;
    setAnswers(prev => ({
      ...prev,
      [q.termId]: { selectedIndex: idx, userAnswer: q.options?.[idx], correct },
    }));
  }

  function handleTextSubmit() {
    if (!q || revealed || !textAnswer.trim()) return;
    setRevealed(true);
    // Case-insensitive match (Fill-the-Blank / Audio Recognition)
    const user = textAnswer.trim().toLowerCase();
    const target = q.correctAnswer.toLowerCase();
    // Tolerant match: exact or Levenshtein distance ≤ 1 for short words.
    const correct = user === target || levenshtein(user, target) <= (target.length <= 8 ? 1 : 2);
    setAnswers(prev => ({
      ...prev,
      [q.termId]: { userAnswer: textAnswer.trim(), correct },
    }));
  }

  function nextQuestion() {
    setRevealed(false);
    setSelectedOption(null);
    setTextAnswer('');
    if (current + 1 >= questions.length) {
      void handleSubmit();
    } else {
      setCurrent(c => c + 1);
    }
  }

  async function handleSubmit() {
    setSubmitting(true);
    try {
      const durationSeconds = Math.max(1, Math.round((performance.now() - startedAtRef.current) / 1000));
      const answerList = Object.entries(answers).map(([termId, a]) => ({
        termId,
        correct: a.correct,
        userAnswer: a.userAnswer ?? (typeof a.selectedIndex === 'number' ? String(a.selectedIndex) : ''),
      }));
      const payload = { answers: answerList, durationSeconds, format };
      const res = await submitVocabQuiz(payload);
      const typed = res as VocabularyQuizResult;
      setResult(typed);
      analytics.track('vocab_quiz_submitted', {
        format,
        score: typed.score,
        termsQuizzed: typed.termsQuizzed,
        durationSeconds: typed.durationSeconds,
      });
    } catch {
      setError('Failed to submit quiz.');
    } finally {
      setSubmitting(false);
    }
  }

  async function playAudio(termId: string) {
    const response = await guardAudio(() => fetchRecallsAudio(termId, 'normal'), { termId });
    if (!response) return;
    playTransientAudio(response.url);
  }

  // Keyboard shortcuts for MCQ formats: 1-4 select, Enter = next when revealed.
  useEffect(() => {
    function onKey(ev: KeyboardEvent) {
      if (!q) return;
      if (revealed && ev.key === 'Enter') { nextQuestion(); return; }
      if (!revealed && q.options && q.options.length > 0) {
        const n = parseInt(ev.key, 10);
        if (n >= 1 && n <= q.options.length) handleSelect(n - 1);
      }
    }
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [q, revealed]);

  const isTextFormat = format === 'fill_blank' || format === 'audio_recognition';
  const isMcqFormat = !isTextFormat;

  return (
    <LearnerDashboardShell>
      <div className="mb-6 flex items-center gap-3">
        <Link href="/vocabulary" className="text-muted transition-colors hover:text-navy">
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <LearnerPageHero title="Vocabulary Quiz" description="Test your medical vocabulary knowledge" icon={HelpCircle} />
      </div>

      {/* Format picker */}
      <Card className="mb-4 border-border bg-surface p-4">
        <div className="mb-2 text-xs font-medium uppercase text-muted">Quiz format</div>
        <div className="flex flex-wrap gap-2">
          {QUIZ_FORMATS.map(fmt => (
            <button
              key={fmt.id}
              onClick={() => setFormat(fmt.id)}
              className={`rounded-full px-3 py-1.5 text-xs font-medium transition-colors ${
                format === fmt.id
                  ? 'bg-primary text-white'
                  : 'border border-border bg-background-light text-navy hover:border-primary/30'
              }`}
            >
              {fmt.label}
              {!fmt.free && <span className="ml-1 opacity-75">• Premium</span>}
            </button>
          ))}
        </div>
      </Card>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}
      {audioUpgradeModal}
      {premiumBlock && (
        <InlineAlert variant="info" className="mb-4">
          {premiumBlock}{' '}
          <Link href="/billing" className="font-semibold text-primary hover:underline">
            Upgrade now →
          </Link>
        </InlineAlert>
      )}

      {loading ? (
        <div className="space-y-3">
          <Skeleton className="h-48 rounded-2xl" />
          {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-12 rounded-xl" />)}
        </div>
      ) : result ? (
        <MotionSection className="mx-auto max-w-md py-12 text-center">
          <Card className="border-border bg-surface p-8">
            <div className="text-6xl font-bold text-navy mb-2">{result.correctCount}/{result.termsQuizzed}</div>
            <div className="text-xl text-muted mb-4">{Math.round(result.score)}% correct</div>
            <div className="text-xs text-muted mb-2">
              {result.durationSeconds}s · {result.format.replace(/_/g, ' ')}
            </div>
            {result.xpAwarded > 0 && (
              <div className="inline-flex items-center gap-1.5 rounded-full bg-warning/10 px-4 py-2 text-sm font-medium text-warning mb-4">
                +{result.xpAwarded} XP earned
              </div>
            )}
            {result.newlyMasteredTermIds?.length > 0 && (
              <div className="mb-6 rounded-2xl bg-success/10 p-3 text-sm text-success">
                🎉 You mastered {result.newlyMasteredTermIds.length} new term{result.newlyMasteredTermIds.length > 1 ? 's' : ''}!
              </div>
            )}
            <div className="flex flex-wrap gap-3 justify-center">
              <Link href="/vocabulary" className="rounded-xl border border-border bg-background-light px-5 py-2.5 text-sm font-medium text-navy shadow-sm transition-colors hover:border-primary/30 hover:bg-surface">
                Back to Vocabulary
              </Link>
              <Link href="/vocabulary/quiz/history" className="rounded-xl border border-border bg-background-light px-5 py-2.5 text-sm font-medium text-navy shadow-sm transition-colors hover:border-primary/30 hover:bg-surface">
                See history
              </Link>
              <button onClick={() => loadQuiz(format)} className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-5 py-2.5 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90">
                <RotateCcw className="w-4 h-4" /> New Quiz
              </button>
            </div>
          </Card>
        </MotionSection>
      ) : q ? (
        <div className="mx-auto max-w-xl">
          <LearnerSurfaceSectionHeader
            eyebrow="Quiz Progress"
            title={q.format.replace(/_/g, ' ')}
            description={
              q.format === 'fill_blank' ? 'Type the correct medical term.' :
              q.format === 'audio_recognition' ? 'Listen to the audio and type what you hear.' :
              q.format === 'context_usage' ? 'Select the sentence that uses the term correctly.' :
              q.format === 'synonym_match' ? 'Select the best synonym for the term.' :
              'Choose the best meaning for the medical term.'
            }
            className="mb-4"
          />
          <div className="mb-3 flex items-center justify-between text-sm text-muted">
            <span>Question {current + 1} of {questions.length}</span>
          </div>
          <div className="mb-6 h-1.5 w-full rounded-full bg-background-light">
            <div className="h-1.5 rounded-full bg-primary transition-all" style={{ width: `${Math.min(100, ((current + 1) / questions.length) * 100)}%` }} />
          </div>

          <Card className="mb-4 border-border bg-surface p-6">
            <div className="mb-2 text-xs font-medium uppercase text-muted">
              {q.format === 'context_usage' ? 'Context Usage' : q.format === 'synonym_match' ? 'Synonym' : q.format === 'fill_blank' ? 'Fill the blank' : q.format === 'audio_recognition' ? 'Audio' : 'What does this word mean?'}
            </div>
            <div className="text-2xl font-bold text-navy" role="heading" aria-level={2}>
              {q.prompt}
            </div>
            {q.format === 'audio_recognition' && (
              <button
                onClick={() => void playAudio(q.termId)}
                className="mt-4 inline-flex items-center gap-2 rounded-full border border-primary bg-primary/5 px-4 py-2 text-sm font-medium text-primary transition-colors hover:bg-primary/10"
                aria-label="Play audio again"
              >
                <Volume2 className="h-4 w-4" /> Play audio
              </button>
            )}
          </Card>

          {isMcqFormat && q.options && q.options.length > 0 && (
            <div className="space-y-2">
              {q.options.map((option, idx) => {
                let cls = 'border border-border bg-surface text-navy hover:border-primary/30 hover:bg-background-light';
                if (revealed) {
                  if (idx === q.correctIndex) cls = 'border border-success/30 bg-success/10 text-success';
                  else if (idx === selectedOption) cls = 'border border-danger/30 bg-danger/10 text-danger';
                  else cls = 'border border-border bg-background-light text-muted opacity-70';
                }
                return (
                  <button
                    key={idx}
                    onClick={() => handleSelect(idx)}
                    disabled={revealed}
                    className={`flex w-full items-center gap-3 rounded-2xl px-4 py-3.5 text-left transition-colors ${cls}`}
                  >
                    <span className="flex h-6 w-6 flex-shrink-0 items-center justify-center rounded-full border border-current text-xs font-bold" aria-hidden="true">
                      {idx + 1}
                    </span>
                    <span className="text-sm">{option}</span>
                    {revealed && idx === q.correctIndex && <CheckCircle2 className="w-4 h-4 text-success ml-auto" />}
                    {revealed && idx === selectedOption && idx !== q.correctIndex && <XCircle className="w-4 h-4 text-danger ml-auto" />}
                  </button>
                );
              })}
            </div>
          )}

          {isTextFormat && (
            <div className="space-y-3">
              <input
                type="text"
                value={textAnswer}
                onChange={e => setTextAnswer(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter' && !revealed) handleTextSubmit(); }}
                disabled={revealed}
                placeholder="Type your answer…"
                className="w-full rounded-2xl border border-border bg-surface px-4 py-3 text-base text-navy focus:outline-none focus:ring-2 focus:ring-primary/30 disabled:opacity-60"
                aria-label="Your answer"
                autoFocus
              />
              {!revealed && (
                <button
                  onClick={handleTextSubmit}
                  disabled={!textAnswer.trim()}
                  className="rounded-xl bg-primary px-6 py-2.5 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90 disabled:opacity-50"
                >
                  Submit answer
                </button>
              )}
              {revealed && (
                <div className={`rounded-2xl p-3 text-sm ${answers[q.termId]?.correct ? 'bg-success/10 text-success' : 'bg-danger/10 text-danger'}`}>
                  {answers[q.termId]?.correct ? 'Correct!' : (<>
                    Not quite. The correct answer is <strong>{q.correctAnswer}</strong>.
                  </>)}
                </div>
              )}
            </div>
          )}

          {revealed && (
            <MotionSection className="mt-4 flex justify-end">
              <button
                onClick={nextQuestion}
                disabled={submitting}
                className="rounded-xl bg-primary px-6 py-2.5 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90 disabled:opacity-50"
                autoFocus
              >
                {current + 1 >= questions.length ? 'Finish Quiz' : 'Next Question →'}
              </button>
            </MotionSection>
          )}
        </div>
      ) : !premiumBlock ? (
        <div className="text-center py-12 text-muted/60">
          No quiz questions available for this format. Try another format or add more words to your vocabulary list first.
        </div>
      ) : null}
    </LearnerDashboardShell>
  );
}

export default function VocabQuizPage() {
  return (
    <Suspense fallback={<PageSkeleton />}>
      <VocabQuizContent />
    </Suspense>
  );
}

// ── Small helper — Levenshtein distance ≤ 2 for fuzzy fill-blank matching ──
function levenshtein(a: string, b: string): number {
  if (a === b) return 0;
  const al = a.length, bl = b.length;
  if (al === 0) return bl;
  if (bl === 0) return al;
  const dp: number[] = new Array(bl + 1);
  for (let j = 0; j <= bl; j++) dp[j] = j;
  for (let i = 1; i <= al; i++) {
    let prev = dp[0];
    dp[0] = i;
    for (let j = 1; j <= bl; j++) {
      const tmp = dp[j];
      dp[j] = a[i - 1] === b[j - 1]
        ? prev
        : 1 + Math.min(dp[j - 1], dp[j], prev);
      prev = tmp;
    }
  }
  return dp[bl];
}
