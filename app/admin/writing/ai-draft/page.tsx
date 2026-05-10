'use client';

import { useState, type FormEvent } from 'react';
import Link from 'next/link';
import { Wand2, AlertTriangle, CheckCircle2, ExternalLink } from 'lucide-react';
import { adminGenerateWritingAiDraft, isApiError } from '@/lib/api';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';

const PROFESSIONS: { value: string; label: string }[] = [
  { value: 'medicine', label: 'Medicine' },
  { value: 'nursing', label: 'Nursing' },
  { value: 'dentistry', label: 'Dentistry' },
  { value: 'dietetics', label: 'Dietetics' },
  { value: 'occupational-therapy', label: 'Occupational therapy' },
  { value: 'optometry', label: 'Optometry' },
  { value: 'pharmacy', label: 'Pharmacy' },
  { value: 'physiotherapy', label: 'Physiotherapy' },
  { value: 'podiatry', label: 'Podiatry' },
  { value: 'radiography', label: 'Radiography' },
  { value: 'speech-pathology', label: 'Speech pathology' },
  { value: 'veterinary', label: 'Veterinary' },
  { value: 'other-allied-health', label: 'Other allied health' },
];

const LETTER_TYPES: { value: string; label: string }[] = [
  { value: 'routine_referral', label: 'Routine referral' },
  { value: 'urgent_referral', label: 'Urgent referral' },
  { value: 'discharge', label: 'Discharge letter' },
  { value: 'transfer', label: 'Transfer letter' },
  { value: 'non_medical_referral', label: 'Non-medical referral' },
  { value: 'gp_referral', label: 'Referral to GP' },
];

const DIFFICULTIES: { value: 'easy' | 'medium' | 'hard'; label: string }[] = [
  { value: 'easy', label: 'Easy' },
  { value: 'medium', label: 'Medium' },
  { value: 'hard', label: 'Hard' },
];

const PROMPT_MIN = 10;
const PROMPT_MAX = 2000;
const CASE_NOTE_MIN = 6;
const CASE_NOTE_MAX = 24;

interface DraftResult {
  contentId: string;
  title: string;
  caseNoteCount: number;
  modelLetterWordCount: number;
  rulebookVersion: string;
  appliedRuleIds: string[];
  warning: string | null;
}

const INITIAL_FORM = {
  profession: 'medicine',
  letterType: 'routine_referral',
  recipientSpecialty: '',
  prompt: '',
  difficulty: 'medium' as 'easy' | 'medium' | 'hard',
  targetCaseNoteCount: 12,
};

export default function AdminWritingAiDraftPage() {
  const [form, setForm] = useState(INITIAL_FORM);
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<DraftResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [quotaExceeded, setQuotaExceeded] = useState(false);

  const trimmedPrompt = form.prompt.trim();
  const promptValid = trimmedPrompt.length >= PROMPT_MIN && trimmedPrompt.length <= PROMPT_MAX;
  const caseCountValid =
    Number.isFinite(form.targetCaseNoteCount) &&
    form.targetCaseNoteCount >= CASE_NOTE_MIN &&
    form.targetCaseNoteCount <= CASE_NOTE_MAX;
  const canSubmit = promptValid && caseCountValid && !loading;

  const handleReset = () => {
    setForm(INITIAL_FORM);
    setResult(null);
    setError(null);
    setQuotaExceeded(false);
  };

  const handleSubmit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!canSubmit) return;

    setLoading(true);
    setError(null);
    setQuotaExceeded(false);
    setResult(null);

    try {
      const recipientSpecialty = form.recipientSpecialty.trim();
      const response = await adminGenerateWritingAiDraft({
        prompt: trimmedPrompt,
        profession: form.profession,
        letterType: form.letterType,
        recipientSpecialty: recipientSpecialty.length > 0 ? recipientSpecialty : undefined,
        difficulty: form.difficulty,
        targetCaseNoteCount: form.targetCaseNoteCount,
      });
      setResult(response);
    } catch (err) {
      if (isApiError(err) && err.status === 429) {
        setQuotaExceeded(true);
      } else if (err instanceof Error) {
        setError(err.message || 'Failed to generate draft.');
      } else {
        setError('Failed to generate draft.');
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <main className="mx-auto max-w-4xl px-6 py-10">
      <MotionSection className="mb-8">
        <div className="flex items-start gap-4">
          <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-primary/10 text-primary">
            <Wand2 className="h-6 w-6" aria-hidden="true" />
          </div>
          <div>
            <h1 className="text-2xl font-bold tracking-tight text-navy">Writing AI Draft</h1>
            <p className="mt-1 text-sm text-muted">
              Generate a grounded Writing case-note paper through the AI gateway. The backend embeds the
              Writing rulebook and refuses ungrounded prompts.
            </p>
          </div>
        </div>
      </MotionSection>

      <Card className="p-6">
        <form onSubmit={handleSubmit} className="flex flex-col gap-5">
          <div className="grid gap-5 md:grid-cols-2">
            <Select
              label="Profession"
              id="profession"
              value={form.profession}
              onChange={(e) => setForm((f) => ({ ...f, profession: e.target.value }))}
              options={PROFESSIONS}
            />

            <Select
              label="Letter type"
              id="letterType"
              value={form.letterType}
              onChange={(e) => setForm((f) => ({ ...f, letterType: e.target.value }))}
              options={LETTER_TYPES}
            />
          </div>

          <Input
            label="Recipient specialty (optional)"
            id="recipientSpecialty"
            value={form.recipientSpecialty}
            onChange={(e) => setForm((f) => ({ ...f, recipientSpecialty: e.target.value }))}
            placeholder="e.g. Cardiology, Diabetes nurse"
          />

          <Textarea
            label="Prompt"
            id="prompt"
            value={form.prompt}
            onChange={(e) => setForm((f) => ({ ...f, prompt: e.target.value }))}
            placeholder="e.g. 60-year-old male with chest pain admitted to A&E…"
            rows={6}
            maxLength={PROMPT_MAX}
            hint={`${trimmedPrompt.length} / ${PROMPT_MAX} characters (min ${PROMPT_MIN})`}
            required
          />

          <fieldset className="flex flex-col gap-2">
            <legend className="text-sm font-semibold tracking-tight text-navy">Difficulty</legend>
            <div className="flex flex-wrap gap-3" role="radiogroup" aria-label="Difficulty">
              {DIFFICULTIES.map((d) => {
                const checked = form.difficulty === d.value;
                return (
                  <label
                    key={d.value}
                    className={`flex cursor-pointer items-center gap-2 rounded-2xl border px-4 py-2 text-sm shadow-sm transition-colors ${
                      checked
                        ? 'border-primary bg-primary/10 text-primary font-semibold'
                        : 'border-gray-200 bg-background-light text-navy hover:border-gray-300'
                    }`}
                  >
                    <input
                      type="radio"
                      name="difficulty"
                      value={d.value}
                      checked={checked}
                      onChange={() => setForm((f) => ({ ...f, difficulty: d.value }))}
                      className="h-4 w-4 text-primary focus:ring-primary"
                    />
                    {d.label}
                  </label>
                );
              })}
            </div>
          </fieldset>

          <Input
            label="Target case-note count"
            id="targetCaseNoteCount"
            type="number"
            min={CASE_NOTE_MIN}
            max={CASE_NOTE_MAX}
            value={form.targetCaseNoteCount}
            onChange={(e) =>
              setForm((f) => ({ ...f, targetCaseNoteCount: Number.parseInt(e.target.value, 10) || 0 }))
            }
            hint={`Range ${CASE_NOTE_MIN}–${CASE_NOTE_MAX}`}
          />

          <div className="flex flex-wrap items-center gap-3 pt-2">
            <Button type="submit" variant="primary" disabled={!canSubmit} loading={loading}>
              Generate draft
            </Button>
            <Button type="button" variant="ghost" onClick={handleReset} disabled={loading}>
              Reset
            </Button>
          </div>
        </form>
      </Card>

      {quotaExceeded && (
        <div className="mt-6">
          <InlineAlert variant="error" title="AI quota exceeded">
            AI quota exceeded — try again later.
          </InlineAlert>
        </div>
      )}

      {error && !quotaExceeded && (
        <div className="mt-6">
          <InlineAlert variant="error" title="Draft generation failed">
            {error}
          </InlineAlert>
        </div>
      )}

      {result && (
        <MotionSection className="mt-8">
          <Card className="p-6">
            <MotionItem>
              <div className="mb-4 flex items-start gap-3">
                <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-emerald-50 text-emerald-600">
                  <CheckCircle2 className="h-5 w-5" aria-hidden="true" />
                </div>
                <div className="flex-1">
                  <h2 className="text-lg font-bold tracking-tight text-navy">{result.title}</h2>
                  <p className="mt-0.5 text-xs text-muted">
                    Content ID:{' '}
                    <Link
                      href={`/admin/content/writing/${result.contentId}`}
                      className="inline-flex items-center gap-1 font-mono text-primary hover:underline"
                    >
                      {result.contentId}
                      <ExternalLink className="h-3 w-3" aria-hidden="true" />
                    </Link>
                  </p>
                </div>
              </div>
            </MotionItem>

            <MotionItem>
              <dl className="grid gap-4 border-t border-gray-100 pt-4 sm:grid-cols-3">
                <div>
                  <dt className="text-xs uppercase tracking-wide text-muted">Case notes</dt>
                  <dd className="mt-1 text-xl font-bold text-navy">{result.caseNoteCount}</dd>
                </div>
                <div>
                  <dt className="text-xs uppercase tracking-wide text-muted">Model letter words</dt>
                  <dd className="mt-1 text-xl font-bold text-navy">{result.modelLetterWordCount}</dd>
                </div>
                <div>
                  <dt className="text-xs uppercase tracking-wide text-muted">Rulebook version</dt>
                  <dd className="mt-1 text-xl font-bold text-navy">{result.rulebookVersion}</dd>
                </div>
              </dl>
            </MotionItem>

            {result.appliedRuleIds.length > 0 && (
              <MotionItem>
                <div className="mt-5 border-t border-gray-100 pt-4">
                  <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">
                    Applied rules
                  </p>
                  <div className="flex flex-wrap gap-2">
                    {result.appliedRuleIds.map((id) => (
                      <Badge key={id} variant="info">
                        {id}
                      </Badge>
                    ))}
                  </div>
                </div>
              </MotionItem>
            )}

            {result.warning && (
              <MotionItem>
                <div className="mt-5 flex items-start gap-3 rounded-2xl border border-amber-200 bg-amber-50 p-4">
                  <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" aria-hidden="true" />
                  <p className="text-sm leading-6 text-amber-800">{result.warning}</p>
                </div>
              </MotionItem>
            )}
          </Card>
        </MotionSection>
      )}
    </main>
  );
}
