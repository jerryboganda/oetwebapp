'use client';

import { memo, useState } from 'react';
import {
  BookOpen,
  FilePenLine,
  Headphones,
  Mic,
  Layers,
  BookMarked,
  Target,
  Volume2,
  FileQuestion,
  AlertTriangle,
} from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import {
  type ReviewAnswerPayload,
  type ReviewItem,
  type ReviewPromptKind,
  type ReviewQuestionPayload,
  type ReviewRichContent,
  SOURCE_TYPE_LABELS,
  safeParseJson,
} from '@/lib/types/review';

interface ReviewItemRendererProps {
  item: ReviewItem;
  revealed: boolean;
}

const PROMPT_ICONS: Record<ReviewPromptKind, React.ElementType> = {
  grammar: BookMarked,
  vocabulary: Layers,
  pronunciation: Mic,
  writing_issue: FilePenLine,
  speaking_issue: Volume2,
  reading_miss: BookOpen,
  listening_miss: Headphones,
  mock_miss: FileQuestion,
  generic: Target,
};

const PROMPT_ACCENT: Record<ReviewPromptKind, string> = {
  grammar: 'text-indigo-600 bg-indigo-50 dark:text-indigo-300 dark:bg-indigo-500/10',
  vocabulary: 'text-violet-600 bg-violet-50 dark:text-violet-300 dark:bg-violet-500/10',
  pronunciation: 'text-purple-600 bg-purple-50 dark:text-purple-300 dark:bg-purple-500/10',
  writing_issue: 'text-rose-600 bg-rose-50 dark:text-rose-300 dark:bg-rose-500/10',
  speaking_issue: 'text-fuchsia-600 bg-fuchsia-50 dark:text-fuchsia-300 dark:bg-fuchsia-500/10',
  reading_miss: 'text-blue-600 bg-blue-50 dark:text-blue-300 dark:bg-blue-500/10',
  listening_miss: 'text-indigo-600 bg-indigo-50 dark:text-indigo-300 dark:bg-indigo-500/10',
  mock_miss: 'text-amber-600 bg-amber-50 dark:text-amber-300 dark:bg-amber-500/10',
  generic: 'text-muted bg-background-light',
};

export const ReviewItemRenderer = memo(function ReviewItemRenderer({ item, revealed }: ReviewItemRendererProps) {
  const question = safeParseJson<ReviewQuestionPayload>(item.questionJson, {});
  const answer = safeParseJson<ReviewAnswerPayload>(item.answerJson, {});
  const rich = safeParseJson<ReviewRichContent>(item.richContentJson ?? null, {});
  const Icon = PROMPT_ICONS[item.promptKind] ?? Target;
  const accent = PROMPT_ACCENT[item.promptKind] ?? PROMPT_ACCENT.generic;

  return (
    <div className="space-y-5">
      <header className="flex items-start justify-between gap-3">
        <div className="flex items-start gap-3">
          <span
            className={cn(
              'inline-flex h-10 w-10 flex-none items-center justify-center rounded-2xl',
              accent,
            )}
            aria-hidden="true"
          >
            <Icon className="h-5 w-5" />
          </span>
          <div>
            <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-primary">
              {SOURCE_TYPE_LABELS[item.sourceType] ?? item.sourceType}
              {item.criterionCode ? (
                <>
                  {' · '}
                  <span className="text-muted">{formatCriterion(item.criterionCode)}</span>
                </>
              ) : null}
            </p>
            <h3 className="mt-1 text-lg font-semibold leading-snug text-navy">
              {item.title ?? question.text ?? 'Review item'}
            </h3>
          </div>
        </div>
        {answer.severity ? <SeverityBadge severity={String(answer.severity)} /> : null}
      </header>

      <PromptBody item={item} question={question} rich={rich} />

      {revealed ? <AnswerBody item={item} answer={answer} rich={rich} /> : null}
    </div>
  );
});

function SeverityBadge({ severity }: { severity: string }) {
  const lower = severity.toLowerCase();
  if (lower === 'high' || lower === 'critical') {
    return <Badge variant="danger">High priority</Badge>;
  }
  if (lower === 'medium') {
    return <Badge variant="warning">Medium</Badge>;
  }
  return <Badge variant="default">{severity}</Badge>;
}

function formatCriterion(code: string) {
  return code
    .split(/[_\-:]/)
    .map((part) => (part.length > 0 ? part[0].toUpperCase() + part.slice(1) : part))
    .join(' ');
}

function PromptBody({
  item,
  question,
  rich,
}: {
  item: ReviewItem;
  question: ReviewQuestionPayload;
  rich: ReviewRichContent;
}) {
  switch (item.promptKind) {
    case 'vocabulary':
      return <VocabularyPrompt rich={rich} />;
    case 'pronunciation':
      return <PronunciationPrompt rich={rich} />;
    case 'writing_issue':
      return <WritingPrompt question={question} rich={rich} />;
    case 'speaking_issue':
      return <SpeakingPrompt question={question} rich={rich} />;
    case 'reading_miss':
      return <ExcerptPrompt label="Reading question" text={question.text} rich={rich} />;
    case 'listening_miss':
      return <ExcerptPrompt label="Listening question" text={question.text} rich={rich} />;
    case 'mock_miss':
      return <ExcerptPrompt label="Mock question" text={question.text} rich={rich} />;
    case 'grammar':
      return <ExcerptPrompt label="Grammar exercise" text={question.text} rich={rich} />;
    default:
      return <ExcerptPrompt label="Prompt" text={question.text} rich={rich} />;
  }
}

function VocabularyPrompt({ rich }: { rich: ReviewRichContent }) {
  return (
    <div className="rounded-3xl border border-border bg-surface p-5 shadow-sm">
      <div className="flex items-baseline justify-between gap-3">
        <p className="text-2xl font-semibold text-navy">{rich.term ?? '—'}</p>
        {rich.ipa ? <span className="font-mono text-sm text-muted">/{rich.ipa}/</span> : null}
      </div>
      {rich.category ? (
        <p className="mt-2 text-xs font-semibold uppercase tracking-[0.18em] text-muted">{rich.category}</p>
      ) : null}
      {rich.audioUrl ? <AudioButton url={rich.audioUrl} label="Play pronunciation" /> : null}
    </div>
  );
}

function PronunciationPrompt({ rich }: { rich: ReviewRichContent }) {
  return (
    <div className="rounded-3xl border border-border bg-surface p-5 shadow-sm">
      <p className="text-sm text-muted">Practise phoneme</p>
      <div className="mt-2 flex items-baseline gap-3">
        <span className="font-mono text-3xl font-semibold text-navy">/{rich.phoneme ?? '?'}/</span>
        {rich.ruleId ? (
          <Badge variant="default" className="text-[10px]">
            Rule {rich.ruleId}
          </Badge>
        ) : null}
      </div>
      {typeof rich.score === 'number' ? (
        <p className="mt-3 text-sm text-muted">
          Last score:{' '}
          <span className="font-semibold text-navy">{Math.round(rich.score)}/100</span>
        </p>
      ) : null}
      {rich.audioUrl ? <AudioButton url={rich.audioUrl} label="Play reference audio" /> : null}
    </div>
  );
}

function WritingPrompt({
  question,
  rich,
}: {
  question: ReviewQuestionPayload;
  rich: ReviewRichContent;
}) {
  return (
    <div className="space-y-3">
      {rich.anchorSnippet ? (
        <blockquote className="rounded-3xl border border-border bg-background-light p-4 text-sm italic text-navy/80">
          “{rich.anchorSnippet}”
        </blockquote>
      ) : null}
      <p className="text-base leading-relaxed text-navy">{question.text ?? '—'}</p>
    </div>
  );
}

function SpeakingPrompt({
  question,
  rich,
}: {
  question: ReviewQuestionPayload;
  rich: ReviewRichContent;
}) {
  return (
    <div className="space-y-3">
      {rich.transcriptLineId ? (
        <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted">
          Transcript line: {rich.transcriptLineId}
        </p>
      ) : null}
      <p className="text-base leading-relaxed text-navy">{question.text ?? '—'}</p>
    </div>
  );
}

function ExcerptPrompt({
  label,
  text,
  rich,
}: {
  label: string;
  text: unknown;
  rich: ReviewRichContent;
}) {
  return (
    <div className="space-y-3">
      {rich.transcriptSnippet ? (
        <blockquote className="rounded-3xl border border-border bg-background-light p-4 text-sm italic text-navy/80">
          “{rich.transcriptSnippet}”
        </blockquote>
      ) : null}
      <div>
        <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">{label}</p>
        <p className="mt-1 text-base leading-relaxed text-navy">{typeof text === 'string' ? text : '—'}</p>
      </div>
    </div>
  );
}

function AnswerBody({
  item,
  answer,
  rich,
}: {
  item: ReviewItem;
  answer: ReviewAnswerPayload;
  rich: ReviewRichContent;
}) {
  if (item.promptKind === 'vocabulary') {
    return (
      <div className="rounded-3xl border border-border bg-background-light p-5 text-navy/90">
        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted">Definition</p>
        <p className="mt-1 text-base leading-relaxed">{rich.definition ?? answer.text ?? '—'}</p>
        {rich.exampleSentence ? (
          <>
            <p className="mt-4 text-xs font-semibold uppercase tracking-[0.18em] text-muted">Example</p>
            <p className="mt-1 text-sm italic text-navy/80">“{rich.exampleSentence}”</p>
          </>
        ) : null}
      </div>
    );
  }

  if (item.promptKind === 'pronunciation') {
    return (
      <div className="rounded-3xl border border-border bg-background-light p-5 text-navy/90">
        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted">Tip</p>
        <p className="mt-1 text-base leading-relaxed">
          {rich.tip ?? answer.text ?? 'Practise with the linked pronunciation drill for guided feedback.'}
        </p>
      </div>
    );
  }

  const answerText =
    (typeof answer.text === 'string' && answer.text) ||
    rich.suggestedFix ||
    '—';

  return (
    <div className="rounded-3xl border border-border bg-background-light p-5 text-navy/90">
      <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted">Correct answer</p>
      <p className="mt-1 text-base leading-relaxed">{answerText}</p>
      {typeof answer.explanation === 'string' && answer.explanation.length > 0 ? (
        <>
          <p className="mt-4 text-xs font-semibold uppercase tracking-[0.18em] text-muted">Why</p>
          <p className="mt-1 text-sm leading-relaxed text-navy/80">{answer.explanation}</p>
        </>
      ) : null}
      {typeof answer.drillPrompt === 'string' && answer.drillPrompt.length > 0 ? (
        <>
          <p className="mt-4 text-xs font-semibold uppercase tracking-[0.18em] text-muted">Drill prompt</p>
          <p className="mt-1 text-sm leading-relaxed text-navy/80">{answer.drillPrompt}</p>
        </>
      ) : null}
    </div>
  );
}

function AudioButton({ url, label }: { url: string; label: string }) {
  const [playing, setPlaying] = useState(false);
  const play = () => {
    try {
      const audio = new Audio(url);
      setPlaying(true);
      audio.addEventListener('ended', () => setPlaying(false));
      audio.addEventListener('error', () => setPlaying(false));
      void audio.play();
    } catch {
      setPlaying(false);
    }
  };
  return (
    <button
      type="button"
      onClick={play}
      className={cn(
        'mt-4 inline-flex items-center gap-2 rounded-full border border-border bg-surface px-3 py-1.5 text-xs font-semibold text-navy transition-colors hover:border-primary hover:text-primary',
        playing ? 'animate-pulse' : '',
      )}
      aria-label={label}
    >
      <Volume2 className="h-3.5 w-3.5" />
      {playing ? 'Playing…' : label}
    </button>
  );
}

export { SOURCE_TYPE_LABELS };
