'use client';

/**
 * Tutor scoring form for the 9 OET criteria.
 *
 * - Linguistic (0-6): Intelligibility, Fluency, Appropriateness, GrammarExpression
 * - Clinical (0-3): RelationshipBuilding, PatientPerspective, Structure,
 *   InformationGathering, InformationGiving
 *
 * In "submit" mode the form asserts that all 9 scores are set and shows
 * validation errors inline. In "draft" mode missing scores are allowed
 * (null) so tutors can save incomplete work.
 */

import { useMemo, useState, type ChangeEvent, type KeyboardEvent } from 'react';
import { X } from 'lucide-react';

import { Textarea } from '@/components/ui/form-controls';
import {
  CLINICAL_CRITERIA,
  CRITERION_LABEL,
  CRITERION_LEVEL_DESCRIPTORS,
  CRITERION_MAX,
  LINGUISTIC_CRITERIA,
  type SpeakingCriterionCode,
} from '@/lib/api/speaking-assessments';
import { cn } from '@/lib/utils';

export interface CriterionRubricFormValue {
  intelligibility: number | null;
  fluency: number | null;
  appropriateness: number | null;
  grammarExpression: number | null;
  relationshipBuilding: number | null;
  patientPerspective: number | null;
  structure: number | null;
  informationGathering: number | null;
  informationGiving: number | null;
  overallFeedbackMarkdown: string;
  strengths: string[];
  improvements: string[];
  recommendedDrills: string[];
  recommendedRulebookEntries: string[];
}

export const EMPTY_RUBRIC_VALUE: CriterionRubricFormValue = {
  intelligibility: null,
  fluency: null,
  appropriateness: null,
  grammarExpression: null,
  relationshipBuilding: null,
  patientPerspective: null,
  structure: null,
  informationGathering: null,
  informationGiving: null,
  overallFeedbackMarkdown: '',
  strengths: [],
  improvements: [],
  recommendedDrills: [],
  recommendedRulebookEntries: [],
};

export interface CriterionRubricFormProps {
  value: CriterionRubricFormValue;
  onChange: (next: CriterionRubricFormValue) => void;
  mode: 'draft' | 'submit';
  /** Optional externally-controlled validation errors keyed by criterion code. */
  externalErrors?: Partial<Record<keyof CriterionRubricFormValue, string>>;
}

function validate(value: CriterionRubricFormValue, mode: 'draft' | 'submit') {
  const errors: Partial<Record<keyof CriterionRubricFormValue, string>> = {};
  if (mode !== 'submit') return errors;

  for (const code of [...LINGUISTIC_CRITERIA, ...CLINICAL_CRITERIA]) {
    const v = value[code as keyof CriterionRubricFormValue] as number | null;
    if (v === null || v === undefined || Number.isNaN(v)) {
      errors[code as keyof CriterionRubricFormValue] = `Please select a ${CRITERION_LABEL[code]} score before submitting.`;
    }
  }
  return errors;
}

function CriterionSlider({
  code,
  value,
  onChange,
  error,
}: {
  code: SpeakingCriterionCode;
  value: number | null;
  onChange: (v: number) => void;
  error?: string;
}) {
  const max = CRITERION_MAX[code];
  const id = `rubric-${code}`;
  const descriptors = CRITERION_LEVEL_DESCRIPTORS[code];
  const descriptor = value !== null ? descriptors[value] : undefined;

  return (
    <div className="rounded-xl border border-border bg-background-light/60 p-3" data-testid={`rubric-slider-${code}`}>
      <div className="flex items-baseline justify-between gap-3">
        <label htmlFor={id} className="text-sm font-semibold text-navy">
          {CRITERION_LABEL[code]}
        </label>
        <span className="text-xs text-muted">0 – {max}</span>
      </div>

      <div className="mt-2 flex items-center gap-3">
        <input
          id={id}
          type="range"
          min={0}
          max={max}
          step={1}
          value={value ?? 0}
          onChange={(e) => onChange(Number(e.target.value))}
          className={cn(
            'h-2 flex-1 cursor-pointer appearance-none rounded-full bg-border accent-primary',
            error && 'ring-1 ring-red-400',
          )}
          aria-describedby={`${id}-descriptor`}
          aria-invalid={!!error}
        />
        <output
          htmlFor={id}
          className="inline-flex h-8 w-12 items-center justify-center rounded-full bg-primary/10 text-sm font-bold text-primary"
        >
          {value ?? '—'}
        </output>
      </div>

      <p id={`${id}-descriptor`} className="mt-2 text-xs leading-relaxed text-muted">
        {descriptor ?? (
          <span className="italic">Move the slider to score this criterion (0 = lowest, {max} = highest).</span>
        )}
      </p>

      {/* Numeric quick-tap buttons (touch-friendly) */}
      <div className="mt-2 flex flex-wrap gap-1">
        {Array.from({ length: max + 1 }).map((_, i) => (
          <button
            key={i}
            type="button"
            onClick={() => onChange(i)}
            className={cn(
              'inline-flex h-7 min-w-7 items-center justify-center rounded-md border px-2 text-xs font-bold transition-colors',
              value === i
                ? 'border-primary bg-primary text-white'
                : 'border-border bg-surface text-navy hover:border-primary/40 hover:text-primary',
            )}
            aria-pressed={value === i}
            aria-label={`Set ${CRITERION_LABEL[code]} to ${i}`}
          >
            {i}
          </button>
        ))}
      </div>

      {error && (
        <p className="mt-2 text-xs font-semibold text-red-600" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

function ChipInput({
  label,
  values,
  onChange,
  placeholder,
}: {
  label: string;
  values: string[];
  onChange: (next: string[]) => void;
  placeholder?: string;
}) {
  const [draft, setDraft] = useState('');
  const inputId = `chip-${label.toLowerCase().replace(/\s+/g, '-')}`;

  const add = () => {
    const trimmed = draft.trim();
    if (!trimmed) return;
    if (values.includes(trimmed)) {
      setDraft('');
      return;
    }
    onChange([...values, trimmed]);
    setDraft('');
  };

  const handleKey = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter' || e.key === ',') {
      e.preventDefault();
      add();
    } else if (e.key === 'Backspace' && !draft && values.length > 0) {
      onChange(values.slice(0, -1));
    }
  };

  const remove = (index: number) => onChange(values.filter((_, i) => i !== index));

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={inputId} className="text-sm font-semibold text-navy">
        {label}
      </label>
      <div className="flex flex-wrap items-center gap-1.5 rounded-2xl border border-border bg-background-light px-3 py-2.5 focus-within:border-primary focus-within:ring-4 focus-within:ring-primary/15">
        {values.map((chip, i) => (
          <span
            key={`${chip}-${i}`}
            className="inline-flex items-center gap-1 rounded-full bg-primary/10 px-2.5 py-0.5 text-xs font-bold text-primary"
          >
            {chip}
            <button
              type="button"
              onClick={() => remove(i)}
              aria-label={`Remove ${chip}`}
              className="rounded-full p-0.5 hover:bg-primary/20"
            >
              <X className="h-3 w-3" aria-hidden />
            </button>
          </span>
        ))}
        <input
          id={inputId}
          value={draft}
          onChange={(e: ChangeEvent<HTMLInputElement>) => setDraft(e.target.value)}
          onKeyDown={handleKey}
          onBlur={add}
          placeholder={placeholder ?? 'Press Enter to add'}
          className="flex-1 border-0 bg-transparent p-0 text-sm text-navy outline-none placeholder:text-muted"
        />
      </div>
    </div>
  );
}

export function CriterionRubricForm({
  value,
  onChange,
  mode,
  externalErrors,
}: CriterionRubricFormProps) {
  const errors = useMemo(() => {
    const internal = validate(value, mode);
    return { ...internal, ...(externalErrors ?? {}) };
  }, [value, mode, externalErrors]);

  const update = <K extends keyof CriterionRubricFormValue>(
    key: K,
    next: CriterionRubricFormValue[K],
  ) => onChange({ ...value, [key]: next });

  const hasAnyError = Object.keys(errors).length > 0;

  return (
    <form className="flex flex-col gap-4" data-testid="criterion-rubric-form">
      <section aria-label="Linguistic criteria">
        <h4 className="mb-2 text-xs font-bold uppercase tracking-wide text-muted">
          Linguistic Criteria (0–6)
        </h4>
        <div className="flex flex-col gap-3">
          {LINGUISTIC_CRITERIA.map((code) => (
            <CriterionSlider
              key={code}
              code={code}
              value={value[code as keyof CriterionRubricFormValue] as number | null}
              onChange={(v) => update(code as keyof CriterionRubricFormValue, v as never)}
              error={errors[code as keyof CriterionRubricFormValue]}
            />
          ))}
        </div>
      </section>

      <section aria-label="Clinical communication criteria">
        <h4 className="mb-2 text-xs font-bold uppercase tracking-wide text-muted">
          Clinical Communication (0–3)
        </h4>
        <div className="flex flex-col gap-3">
          {CLINICAL_CRITERIA.map((code) => (
            <CriterionSlider
              key={code}
              code={code}
              value={value[code as keyof CriterionRubricFormValue] as number | null}
              onChange={(v) => update(code as keyof CriterionRubricFormValue, v as never)}
              error={errors[code as keyof CriterionRubricFormValue]}
            />
          ))}
        </div>
      </section>

      <Textarea
        label="Overall feedback (Markdown supported)"
        hint="Two to four sentences summarising your tutor judgement. Visible to the learner."
        value={value.overallFeedbackMarkdown}
        onChange={(e) => update('overallFeedbackMarkdown', e.target.value)}
        rows={5}
      />

      <ChipInput
        label="Strengths"
        values={value.strengths}
        onChange={(next) => update('strengths', next)}
        placeholder="e.g. Clear chunking when explaining medication; press Enter"
      />

      <ChipInput
        label="Areas to improve"
        values={value.improvements}
        onChange={(next) => update('improvements', next)}
        placeholder="e.g. Use more open questions"
      />

      <ChipInput
        label="Recommended drills"
        values={value.recommendedDrills}
        onChange={(next) => update('recommendedDrills', next)}
        placeholder="Drill slug or short title"
      />

      <ChipInput
        label="Recommended rulebook entries"
        values={value.recommendedRulebookEntries}
        onChange={(next) => update('recommendedRulebookEntries', next)}
        placeholder="Rulebook slug or short title"
      />

      {mode === 'submit' && hasAnyError && (
        <div
          role="alert"
          data-testid="rubric-validation-summary"
          className="rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-800 dark:border-red-800/40 dark:bg-red-950/30 dark:text-red-200"
        >
          <p className="font-bold">Please address the following before submitting:</p>
          <ul className="ml-4 mt-1 list-disc space-y-0.5 text-xs">
            {Object.entries(errors).map(([key, msg]) => (
              <li key={key}>{msg}</li>
            ))}
          </ul>
        </div>
      )}
    </form>
  );
}

export default CriterionRubricForm;
