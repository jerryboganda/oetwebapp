'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/auth-context';
import { submitOnboarding, type OnboardingRequest } from '@/lib/reading-pathway-api';

const TARGET_BANDS = [300, 350, 400, 450, 500] as const;

const PROFESSIONS = [
  'Doctor',
  'Nurse',
  'Pharmacist',
  'Dentist',
  'Physiotherapist',
  'Occupational Therapist',
  'Radiographer',
  'Other',
] as const;

interface FormData {
  targetBand: number;
  examDate: string;
  hoursPerWeek: number;
  profession: string;
  hasTakenBefore: boolean;
  previousScore: string;
  selfRatedSpeed: number;
  selfRatedVocab: number;
}

const INITIAL_FORM: FormData = {
  targetBand: 350,
  examDate: '',
  hoursPerWeek: 5,
  profession: '',
  hasTakenBefore: false,
  previousScore: '',
  selfRatedSpeed: 3,
  selfRatedVocab: 3,
};

function StepIndicator({ current, total }: { current: number; total: number }) {
  return (
    <div className="mb-8 flex items-center justify-between">
      {Array.from({ length: total }, (_, i) => i + 1).map((step) => (
        <div key={step} className="flex flex-1 items-center">
          <div
            className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-sm font-bold transition-colors ${
              step < current
                ? 'bg-violet-600 text-white'
                : step === current
                  ? 'border-2 border-violet-600 bg-white text-violet-600'
                  : 'border-2 border-gray-200 bg-white text-gray-400'
            }`}
          >
            {step < current ? '✓' : step}
          </div>
          {step < total && (
            <div
              className={`h-0.5 flex-1 transition-colors ${step < current ? 'bg-violet-600' : 'bg-gray-200'}`}
            />
          )}
        </div>
      ))}
    </div>
  );
}

export default function ProfileSetupPage() {
  const { isAuthenticated } = useAuth();
  const router = useRouter();
  const [step, setStep] = useState(1);
  const [form, setForm] = useState<FormData>(INITIAL_FORM);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!isAuthenticated) {
    router.replace('/sign-in');
    return null;
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
        selfRatedVocab: form.selfRatedVocab,
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
    <div className="flex min-h-screen flex-col items-center justify-center bg-gradient-to-b from-violet-50 to-white px-4 py-16">
      <div className="w-full max-w-lg">
        <div className="mb-6 text-center">
          <h1 className="text-2xl font-extrabold text-gray-900">Set Up Your Profile</h1>
          <p className="mt-1 text-sm text-gray-500">Step {step} of 3</p>
        </div>

        <StepIndicator current={step} total={3} />

        <div className="rounded-2xl border border-gray-100 bg-white p-8 shadow-sm">
          {step === 1 && (
            <div className="space-y-6">
              <h2 className="text-lg font-bold text-gray-900">Your Goals</h2>

              {/* Target band */}
              <div>
                <label className="mb-2 block text-sm font-semibold text-gray-700">
                  Target Band (OET scale)
                </label>
                <div className="grid grid-cols-5 gap-2">
                  {TARGET_BANDS.map((band) => (
                    <button
                      key={band}
                      type="button"
                      onClick={() => update('targetBand', band)}
                      className={`rounded-xl border py-3 text-sm font-bold transition-colors ${
                        form.targetBand === band
                          ? 'border-violet-600 bg-violet-600 text-white'
                          : 'border-gray-200 bg-white text-gray-700 hover:border-violet-300'
                      }`}
                    >
                      {band}
                    </button>
                  ))}
                </div>
              </div>

              {/* Exam date */}
              <div>
                <label
                  htmlFor="examDate"
                  className="mb-2 block text-sm font-semibold text-gray-700"
                >
                  Exam Date <span className="font-normal text-gray-400">(optional)</span>
                </label>
                <input
                  id="examDate"
                  type="date"
                  value={form.examDate}
                  onChange={(e) => update('examDate', e.target.value)}
                  className="w-full rounded-xl border border-gray-200 px-4 py-3 text-sm text-gray-900 focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-200"
                />
              </div>

              {/* Hours per week */}
              <div>
                <label
                  htmlFor="hoursPerWeek"
                  className="mb-2 flex items-center justify-between text-sm font-semibold text-gray-700"
                >
                  <span>Hours per week</span>
                  <span className="rounded-full bg-violet-100 px-3 py-0.5 text-violet-700">
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
                  className="w-full accent-violet-600"
                />
                <div className="mt-1 flex justify-between text-xs text-gray-400">
                  <span>1 hr</span>
                  <span>20 hrs</span>
                </div>
              </div>
            </div>
          )}

          {step === 2 && (
            <div className="space-y-6">
              <h2 className="text-lg font-bold text-gray-900">Your Background</h2>

              {/* Profession */}
              <div>
                <p className="mb-2 text-sm font-semibold text-gray-700">Profession</p>
                <div className="grid grid-cols-2 gap-2">
                  {PROFESSIONS.map((prof) => (
                    <button
                      key={prof}
                      type="button"
                      onClick={() => update('profession', prof)}
                      className={`rounded-xl border px-3 py-3 text-sm font-medium transition-colors ${
                        form.profession === prof
                          ? 'border-violet-600 bg-violet-600 text-white'
                          : 'border-gray-200 bg-white text-gray-700 hover:border-violet-300'
                      }`}
                    >
                      {prof}
                    </button>
                  ))}
                </div>
              </div>

              {/* Has taken before */}
              <div>
                <p className="mb-2 text-sm font-semibold text-gray-700">
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
                            ? 'border-violet-600 bg-violet-600 text-white'
                            : 'border-gray-200 bg-white text-gray-700 hover:border-violet-300'
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
                    className="mb-2 block text-sm font-semibold text-gray-700"
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
                    className="w-full rounded-xl border border-gray-200 px-4 py-3 text-sm text-gray-900 focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-200"
                  />
                </div>
              )}
            </div>
          )}

          {step === 3 && (
            <div className="space-y-6">
              <h2 className="text-lg font-bold text-gray-900">Self-Assessment</h2>

              {/* Reading speed */}
              <div>
                <label
                  htmlFor="selfRatedSpeed"
                  className="mb-2 flex items-center justify-between text-sm font-semibold text-gray-700"
                >
                  <span>How would you rate your reading speed?</span>
                  <span className="rounded-full bg-violet-100 px-3 py-0.5 text-violet-700">
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
                  className="w-full accent-violet-600"
                />
                <div className="mt-1 flex justify-between text-xs text-gray-400">
                  <span>Very slow</span>
                  <span>Very fast</span>
                </div>
              </div>

              {/* Vocabulary */}
              <div>
                <label
                  htmlFor="selfRatedVocab"
                  className="mb-2 flex items-center justify-between text-sm font-semibold text-gray-700"
                >
                  <span>How would you rate your medical vocabulary?</span>
                  <span className="rounded-full bg-violet-100 px-3 py-0.5 text-violet-700">
                    {form.selfRatedVocab}/5
                  </span>
                </label>
                <input
                  id="selfRatedVocab"
                  type="range"
                  min={1}
                  max={5}
                  value={form.selfRatedVocab}
                  onChange={(e) => update('selfRatedVocab', Number(e.target.value))}
                  className="w-full accent-violet-600"
                />
                <div className="mt-1 flex justify-between text-xs text-gray-400">
                  <span>Very limited</span>
                  <span>Very strong</span>
                </div>
              </div>

              {error && (
                <p className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
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
                className="flex-1 rounded-xl border border-gray-200 py-3 text-sm font-semibold text-gray-700 transition hover:bg-gray-50"
              >
                Back
              </button>
            )}
            {step < 3 ? (
              <button
                type="button"
                onClick={() => setStep((s) => s + 1)}
                disabled={step === 2 && !form.profession}
                className="flex-1 rounded-xl bg-violet-600 py-3 text-sm font-bold text-white transition hover:bg-violet-700 disabled:opacity-50"
              >
                Next
              </button>
            ) : (
              <button
                type="button"
                onClick={handleSubmit}
                disabled={submitting}
                className="flex-1 rounded-xl bg-violet-600 py-3 text-sm font-bold text-white transition hover:bg-violet-700 disabled:opacity-50"
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
