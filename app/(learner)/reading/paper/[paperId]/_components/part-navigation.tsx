import { useCallback } from 'react';
import { Flag } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import { createReadingPaperAnnotation, type ReadingPaperAnnotationDto, type ReadingLearnerStructureDto, type ReadingPartCode, type ReadingQuestionLearnerDto } from '@/lib/reading-authoring-api';
import { ReadingPdfViewer } from '@/components/domain/reading-pdf-viewer';
import { readingPublicDisplayNumber } from '@/lib/reading-display-number';
import { type ReadingSectionCode, isAnsweredJson } from '../reading-paper-helpers';
import { QuestionInput } from './question-controls';

export function PartTabs({
  structure,
  activePart,
  answers,
  flagged,
  partALocked,
  partBCAccessible,
  onChange,
}: {
  structure: ReadingLearnerStructureDto;
  activePart: ReadingPartCode;
  answers: Record<string, string>;
  flagged: Set<string>;
  partALocked: boolean;
  partBCAccessible: boolean;
  onChange: (part: ReadingPartCode) => void;
}) {
  return (
    <div
      className="flex gap-2 overflow-x-auto border-b border-border"
      role="tablist"
      aria-label="Reading parts"
    >
      {structure.parts.map((part) => {
        const answered = part.questions.filter((question) => isAnsweredJson(answers[question.id])).length;
        const flaggedCount = part.questions.filter((question) => flagged.has(question.id)).length;
        const isActive = activePart === part.partCode;
        const isLocked = (part.partCode === 'A' && partALocked)
          || ((part.partCode === 'B' || part.partCode === 'C') && !partBCAccessible);
        const partALabel = isLocked ? ' (locked)' : '';
        return (
          <button
            key={part.partCode}
            type="button"
            role="tab"
            id={`reading-part-tab-${part.partCode}`}
            aria-controls={`reading-part-panel-${part.partCode}`}
            aria-selected={isActive}
            aria-label={`Part ${part.partCode}, ${answered} of ${part.questions.length} answered${flaggedCount ? `, ${flaggedCount} flagged` : ''}${partALabel}`}
            tabIndex={isActive ? 0 : -1}
            onClick={() => onChange(part.partCode)}
            disabled={isLocked}
            className={cn(
              'min-h-11 shrink-0 border-b-2 px-4 py-2 text-left text-sm font-bold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary',
              isActive
                ? 'border-primary text-primary'
                : 'border-transparent text-muted hover:text-navy',
              isLocked && 'cursor-not-allowed opacity-60 hover:text-muted',
            )}
          >
            <span>Part {part.partCode}</span>
            <span className="ml-2 text-xs font-semibold text-muted" aria-hidden="true">{answered}/{part.questions.length}</span>
            {flaggedCount ? <span className="ml-2 text-xs text-warning-strong" aria-hidden="true">{flaggedCount} flagged</span> : null}
            {isLocked ? <span className="ml-2 text-xs text-danger-strong" aria-hidden="true">locked</span> : null}
          </button>
        );
      })}
    </div>
  );
}

export function SectionTabs({
  sections,
  activeSection,
  onChange,
}: {
  sections: Array<{ code: ReadingSectionCode; label: string; questions: ReadingQuestionLearnerDto[] }>;
  activeSection: ReadingSectionCode | null;
  onChange: (section: ReadingSectionCode) => void;
}) {
  return (
    <div
      className="flex gap-2 overflow-x-auto rounded-2xl border border-border bg-surface p-2 shadow-sm"
      aria-label="Reading sections"
    >
      {sections.map((section) => {
        const isActive = activeSection === section.code;
        return (
          <button
            key={section.code}
            type="button"
            aria-pressed={isActive}
            aria-label={`Section ${section.label}, ${section.questions.length} question${section.questions.length === 1 ? '' : 's'}`}
            onClick={() => onChange(section.code)}
            className={cn(
              'min-h-10 shrink-0 rounded-xl px-4 py-2 text-sm font-bold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary',
              isActive
                ? 'bg-primary text-white shadow-sm'
                : 'bg-background-light text-muted hover:text-navy',
            )}
          >
            {section.label}
            <span className="ml-2 text-xs font-semibold opacity-80" aria-hidden="true">{section.questions.length}</span>
          </button>
        );
      })}
    </div>
  );
}

export function PartBody({
  paperId,
  part,
  assetKey,
  questionPaperAssets,
  pdfAnnotations,
  answers,
  flagged,
  activeQuestionId,
  eliminatedChoices,
  locked,
  onCreatePdfAnnotation,
  onDeletePdfAnnotation,
  onClearPdfAsset,
  onClearPdfPaper,
  onActiveQuestionChange,
  onToggleFlag,
  onToggleEliminated,
  onAnswerChange,
}: {
  paperId: string;
  part: ReadingLearnerStructureDto['parts'][number];
  assetKey: string;
  questionPaperAssets: NonNullable<ReadingLearnerStructureDto['paper']['questionPaperAssets']>;
  pdfAnnotations: ReadingPaperAnnotationDto[];
  answers: Record<string, string>;
  flagged: Set<string>;
  activeQuestionId: string | null;
  eliminatedChoices: Set<string>;
  locked: boolean;
  onCreatePdfAnnotation: (body: Parameters<typeof createReadingPaperAnnotation>[1]) => Promise<void>;
  onDeletePdfAnnotation: (annotationId: string) => Promise<void>;
  onClearPdfAsset: (assetId: string) => Promise<void>;
  onClearPdfPaper: () => Promise<void>;
  onActiveQuestionChange: (questionId: string) => void;
  onToggleFlag: (questionId: string) => void;
  onToggleEliminated: (questionId: string, optionValue: string) => void;
  onAnswerChange: (question: ReadingQuestionLearnerDto, value: unknown) => void;
}) {
  if (part.questions.length === 0) {
    return (
      <div
        className="rounded-[20px] border border-border bg-surface p-8 text-center shadow-sm"
        role="tabpanel"
        id={`reading-part-panel-${part.partCode}`}
        aria-labelledby={`reading-part-tab-${part.partCode}`}
      >
        <p className="text-sm text-muted">No questions available for this section in the selected drill.</p>
      </div>
    );
  }

  const activeQuestion = part.questions.find((question) => question.id === activeQuestionId) ?? part.questions[0];

  return (
    <div
      className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1.1fr)_minmax(360px,0.9fr)]"
      role="tabpanel"
      id={`reading-part-panel-${part.partCode}`}
      aria-labelledby={`reading-part-tab-${part.partCode}`}
    >
      <ReadingPdfViewer
        paperId={paperId}
        partCode={assetKey}
        assets={questionPaperAssets}
        annotations={pdfAnnotations}
        onCreateAnnotation={onCreatePdfAnnotation}
        onDeleteAnnotation={onDeletePdfAnnotation}
        onClearAsset={onClearPdfAsset}
        onClearPaper={onClearPdfPaper}
      />

      <section
        className="rounded-[20px] border border-border bg-surface p-5 shadow-sm"
        aria-label={`Questions for Part ${part.partCode}`}
      >
        <div className="mb-4 flex flex-col gap-3">
          <div className="flex items-center justify-between gap-3">
            <h2 className="text-sm font-black uppercase tracking-[0.18em] text-muted">Questions</h2>
            {locked ? <Badge variant="warning">Inputs locked</Badge> : null}
          </div>
          <QuestionNavigator
            partCode={part.partCode}
            questions={part.questions}
            answers={answers}
            flagged={flagged}
            activeQuestionId={activeQuestion?.id ?? null}
            onSelect={onActiveQuestionChange}
          />
        </div>

        {activeQuestion ? (
          <QuestionInput
            partCode={part.partCode}
            question={activeQuestion}
            texts={part.texts}
            valueJson={answers[activeQuestion.id] ?? ''}
            flagged={flagged.has(activeQuestion.id)}
            eliminatedChoices={eliminatedChoices}
            locked={locked}
            onToggleFlag={() => onToggleFlag(activeQuestion.id)}
            onToggleEliminated={(optionValue) => onToggleEliminated(activeQuestion.id, optionValue)}
            onChange={(value) => onAnswerChange(activeQuestion, value)}
          />
        ) : null}
      </section>
    </div>
  );
}

export function QuestionNavigator({
  partCode,
  questions,
  answers,
  flagged,
  activeQuestionId,
  onSelect,
}: {
  partCode: ReadingPartCode;
  questions: ReadingQuestionLearnerDto[];
  answers: Record<string, string>;
  flagged: Set<string>;
  activeQuestionId: string | null;
  onSelect: (questionId: string) => void;
}) {
  const handleKeyDown = useCallback(
    (event: React.KeyboardEvent<HTMLDivElement>) => {
      // Arrow-key navigation between question buttons. Left/Up = previous,
      // Right/Down = next, Home = first, End = last. Wraps at the ends.
      const key = event.key;
      if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(key))
        return;
      if (questions.length === 0) return;
      event.preventDefault();
      const currentIndex = Math.max(0, questions.findIndex((q) => q.id === activeQuestionId));
      let nextIndex = currentIndex;
      if (key === 'ArrowLeft' || key === 'ArrowUp') {
        nextIndex = (currentIndex - 1 + questions.length) % questions.length;
      } else if (key === 'ArrowRight' || key === 'ArrowDown') {
        nextIndex = (currentIndex + 1) % questions.length;
      } else if (key === 'Home') {
        nextIndex = 0;
      } else if (key === 'End') {
        nextIndex = questions.length - 1;
      }
      const next = questions[nextIndex];
      if (next) onSelect(next.id);
    },
    [questions, activeQuestionId, onSelect],
  );

  return (
    <div
      className="grid grid-cols-[repeat(auto-fill,minmax(42px,1fr))] gap-2"
      role="group"
      aria-label="Question navigator (use arrow keys to move between questions)"
      onKeyDown={handleKeyDown}
    >
      {questions.map((question) => {
        const answered = isAnsweredJson(answers[question.id]);
        const isActive = question.id === activeQuestionId;
        const isFlagged = flagged.has(question.id);
        const publicNumber = readingPublicDisplayNumber(partCode, question.displayOrder);
        return (
          <button
            key={question.id}
            type="button"
            onClick={() => onSelect(question.id)}
            tabIndex={isActive ? 0 : -1}
            aria-current={isActive ? 'true' : undefined}
            aria-label={`Question ${publicNumber}${answered ? ', answered' : ', unanswered'}${isFlagged ? ', flagged' : ''}`}
            className={cn(
              'relative min-h-11 rounded-lg border text-sm font-bold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary',
              isActive ? 'border-primary bg-primary text-white dark:bg-primary-700' : 'border-border bg-background-light text-navy hover:border-primary/40',
              answered && !isActive && 'border-success/30 bg-success/10 text-success-strong',
              isFlagged && !isActive && 'border-warning/30 bg-warning/10 text-warning-strong',
            )}
          >
            {publicNumber}
            {isFlagged ? <Flag className="absolute right-1 top-1 h-3 w-3 fill-current" aria-hidden="true" /> : null}
          </button>
        );
      })}
    </div>
  );
}
