'use client';

import { ProfessionSelector } from "@/components/domain/profession-selector";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { Button } from "@/components/ui/button";
import { Checkbox, Input, Select } from "@/components/ui/form-controls";
import { useAnalytics } from '@/hooks/use-analytics';
import { fetchExamFamilies, fetchUserProfile, updateUserProfile } from '@/lib/api';
import { type ExamFamilyCode, type SubTest } from '@/lib/mock-data';
import { zodResolver } from '@hookform/resolvers/zod';
import { ArrowRight, Save } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { useEffect, useMemo, useState } from 'react';
import { Controller, useForm, type Resolver } from 'react-hook-form';
import { z } from 'zod';

const SUB_TESTS: SubTest[] = ['Writing', 'Speaking', 'Reading', 'Listening'];
const SCORE_FIELD_KEYS = ['targetWriting', 'targetSpeaking', 'targetReading', 'targetListening'] as const;
const EXAM_FAMILY_OPTIONS: Array<{ value: ExamFamilyCode; label: string }> = [
  { value: 'oet', label: 'OET' },
  { value: 'ielts', label: 'IELTS' },
  { value: 'pte', label: 'PTE' },
];

const EXAM_FAMILY_COPY: Record<ExamFamilyCode, { label: string; scoreHint: string; scorePlaceholder: string; attemptsLabel: string; studyLabel: string; helperText: string }> = {
  oet: {
    label: 'OET',
    scoreHint: 'OET scores use the 0 to 500 scale.',
    scorePlaceholder: 'e.g. 350',
    attemptsLabel: 'Previous OET Attempts',
    studyLabel: 'Hours per week for OET prep',
    helperText: 'OET remains the flagship experience, with profession-specific coaching and deeper review workflows.',
  },
  ielts: {
    label: 'IELTS',
    scoreHint: 'IELTS target bands currently use the shared 0 to 9 scale.',
    scorePlaceholder: 'e.g. 7',
    attemptsLabel: 'Previous IELTS Attempts',
    studyLabel: 'Hours per week for IELTS prep',
    helperText: 'IELTS uses the shared four-skill core while exam-specific surfaces continue to expand.',
  },
  pte: {
    label: 'PTE',
    scoreHint: 'PTE target scores currently use the 10 to 90 scale.',
    scorePlaceholder: 'e.g. 65',
    attemptsLabel: 'Previous PTE Attempts',
    studyLabel: 'Hours per week for PTE prep',
    helperText: 'PTE support is still limited while the distinct simulation and remediation engine is being planned.',
  },
};

const SCORE_RANGES: Record<ExamFamilyCode, { min: number; max: number; label: string }> = {
  oet: { min: 0, max: 500, label: '0-500' },
  ielts: { min: 0, max: 9, label: '0-9' },
  pte: { min: 10, max: 90, label: '10-90' },
};

const scoreField = z.union([z.coerce.number(), z.literal('')]).optional();

const goalSchema = z.object({
  examFamilyCode: z.enum(['oet', 'ielts', 'pte']),
  profession: z.string().min(1, 'Please select your profession'),
  examDate: z.string().optional(),
  targetWriting: scoreField,
  targetSpeaking: scoreField,
  targetReading: scoreField,
  targetListening: scoreField,
  previousAttempts: z.union([z.coerce.number().min(0).max(20), z.literal('')]).optional(),
  weakSubTests: z.array(z.string()).optional(),
  studyHoursPerWeek: z.union([z.coerce.number().min(1, 'At least 1 hour').max(60), z.literal('')]).optional(),
  targetCountry: z.string().optional(),
}).superRefine((data, ctx) => {
  const range = SCORE_RANGES[data.examFamilyCode];

  for (const fieldName of SCORE_FIELD_KEYS) {
    const value = data[fieldName];
    if (value === '' || value === undefined) {
      continue;
    }

    const numericValue = Number(value);
    if (!Number.isFinite(numericValue) || numericValue < range.min || numericValue > range.max) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: [fieldName],
        message: `Enter a score in the ${range.label} range for ${EXAM_FAMILY_COPY[data.examFamilyCode].label}.`,
      });
    }
  }
});

type GoalFormData = z.infer<typeof goalSchema>;

const COUNTRIES = [
  { value: 'australia', label: 'Australia' },
  { value: 'uk', label: 'United Kingdom' },
  { value: 'new-zealand', label: 'New Zealand' },
  { value: 'ireland', label: 'Ireland' },
  { value: 'singapore', label: 'Singapore' },
  { value: 'dubai', label: 'Dubai / UAE' },
  { value: 'other', label: 'Other' },
];

function toNullableScore(value: number | '' | undefined): number | null {
  if (value === '' || value === undefined) {
    return null;
  }

  return Number(value);
}

export default function GoalSetupPage() {
  const router = useRouter();
  const { track } = useAnalytics();
  const [saving, setSaving] = useState(false);
  const [examFamilyOptions, setExamFamilyOptions] = useState(EXAM_FAMILY_OPTIONS);

  const {
    register,
    handleSubmit,
    control,
    reset,
    watch,
    formState: { errors },
  } = useForm<GoalFormData>({
    // Zod v4 + @hookform/resolvers still ships a loose resolver signature that
    // doesn't line up perfectly with react-hook-form's Resolver<T>. Narrow the
    // cast through `unknown` so we at least pin the target shape, and keep the
    // entire expression typed to Resolver<GoalFormData>.
    resolver: zodResolver(goalSchema) as unknown as Resolver<GoalFormData>,
    defaultValues: {
      examFamilyCode: 'oet',
      profession: '',
      examDate: '',
      weakSubTests: [],
      studyHoursPerWeek: '',
      targetCountry: '',
    },
  });

  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        const [profile, examFamiliesResponse] = await Promise.all([
          fetchUserProfile(),
          fetchExamFamilies().catch(() => null),
        ]);

        if (cancelled) return;

        const remoteOptions = Array.isArray((examFamiliesResponse as { examFamilies?: unknown[] } | null)?.examFamilies)
          ? ((examFamiliesResponse as { examFamilies: Array<Record<string, unknown>> }).examFamilies
              .map((item) => {
                const value = String(item.code ?? '').trim().toLowerCase();
                const label = String(item.label ?? item.code ?? '').trim();
                if (!value || !label) {
                  return null;
                }

                return { value: value as ExamFamilyCode, label };
              })
              .filter((item): item is { value: ExamFamilyCode; label: string } => item !== null))
          : [];

        if (remoteOptions.length > 0) {
          setExamFamilyOptions(remoteOptions);
        }

        reset({
          examFamilyCode: profile.examFamilyCode ?? 'oet',
          profession: profile.profession || '',
          examDate: profile.examDate || '',
          targetWriting: profile.targetScores.Writing ?? '',
          targetSpeaking: profile.targetScores.Speaking ?? '',
          targetReading: profile.targetScores.Reading ?? '',
          targetListening: profile.targetScores.Listening ?? '',
          previousAttempts: profile.previousAttempts > 0 ? profile.previousAttempts : '',
          weakSubTests: profile.weakSubTests ?? [],
          studyHoursPerWeek: profile.studyHoursPerWeek > 0 ? profile.studyHoursPerWeek : '',
          targetCountry: profile.targetCountry || '',
        });
      } catch {
        // Leave defaults if profile bootstrap fails.
      }
    })();

    return () => { cancelled = true; };
  }, [reset]);

  const selectedExamFamily = (watch('examFamilyCode') ?? 'oet') as ExamFamilyCode;
  const examFamilyCopy = useMemo(
    () => EXAM_FAMILY_COPY[selectedExamFamily] ?? EXAM_FAMILY_COPY.oet,
    [selectedExamFamily],
  );
  const scoreRange = useMemo(
    () => SCORE_RANGES[selectedExamFamily] ?? SCORE_RANGES.oet,
    [selectedExamFamily],
  );

  const onSubmit = async (data: GoalFormData) => {
    setSaving(true);
    try {
      const targetScores: Record<SubTest, number | null> = {
        Writing: toNullableScore(data.targetWriting),
        Speaking: toNullableScore(data.targetSpeaking),
        Reading: toNullableScore(data.targetReading),
        Listening: toNullableScore(data.targetListening),
      };

      await updateUserProfile({
        examFamilyCode: data.examFamilyCode as ExamFamilyCode,
        profession: data.profession,
        examDate: data.examDate || null,
        targetScores,
        previousAttempts: data.previousAttempts === '' || data.previousAttempts === undefined ? 0 : Number(data.previousAttempts),
        weakSubTests: (data.weakSubTests ?? []) as SubTest[],
        studyHoursPerWeek: data.studyHoursPerWeek === '' || data.studyHoursPerWeek === undefined ? 10 : Number(data.studyHoursPerWeek),
        targetCountry: data.targetCountry || '',
        goalsComplete: true,
      });

      track('goals_saved', { examFamilyCode: data.examFamilyCode });
      router.push('/diagnostic');
    } finally {
      setSaving(false);
    }
  };

  return (
    <LearnerDashboardShell pageTitle="Set Your Goals">
      <div className="space-y-8">
        <div>
          <h1 className="text-2xl font-bold text-navy">Set your study goals</h1>
          <p className="text-muted mt-1">
            Help us personalise your study plan. All fields are optional except profession - you can update them anytime in Settings.
          </p>
        </div>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-8">
          <section className="space-y-4">
            <h2 className="text-lg font-semibold text-navy">Exam Focus</h2>
            <Select
              label="Exam Family"
              options={examFamilyOptions}
              placeholder="Select exam family..."
              {...register('examFamilyCode')}
              error={errors.examFamilyCode?.message}
            />
            <p className="text-sm text-muted">{examFamilyCopy.helperText}</p>
          </section>

          <section className="space-y-3">
            <h2 className="text-lg font-semibold text-navy">Profession</h2>
            <Controller
              name="profession"
              control={control}
              render={({ field }) => (
                <ProfessionSelector
                  value={field.value}
                  onChange={field.onChange}
                />
              )}
            />
            {errors.profession && (
              <p className="text-xs text-danger">{errors.profession.message}</p>
            )}
          </section>

          <section className="space-y-4">
            <h2 className="text-lg font-semibold text-navy">Exam Details</h2>
            <div className="grid gap-4 sm:grid-cols-2">
              <Input
                label="Exam Date"
                type="date"
                hint="Leave blank if not yet scheduled"
                {...register('examDate')}
                error={errors.examDate?.message}
              />
              <Select
                label="Target Country"
                options={COUNTRIES}
                placeholder="Select country..."
                {...register('targetCountry')}
                error={errors.targetCountry?.message}
              />
            </div>
            <Input
              label={examFamilyCopy.attemptsLabel}
              type="number"
              min={0}
              max={20}
              placeholder="0"
              {...register('previousAttempts')}
              error={errors.previousAttempts?.message}
            />
          </section>

          <section className="space-y-4">
            <h2 className="text-lg font-semibold text-navy">Target Scores (optional)</h2>
            <p className="text-sm text-muted">{examFamilyCopy.scoreHint} Leave blank if you are not ready to set a target yet.</p>
            <div className="grid gap-4 grid-cols-2 sm:grid-cols-4">
              {SUB_TESTS.map((subTest) => (
                <Input
                  key={subTest}
                  label={subTest}
                  type="number"
                  min={scoreRange.min}
                  max={scoreRange.max}
                  placeholder={examFamilyCopy.scorePlaceholder}
                  {...register(`target${subTest}` as keyof GoalFormData)}
                  error={
                    subTest === 'Writing'
                      ? errors.targetWriting?.message
                      : subTest === 'Speaking'
                        ? errors.targetSpeaking?.message
                        : subTest === 'Reading'
                          ? errors.targetReading?.message
                          : errors.targetListening?.message
                  }
                />
              ))}
            </div>
          </section>

          <section className="space-y-3">
            <h2 className="text-lg font-semibold text-navy">Which sub-tests feel hardest?</h2>
            <div className="grid gap-2 grid-cols-2">
              {SUB_TESTS.map((subTest) => (
                <Checkbox
                  key={subTest}
                  label={subTest}
                  value={subTest}
                  {...register('weakSubTests')}
                />
              ))}
            </div>
          </section>

          <section className="space-y-3">
            <h2 className="text-lg font-semibold text-navy">Study Commitment</h2>
            <Input
              label={examFamilyCopy.studyLabel}
              type="number"
              min={1}
              max={60}
              placeholder="e.g. 10"
              hint="This helps us pace your study plan."
              {...register('studyHoursPerWeek')}
              error={errors.studyHoursPerWeek?.message}
            />
          </section>

          <div className="flex flex-col sm:flex-row gap-3 pt-4 border-t">
            <Button type="submit" variant="primary" loading={saving} fullWidth className="sm:w-auto">
              <Save className="w-4 h-4 mr-2" />
              Save & Continue to Diagnostic
              <ArrowRight className="w-4 h-4 ml-2" />
            </Button>
          </div>
        </form>
      </div>
    </LearnerDashboardShell>
  );
}
