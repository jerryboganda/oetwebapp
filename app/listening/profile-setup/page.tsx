'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  Stethoscope,
  HeartPulse,
  Pill,
  Smile,
  Eye,
  Activity,
  HandHelping,
  Footprints,
  Scan,
  MessageCircle,
  PawPrint,
  Apple,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { useAuth } from '@/contexts/auth-context';
import { submitOnboarding, type OnboardingPayload } from '@/lib/listening-pathway-api';

// ─────────────────────────────────────────────────────────────────────────────
// Static enum-like data
// ─────────────────────────────────────────────────────────────────────────────

const TARGET_BANDS = [
  { value: 'B', label: 'B', score: '350+' },
  { value: 'B+', label: 'B+', score: '400+' },
  { value: 'A', label: 'A', score: '450+' },
] as const;

const PROFESSIONS: { value: string; label: string; icon: LucideIcon }[] = [
  { value: 'Medicine', label: 'Medicine', icon: Stethoscope },
  { value: 'Nursing', label: 'Nursing', icon: HeartPulse },
  { value: 'Pharmacy', label: 'Pharmacy', icon: Pill },
  { value: 'Dentistry', label: 'Dentistry', icon: Smile },
  { value: 'Optometry', label: 'Optometry', icon: Eye },
  { value: 'Physiotherapy', label: 'Physiotherapy', icon: Activity },
  { value: 'Occupational Therapy', label: 'Occupational Therapy', icon: HandHelping },
  { value: 'Podiatry', label: 'Podiatry', icon: Footprints },
  { value: 'Radiography', label: 'Radiography', icon: Scan },
  { value: 'Speech Pathology', label: 'Speech Pathology', icon: MessageCircle },
  { value: 'Veterinary', label: 'Veterinary', icon: PawPrint },
  { value: 'Dietetics', label: 'Dietetics', icon: Apple },
];

type ExposureSource =
  | 'american_tv'
  | 'british_tv'
  | 'both'
  | 'australian'
  | 'mixed'
  | 'other';

const EXPOSURE_SOURCES: { value: ExposureSource; label: string }[] = [
  { value: 'american_tv', label: 'Mostly American TV / films' },
  { value: 'british_tv', label: 'Mostly British TV / films' },
  { value: 'both', label: 'Both American and British' },
  { value: 'australian', label: 'Mostly Australian / NZ media' },
  { value: 'mixed', label: 'Mixed sources (podcasts, news, varied)' },
  { value: 'other', label: 'Other / non-native English contexts' },
];

// ─────────────────────────────────────────────────────────────────────────────
// Form state
// ─────────────────────────────────────────────────────────────────────────────

interface FormData {
  // Step 1 – Goal
  targetBand: string;
  examDate: string;
  examNotBooked: boolean;
  hoursPerWeek: number;
  // Step 2 – Profession
  profession: string;
  // Step 3 – Audio context
  englishExposureSource: ExposureSource;
  comfortBritish: number;
  comfortAustralian: number;
  comfortVarious: number;
  // Step 4 – Self-rating
  selfRatedSpeed: number;
  selfRatedNoteTaking: number;
  selfRatedSpelling: number;
  hasTakenBefore: boolean;
  previousScore: string;
}

const INITIAL_FORM: FormData = {
  targetBand: 'B',
  examDate: '',
  examNotBooked: false,
  hoursPerWeek: 5,
  profession: '',
  englishExposureSource: 'both',
  comfortBritish: 3,
  comfortAustralian: 3,
  comfortVarious: 3,
  selfRatedSpeed: 3,
  selfRatedNoteTaking: 3,
  selfRatedSpelling: 3,
  hasTakenBefore: false,
  previousScore: '',
};

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

function StepIndicator({ current, total }: { current: number; total: number }) {
  return (
    <div className="mb-8 flex items-center justify-between" aria-label={`Step ${current} of ${total}`}>
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

interface ComfortSliderProps {
  id: string;
  label: string;
  value: number;
  onChange: (next: number) => void;
}

function ComfortSlider({ id, label, value, onChange }: ComfortSliderProps) {
  return (
    <div>
      <label
        htmlFor={id}
        className="mb-2 flex items-center justify-between text-sm font-semibold text-navy"
      >
        <span>{label}</span>
        <span className="rounded-full bg-lavender px-3 py-0.5 text-primary">{value}/5</span>
      </label>
      <input
        id={id}
        type="range"
        min={1}
        max={5}
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
        className="w-full accent-primary"
        aria-valuemin={1}
        aria-valuemax={5}
        aria-valuenow={value}
      />
      <div className="mt-1 flex justify-between text-xs text-muted">
        <span>Uncomfortable</span>
        <span>Very confident</span>
      </div>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Page
// ─────────────────────────────────────────────────────────────────────────────

export default function ListeningProfileSetupPage() {
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
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-lavender border-t-primary" />
      </div>
    );
  }

  const update = <K extends keyof FormData>(key: K, value: FormData[K]) =>
    setForm((prev) => ({ ...prev, [key]: value }));

  const canProceed = (currentStep: number): boolean => {
    if (currentStep === 1) return Boolean(form.targetBand);
    if (currentStep === 2) return Boolean(form.profession);
    if (currentStep === 3) return Boolean(form.englishExposureSource);
    return true;
  };

  const handleSubmit = async () => {
    setSubmitting(true);
    setError(null);
    try {
      const payload: OnboardingPayload = {
        targetBand: form.targetBand,
        examDate: form.examNotBooked || !form.examDate ? null : form.examDate,
        hoursPerWeek: form.hoursPerWeek,
        profession: form.profession,
        englishExposureSource: form.englishExposureSource,
        comfortBritish: form.comfortBritish,
        comfortAustralian: form.comfortAustralian,
        comfortVarious: form.comfortVarious,
        hasTakenBefore: form.hasTakenBefore,
        previousScore:
          form.hasTakenBefore && form.previousScore !== ''
            ? Number(form.previousScore)
            : null,
        selfRatedSpeed: form.selfRatedSpeed,
        selfRatedNoteTaking: form.selfRatedNoteTaking,
        selfRatedSpelling: form.selfRatedSpelling,
      };
      await submitOnboarding(payload);
      router.push('/listening/audio-check');
    } catch {
      setError('Something went wrong saving your profile. Please try again.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-background-light px-4 py-16">
      <div className="w-full max-w-lg">
        <div className="mb-6 text-center">
          <h1 className="text-2xl font-extrabold text-navy">Set up your listening profile</h1>
          <p className="mt-1 text-sm text-muted">Step {step} of 4</p>
        </div>

        <StepIndicator current={step} total={4} />

        <div className="rounded-2xl border border-border bg-surface p-8 shadow-sm">
          {/* Step 1 — Goal */}
          {step === 1 && (
            <div className="space-y-6">
              <h2 className="text-lg font-bold text-navy">Your goal</h2>

              <div>
                <p className="mb-2 block text-sm font-semibold text-navy">
                  Target band (OET scale)
                </p>
                <div className="grid grid-cols-3 gap-2" role="radiogroup" aria-label="Target band">
                  {TARGET_BANDS.map((band) => (
                    <button
                      key={band.value}
                      type="button"
                      role="radio"
                      aria-checked={form.targetBand === band.value}
                      onClick={() => update('targetBand', band.value)}
                      className={`rounded-xl border py-3 text-sm font-bold transition-colors ${
                        form.targetBand === band.value
                          ? 'border-primary bg-primary text-white dark:bg-violet-700'
                          : 'border-border bg-surface text-navy hover:border-border-hover'
                      }`}
                    >
                      <span className="block">{band.label}</span>
                      <span className="block text-xs font-medium opacity-80">{band.score}</span>
                    </button>
                  ))}
                </div>
              </div>

              <div>
                <label
                  htmlFor="examDate"
                  className="mb-2 block text-sm font-semibold text-navy"
                >
                  Exam date
                </label>
                <input
                  id="examDate"
                  type="date"
                  value={form.examDate}
                  disabled={form.examNotBooked}
                  onChange={(event) => update('examDate', event.target.value)}
                  className="w-full rounded-xl border border-border px-4 py-3 text-sm text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20 disabled:cursor-not-allowed disabled:bg-background-light disabled:text-muted"
                />
                <label className="mt-2 flex cursor-pointer items-center gap-2 text-sm text-muted">
                  <input
                    type="checkbox"
                    checked={form.examNotBooked}
                    onChange={(event) => update('examNotBooked', event.target.checked)}
                    className="rounded border-border text-primary focus:ring-primary"
                  />
                  I haven&apos;t booked my exam yet
                </label>
              </div>

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
                  min={3}
                  max={20}
                  value={form.hoursPerWeek}
                  onChange={(event) => update('hoursPerWeek', Number(event.target.value))}
                  className="w-full accent-primary"
                />
                <div className="mt-1 flex justify-between text-xs text-muted">
                  <span>3 hrs</span>
                  <span>20 hrs</span>
                </div>
              </div>
            </div>
          )}

          {/* Step 2 — Profession */}
          {step === 2 && (
            <div className="space-y-6">
              <h2 className="text-lg font-bold text-navy">Your profession</h2>
              <p className="text-sm text-muted">
                We&apos;ll tailor scenarios and vocabulary to your field.
              </p>
              <div className="grid grid-cols-2 gap-2 sm:grid-cols-3" role="radiogroup" aria-label="Profession">
                {PROFESSIONS.map((prof) => {
                  const Icon = prof.icon;
                  const active = form.profession === prof.value;
                  return (
                    <button
                      key={prof.value}
                      type="button"
                      role="radio"
                      aria-checked={active}
                      onClick={() => update('profession', prof.value)}
                      className={`flex flex-col items-center gap-2 rounded-xl border px-3 py-4 text-center text-xs font-medium transition-colors ${
                        active
                          ? 'border-primary bg-primary text-white dark:bg-violet-700'
                          : 'border-border bg-surface text-navy hover:border-border-hover'
                      }`}
                    >
                      <Icon className="h-6 w-6" aria-hidden />
                      <span>{prof.label}</span>
                    </button>
                  );
                })}
              </div>
            </div>
          )}

          {/* Step 3 — Audio context */}
          {step === 3 && (
            <div className="space-y-6">
              <h2 className="text-lg font-bold text-navy">Your audio context</h2>

              <div>
                <label
                  htmlFor="exposureSource"
                  className="mb-2 block text-sm font-semibold text-navy"
                >
                  Where do you encounter English most?
                </label>
                <select
                  id="exposureSource"
                  value={form.englishExposureSource}
                  onChange={(event) =>
                    update('englishExposureSource', event.target.value as ExposureSource)
                  }
                  className="w-full rounded-xl border border-border px-4 py-3 text-sm text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
                >
                  {EXPOSURE_SOURCES.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
              </div>

              <ComfortSlider
                id="comfortBritish"
                label="Comfort with British accents"
                value={form.comfortBritish}
                onChange={(value) => update('comfortBritish', value)}
              />
              <ComfortSlider
                id="comfortAustralian"
                label="Comfort with Australian / NZ accents"
                value={form.comfortAustralian}
                onChange={(value) => update('comfortAustralian', value)}
              />
              <ComfortSlider
                id="comfortVarious"
                label="Comfort with various / non-native accents"
                value={form.comfortVarious}
                onChange={(value) => update('comfortVarious', value)}
              />
            </div>
          )}

          {/* Step 4 — Self-rating */}
          {step === 4 && (
            <div className="space-y-6">
              <h2 className="text-lg font-bold text-navy">Self-assessment</h2>

              <div>
                <label
                  htmlFor="selfRatedSpeed"
                  className="mb-2 flex items-center justify-between text-sm font-semibold text-navy"
                >
                  <span>Keeping up with fast spoken English</span>
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
                  onChange={(event) => update('selfRatedSpeed', Number(event.target.value))}
                  className="w-full accent-primary"
                />
                <div className="mt-1 flex justify-between text-xs text-muted">
                  <span>Struggle</span>
                  <span>Very strong</span>
                </div>
              </div>

              <div>
                <label
                  htmlFor="selfRatedNoteTaking"
                  className="mb-2 flex items-center justify-between text-sm font-semibold text-navy"
                >
                  <span>Note-taking while listening</span>
                  <span className="rounded-full bg-lavender px-3 py-0.5 text-primary">
                    {form.selfRatedNoteTaking}/5
                  </span>
                </label>
                <input
                  id="selfRatedNoteTaking"
                  type="range"
                  min={1}
                  max={5}
                  value={form.selfRatedNoteTaking}
                  onChange={(event) => update('selfRatedNoteTaking', Number(event.target.value))}
                  className="w-full accent-primary"
                />
                <div className="mt-1 flex justify-between text-xs text-muted">
                  <span>I miss details</span>
                  <span>I capture everything</span>
                </div>
              </div>

              <div>
                <label
                  htmlFor="selfRatedSpelling"
                  className="mb-2 flex items-center justify-between text-sm font-semibold text-navy"
                >
                  <span>Spelling under time pressure</span>
                  <span className="rounded-full bg-lavender px-3 py-0.5 text-primary">
                    {form.selfRatedSpelling}/5
                  </span>
                </label>
                <input
                  id="selfRatedSpelling"
                  type="range"
                  min={1}
                  max={5}
                  value={form.selfRatedSpelling}
                  onChange={(event) => update('selfRatedSpelling', Number(event.target.value))}
                  className="w-full accent-primary"
                />
                <div className="mt-1 flex justify-between text-xs text-muted">
                  <span>Often misspell</span>
                  <span>Very accurate</span>
                </div>
              </div>

              <div>
                <p className="mb-2 text-sm font-semibold text-navy">
                  Have you taken OET Listening before?
                </p>
                <div className="flex gap-3" role="radiogroup" aria-label="OET Listening history">
                  {(['Yes', 'No'] as const).map((option) => {
                    const isYes = option === 'Yes';
                    const active = form.hasTakenBefore === isYes;
                    return (
                      <button
                        key={option}
                        type="button"
                        role="radio"
                        aria-checked={active}
                        onClick={() => update('hasTakenBefore', isYes)}
                        className={`flex-1 rounded-xl border py-3 text-sm font-bold transition-colors ${
                          active
                            ? 'border-primary bg-primary text-white dark:bg-violet-700'
                            : 'border-border bg-surface text-navy hover:border-border-hover'
                        }`}
                      >
                        {option}
                      </button>
                    );
                  })}
                </div>
              </div>

              {form.hasTakenBefore && (
                <div>
                  <label
                    htmlFor="previousScore"
                    className="mb-2 block text-sm font-semibold text-navy"
                  >
                    Previous OET Listening score (0–500)
                  </label>
                  <input
                    id="previousScore"
                    type="number"
                    min={0}
                    max={500}
                    placeholder="e.g. 320"
                    value={form.previousScore}
                    onChange={(event) => update('previousScore', event.target.value)}
                    className="w-full rounded-xl border border-border px-4 py-3 text-sm text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
                  />
                </div>
              )}

              {error && (
                <p
                  className="rounded-xl border border-danger/20 bg-danger/10 px-4 py-3 text-sm text-danger"
                  role="alert"
                >
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
            {step < 4 ? (
              <button
                type="button"
                onClick={() => setStep((s) => s + 1)}
                disabled={!canProceed(step)}
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
                {submitting ? 'Saving…' : 'Complete'}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
