'use client';

/**
 * Candidate-card editor for the OET Speaking role-play builder.
 *
 * Renders the form an admin uses to author the **public** side of a
 * two-card role-play (the side the learner sees during the 3-minute
 * preparation). The hidden `InterlocutorScript` is authored separately
 * via `InterlocutorScriptEditor` — this component must NEVER show or
 * write interlocutor fields.
 *
 * Used by:
 *   - app/admin/content/speaking/role-play-cards/new/page.tsx (step 1)
 *   - app/admin/content/speaking/role-play-cards/[id]/page.tsx (edit)
 */

import { useMemo, useState, type FormEvent } from 'react';
import { Save } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Checkbox, Input, Select, Textarea } from '@/components/ui/form-controls';
import { TaskListEditor, normaliseTasks } from '@/components/domain/speaking/TaskListEditor';
import {
  DEFAULT_DISCLAIMER,
  PROFESSION_OPTIONS,
  SPEAKING_CRITERIA_OPTIONS,
  adminPreviewSpeakingCardClassification,
  type CreateRolePlayCardInput,
  type RolePlayCardDetail,
  type SpeakingCardTypeDetail,
} from '@/lib/api/speaking-role-play-cards';
import {
  SPEAKING_BEHAVIOURAL_TAGS,
  SPEAKING_PRIMARY_CATEGORIES,
} from '@/lib/speaking/category-taxonomy';

export type RolePlayCardEditorMode = 'create' | 'edit';

export interface RolePlayCardEditorValue extends CreateRolePlayCardInput {
  // Same shape as the API input — kept as a named alias to make the
  // intent explicit in consuming components.
}

export interface RolePlayCardEditorProps {
  mode: RolePlayCardEditorMode;
  initial?: Partial<RolePlayCardDetail>;
  submitting?: boolean;
  /** Resolves with the saved card so the parent can route / refresh. */
  onSubmit: (value: RolePlayCardEditorValue) => Promise<void> | void;
  /** Optional secondary action shown alongside the submit button. */
  secondaryAction?: React.ReactNode;
  /** Hidden card types for the classification select (2026-06-11 rebuild). */
  cardTypes?: SpeakingCardTypeDetail[];
}

function nullableString(value: string | undefined | null): string | undefined {
  if (value == null) return undefined;
  const trimmed = value.trim();
  return trimmed.length === 0 ? undefined : trimmed;
}

export function RolePlayCardEditor({
  mode,
  initial,
  submitting,
  onSubmit,
  secondaryAction,
  cardTypes,
}: RolePlayCardEditorProps) {
  const [profession, setProfession] = useState(initial?.professionId ?? 'nursing');
  const [cardTypeId, setCardTypeId] = useState<string>(initial?.cardTypeId ?? '');
  const [displayCardNumber, setDisplayCardNumber] = useState<string>(
    initial?.displayCardNumber != null ? String(initial.displayCardNumber) : '',
  );
  const [scenarioTitle, setScenarioTitle] = useState(initial?.scenarioTitle ?? '');
  const [setting, setSetting] = useState(initial?.setting ?? '');
  const [candidateRole, setCandidateRole] = useState(initial?.candidateRole ?? '');
  const [interlocutorRole, setInterlocutorRole] = useState(initial?.interlocutorRole ?? 'Patient');
  const [patientName, setPatientName] = useState(initial?.patientName ?? '');
  const [patientAge, setPatientAge] = useState(initial?.patientAge ?? '');
  const [background, setBackground] = useState(initial?.background ?? '');
  const [tasks, setTasks] = useState<string[]>(() => {
    const seeded = initial?.tasks ?? [];
    return seeded.length > 0 ? [...seeded] : ['', '', ''];
  });
  const [allowedNotes, setAllowedNotes] = useState<boolean>(initial?.allowedNotes ?? true);
  const [prepTimeSeconds, setPrepTimeSeconds] = useState<number>(initial?.prepTimeSeconds ?? 180);
  const [rolePlayTimeSeconds, setRolePlayTimeSeconds] = useState<number>(initial?.rolePlayTimeSeconds ?? 300);
  const [patientEmotion, setPatientEmotion] = useState(initial?.patientEmotion ?? 'worried');
  const [communicationGoal, setCommunicationGoal] = useState(initial?.communicationGoal ?? 'Reassure');
  const [clinicalTopic, setClinicalTopic] = useState(initial?.clinicalTopic ?? '');
  // FINAL 2026-09-06 — Difficulty is removed from the Speaking UI entirely.
  // The candidate-visible primary category + behavioural tags drive the
  // catalogue filter instead.
  const [primaryCategory, setPrimaryCategory] = useState<string>(initial?.primaryCategory ?? 'Other Cards');
  const [secondaryTags, setSecondaryTags] = useState<string[]>(initial?.secondaryTags ?? []);
  const [categoryNeedsReview, setCategoryNeedsReview] = useState<boolean>(initial?.categoryNeedsReview ?? true);
  const [suggesting, setSuggesting] = useState(false);
  const [suggestError, setSuggestError] = useState<string | null>(null);
  const [criteriaFocus, setCriteriaFocus] = useState<string[]>(initial?.criteriaFocus ?? []);
  const [disclaimer, setDisclaimer] = useState(initial?.disclaimer ?? DEFAULT_DISCLAIMER);
  const [isLiveTutorEligible, setIsLiveTutorEligible] = useState<boolean>(initial?.isLiveTutorEligible ?? false);

  const cleanedTasks = useMemo(() => normaliseTasks(tasks), [tasks]);
  const taskCount = cleanedTasks.length;

  const validationHints = useMemo(() => {
    const hints: string[] = [];
    if (taskCount < 3) hints.push(`At least 3 tasks required for publish (currently ${taskCount}).`);
    if (criteriaFocus.length === 0) hints.push('Pick at least one criterion this card stresses.');
    if (!scenarioTitle.trim()) hints.push('Scenario title is required.');
    if (!setting.trim()) hints.push('Setting is required.');
    if (!candidateRole.trim()) hints.push('Candidate role is required.');
    if (!background.trim()) hints.push('Background is required.');
    if (!clinicalTopic.trim()) hints.push('Clinical topic helps drill recommendation. Please fill it in.');
    if (prepTimeSeconds <= 0 || prepTimeSeconds > 600) hints.push('Prep time must be between 1 and 600 seconds.');
    if (rolePlayTimeSeconds <= 0 || rolePlayTimeSeconds > 1800) {
      hints.push('Role-play time must be between 1 and 1800 seconds.');
    }
    return hints;
  }, [taskCount, criteriaFocus.length, scenarioTitle, setting, candidateRole, background, clinicalTopic, prepTimeSeconds, rolePlayTimeSeconds]);

  const toggleCriterion = (code: string) => {
    setCriteriaFocus(prev => prev.includes(code) ? prev.filter(c => c !== code) : [...prev, code]);
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    const payload: CreateRolePlayCardInput = {
      professionId: profession,
      scenarioTitle: scenarioTitle.trim(),
      setting: setting.trim(),
      candidateRole: candidateRole.trim(),
      interlocutorRole: interlocutorRole.trim() || 'Patient',
      patientName: nullableString(patientName),
      patientAge: nullableString(patientAge),
      background: background.trim(),
      tasks: cleanedTasks,
      allowedNotes,
      prepTimeSeconds,
      rolePlayTimeSeconds,
      patientEmotion: patientEmotion.trim() || 'neutral',
      communicationGoal: communicationGoal.trim() || 'Inform',
      clinicalTopic: clinicalTopic.trim() || 'general',
      primaryCategory,
      secondaryTags,
      categoryNeedsReview,
      criteriaFocus,
      disclaimer: disclaimer.trim() || DEFAULT_DISCLAIMER,
      isLiveTutorEligible,
      // Hidden card type — "" clears (sent as empty string so the backend
      // distinguishes "clear" from "leave unchanged" on PATCH).
      cardTypeId: cardTypeId,
      displayCardNumber: displayCardNumber.trim() === '' ? null : Number(displayCardNumber),
    };
    await onSubmit(payload);
  };

  return (
    <form onSubmit={(e) => void handleSubmit(e)} className="space-y-6">
      {/* Section 1: classification */}
      <section className="space-y-4">
        <h3 className="text-sm font-bold uppercase tracking-[0.16em] text-muted">Card classification</h3>
        <div className="grid gap-4 md:grid-cols-2">
          <Select
            label="Profession"
            value={profession}
            onChange={(e) => setProfession(e.target.value)}
            options={PROFESSION_OPTIONS}
            required
          />
          <div className="flex items-end gap-2">
            <div className="flex-1">
              <Select
                label="Primary category (visible to learners)"
                value={primaryCategory}
                onChange={(e) => {
                  setPrimaryCategory(e.target.value);
                  setCategoryNeedsReview(false);
                }}
                options={SPEAKING_PRIMARY_CATEGORIES.map((category) => ({ value: category, label: category }))}
                required
              />
            </div>
            <Button
              type="button"
              variant="secondary"
              disabled={suggesting}
              onClick={() => {
                setSuggesting(true);
                setSuggestError(null);
                // Server-authoritative preview (2026-09-09) — never classify
                // client-side; the backend §8B classifier is the only source
                // of truth for this suggestion.
                adminPreviewSpeakingCardClassification({
                  scenarioTitle,
                  setting,
                  background,
                  tasks: cleanedTasks,
                  clinicalTopic,
                  patientEmotion,
                })
                  .then((suggestion) => {
                    setPrimaryCategory(suggestion.primaryCategory);
                    setSecondaryTags(suggestion.secondaryTags);
                    setCategoryNeedsReview(suggestion.categoryNeedsReview);
                  })
                  .catch((e) => {
                    setSuggestError(e instanceof Error ? e.message : 'Could not get a suggestion.');
                  })
                  .finally(() => setSuggesting(false));
              }}
            >
              {suggesting ? 'Suggesting…' : 'Suggest'}
            </Button>
          </div>
        </div>
        {suggestError ? <p className="text-xs text-danger">{suggestError}</p> : null}
        <div className="flex flex-wrap items-center gap-4">
          <span className="text-sm font-medium text-navy">Behavioural tags:</span>
          {SPEAKING_BEHAVIOURAL_TAGS.map((tag) => (
            <Checkbox
              key={tag}
              label={tag}
              checked={secondaryTags.includes(tag)}
              onChange={(e) =>
                setSecondaryTags((prev) =>
                  e.target.checked ? [...prev, tag] : prev.filter((t) => t !== tag),
                )
              }
            />
          ))}
          <Checkbox
            label="Needs category review"
            checked={categoryNeedsReview}
            onChange={(e) => setCategoryNeedsReview(e.target.checked)}
          />
        </div>
        <div className="grid gap-4 md:grid-cols-2">
          <Input
            label="Scenario title"
            value={scenarioTitle}
            onChange={(e) => setScenarioTitle(e.target.value)}
            placeholder='e.g. "Discharge advice after appendectomy"'
            maxLength={200}
            required
          />
          <Input
            label="Clinical topic"
            value={clinicalTopic}
            onChange={(e) => setClinicalTopic(e.target.value)}
            placeholder='e.g. "Post-operative pain management"'
            maxLength={96}
            required
          />
        </div>
        {/* Hidden card type + printed card number (2026-06-11 rebuild). Never
            shown to students — aids human + AI marking only. */}
        <div className="grid gap-4 md:grid-cols-2">
          <Select
            label="Card type (hidden from students)"
            value={cardTypeId}
            onChange={(e) => setCardTypeId(e.target.value)}
            options={[
              { value: '', label: '— None —' },
              ...(cardTypes ?? [])
                .filter((t) => t.isActive || t.id === cardTypeId)
                .map((t) => ({ value: t.id, label: t.isActive ? t.name : `${t.name} (inactive)` })),
            ]}
          />
          <Input
            label="Printed card number (optional)"
            type="number"
            min={1}
            value={displayCardNumber}
            onChange={(e) => setDisplayCardNumber(e.target.value)}
            placeholder='e.g. "2" → "CANDIDATE CARD NO. 2"'
          />
        </div>
      </section>

      {/* Section 2: candidate-facing context */}
      <section className="space-y-4">
        <h3 className="text-sm font-bold uppercase tracking-[0.16em] text-muted">Candidate card content</h3>
        <div className="grid gap-4 md:grid-cols-2">
          <Input
            label="Setting"
            value={setting}
            onChange={(e) => setSetting(e.target.value)}
            placeholder='e.g. "Surgical ward"'
            maxLength={160}
            required
          />
          <Input
            label="Candidate role"
            value={candidateRole}
            onChange={(e) => setCandidateRole(e.target.value)}
            placeholder='e.g. "Nurse"'
            maxLength={64}
            required
          />
        </div>
        <div className="grid gap-4 md:grid-cols-3">
          <Input
            label="Interlocutor role"
            value={interlocutorRole}
            onChange={(e) => setInterlocutorRole(e.target.value)}
            placeholder='e.g. "Patient", "Parent"'
            maxLength={64}
          />
          <Input
            label="Patient name (optional)"
            value={patientName ?? ''}
            onChange={(e) => setPatientName(e.target.value)}
            placeholder='e.g. "Mrs. Lee"'
            maxLength={80}
          />
          <Input
            label="Patient age (optional)"
            value={patientAge ?? ''}
            onChange={(e) => setPatientAge(e.target.value)}
            placeholder='e.g. "48", "early 30s"'
            maxLength={32}
          />
        </div>

        <Textarea
          label="Background (case detail)"
          value={background}
          onChange={(e) => setBackground(e.target.value)}
          placeholder="Multi-line case background, written exactly as the candidate will read it…"
          rows={5}
          maxLength={4000}
          required
          hint={`${background.length}/4000 characters`}
        />
      </section>

      {/* Section 3: tasks */}
      <section className="space-y-4">
        <div>
          <h3 className="text-sm font-bold uppercase tracking-[0.16em] text-muted">Task bullets</h3>
          <p className="mt-1 text-xs text-muted">Every bullet printed on the candidate card, in printed order. At least 3 are required to publish.</p>
        </div>
        <TaskListEditor
          tasks={tasks}
          onChange={setTasks}
          addLabel="Add task"
          itemNoun="Task"
          placeholder='e.g. "Explain the discharge medication regimen."'
          emptyHint="No task bullets yet."
          minRecommended={3}
        />
      </section>

      {/* Section 4: scoring focus + persona */}
      <section className="space-y-4">
        <h3 className="text-sm font-bold uppercase tracking-[0.16em] text-muted">Persona & scoring focus</h3>
        <div className="grid gap-4 md:grid-cols-2">
          <Input
            label="Patient emotion"
            value={patientEmotion}
            onChange={(e) => setPatientEmotion(e.target.value)}
            placeholder='e.g. "worried", "anxious", "angry"'
            maxLength={64}
          />
          <Input
            label="Communication goal"
            value={communicationGoal}
            onChange={(e) => setCommunicationGoal(e.target.value)}
            placeholder='e.g. "Reassure", "Explain", "Persuade"'
            maxLength={64}
          />
        </div>

        <fieldset>
          <legend className="mb-2 text-sm font-semibold tracking-tight text-navy">Criteria this card stresses</legend>
          <p className="mb-3 text-xs text-muted">Pick the OET criteria this scenario is designed to expose. Surfaced to the AI scorer and the drill recommender.</p>
          <div className="grid gap-2 sm:grid-cols-2">
            {SPEAKING_CRITERIA_OPTIONS.map((opt) => {
              const checked = criteriaFocus.includes(opt.value);
              return (
                <button
                  key={opt.value}
                  type="button"
                  aria-pressed={checked}
                  onClick={() => toggleCriterion(opt.value)}
                  className={`flex items-center justify-between gap-2 rounded-2xl border px-3 py-2 text-left text-sm transition ${
                    checked
                      ? 'border-primary bg-primary/10 text-primary'
                      : 'border-border bg-background-light text-navy hover:border-primary/40'
                  }`}
                >
                  <span>{opt.label}</span>
                  <span className="text-[10px] font-bold uppercase tracking-widest text-muted">{opt.band}</span>
                </button>
              );
            })}
          </div>
        </fieldset>
      </section>

      {/* Section 5: timing + delivery */}
      <section className="space-y-4">
        <h3 className="text-sm font-bold uppercase tracking-[0.16em] text-muted">Timing & delivery</h3>
        <div className="grid gap-4 md:grid-cols-2">
          <Input
            label="Prep time (seconds)"
            type="number"
            value={String(prepTimeSeconds)}
            onChange={(e) => setPrepTimeSeconds(Number(e.target.value || 0))}
            min={30}
            max={600}
            step={15}
            hint="OET default is 180 seconds (3 minutes)."
          />
          <Input
            label="Role-play time (seconds)"
            type="number"
            value={String(rolePlayTimeSeconds)}
            onChange={(e) => setRolePlayTimeSeconds(Number(e.target.value || 0))}
            min={60}
            max={1800}
            step={30}
            hint="OET default is 300 seconds (5 minutes)."
          />
        </div>
        <Checkbox
          label="Allow candidate to take notes during preparation"
          checked={allowedNotes}
          onChange={(e) => setAllowedNotes(e.target.checked)}
        />
        <Checkbox
          label="Eligible for live tutor (premium booking flow)"
          checked={isLiveTutorEligible}
          onChange={(e) => setIsLiveTutorEligible(e.target.checked)}
        />
      </section>

      {/* Section 6: disclaimer */}
      <section className="space-y-2">
        <h3 className="text-sm font-bold uppercase tracking-[0.16em] text-muted">Footer disclaimer</h3>
        <Textarea
          label="Disclaimer shown beneath the card"
          value={disclaimer}
          onChange={(e) => setDisclaimer(e.target.value)}
          rows={2}
          maxLength={400}
          hint="Defaults to the standard practice-estimate disclaimer."
        />
      </section>

      {/* Section 7: source provenance — read-only, admin/tutor only. Never
          rendered on the learner card face. */}
      {initial?.sourceAttribution ? (
        <section className="space-y-2">
          <h3 className="text-sm font-bold uppercase tracking-[0.16em] text-muted">Source</h3>
          <p className="rounded-2xl border border-border bg-surface-muted px-4 py-3 text-xs text-muted">
            {initial.sourceAttribution}
            <span className="mt-1 block opacity-70">
              Rights notice and page reference from the printed original. Admins and tutors only — learners never see this.
            </span>
          </p>
        </section>
      ) : null}

      {/* Validation hint bar */}
      {validationHints.length > 0 ? (
        <div className="rounded-2xl border border-amber-300 bg-amber-50 px-4 py-3 text-xs text-amber-800 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-200">
          <p className="font-bold uppercase tracking-wider">Before publish</p>
          <ul className="mt-1 list-disc pl-4 space-y-0.5">
            {validationHints.map((hint, i) => <li key={i}>{hint}</li>)}
          </ul>
        </div>
      ) : null}

      <div className="flex flex-wrap items-center gap-2 border-t border-border pt-4">
        <Button type="submit" variant="primary" disabled={submitting}>
          <Save className="mr-1.5 h-4 w-4" />
          {mode === 'create' ? 'Save as draft' : 'Save changes'}
        </Button>
        {secondaryAction}
      </div>
    </form>
  );
}

export default RolePlayCardEditor;
