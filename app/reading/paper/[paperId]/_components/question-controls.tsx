import { useMemo } from 'react';
import { Flag, Strikethrough } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { cn } from '@/lib/utils';
import type { ReadingLearnerStructureDto, ReadingPartCode, ReadingQuestionLearnerDto } from '@/lib/reading-authoring-api';
import { readingPublicDisplayNumber } from '@/lib/reading-display-number';
import { toOptionList, toMatchingOptions, parseAnswer } from '../reading-paper-helpers';

export function QuestionInput({
  partCode,
  question,
  texts,
  valueJson,
  flagged,
  eliminatedChoices,
  locked,
  onToggleFlag,
  onToggleEliminated,
  onChange,
}: {
  partCode: ReadingPartCode;
  question: ReadingQuestionLearnerDto;
  texts: ReadingLearnerStructureDto['parts'][number]['texts'];
  valueJson: string;
  flagged: boolean;
  eliminatedChoices: Set<string>;
  locked: boolean;
  onToggleFlag: () => void;
  onToggleEliminated: (optionValue: string) => void;
  onChange: (value: unknown) => void;
}) {
  const current = useMemo(() => parseAnswer(valueJson), [valueJson]);

  return (
    <div className="space-y-5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-xs font-black uppercase tracking-[0.16em] text-muted">Question {readingPublicDisplayNumber(partCode, question.displayOrder)}</p>
          <h3 className="mt-2 text-base font-semibold leading-7 text-navy selection:bg-warning/30" data-reading-highlight-scope="stem">{question.stem}</h3>
        </div>
        <Button variant="ghost" size="sm" onClick={onToggleFlag} aria-pressed={flagged}>
          <Flag className={cn('h-4 w-4', flagged && 'fill-current text-warning')} />
          {flagged ? 'Flagged' : 'Flag'}
        </Button>
      </div>

      {question.questionType === 'MultipleChoice3'
        || question.questionType === 'MultipleChoice4'
        || question.questionType === 'MultipleChoiceFlexible' ? (
        <McqControl
          question={question}
          current={current}
          eliminatedChoices={eliminatedChoices}
          locked={locked}
          onToggleEliminated={onToggleEliminated}
          onChange={onChange}
        />
      ) : question.questionType === 'MatchingTextReference' ? (
        <MatchingControl question={question} texts={texts} current={current} locked={locked} onChange={onChange} />
      ) : question.questionType === 'ShortAnswerLabeled' ? (
        <LabeledTextAnswerControl question={question} current={current} locked={locked} onChange={onChange} />
      ) : (
        <TextAnswerControl current={current} locked={locked} onChange={onChange} />
      )}
    </div>
  );
}

export function McqControl({
  question,
  current,
  eliminatedChoices,
  locked,
  onToggleEliminated,
  onChange,
}: {
  question: ReadingQuestionLearnerDto;
  current: unknown;
  eliminatedChoices: Set<string>;
  locked: boolean;
  onToggleEliminated: (optionValue: string) => void;
  onChange: (value: unknown) => void;
}) {
  const options = toOptionList(question.options);
  // R08 — accessible status mirroring BCQuestionRenderer: announce how many
  // options the learner has ruled out so screen-reader users get parity with
  // the visual line-through.
  const statusId = `mcq-${question.id}-status`;
  const struckCount = options.reduce((count, option, index) => {
    const letter = option.value || String.fromCharCode(65 + index);
    return count + (eliminatedChoices.has(`${question.id}:${letter}`) ? 1 : 0);
  }, 0);
  const struckSummary = struckCount === 0
    ? 'No options are ruled out.'
    : `${struckCount} option${struckCount === 1 ? '' : 's'} ruled out.`;

  return (
    <div className="space-y-2">
      {options.map((option, index) => {
        const letter = option.value || String.fromCharCode(65 + index);
        const eliminated = eliminatedChoices.has(`${question.id}:${letter}`);
        return (
          <div key={`${letter}-${option.label}`} className="flex items-stretch gap-2">
            <label
              data-reading-answer-choice="true"
              aria-describedby={eliminated ? statusId : undefined}
              onContextMenu={(event) => {
                event.preventDefault();
                if (!locked) onToggleEliminated(letter);
              }}
              className={cn(
                'flex min-h-11 flex-1 cursor-pointer items-start gap-3 rounded-lg border border-border bg-background-light p-3 text-sm transition-colors',
                current === letter && 'border-primary bg-primary/5',
                eliminated && 'text-muted line-through decoration-2',
                locked && 'cursor-not-allowed opacity-70',
              )}
            >
              <input
                type="radio"
                name={question.id}
                className="mt-1"
                disabled={locked}
                checked={current === letter}
                onChange={() => onChange(letter)}
              />
              <span className="font-mono font-bold text-navy">{letter}.</span>
              <span className={cn('leading-6 text-navy', eliminated && 'text-muted')}>{option.label}</span>
            </label>
            {/* Visible rule-out toggle (parity with Listening's BCQuestionRenderer).
                Sits outside the label so it never toggles the radio; right-click
                on the option still works for mouse users. */}
            <Button
              type="button"
              variant={eliminated ? 'secondary' : 'outline'}
              size="sm"
              aria-pressed={eliminated}
              aria-label={`${eliminated ? 'Restore' : 'Rule out'} option ${letter}`}
              onClick={() => { if (!locked) onToggleEliminated(letter); }}
              disabled={locked}
              className="self-stretch px-3"
            >
              <Strikethrough className="h-4 w-4" aria-hidden="true" />
            </Button>
          </div>
        );
      })}
      <p id={statusId} className="sr-only" aria-live="polite">
        {struckSummary}
      </p>
    </div>
  );
}

export function MatchingControl({
  question,
  texts,
  current,
  locked,
  onChange,
}: {
  question: ReadingQuestionLearnerDto;
  texts: ReadingLearnerStructureDto['parts'][number]['texts'];
  current: unknown;
  locked: boolean;
  onChange: (value: unknown) => void;
}) {
  const options = toMatchingOptions(question.options, texts);
  const multi = question.points > 1 || Array.isArray(current);
  const selected = Array.isArray(current)
    ? current.map(String)
    : typeof current === 'string' && current ? [current] : [];

  const toggle = (value: string) => {
    if (!multi) {
      onChange(value);
      return;
    }
    const next = selected.includes(value)
      ? selected.filter((item) => item !== value)
      : [...selected, value];
    onChange(next);
  };

  return (
    <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
      {options.map((option) => {
        const isSelected = selected.includes(option.value);
        return (
          <button
            key={option.value}
            type="button"
            disabled={locked}
            onClick={() => toggle(option.value)}
            className={cn(
              'min-h-12 rounded-lg border border-border bg-background-light px-3 py-2 text-left transition-colors disabled:cursor-not-allowed disabled:opacity-70',
              isSelected && 'border-primary bg-primary/5',
            )}
          >
            <span className="block text-sm font-bold text-navy">Text {option.value}</span>
            {option.label ? <span className="block text-xs text-muted">{option.label}</span> : null}
          </button>
        );
      })}
    </div>
  );
}

export function TextAnswerControl({
  current,
  locked,
  onChange,
}: {
  current: unknown;
  locked: boolean;
  onChange: (value: unknown) => void;
}) {
  return (
    <input
      className="min-h-11 w-full rounded-lg border border-border bg-background-light px-3 py-2 text-sm text-navy outline-none transition focus:border-primary focus:ring-2 focus:ring-primary/20 disabled:cursor-not-allowed disabled:opacity-70"
      placeholder="Type your answer"
      disabled={locked}
      value={typeof current === 'string' ? current : ''}
      onChange={(event) => onChange(event.target.value)}
    />
  );
}

export function LabeledTextAnswerControl({
  question,
  current,
  locked,
  onChange,
}: {
  question: ReadingQuestionLearnerDto;
  current: unknown;
  locked: boolean;
  onChange: (value: unknown) => void;
}) {
  const options = toOptionList(question.options);
  const answerMap = current && typeof current === 'object' && !Array.isArray(current)
    ? current as Record<string, unknown>
    : {};
  const fields = options.length > 0
    ? options
    : [{ value: 'answer1', label: 'Answer 1' }];

  const update = (key: string, value: string) => {
    onChange({
      ...answerMap,
      [key]: value,
    });
  };

  return (
    <div className="space-y-3">
      {fields.map((field, index) => {
        const key = field.value || `answer${index + 1}`;
        const value = typeof answerMap[key] === 'string' ? String(answerMap[key]) : '';
        return (
          <Input
            key={key}
            label={field.label || `Answer ${index + 1}`}
            value={value}
            disabled={locked}
            onChange={(event) => update(key, event.target.value)}
            placeholder={`Type ${field.label || `answer ${index + 1}`}...`}
          />
        );
      })}
    </div>
  );
}
