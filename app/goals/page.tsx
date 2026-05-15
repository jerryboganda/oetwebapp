'use client';

import { useRouter } from 'next/navigation';
import { useForm, Controller, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Save, ArrowRight, CalendarDays, Stethoscope, Target } from 'lucide-react';
import { Button, Card, Checkbox, Input, Select } from '@/components/ui';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero, LearnerSurfaceSectionHeader, ProfessionSelector } from '@/components/domain';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { useAnalytics } from '@/hooks/use-analytics';
import { type ExamFamilyCode, type SubTest } from '@/lib/mock-data';
import { fetchExamFamilies, fetchUserProfile, updateUserProfile } from '@/lib/api';
import { TARGET_COUNTRY_OPTIONS, isTargetCountry } from '@/lib/auth/target-countries';
import { useEffect, useMemo, useState } from 'react';

const SUB_TESTS: SubTest[] = ['Writing', 'Speaking', 'Reading', 'Listening'];
const SCORE_FIELD_KEYS = ['targetWriting', 'targetSpeaking', 'targetReading', 'targetListening'] as const;
const EXAM_FAMILY_OPTIONS: Array<{ value: ExamFamilyCode; label: string }> = [
  { value: 'oet', label: 'OET' },
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
    helperText: 'IELTS foundations are beta-gated and cannot be selected for public launch study plans yet.',
  },
  pte: {
    label: 'PTE',
    scoreHint: 'PTE target scores currently use the 10 to 90 scale.',
    scorePlaceholder: 'e.g. 65',
    attemptsLabel: 'Previous PTE Attempts',
    studyLabel: 'Hours per week for PTE prep',
    helperText: 'PTE foundations are beta-gated until a dedicated simulation and remediation engine is ready.',
  },
};

const SCORE_RANGES: Record<ExamFamilyCode, { min: number; max: number; label: string }> = {
  oet: { min: 0, max: 500, label: '0-500' },
  ielts: { min: 0, max: 9, label: '0-9' },
  pte: { min: 10, max: 90, label: '10-90' },
};

const scoreField = z.union([z.coerce.number(), z.literal('')]).optional();

const IELTS_PATHWAY_OPTIONS = [
  { value: 'academic', label: 'Academic' },
  { value: 'general', label: 'General Training' },
] as const;

const goalSchema = z.object({
  examFamilyCode: z.enum(['oet', 'ielts', 'pte']),
  ieltsPathway: z.enum(['academic', 'general']).optional(),
  profession: z.string().min(1, 'Please select your profession'),
  examDate: z.string().optional(),
  targetWriting: scoreField,
  targetSpeaking: scoreField,
  targetReading: scoreField,
  targetListening: scoreField,
  previousAttempts: z.union([z.coerce.number().min(0).max(20), z.literal('')]).optional(),
  weakSubTests: z.array(z.string()).optional(),
  studyHoursPerWeek: z.union([z.coerce.number().min(1, 'At least 1 hour').max(60), z.literal('')]).optional(),
  targetCountry: z
    .string()
    .min(2, 'Select your target country')
    .refine((value) => Boolean(isTargetCountry(value)), 'Select a valid target country'),
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

const COUNTRIES = TARGET_COUNTRY_OPTIONS.map((country) => ({
  value: country,
  label: country,
}));

const LEGACY_TARGET_COUNTRY_ALIASES: Readonly<Record<string, string>> = {
  australia: 'Australia',
  uk: 'United Kingdom',
  'united kingdom': 'United Kingdom',
  ireland: 'Ireland',
  'new-zealand': 'New Zealand',
  'new zealand': 'New Zealand',
  canada: 'Canada',
  us: 'USA',
  usa: 'USA',
  singapore: 'Other Countries',
  dubai: 'Gulf Countries',
  'dubai / uae': 'Gulf Countries',
  uae: 'Gulf Countries',
  other: 'Other Countries',
};

function normalizeGoalTargetCountry(value: string | null | undefined): string {
  const trimmed = value?.trim() ?? '';
  if (!trimmed) return '';
  if (isTargetCountry(trimmed)) return trimmed;
  return LEGACY_TARGET_COUNTRY_ALIASES[trimmed.toLowerCase()] ?? '';
}

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
              .filter((item): item is { value: ExamFamilyCode; label: string } => item !== null && item.value === 'oet'))
          : [];

        if (remoteOptions.length > 0) {
          setExamFamilyOptions(remoteOptions);
        }

        reset({
          examFamilyCode: 'oet',
          ieltsPathway: profile.ieltsPathway ?? undefined,
          profession: profile.profession || '',
          examDate: profile.examDate || '',
          targetWriting: profile.targetScores.Writing ?? '',
          targetSpeaking: profile.targetScores.Speaking ?? '',
          targetReading: profile.targetScores.Reading ?? '',
          targetListening: profile.targetScores.Listening ?? '',
          previousAttempts: profile.previousAttempts > 0 ? profile.previousAttempts : '',
          weakSubTests: profile.weakSubTests ?? [],
          studyHoursPerWeek: profile.studyHoursPerWeek > 0 ? profile.studyHoursPerWeek : '',
          targetCountry: normalizeGoalTargetCountry(profile.targetCountry),
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
        ieltsPathway: data.ieltsPathway,
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
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Goal Setup"
          icon={Target}
          accent="primary"
          title="Set the signals your study plan should follow"
          description="Tell the platform your exam, profession, target country, and weekly commitment so diagnostics and study-plan pacing stay relevant."
          highlights={[
            { icon: Stethoscope, label: 'Profession', value: watch('profession') || 'Required' },
            { icon: CalendarDays, label: 'Exam date', value: watch('examDate') || 'Not scheduled' },
            { icon: Target, label: 'Target country', value: watch('targetCountry') || 'Required' },
          ]}
        />

        <LearnerSkillSwitcher compact />

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-6">
          <Card className="space-y-4">
            <LearnerSurfaceSectionHeader
              title="Exam Focus"
              description="Choose the exam family that should shape score ranges and preparation copy."
            />
            <Select
              label="Exam Family"
              options={examFamilyOptions}
              placeholder="Select exam family..."
              {...register('examFamilyCode')}
              error={errors.examFamilyCode?.message}
            />
            <InlineAlert variant="info">
              IELTS and PTE remain beta-gated foundations. OET is the only public-launch study-plan exam family.
            </InlineAlert>
            {selectedExamFamily === 'ielts' && (
              <Select
                label="IELTS Pathway"
                options={[...IELTS_PATHWAY_OPTIONS]}
                placeholder="Select Academic or General Training..."
                {...register('ieltsPathway')}
                error={errors.ieltsPathway?.message}
              />
            )}
            <p className="text-sm text-muted">{examFamilyCopy.helperText}</p>
          </Card>

          <Card className="space-y-3">
            <LearnerSurfaceSectionHeader
              title="Profession"
              description="Profession controls the OET context used throughout learner practice."
            />
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
          </Card>

          <Card className="space-y-4">
            <LearnerSurfaceSectionHeader
              title="Exam Details"
              description="These details help prioritize readiness, review timing, and the diagnostic route."
            />
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
          </Card>

          <Card className="space-y-4">
            <LearnerSurfaceSectionHeader
              title="Target Scores (optional)"
              description={examFamilyCopy.scoreHint + ' Leave blank if you are not ready to set a target yet.'}
            />
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
          </Card>

          <Card className="space-y-3">
            <LearnerSurfaceSectionHeader
              title="Which sub-tests feel hardest?"
              description="This helps surface practice recommendations before enough scored evidence exists."
            />
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
          </Card>

          <Card className="space-y-3">
            <LearnerSurfaceSectionHeader
              title="Study Commitment"
              description="Weekly study hours pace the study plan so recommendations stay realistic."
            />
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
          </Card>

          <Card className="flex flex-col gap-4 border-primary/20 bg-primary/5 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <p className="text-sm font-bold text-navy">Ready to turn goals into evidence?</p>
              <p className="mt-1 text-sm text-muted">Saving sends you to the diagnostic flow so your plan can start with a baseline.</p>
            </div>
            <Button type="submit" variant="primary" loading={saving} fullWidth className="sm:w-auto">
              <Save className="w-4 h-4 mr-2" />
              Save & Continue to Diagnostic
              <ArrowRight className="w-4 h-4 ml-2" />
            </Button>
          </Card>
        </form>
      </div>
    </LearnerDashboardShell>
  );
}
