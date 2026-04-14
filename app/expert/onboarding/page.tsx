'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { motion, AnimatePresence } from 'motion/react';
import {
  ArrowLeft,
  ArrowRight,
  Award,
  BookOpen,
  CalendarClock,
  CheckCircle2,
  CreditCard,
  FileCheck,
  User,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Stepper } from '@/components/ui/stepper';
import { Input, Textarea, Select } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import {
  completeExpertOnboarding,
  fetchExpertOnboardingStatus,
  fetchExpertSchedule,
  isApiError,
  saveExpertOnboardingProfile,
  saveExpertOnboardingQualifications,
  saveExpertOnboardingRates,
  saveExpertSchedule,
} from '@/lib/api';
import type {
  ExpertOnboardingProfile,
  ExpertOnboardingQualifications,
  ExpertOnboardingRates,
  ExpertOnboardingStatus,
  ExpertSchedule,
  ExpertScheduleDay,
} from '@/lib/types/expert';

type WizardStep = 'welcome' | 'profile' | 'qualifications' | 'schedule' | 'rates' | 'review';

const STEP_ORDER: WizardStep[] = ['welcome', 'profile', 'qualifications', 'schedule', 'rates', 'review'];

const STEP_META = [
  { id: 'welcome', label: 'Welcome', icon: <BookOpen className="w-4 h-4" /> },
  { id: 'profile', label: 'Profile', icon: <User className="w-4 h-4" /> },
  { id: 'qualifications', label: 'Qualifications', icon: <Award className="w-4 h-4" /> },
  { id: 'schedule', label: 'Schedule', icon: <CalendarClock className="w-4 h-4" /> },
  { id: 'rates', label: 'Rates', icon: <CreditCard className="w-4 h-4" /> },
  { id: 'review', label: 'Review', icon: <FileCheck className="w-4 h-4" /> },
];

const DAY_ORDER = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'] as const;

const TIME_OPTIONS = Array.from({ length: 24 }).map((_, i) => {
  const hour = i.toString().padStart(2, '0');
  return { value: `${hour}:00`, label: `${hour}:00` };
});

const TIMEZONE_OPTIONS = [
  { value: 'UTC', label: 'UTC' },
  { value: 'Europe/London', label: 'London (GMT/BST)' },
  { value: 'America/New_York', label: 'New York (ET)' },
  { value: 'Asia/Dubai', label: 'Dubai (GST)' },
  { value: 'Asia/Kolkata', label: 'India (IST)' },
  { value: 'Australia/Sydney', label: 'Sydney (AEST)' },
];

const CURRENCY_OPTIONS = [
  { value: 'GBP', label: 'GBP (£)' },
  { value: 'USD', label: 'USD ($)' },
  { value: 'EUR', label: 'EUR (€)' },
  { value: 'AUD', label: 'AUD (A$)' },
];

function defaultScheduleDays(): Record<string, ExpertScheduleDay> {
  const days: Record<string, ExpertScheduleDay> = {};
  for (const d of DAY_ORDER) {
    days[d] = { active: false, start: '09:00', end: '17:00' };
  }
  return days;
}

export default function ExpertOnboardingPage() {
  const router = useRouter();
  const [currentStep, setCurrentStep] = useState(0);
  const [direction, setDirection] = useState(1);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [completedSteps, setCompletedSteps] = useState<Set<WizardStep>>(new Set());

  // Form state
  const [profile, setProfile] = useState<ExpertOnboardingProfile>({ displayName: '', bio: '' });
  const [qualifications, setQualifications] = useState<ExpertOnboardingQualifications>({
    qualifications: '',
    certifications: '',
    experienceYears: 0,
  });
  const [schedule, setSchedule] = useState<ExpertSchedule>({
    timezone: 'UTC',
    days: defaultScheduleDays(),
  });
  const [rates, setRates] = useState<ExpertOnboardingRates>({
    hourlyRateMinorUnits: 0,
    sessionRateMinorUnits: 0,
    currency: 'GBP',
  });
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  // Load onboarding status
  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        const [status, existingSchedule] = await Promise.all([
          fetchExpertOnboardingStatus(),
          fetchExpertSchedule().catch(() => null),
        ]);
        if (cancelled) return;

        if (status.isComplete) {
          router.replace('/expert');
          return;
        }

        // Restore saved progress
        const done = new Set<WizardStep>(status.completedSteps as WizardStep[]);
        setCompletedSteps(done);

        if (status.profile) {
          setProfile(status.profile);
        }
        if (status.qualifications) {
          setQualifications(status.qualifications);
        }
        if (status.rates) {
          setRates(status.rates);
        }
        if (existingSchedule) {
          setSchedule({
            timezone: existingSchedule.timezone || 'UTC',
            days: { ...defaultScheduleDays(), ...existingSchedule.days },
          });
        }

        // Jump to first incomplete step
        const firstIncomplete = STEP_ORDER.findIndex((s) => !done.has(s));
        if (firstIncomplete > 0) {
          setCurrentStep(firstIncomplete);
        }

        analytics.track('expert_onboarding_started', { completedSteps: done.size });
      } catch (err) {
        if (!cancelled) {
          setError(isApiError(err) ? err.userMessage : 'Unable to load onboarding status.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => { cancelled = true; };
  }, [router]);

  const stepKey = STEP_ORDER[currentStep];

  // Validation
  const validateStep = useCallback((step: WizardStep): boolean => {
    const errors: Record<string, string> = {};

    if (step === 'profile') {
      if (!profile.displayName.trim()) errors.displayName = 'Display name is required';
      if (!profile.bio.trim()) errors.bio = 'Bio is required';
    }

    if (step === 'qualifications') {
      if (!qualifications.qualifications.trim()) errors.qualifications = 'Qualifications are required';
      if (qualifications.experienceYears < 0) errors.experienceYears = 'Experience years must be 0 or more';
    }

    if (step === 'rates') {
      if (rates.hourlyRateMinorUnits <= 0) errors.hourlyRate = 'Hourly rate must be greater than 0';
      if (rates.sessionRateMinorUnits <= 0) errors.sessionRate = 'Session rate must be greater than 0';
    }

    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  }, [profile, qualifications, rates]);

  // Save step data
  const saveCurrentStep = useCallback(async (): Promise<boolean> => {
    const step = STEP_ORDER[currentStep];

    if (step === 'welcome') {
      setCompletedSteps((prev) => new Set([...prev, 'welcome']));
      return true;
    }

    if (!validateStep(step)) return false;

    setSaving(true);
    setError(null);

    try {
      if (step === 'profile') {
        await saveExpertOnboardingProfile(profile);
        analytics.track('expert_onboarding_profile_saved');
      } else if (step === 'qualifications') {
        await saveExpertOnboardingQualifications(qualifications);
        analytics.track('expert_onboarding_qualifications_saved');
      } else if (step === 'schedule') {
        await saveExpertSchedule(schedule);
        analytics.track('expert_onboarding_schedule_saved');
      } else if (step === 'rates') {
        await saveExpertOnboardingRates(rates);
        analytics.track('expert_onboarding_rates_saved');
      }

      setCompletedSteps((prev) => new Set([...prev, step]));
      return true;
    } catch (err) {
      setError(isApiError(err) ? err.userMessage : 'Unable to save. Please try again.');
      return false;
    } finally {
      setSaving(false);
    }
  }, [currentStep, profile, qualifications, schedule, rates, validateStep]);

  const goNext = useCallback(async () => {
    const saved = await saveCurrentStep();
    if (!saved) return;

    if (currentStep < STEP_ORDER.length - 1) {
      setDirection(1);
      setCurrentStep((s) => s + 1);
    }
  }, [currentStep, saveCurrentStep]);

  const goPrev = useCallback(() => {
    if (currentStep > 0) {
      setDirection(-1);
      setCurrentStep((s) => s - 1);
      setFieldErrors({});
      setError(null);
    }
  }, [currentStep]);

  const handleComplete = useCallback(async () => {
    setSaving(true);
    setError(null);
    try {
      await completeExpertOnboarding();
      analytics.track('expert_onboarding_completed');
      router.push('/expert');
    } catch (err) {
      setError(isApiError(err) ? err.userMessage : 'Unable to complete onboarding.');
    } finally {
      setSaving(false);
    }
  }, [router]);

  const updateScheduleDay = (day: string, patch: Partial<ExpertScheduleDay>) => {
    setSchedule((prev) => ({
      ...prev,
      days: { ...prev.days, [day]: { ...prev.days[day], ...patch } },
    }));
  };

  const stepperSteps = useMemo(
    () =>
      STEP_META.map((s) => ({
        id: s.id,
        label: s.label,
        icon: completedSteps.has(s.id as WizardStep) ? <CheckCircle2 className="w-4 h-4 text-success" /> : s.icon,
      })),
    [completedSteps],
  );

  if (loading) {
    return (
      <div className="flex flex-1 items-center justify-center p-8">
        <div className="w-full max-w-2xl space-y-8">
          <Skeleton className="h-8 w-full rounded-xl" />
          <Skeleton className="h-[400px] w-full rounded-2xl" />
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-1 items-center justify-center p-4 md:p-8">
      <div className="w-full max-w-2xl space-y-8">
        {/* Stepper */}
        <div className="md:hidden">
          <Stepper steps={stepperSteps} currentStep={currentStep} orientation="vertical" />
        </div>
        <div className="hidden md:block">
          <Stepper steps={stepperSteps} currentStep={currentStep} />
        </div>

        {/* Error banner */}
        {error && (
          <InlineAlert variant="error" title="Something went wrong">
            {error}
          </InlineAlert>
        )}

        {/* Step content */}
        <AnimatePresence mode="wait" custom={direction}>
          <motion.div
            key={stepKey}
            custom={direction}
            initial={{ opacity: 0, x: direction * 60 }}
            animate={{ opacity: 1, x: 0 }}
            exit={{ opacity: 0, x: direction * -60 }}
            transition={{ duration: 0.25 }}
            className="bg-surface rounded-2xl p-6 md:p-10 shadow-clinical"
          >
            {stepKey === 'welcome' && <WelcomeStep />}
            {stepKey === 'profile' && (
              <ProfileStep
                profile={profile}
                onUpdate={(patch) => {
                  setProfile((p) => ({ ...p, ...patch }));
                  setFieldErrors((e) => {
                    const next = { ...e };
                    for (const key of Object.keys(patch)) delete next[key];
                    return next;
                  });
                }}
                errors={fieldErrors}
              />
            )}
            {stepKey === 'qualifications' && (
              <QualificationsStep
                qualifications={qualifications}
                onUpdate={(patch) => {
                  setQualifications((q) => ({ ...q, ...patch }));
                  setFieldErrors((e) => {
                    const next = { ...e };
                    for (const key of Object.keys(patch)) delete next[key];
                    return next;
                  });
                }}
                errors={fieldErrors}
              />
            )}
            {stepKey === 'schedule' && (
              <ScheduleStep
                schedule={schedule}
                onTimezoneChange={(tz) => setSchedule((s) => ({ ...s, timezone: tz }))}
                onDayUpdate={updateScheduleDay}
              />
            )}
            {stepKey === 'rates' && (
              <RatesStep
                rates={rates}
                onUpdate={(patch) => {
                  setRates((r) => ({ ...r, ...patch }));
                  setFieldErrors((e) => {
                    const next = { ...e };
                    for (const key of Object.keys(patch)) delete next[key];
                    return next;
                  });
                }}
                errors={fieldErrors}
              />
            )}
            {stepKey === 'review' && (
              <ReviewStep profile={profile} qualifications={qualifications} schedule={schedule} rates={rates} />
            )}
          </motion.div>
        </AnimatePresence>

        {/* Navigation */}
        <div className="flex items-center justify-between">
          <Button
            variant="ghost"
            onClick={goPrev}
            disabled={currentStep === 0 || saving}
            className={currentStep === 0 ? 'invisible' : ''}
          >
            <ArrowLeft className="w-4 h-4 mr-2" />
            Back
          </Button>

          <span className="text-sm text-muted">
            {currentStep + 1} of {STEP_ORDER.length}
          </span>

          {stepKey === 'review' ? (
            <Button variant="primary" onClick={handleComplete} loading={saving}>
              Complete Setup
              <CheckCircle2 className="w-4 h-4 ml-2" />
            </Button>
          ) : stepKey === 'welcome' ? (
            <Button variant="primary" onClick={goNext} loading={saving}>
              Continue
              <ArrowRight className="w-4 h-4 ml-2" />
            </Button>
          ) : (
            <Button variant="primary" onClick={goNext} loading={saving}>
              Save & Continue
              <ArrowRight className="w-4 h-4 ml-2" />
            </Button>
          )}
        </div>
      </div>
    </div>
  );
}

/* ─── Step Components ─── */

function WelcomeStep() {
  return (
    <>
      <div className="w-14 h-14 rounded-xl bg-lavender flex items-center justify-center mb-6">
        <BookOpen className="w-7 h-7 text-primary" />
      </div>
      <h2 className="text-2xl font-bold text-navy mb-3">Welcome to the Expert Console</h2>
      <p className="text-muted leading-relaxed mb-6">
        Let&rsquo;s get your expert profile set up so learners can find you and you can start reviewing submissions.
        This wizard will walk you through the essential steps.
      </p>
      <ul className="space-y-3">
        {[
          'Set up your public profile and bio',
          'Add your teaching qualifications and experience',
          'Configure your weekly availability schedule',
          'Set your rates for private speaking sessions',
          'Review everything and go live',
        ].map((item) => (
          <li key={item} className="flex gap-3 items-start">
            <CheckCircle2 className="w-5 h-5 text-success flex-shrink-0 mt-0.5" />
            <span className="text-sm text-navy/80">{item}</span>
          </li>
        ))}
      </ul>
    </>
  );
}

function ProfileStep({
  profile,
  onUpdate,
  errors,
}: {
  profile: ExpertOnboardingProfile;
  onUpdate: (patch: Partial<ExpertOnboardingProfile>) => void;
  errors: Record<string, string>;
}) {
  return (
    <>
      <div className="w-14 h-14 rounded-xl bg-lavender flex items-center justify-center mb-6">
        <User className="w-7 h-7 text-primary" />
      </div>
      <h2 className="text-2xl font-bold text-navy mb-3">Your Name &amp; Profile</h2>
      <p className="text-muted leading-relaxed mb-6">
        Set up the name and bio that learners will see when they find your profile.
      </p>
      <div className="space-y-4">
        <Input
          label="Display Name"
          placeholder="Dr Jane Smith"
          value={profile.displayName}
          onChange={(e) => onUpdate({ displayName: e.target.value })}
          error={errors.displayName}
          required
        />
        <Textarea
          label="Bio"
          placeholder="Tell learners about your background, teaching style, and areas of expertise..."
          value={profile.bio}
          onChange={(e) => onUpdate({ bio: e.target.value })}
          error={errors.bio}
          rows={4}
          required
        />
      </div>
    </>
  );
}

function QualificationsStep({
  qualifications,
  onUpdate,
  errors,
}: {
  qualifications: ExpertOnboardingQualifications;
  onUpdate: (patch: Partial<ExpertOnboardingQualifications>) => void;
  errors: Record<string, string>;
}) {
  return (
    <>
      <div className="w-14 h-14 rounded-xl bg-lavender flex items-center justify-center mb-6">
        <Award className="w-7 h-7 text-primary" />
      </div>
      <h2 className="text-2xl font-bold text-navy mb-3">Teaching Qualifications</h2>
      <p className="text-muted leading-relaxed mb-6">
        Help us verify your credentials and match you with the right learners.
      </p>
      <div className="space-y-4">
        <Textarea
          label="Qualifications"
          placeholder="e.g. MBBS, OET Trainer Certification, TESOL..."
          value={qualifications.qualifications}
          onChange={(e) => onUpdate({ qualifications: e.target.value })}
          error={errors.qualifications}
          rows={3}
          required
        />
        <Textarea
          label="Certifications"
          placeholder="List relevant certifications (optional)"
          value={qualifications.certifications}
          onChange={(e) => onUpdate({ certifications: e.target.value })}
          rows={2}
        />
        <Input
          label="Years of Experience"
          type="number"
          min={0}
          max={50}
          value={qualifications.experienceYears || ''}
          onChange={(e) => onUpdate({ experienceYears: Math.max(0, parseInt(e.target.value, 10) || 0) })}
          error={errors.experienceYears}
        />
      </div>
    </>
  );
}

function ScheduleStep({
  schedule,
  onTimezoneChange,
  onDayUpdate,
}: {
  schedule: ExpertSchedule;
  onTimezoneChange: (tz: string) => void;
  onDayUpdate: (day: string, patch: Partial<ExpertScheduleDay>) => void;
}) {
  return (
    <>
      <div className="w-14 h-14 rounded-xl bg-lavender flex items-center justify-center mb-6">
        <CalendarClock className="w-7 h-7 text-primary" />
      </div>
      <h2 className="text-2xl font-bold text-navy mb-3">Weekly Availability</h2>
      <p className="text-muted leading-relaxed mb-6">
        Set the days and hours you&rsquo;re available for review work. You can adjust this anytime later.
      </p>
      <div className="space-y-4">
        <Select
          label="Timezone"
          options={TIMEZONE_OPTIONS}
          value={schedule.timezone}
          onChange={(e) => onTimezoneChange(e.target.value)}
        />
        <div className="space-y-3">
          {DAY_ORDER.map((day) => {
            const d = schedule.days[day] ?? { active: false, start: '09:00', end: '17:00' };
            return (
              <div key={day} className="flex items-center gap-3 rounded-xl border border-gray-200 bg-background-light p-3">
                <label className="flex items-center gap-2 min-w-[120px]">
                  <input
                    type="checkbox"
                    checked={d.active}
                    onChange={(e) => onDayUpdate(day, { active: e.target.checked })}
                    className="rounded border-gray-300 text-primary focus:ring-primary"
                  />
                  <span className="text-sm font-medium text-navy capitalize">{day}</span>
                </label>
                {d.active && (
                  <div className="flex items-center gap-2 text-sm">
                    <select
                      value={d.start}
                      onChange={(e) => onDayUpdate(day, { start: e.target.value })}
                      className="rounded-lg border border-gray-200 bg-surface px-2 py-1 text-sm"
                    >
                      {TIME_OPTIONS.map((t) => (
                        <option key={t.value} value={t.value}>{t.label}</option>
                      ))}
                    </select>
                    <span className="text-muted">to</span>
                    <select
                      value={d.end}
                      onChange={(e) => onDayUpdate(day, { end: e.target.value })}
                      className="rounded-lg border border-gray-200 bg-surface px-2 py-1 text-sm"
                    >
                      {TIME_OPTIONS.map((t) => (
                        <option key={t.value} value={t.value}>{t.label}</option>
                      ))}
                    </select>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </div>
    </>
  );
}

function RatesStep({
  rates,
  onUpdate,
  errors,
}: {
  rates: ExpertOnboardingRates;
  onUpdate: (patch: Partial<ExpertOnboardingRates>) => void;
  errors: Record<string, string>;
}) {
  return (
    <>
      <div className="w-14 h-14 rounded-xl bg-lavender flex items-center justify-center mb-6">
        <CreditCard className="w-7 h-7 text-primary" />
      </div>
      <h2 className="text-2xl font-bold text-navy mb-3">Session Rates</h2>
      <p className="text-muted leading-relaxed mb-6">
        Set your rates for private speaking sessions. These can be adjusted later from your profile.
      </p>
      <div className="space-y-4">
        <Select
          label="Currency"
          options={CURRENCY_OPTIONS}
          value={rates.currency}
          onChange={(e) => onUpdate({ currency: e.target.value })}
        />
        <Input
          label="Hourly Rate (in minor units, e.g. 5000 = £50.00)"
          type="number"
          min={0}
          value={rates.hourlyRateMinorUnits || ''}
          onChange={(e) => onUpdate({ hourlyRateMinorUnits: Math.max(0, parseInt(e.target.value, 10) || 0) })}
          error={errors.hourlyRate}
        />
        <Input
          label="Session Rate (in minor units, e.g. 7500 = £75.00)"
          type="number"
          min={0}
          value={rates.sessionRateMinorUnits || ''}
          onChange={(e) => onUpdate({ sessionRateMinorUnits: Math.max(0, parseInt(e.target.value, 10) || 0) })}
          error={errors.sessionRate}
        />
      </div>
    </>
  );
}

function ReviewStep({
  profile,
  qualifications,
  schedule,
  rates,
}: {
  profile: ExpertOnboardingProfile;
  qualifications: ExpertOnboardingQualifications;
  schedule: ExpertSchedule;
  rates: ExpertOnboardingRates;
}) {
  const activeDays = DAY_ORDER.filter((d) => schedule.days[d]?.active);
  const currencySymbol = rates.currency === 'GBP' ? '£' : rates.currency === 'USD' ? '$' : rates.currency === 'EUR' ? '€' : 'A$';

  return (
    <>
      <div className="w-14 h-14 rounded-xl bg-lavender flex items-center justify-center mb-6">
        <FileCheck className="w-7 h-7 text-primary" />
      </div>
      <h2 className="text-2xl font-bold text-navy mb-3">Review &amp; Complete</h2>
      <p className="text-muted leading-relaxed mb-6">
        Check everything looks right, then complete your setup to start receiving review work.
      </p>
      <div className="space-y-4">
        <SummarySection title="Profile">
          <SummaryRow label="Display Name" value={profile.displayName || '—'} />
          <SummaryRow label="Bio" value={profile.bio || '—'} />
        </SummarySection>
        <SummarySection title="Qualifications">
          <SummaryRow label="Qualifications" value={qualifications.qualifications || '—'} />
          <SummaryRow label="Certifications" value={qualifications.certifications || '—'} />
          <SummaryRow label="Experience" value={`${qualifications.experienceYears} years`} />
        </SummarySection>
        <SummarySection title="Schedule">
          <SummaryRow label="Timezone" value={schedule.timezone} />
          <SummaryRow
            label="Active Days"
            value={activeDays.length > 0 ? activeDays.map((d) => d.charAt(0).toUpperCase() + d.slice(1)).join(', ') : 'No days selected'}
          />
        </SummarySection>
        <SummarySection title="Rates">
          <SummaryRow label="Hourly Rate" value={`${currencySymbol}${(rates.hourlyRateMinorUnits / 100).toFixed(2)}`} />
          <SummaryRow label="Session Rate" value={`${currencySymbol}${(rates.sessionRateMinorUnits / 100).toFixed(2)}`} />
        </SummarySection>
      </div>
    </>
  );
}

function SummarySection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-gray-200 bg-background-light p-4">
      <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted mb-2">{title}</p>
      <div className="space-y-1">{children}</div>
    </div>
  );
}

function SummaryRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4 text-sm">
      <span className="text-muted">{label}</span>
      <span className="font-medium text-navy text-right">{value}</span>
    </div>
  );
}
