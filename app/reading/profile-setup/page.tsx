'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/auth-context';
import { submitOnboarding, type OnboardingRequest } from '@/lib/reading-pathway-api';

const TARGET_BANDS = [
  { value: 'B', label: 'B', score: '350+' },
  { value: 'B+', label: 'B+', score: '400+' },
  { value: 'A', label: 'A', score: '450+' },
] as const;

const PROFESSIONS = [
  'Medicine',
  'Nursing',
  'Pharmacy',
  'Dentist',
  'Optometry',
  'Physiotherapist',
  'Occupational Therapist',
  'Podiatry',
  'Radiographer',
  'Speech Pathology',
  'Veterinary Science',
  'Dietetics',
  'Other',
] as const;

interface FormData {
  targetBand: string;
  examDate: string;
  hoursPerWeek: number;
  profession: string;
  hasTakenBefore: boolean;
  previousScore: string;
  selfRatedSpeed: number;
  selfRatedVocabulary: number;
}

const INITIAL_FORM: FormData = {
  targetBand: 'B',
  examDate: '',
  hoursPerWeek: 5,
  profession: '',
  hasTakenBefore: false,
  previousScore: '',
  selfRatedSpeed: 3,
  selfRatedVocabulary: 3,
};

function StepIndicator({ current, total }: { current: number; total: number }) {
  return (
    <div className="mb-8 flex items-center justify-between">
      {Array.from({ length: total }, (_, i) => i + 1).map((step) => (
        <div key={step} className="flex flex-1 items-center">
          <div
            className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-sm font-bold transition-colors ${
              step < current
                ? 'bg-primary text-white dark:bg-violet-700'
                : step === current
                  ? 'border-2 border-primary bg-surface text-primary'
                  : 'border-2 border-border bg-surface text-muted'
            }`}
          >
            {step < current ? '✓' : step}
          </div>
          {step < total && (
            <div
              className={`h-0.5 flex-1 transition-colors ${step < current ? 'bg-primary' : 'bg-border'}`}
            />
          )}
        </div>
      ))}
    </div>
  );
}

export default function ProfileSetupPage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const router = useRouter();
  const [step, setStep] = useState(1);
  const [form, setForm] = useState<FormData>(INITIAL_FORM);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!authLoading && !isAuthenticated) {
      router.replace('/sign-in');
    }
  }, [authLoading, isAuthenticated, router]);

  if (authLoading || !isAuthenticated) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-background-light px-4">
        <div className="h-8 w-8 motion-safe:animate-spin rounded-full border-4 border-violet-200 border-t-violet-600" />
      </div>
    );
  }

  const update = <K extends keyof FormData>(key: K, value: FormData[K]) =>
    setForm((prev) => ({ ...prev, [key]: value }));

  const handleSubmit = async () => {
    setSubmitting(true);
    setError(null);
    try {
      const req: OnboardingRequest = {
        targetBand: form.targetBand,
        examDate: form.examDate || null,
        hoursPerWeek: form.hoursPerWeek,
        profession: form.profession,
        hasTakenBefore: form.hasTakenBefore,
        previousScore:
          form.hasTakenBefore && form.previousScore !== ''
            ? Number(form.previousScore)
            : null,
        selfRatedSpeed: form.selfRatedSpeed,
        selfRatedVocabulary: form.selfRatedVocabulary,
      };
      await submitOnboarding(req);
      router.push('/reading/diagnostic');
    } catch {
      setError('Something went wrong. Please try again.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-background-light px-4 py-16">
      <div className="w-full max-w-lg">
        <div className="mb-6 text-center">
          <h1 className="text-2xl font-extrabold text-navy">Set Up Your Profile</h1>
          <p className="mt-1 text-sm text-muted">Step {step} of 3</p>
        </div>

        <StepIndicator current={step} total={3} />

        <div className="rounded-2xl border border-border bg-surface p-8 shadow-sm">
          {step === 1 && (
            <div className="space-y-6">
              <h2 className="text-lg font-bold text-navy">Your Goals</h2>

              {/* Target band */}
              <div>
                <label className="mb-2 block text-sm font-semibold text-navy">
                  Target Band (OET scale)
                </label>
                <div className="grid grid-cols-3 gap-2">
                  {TARGET_BANDS.map((band) => (
                    <button
                      key={band.value}
                      type="button"
                      onClick={() => update('targetBand', band.value)}
                      className={`rounded-xl border py-3 text-sm font-bold transition-colors ${
                        form.targetBand === band.value
                          ? 'border-primary bg-primary text-white dark:bg-violet-700'
                          : 'border-border bg-surface text-navy hover:border-primary/40'
                      }`}
                    >
                      <span className="block">{band.label}</span>
                      <span className="block text-xs font-medium opacity-80">{band.score}</span>
                    </button>
                  ))}
                </div>
              </div>

              {/* Exam date */}
              <div>
                <label
                  htmlFor="examDate"
                  className="mb-2 block text-sm font-semibold text-navy"
                >
                  Exam Date <span className="font-normal text-muted">(optional)</span>
                </label>
                <input
                  id="examDate"
                  type="date"
                  value={form.examDate}
                  onChange={(e) => update('examDate', e.target.value)}
                  className="w-full rounded-xl border border-border px-4 py-3 text-sm text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
                />
              </div>

              {/* Hours per week */}
              <div>
                <label
                  htmlFor="hoursPerWeek"
                  className="mb-2 flex items-center justify-between text-sm font-semibold text-navy"
                >
                  <span>Hours per week</span>
                  <span className="rounded-full bg-lavender px-3 py-0.5 text-primary">
                    {form.hoursPerWeek} hrs
                  </span>
                </label>
                <input
                  id="hoursPerWeek"
                  type="range"
                  min={1}
                  max={20}
                  value={form.hoursPerWeek}
                  onChange={(e) => update('hoursPerWeek', Number(e.target.value))}
                  className="w-full accent-primary"
                />
                <div className="mt-1 flex justify-between text-xs text-muted">
                  <span>1 hr</span>
                  <span>20 hrs</span>
                </div>
              </div>
            </div>
          )}

          {step === 2 && (
            <div className="space-y-6">
              <h2 className="text-lg font-bold text-navy">Your Background</h2>

              {/* Profession */}
              <div>
                <p className="mb-2 text-sm font-semibold text-navy">Profession</p>
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
                  {PROFESSIONS.map((prof) => (
                    <button
                      key={prof}
                      type="button"
                      onClick={() => update('profession', prof)}
                      className={`rounded-xl border px-3 py-3 text-sm font-medium transition-colors ${
                        form.profession === prof
                          ? 'border-primary bg-primary text-white dark:bg-violet-700'
                          : 'border-border bg-surface text-navy hover:border-primary/40'
                      }`}
                    >
                      {prof}
                    </button>
                  ))}
                </div>
              </div>

              {/* Has taken before */}
              <div>
                <p className="mb-2 text-sm font-semibold text-navy">
                  Have you taken the OET before?
                </p>
                <div className="flex gap-3">
                  {(['Yes', 'No'] as const).map((option) => {
                    const isYes = option === 'Yes';
                    const active = form.hasTakenBefore === isYes;
                    return (
                      <button
                        key={option}
                        type="button"
                        onClick={() => update('hasTakenBefore', isYes)}
                        className={`flex-1 rounded-xl border py-3 text-sm font-bold transition-colors ${
                          active
                            ? 'border-primary bg-primary text-white dark:bg-violet-700'
                            : 'border-border bg-surface text-navy hover:border-primary/40'
                        }`}
                      >
                        {option}
                      </button>
                    );
                  })}
                </div>
              </div>

              {/* Previous score */}
              {form.hasTakenBefore && (
                <div>
                  <label
                    htmlFor="previousScore"
                    className="mb-2 block text-sm font-semibold text-navy"
                  >
                    Previous OET Reading score (0–500)
                  </label>
                  <input
                    id="previousScore"
                    type="number"
                    min={0}
                    max={500}
                    placeholder="e.g. 300"
                    value={form.previousScore}
                    onChange={(e) => update('previousScore', e.target.value)}
                    className="w-full rounded-xl border border-border px-4 py-3 text-sm text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
                  />
                </div>
              )}
            </div>
          )}

          {step === 3 && (
            <div className="space-y-6">
              <h2 className="text-lg font-bold text-navy">Self-Assessment</h2>

              {/* Reading speed */}
              <div>
                <label
                  htmlFor="selfRatedSpeed"
                  className="mb-2 flex items-center justify-between text-sm font-semibold text-navy"
                >
                  <span>How would you rate your reading speed?</span>
                  <span className="rounded-full bg-lavender px-3 py-0.5 text-primary">
                    {form.selfRatedSpeed}/5
                  </span>
                </label>
                <input
                  id="selfRatedSpeed"
                  type="range"
                  min={1}
                  max={5}
                  value={form.selfRatedSpeed}
                  onChange={(e) => update('selfRatedSpeed', Number(e.target.value))}
                  className="w-full accent-primary"
                />
                <div className="mt-1 flex justify-between text-xs text-muted">
                  <span>Very slow</span>
                  <span>Very fast</span>
                </div>
              </div>

              {/* Vocabulary */}
              <div>
                <label
                  htmlFor="selfRatedVocabulary"
                  className="mb-2 flex items-center justify-between text-sm font-semibold text-navy"
                >
                  <span>How would you rate your medical vocabulary?</span>
                  <span className="rounded-full bg-lavender px-3 py-0.5 text-primary">
                    {form.selfRatedVocabulary}/5
                  </span>
                </label>
                <input
                  id="selfRatedVocabulary"
                  type="range"
                  min={1}
                  max={5}
                  value={form.selfRatedVocabulary}
                  onChange={(e) => update('selfRatedVocabulary', Number(e.target.value))}
                  className="w-full accent-primary"
                />
                <div className="mt-1 flex justify-between text-xs text-muted">
                  <span>Very limited</span>
                  <span>Very strong</span>
                </div>
              </div>

              {error && (
                <p className="rounded-xl border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {error}
                </p>
              )}
            </div>
          )}

          {/* Navigation */}
          <div className="mt-8 flex gap-3">
            {step > 1 && (
              <button
                type="button"
                onClick={() => setStep((s) => s - 1)}
                className="flex-1 rounded-xl border border-border py-3 text-sm font-semibold text-navy transition-colors hover:bg-background-light"
              >
                Back
              </button>
            )}
            {step < 3 ? (
              <button
                type="button"
                onClick={() => setStep((s) => s + 1)}
                disabled={step === 2 && !form.profession}
                className="flex-1 rounded-xl bg-primary py-3 text-sm font-bold text-white transition-[color,background-color,transform] duration-200 hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600 disabled:opacity-50"
              >
                Next
              </button>
            ) : (
              <button
                type="button"
                onClick={handleSubmit}
                disabled={submitting}
                className="flex-1 rounded-xl bg-primary py-3 text-sm font-bold text-white transition-[color,background-color,transform] duration-200 hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600 disabled:opacity-50"
              >
                {submitting ? 'Saving…' : 'Start Diagnostic'}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
