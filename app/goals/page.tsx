'use client';

import { useRouter } from 'next/navigation';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Save, ArrowRight } from 'lucide-react';
import { Button, Input, Select, Checkbox } from '@/components/ui';
import { ProfessionSelector } from '@/components/domain';
import { LearnerDashboardShell } from '@/components/layout';
import { useAnalytics } from '@/hooks/use-analytics';
import { type SubTest } from '@/lib/mock-data';
import { fetchUserProfile, updateUserProfile } from '@/lib/api';
import { useEffect, useState } from 'react';

const SUB_TESTS: SubTest[] = ['Writing', 'Speaking', 'Reading', 'Listening'];

const goalSchema = z.object({
  profession: z.string().min(1, 'Please select your profession'),
  examDate: z.string().optional(),
  targetWriting: z.union([z.coerce.number().min(0).max(500), z.literal('')]).optional(),
  targetSpeaking: z.union([z.coerce.number().min(0).max(500), z.literal('')]).optional(),
  targetReading: z.union([z.coerce.number().min(0).max(500), z.literal('')]).optional(),
  targetListening: z.union([z.coerce.number().min(0).max(500), z.literal('')]).optional(),
  previousAttempts: z.union([z.coerce.number().min(0).max(20), z.literal('')]).optional(),
  weakSubTests: z.array(z.string()).optional(),
  studyHoursPerWeek: z.union([z.coerce.number().min(1, 'At least 1 hour').max(60), z.literal('')]).optional(),
  targetCountry: z.string().optional(),
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

export default function GoalSetupPage() {
  const router = useRouter();
  const { track } = useAnalytics();
  const [saving, setSaving] = useState(false);

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors },
  } = useForm<GoalFormData>({
    resolver: zodResolver(goalSchema) as any,
    defaultValues: {
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
        const profile = await fetchUserProfile();
        if (cancelled) return;
        reset({
          profession: profile.profession || '',
          examDate: profile.examDate || '',
          targetWriting: profile.targetScores.Writing ?? '',
          targetSpeaking: profile.targetScores.Speaking ?? '',
          targetReading: profile.targetScores.Reading ?? '',
          targetListening: profile.targetScores.Listening ?? '',
          previousAttempts: profile.previousAttempts || '',
          weakSubTests: profile.weakSubTests ?? [],
          studyHoursPerWeek: profile.studyHoursPerWeek || '',
          targetCountry: profile.targetCountry || '',
        });
      } catch {
        // Leave defaults if profile bootstrap fails.
      }
    })();

    return () => { cancelled = true; };
  }, [reset]);

  const onSubmit = async (data: GoalFormData) => {
    setSaving(true);
    try {
      const targetScores: Record<SubTest, number | null> = {
        Writing: data.targetWriting ? Number(data.targetWriting) : null,
        Speaking: data.targetSpeaking ? Number(data.targetSpeaking) : null,
        Reading: data.targetReading ? Number(data.targetReading) : null,
        Listening: data.targetListening ? Number(data.targetListening) : null,
      };
      await updateUserProfile({
        profession: data.profession,
        examDate: data.examDate || null,
        targetScores,
        previousAttempts: data.previousAttempts ? Number(data.previousAttempts) : 0,
        weakSubTests: (data.weakSubTests ?? []) as SubTest[],
        studyHoursPerWeek: data.studyHoursPerWeek ? Number(data.studyHoursPerWeek) : 10,
        targetCountry: data.targetCountry || '',
        goalsComplete: true,
      });
      track('goals_saved');
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
            Help us personalise your study plan. All fields are optional except profession — you can update them anytime in Settings.
          </p>
        </div>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-8">
          {/* Profession */}
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
              <p className="text-xs text-red-600">{errors.profession.message}</p>
            )}
          </section>

          {/* Exam details */}
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
              label="Previous OET Attempts"
              type="number"
              min={0}
              max={20}
              placeholder="0"
              {...register('previousAttempts')}
              error={errors.previousAttempts?.message}
            />
          </section>

          {/* Target scores */}
          <section className="space-y-4">
            <h2 className="text-lg font-semibold text-navy">Target Scores (optional)</h2>
            <p className="text-sm text-muted">OET scores range from 0 to 500. Leave blank if unsure.</p>
            <div className="grid gap-4 grid-cols-2 sm:grid-cols-4">
              {SUB_TESTS.map((st) => (
                <Input
                  key={st}
                  label={st}
                  type="number"
                  min={0}
                  max={500}
                  placeholder="e.g. 350"
                  {...register(`target${st}` as keyof GoalFormData)}
                />
              ))}
            </div>
          </section>

          {/* Weak sub-tests */}
          <section className="space-y-3">
            <h2 className="text-lg font-semibold text-navy">Which sub-tests feel hardest?</h2>
            <div className="grid gap-2 grid-cols-2">
              {SUB_TESTS.map((st) => (
                <Checkbox
                  key={st}
                  label={st}
                  value={st}
                  {...register('weakSubTests')}
                />
              ))}
            </div>
          </section>

          {/* Study hours */}
          <section className="space-y-3">
            <h2 className="text-lg font-semibold text-navy">Study Commitment</h2>
            <Input
              label="Hours per week for OET prep"
              type="number"
              min={1}
              max={60}
              placeholder="e.g. 10"
              hint="This helps us pace your study plan"
              {...register('studyHoursPerWeek')}
              error={errors.studyHoursPerWeek?.message}
            />
          </section>

          {/* Actions */}
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
