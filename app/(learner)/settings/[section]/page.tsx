'use client';

import { useEffect, useMemo, useState, type ChangeEvent } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Accessibility,
  ArrowLeft,
  Bell,
  BellRing,
  BookOpen,
  BriefcaseMedical,
  Calendar,
  CalendarClock,
  Clock3,
  Contrast,
  Database,
  Globe2,
  Headphones,
  Keyboard,
  Loader2,
  Lock,
  Mail,
  MessageCircle,
  MessageSquareMore,
  Mic,
  NotebookText,
  Save,
  Settings2,
  Shield,
  ShieldCheck,
  SlidersHorizontal,
  Sparkles,
  Target,
  Trash2,
  Type,
  User,
  UserCircle2,
  Volume2,
  Wifi,
  type LucideIcon,
} from 'lucide-react';
import { InlineAlert } from '@/components/ui/alert';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Button, buttonClassName } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { Switch } from '@/components/ui/switch';
import { UserAvatar } from '@/components/ui/user-avatar';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import { ApiError, fetchSettingsSection, updateMyAvatar, updateSettingsSection, uploadMedia } from '@/lib/api';
import { TARGET_COUNTRY_OPTIONS } from '@/lib/auth/target-countries';
import { deleteAccount } from '@/lib/auth-client';
import { fetchSupportWhatsApp, normalizeWhatsAppNumber, PLATFORM_WHATSAPP } from '@/lib/billing/whatsapp';
import { useProfessions } from '@/lib/hooks/use-professions';
import type { LearnerSurfaceAccent } from '@/lib/learner-surface';
import type { SettingsSectionData, SettingsSectionId } from '@/lib/mock-data';
import { queryKeys } from '@/lib/query/hooks';
import { cn } from '@/lib/utils';
import { useAuth } from '@/contexts/auth-context';

type FieldType = 'text' | 'email' | 'number' | 'date' | 'select' | 'textarea' | 'toggle';
type FieldTagTone = 'section' | 'muted' | 'writing' | 'speaking' | 'reading' | 'listening' | 'study';
type EditableSettingsSectionId = Parameters<typeof updateSettingsSection>[0];

interface FieldConfig {
  key: string;
  label: string;
  type: FieldType;
  description: string;
  icon: LucideIcon;
  primaryTag: string;
  secondaryTag?: string;
  primaryTagTone?: FieldTagTone;
  secondaryTagTone?: FieldTagTone;
  options?: SelectOption[];
  min?: number;
  max?: number;
}

interface SectionConfig {
  title: string;
  heroTitle?: string;
  description: string;
  eyebrow: string;
  icon: LucideIcon;
  accent: LearnerSurfaceAccent;
  helperBadge: string;
  helperCardTitle: string;
  helperCardBody: string;
  fields: FieldConfig[];
}

/** Section accent: the icon tiles and the section-toned field tags. */
const accentStyles: Record<LearnerSurfaceAccent, { icon: string; badge: string }> = {
  primary: { icon: 'bg-primary/10 text-primary', badge: 'border-primary/20 bg-primary/10 text-primary' },
  navy: { icon: 'bg-navy/10 text-navy', badge: 'border-navy/20 bg-navy/10 text-navy' },
  amber: { icon: 'bg-warning/10 text-warning-strong', badge: 'border-warning/20 bg-warning/10 text-warning-strong' },
  blue: { icon: 'bg-info/10 text-info', badge: 'border-info/20 bg-info/10 text-info' },
  indigo: { icon: 'bg-lavender text-primary-dark', badge: 'border-primary/20 bg-lavender text-primary-dark' },
  purple: { icon: 'bg-lavender text-primary-dark', badge: 'border-primary/20 bg-lavender text-primary-dark' },
  rose: { icon: 'bg-danger/10 text-danger-strong', badge: 'border-danger/20 bg-danger/10 text-danger-strong' },
  emerald: { icon: 'bg-success/10 text-success-strong', badge: 'border-success/20 bg-success/10 text-success-strong' },
  slate: { icon: 'bg-background-light text-navy', badge: 'border-border bg-background-light text-navy' },
  listening: { icon: 'bg-skill-listening/10 text-skill-listening', badge: 'border-skill-listening/20 bg-skill-listening/10 text-skill-listening' },
  reading: { icon: 'bg-skill-reading/10 text-skill-reading', badge: 'border-skill-reading/20 bg-skill-reading/10 text-skill-reading' },
  writing: { icon: 'bg-skill-writing/10 text-skill-writing', badge: 'border-skill-writing/20 bg-skill-writing/10 text-skill-writing' },
  speaking: { icon: 'bg-skill-speaking/10 text-skill-speaking', badge: 'border-skill-speaking/20 bg-skill-speaking/10 text-skill-speaking' },
};

/** Sub-test tags wear their skill identity colour, never a status colour. */
const tagToneStyles: Record<Exclude<FieldTagTone, 'section'>, string> = {
  muted: 'border-border bg-background-light text-muted',
  writing: 'border-skill-writing/20 bg-skill-writing/10 text-skill-writing',
  speaking: 'border-skill-speaking/20 bg-skill-speaking/10 text-skill-speaking',
  reading: 'border-skill-reading/20 bg-skill-reading/10 text-skill-reading',
  listening: 'border-skill-listening/20 bg-skill-listening/10 text-skill-listening',
  study: 'border-warning/20 bg-warning/10 text-warning-strong',
};

const SECTION_CONFIG: Record<SettingsSectionId, SectionConfig> = {
  profile: {
    title: 'Profile',
    heroTitle: 'Your Profile',
    description: 'Manage your name, email, profession, and identity details.',
    eyebrow: 'Account & Identity',
    icon: User,
    accent: 'blue',
    helperBadge: 'Identity',
    helperCardTitle: 'Personal information',
    helperCardBody: 'Your identity details used across the platform.',
    fields: [
      {
        key: 'displayName',
        label: 'Display Name',
        type: 'text',
        description: 'Shown across the learner workspace and report surfaces.',
        icon: UserCircle2,
        primaryTag: '',
        secondaryTag: '',
        secondaryTagTone: 'muted',
      },
      {
        key: 'email',
        label: 'Email',
        type: 'email',
        description: 'Used for sign-in, review updates, and billing communication.',
        icon: Mail,
        primaryTag: '',
        secondaryTag: '',
        secondaryTagTone: 'muted',
      },
      {
        key: 'professionId',
        label: 'Profession',
        type: 'select',
        description: 'Decides which videos, materials, and writing and speaking cases you get. Locked once you buy a package.',
        icon: BriefcaseMedical,
        primaryTag: '',
        secondaryTag: '',
        secondaryTagTone: 'muted',
        // No static options: the choices come from the signup catalog via
        // useProfessions(), which is the same list the backend validates against.
      },
    ],
  },
  privacy: {
    title: 'Privacy & Data',
    description: 'Review and manage the recordings and data we hold about you.',
    eyebrow: 'Privacy Controls',
    icon: Shield,
    accent: 'rose',
    helperBadge: 'Your Data',
    helperCardTitle: 'Recordings & data',
    helperCardBody: 'Your speaking recordings are auto-deleted on a retention schedule and only accessed by reviewers with an audited reason. You can review or delete individual recordings yourself, and request full erasure of your account and data at any time.',
    // No toggles here: recording retention, consent, and reviewer access are
    // governed by the compliance system (consent records + retention workers +
    // audited access) and surfaced through the real controls below, not by
    // free-standing preference switches that would give a false sense of control.
    fields: [],
  },
  notifications: {
    title: 'Notifications',
    description: 'Choose how you receive study reminders, review updates, and billing alerts.',
    eyebrow: 'Learner Notifications',
    icon: Bell,
    accent: 'indigo',
    helperBadge: 'Reminders',
    helperCardTitle: 'Notification preferences',
    helperCardBody: 'Control what alerts you receive and how often.',
    fields: [
      {
        key: 'emailReminders',
        label: 'Email reminders',
        type: 'toggle',
        description: 'Send study-plan reminders and goal pacing prompts by email.',
        icon: BellRing,
        primaryTag: 'Email',
        secondaryTag: 'Study pacing',
        secondaryTagTone: 'muted',
      },
      {
        key: 'reviewUpdates',
        label: 'Tutor review updates',
        type: 'toggle',
        description: 'Notify the learner when tutor review status changes.',
        icon: MessageSquareMore,
        primaryTag: 'Review Updates',
        secondaryTag: 'Status changes',
        secondaryTagTone: 'muted',
      },
      {
        key: 'reminderCadence',
        label: 'Reminder cadence',
        type: 'select',
        description: 'How often the learner wants practice reminders.',
        icon: Clock3,
        primaryTag: 'Cadence',
        secondaryTag: 'Delivery timing',
        secondaryTagTone: 'muted',
        options: [
          { value: 'off', label: 'Off' },
          { value: 'daily', label: 'Daily' },
          { value: 'weekly', label: 'Weekly' },
        ],
      },
    ],
  },
  audio: {
    title: 'Audio Preferences',
    description: 'Manage playback defaults, transcript support, and low-bandwidth settings for audio content.',
    eyebrow: 'Audio & Playback',
    icon: Volume2,
    accent: 'amber',
    helperBadge: 'Playback',
    helperCardTitle: 'Audio settings',
    helperCardBody: 'These settings affect listening and speaking playback, especially on slower networks.',
    fields: [
      {
        key: 'playbackSpeed',
        label: 'Default playback speed',
        type: 'select',
        description: 'Used when a practice flow supports replay.',
        icon: SlidersHorizontal,
        primaryTag: 'Playback',
        secondaryTag: 'Replay support',
        secondaryTagTone: 'muted',
        options: [
          { value: '0.75', label: '0.75x' },
          { value: '1.0', label: '1.0x' },
          { value: '1.25', label: '1.25x' },
        ],
      },
      {
        key: 'lowBandwidthMode',
        label: 'Low-bandwidth mode',
        type: 'toggle',
        description: 'Reduce preloading and heavy media fetches on slower connections.',
        icon: Wifi,
        primaryTag: 'Performance',
        secondaryTag: 'Network aware',
        secondaryTagTone: 'muted',
      },
    ],
  },
  accessibility: {
    title: 'Accessibility',
    description: 'Adjust reading, audio, and navigation to suit your needs.',
    eyebrow: 'Accessibility',
    icon: Accessibility,
    accent: 'emerald',
    helperBadge: 'Accessibility',
    helperCardTitle: 'Accessibility options',
    helperCardBody: 'These settings help make the platform more comfortable for your needs.',
    fields: [
      {
        key: 'largeText',
        label: 'Large text mode',
        type: 'toggle',
        description: 'Increase text size for learner-facing content.',
        icon: Type,
        primaryTag: 'Typography',
        secondaryTag: 'Readable text',
        secondaryTagTone: 'muted',
      },
      {
        key: 'highContrast',
        label: 'High-contrast emphasis',
        type: 'toggle',
        description: 'Strengthen contrast on instructional surfaces and cards.',
        icon: Contrast,
        primaryTag: 'Contrast',
        secondaryTag: 'Instructional cards',
        secondaryTagTone: 'muted',
      },
      {
        key: 'reduceMotion',
        label: 'Reduce motion',
        type: 'toggle',
        description: 'Minimize animation on dashboards and review flows.',
        icon: Sparkles,
        primaryTag: 'Motion',
        secondaryTag: 'Calmer transitions',
        secondaryTagTone: 'muted',
      },
      {
        key: 'keyboardHints',
        label: 'Keyboard hints',
        type: 'toggle',
        description: 'Show extra keyboard guidance where task flows are complex.',
        icon: Keyboard,
        primaryTag: 'Keyboard',
        secondaryTag: 'Task guidance',
        secondaryTagTone: 'muted',
      },
    ],
  },
  'danger-zone': {
    title: 'Delete Account',
    description: 'Permanently delete your account and all associated data.',
    eyebrow: 'Danger Zone',
    icon: Trash2,
    accent: 'rose',
    helperBadge: 'Irreversible',
    helperCardTitle: 'Account deletion',
    helperCardBody: 'This action schedules your account for permanent deletion after a 30-day grace period.',
    fields: [],
  },
  study: {
    title: 'Exam Date & Study Preferences',
    description: 'Set your exam date, study volume, and target country to personalise your study plan.',
    eyebrow: 'Study Preferences',
    icon: Calendar,
    accent: 'navy',
    helperBadge: 'Study Planning',
    helperCardTitle: 'Study planning',
    helperCardBody: 'These values influence your readiness score, study-plan pacing, and reminder timing.',
    fields: [
      {
        key: 'targetExamDate',
        label: 'Target exam date',
        type: 'date',
        description: 'Used by readiness and study-plan pacing.',
        icon: CalendarClock,
        primaryTag: 'Readiness',
        secondaryTag: 'Exam window',
        secondaryTagTone: 'muted',
      },
      {
        key: 'studyHoursPerWeek',
        label: 'Study hours per week',
        type: 'number',
        description: 'Drives pacing and checkpoint intensity.',
        icon: Clock3,
        primaryTag: 'Study Plan',
        secondaryTag: 'Pacing signal',
        secondaryTagTone: 'muted',
        min: 1,
        max: 60,
      },
      {
        key: 'targetCountry',
        label: 'Target country',
        type: 'select',
        description: 'Used for country-aware writing thresholds and study planning.',
        icon: Globe2,
        primaryTag: 'Destination',
        secondaryTag: 'Required',
        secondaryTagTone: 'muted',
        options: TARGET_COUNTRY_OPTIONS.map((country) => ({ value: country, label: country })),
      },
      {
        key: 'reminderCadence',
        label: 'Study reminder cadence',
        type: 'select',
        description: 'Controls how often the learner is prompted to stay on plan.',
        icon: BellRing,
        primaryTag: 'Reminders',
        secondaryTag: 'Prompt timing',
        secondaryTagTone: 'muted',
        options: [
          { value: 'daily', label: 'Daily' },
          { value: 'weekly', label: 'Weekly' },
          { value: 'off', label: 'Off' },
        ],
      },
    ],
  },
  goals: {
    title: 'Goals',
    description: 'Set score targets and identify focus areas so your study plan stays directed.',
    eyebrow: 'Target Scores',
    icon: Settings2,
    accent: 'purple',
    helperBadge: 'Target Scores',
    helperCardTitle: 'Your targets',
    helperCardBody: 'Score targets and skill focus help keep your study direction clear, especially for Writing and Speaking.',
    fields: [
      {
        key: 'overallGoal',
        label: 'Overall goal',
        type: 'textarea',
        description: 'Short narrative summary of what the learner is aiming for.',
        icon: Target,
        primaryTag: 'Direction',
        primaryTagTone: 'section',
        secondaryTag: 'Goal summary',
        secondaryTagTone: 'muted',
      },
      {
        key: 'targetScoresBySubtest.writing',
        label: 'Writing target score',
        type: 'number',
        description: 'Target OET score for Writing.',
        icon: NotebookText,
        primaryTag: 'Writing',
        primaryTagTone: 'writing',
        secondaryTag: 'Score target',
        secondaryTagTone: 'muted',
        min: 0,
        max: 500,
      },
      {
        key: 'targetScoresBySubtest.speaking',
        label: 'Speaking target score',
        type: 'number',
        description: 'Target OET score for Speaking.',
        icon: Mic,
        primaryTag: 'Speaking',
        primaryTagTone: 'speaking',
        secondaryTag: 'Score target',
        secondaryTagTone: 'muted',
        min: 0,
        max: 500,
      },
      {
        key: 'targetScoresBySubtest.reading',
        label: 'Reading target score',
        type: 'number',
        description: 'Target OET score for Reading.',
        icon: BookOpen,
        primaryTag: 'Reading',
        primaryTagTone: 'reading',
        secondaryTag: 'Score target',
        secondaryTagTone: 'muted',
        min: 0,
        max: 500,
      },
      {
        key: 'targetScoresBySubtest.listening',
        label: 'Listening target score',
        type: 'number',
        description: 'Target OET score for Listening.',
        icon: Headphones,
        primaryTag: 'Listening',
        primaryTagTone: 'listening',
        secondaryTag: 'Score target',
        secondaryTagTone: 'muted',
        min: 0,
        max: 500,
      },
      {
        key: 'studyHoursPerWeek',
        label: 'Planned weekly study hours',
        type: 'number',
        description: 'Helps study-plan generation keep pace with the target.',
        icon: Clock3,
        primaryTag: 'Study Pace',
        primaryTagTone: 'study',
        secondaryTag: 'Weekly load',
        secondaryTagTone: 'muted',
        min: 1,
        max: 60,
      },
    ],
  },
};

function readNestedValue(values: Record<string, unknown>, key: string): unknown {
  return key.split('.').reduce<unknown>((current, part) => {
    if (current && typeof current === 'object' && part in (current as Record<string, unknown>)) {
      return (current as Record<string, unknown>)[part];
    }
    return undefined;
  }, values);
}

function setNestedValue(values: Record<string, unknown>, key: string, value: unknown): Record<string, unknown> {
  const next = structuredClone(values);
  const parts = key.split('.');
  let cursor: Record<string, unknown> = next;
  for (let index = 0; index < parts.length - 1; index += 1) {
    const part = parts[index];
    const current = cursor[part];
    if (!current || typeof current !== 'object') {
      cursor[part] = {};
    }
    cursor = cursor[part] as Record<string, unknown>;
  }
  cursor[parts[parts.length - 1]] = value;
  return next;
}

function toSectionId(value: string | undefined): SettingsSectionId | null {
  if (!value) return null;
  return Object.keys(SECTION_CONFIG).includes(value) ? (value as SettingsSectionId) : null;
}

function fieldValue(values: Record<string, unknown>, field: FieldConfig): string | boolean {
  const rawValue = readNestedValue(values, field.key);
  if (field.type === 'toggle') {
    return Boolean(rawValue);
  }
  if (rawValue === null || rawValue === undefined) {
    return '';
  }
  return String(rawValue);
}

function isFieldConfigured(field: FieldConfig, value: string | boolean) {
  return field.type === 'toggle' ? Boolean(value) : String(value).trim().length > 0;
}

function renderTag(label: string, accent: LearnerSurfaceAccent, tone: FieldTagTone = 'section') {
  if (!label) return null;
  const className = tone === 'section' ? accentStyles[accent].badge : tagToneStyles[tone];

  return (
    <span key={`${label}-${tone}`} className={cn('inline-flex items-center rounded-full border px-2 py-0.5 tile-label', className)}>
      {label}
    </span>
  );
}

function fieldStatus(field: FieldConfig, value: string | boolean): { label: string; variant: BadgeProps['variant'] } {
  if (field.type === 'toggle') {
    return {
      label: Boolean(value) ? 'On' : 'Off',
      variant: Boolean(value) ? 'success' : 'muted',
    };
  }

  return {
    label: isFieldConfigured(field, value) ? 'Set' : 'Not set',
    variant: isFieldConfigured(field, value) ? 'info' : 'muted',
  };
}

interface SelectOption {
  value: string;
  label: string;
  disabled?: boolean;
}

/**
 * The profession select may only offer ids the backend will accept — a PATCH is validated
 * against the signup catalog and anything else is rejected as `unknown_profession`. The
 * catalog (via useProfessions) is therefore the only source of choices, so no hardcoded
 * list can drift out of it. A value the account already holds but the catalog no longer
 * offers — an archived or legacy id — is kept as a disabled entry so the control shows
 * what is stored instead of rendering blank, while still not offering it as a choice.
 */
function professionSelectOptions(catalog: SelectOption[], current: string): SelectOption[] {
  if (!current || catalog.some((option) => option.value === current)) return catalog;
  return [...catalog, { value: current, label: `${current} (no longer available)`, disabled: true }];
}

/**
 * Rendered under the Profession field once the backend answers a change with
 * `profession_locked`. Access is granted per profession — the videos and materials a
 * package unlocks are resolved from it — so moving it after a purchase would re-point
 * everything the learner already paid for. Only an admin can do that, hence the WhatsApp
 * hand-off rather than a retry.
 */
function ProfessionLockNotice() {
  const { user } = useAuth();
  const [supportNumber, setSupportNumber] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void fetchSupportWhatsApp().then((settings) => {
      if (!cancelled) setSupportNumber(settings.whatsAppNumber);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  // Built against the fallback number until the settings read lands, so the CTA is never
  // a dead link on first paint.
  const href = `https://wa.me/${normalizeWhatsAppNumber(supportNumber) ?? PLATFORM_WHATSAPP}?text=${encodeURIComponent(
    [
      'Hello OET team, I need to change the profession on my account.',
      '',
      `Registered email: ${user?.email ?? ''}`,
      '',
      'Please tell me what you need from me to move my access to a different profession.',
    ].join('\n'),
  )}`;

  return (
    <div className="rounded-2xl border border-warning/30 bg-warning/10 p-4 sm:p-5">
      <div className="flex items-start gap-4">
        <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-warning/30 bg-warning/15">
          <Lock className="h-5 w-5 text-warning-strong" aria-hidden="true" />
        </div>
        <div className="min-w-0 flex-1 space-y-3">
          <p className="eyebrow text-warning-strong">Profession locked</p>
          <p className="max-w-2xl text-sm leading-relaxed text-navy">
            Your videos and materials are tied to the profession you registered with, so changing it now would
            re-point every package on your account. That is why it locks after your first purchase — our team can
            move you across and re-point your access for you.
          </p>
          <a
            href={href}
            target="_blank"
            rel="noopener noreferrer"
            className="pressable inline-flex min-h-11 items-center justify-center gap-2 rounded-control bg-[#25D366] px-5 py-2.5 text-sm font-semibold text-white shadow-sm hover:brightness-95 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
          >
            <MessageCircle className="h-4 w-4" aria-hidden="true" />
            Request a change on WhatsApp
          </a>
        </div>
      </div>
    </div>
  );
}

function isNativeFieldInvalid(field: FieldConfig, value: string | boolean, sectionInvalid: boolean): boolean {
  if (field.type === 'toggle') return false;
  const empty = String(value).trim().length === 0;
  if (empty && (field.key === 'displayName' || field.key === 'email' || field.key === 'professionId' || field.secondaryTag === 'Required')) {
    return true;
  }
  return sectionInvalid;
}

function SettingsSectionForm({
  accent,
  data,
  onChange,
  professionLocked = false,
  originalEmail,
  emailPassword = '',
  onEmailPasswordChange,
  invalid = false,
}: {
  accent: LearnerSurfaceAccent;
  data: SettingsSectionData;
  onChange: (key: string, value: string | boolean) => void;
  /** Set once the backend has rejected a change with `profession_locked`. */
  professionLocked?: boolean;
  /** The account's currently-stored email (pre-edit), used to detect a real change. */
  originalEmail?: string;
  emailPassword?: string;
  onEmailPasswordChange?: (value: string) => void;
  invalid?: boolean;
}) {
  const { options: professionOptions } = useProfessions();
  const config = SECTION_CONFIG[data.section];
  const palette = accentStyles[accent];
  const isProfile = data.section === 'profile';

  return (
    <Card padding="none" className="divide-y divide-border overflow-hidden">
      {config.fields.map((field) => {
        const value = fieldValue(data.values, field);
        const status = fieldStatus(field, value);
        const FieldIcon = field.icon;
        const isToggle = field.type === 'toggle';
        const isProfessionField = field.key === 'professionId';
        const isLocked = isProfessionField && professionLocked;
        const selectOptions: SelectOption[] = isProfessionField
          ? professionSelectOptions(professionOptions, String(value))
          : (field.options ?? []);
        // H1 (security): an email change is re-authenticated with the current
        // password (see LearnerService.PatchSettingsSectionAsync) — reveal the
        // confirmation field only once the learner has actually edited it away
        // from the stored value, so every other profile field stays password-free.
        const isEmailField = field.key === 'email';
        const emailChanged = isEmailField
          && originalEmail !== undefined
          && String(value).trim().toLowerCase() !== originalEmail.trim().toLowerCase();
        const fieldInvalid = isNativeFieldInvalid(field, value, invalid);
        const passwordInvalid = emailChanged && !emailPassword.trim();

        return (
          <div key={field.key} className="p-4 sm:p-5">
            <div className={cn('flex flex-col gap-4', field.type !== 'textarea' && 'xl:flex-row xl:items-start')}>
              <div className="flex min-w-0 flex-1 items-start gap-4">
                <div className={cn('flex h-11 w-11 shrink-0 items-center justify-center rounded-xl', palette.icon)}>
                  <FieldIcon className="h-5 w-5" aria-hidden="true" />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    {isToggle ? (
                      <p className="text-base font-semibold text-navy">{field.label}</p>
                    ) : (
                      <label className="text-base font-semibold text-navy" htmlFor={field.key}>{field.label}</label>
                    )}
                    {renderTag(field.primaryTag, accent, field.primaryTagTone ?? 'section')}
                    {/* Only "Required" carries meaning; the other secondary tags repeated the label. */}
                    {field.secondaryTag === 'Required' ? renderTag(field.secondaryTag, accent, field.secondaryTagTone ?? 'muted') : null}
                    {!isToggle ? (
                      <Badge variant={isLocked ? 'muted' : status.variant}>
                        {isLocked ? 'Locked' : status.label}
                      </Badge>
                    ) : null}
                  </div>
                  <p className="mt-1 max-w-2xl text-sm leading-relaxed text-muted">{field.description}</p>
                </div>
                {isToggle ? (
                  <Switch
                    checked={Boolean(value)}
                    onChange={() => onChange(field.key, !Boolean(value))}
                    label={`Toggle ${field.label}`}
                  />
                ) : null}
              </div>

              {!isToggle ? (
                <div className={cn('w-full', field.type !== 'textarea' && 'xl:w-96 xl:shrink-0')}>
                  {field.type === 'select' ? (
                    <Select
                      id={field.key}
                      value={String(value)}
                      disabled={isLocked}
                      aria-invalid={fieldInvalid}
                      aria-describedby={isLocked ? `${field.key}-lock` : undefined}
                      onChange={(event) => onChange(field.key, event.target.value)}
                      options={[{ value: '', label: 'Select an option…', disabled: true }, ...selectOptions]}
                      className={cn('w-full cursor-pointer', isLocked && 'cursor-not-allowed opacity-60')}
                    />
                  ) : field.type === 'textarea' ? (
                    <Textarea
                      id={field.key}
                      value={String(value)}
                      aria-invalid={fieldInvalid}
                      onChange={(event) => onChange(field.key, event.target.value)}
                      className="min-h-32"
                    />
                  ) : (
                    <Input
                      id={field.key}
                      type={field.type}
                      min={field.min}
                      max={field.max}
                      value={String(value)}
                      aria-invalid={fieldInvalid}
                      onChange={(event) => onChange(field.key, event.target.value)}
                      placeholder={isProfile ? `Enter your ${field.label.toLowerCase()}` : undefined}
                      className="w-full"
                    />
                  )}
                </div>
              ) : null}
            </div>

            {emailChanged ? (
              <div className="mt-4 rounded-2xl border border-warning/30 bg-warning/10 p-4 sm:p-5">
                <label htmlFor="email-current-password" className="block text-sm font-bold text-navy">
                  Confirm your current password
                </label>
                <p className="mt-1 text-xs text-muted">
                  For your security, changing your email requires your current password.
                </p>
                <div className="mt-3">
                  <Input
                    id="email-current-password"
                    type="password"
                    autoComplete="current-password"
                    value={emailPassword}
                    aria-invalid={passwordInvalid}
                    onChange={(event) => onEmailPasswordChange?.(event.target.value)}
                    placeholder="Current password"
                  />
                </div>
              </div>
            ) : null}
            {isLocked ? (
              <div id={`${field.key}-lock`} className="mt-4">
                <ProfessionLockNotice />
              </div>
            ) : null}
          </div>
        );
      })}
    </Card>
  );
}

const AVATAR_MAX_BYTES = 10 * 1024 * 1024;
const AVATAR_ALLOWED_TYPES = ['image/jpeg', 'image/jpg', 'image/png', 'image/gif', 'image/webp'];

/** Profile photo upload/remove control. Uploads to /v1/media/upload, then points
 * the account at it via PUT /v1/me/avatar; refreshSession() pulls the new
 * avatarUrl into AuthContext so the header updates immediately. */
function AvatarUploadCard() {
  const { user, refreshSession } = useAuth();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleFileSelected = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0] ?? null;
    event.target.value = '';
    if (!file) return;
    if (!AVATAR_ALLOWED_TYPES.includes(file.type)) {
      setError('Please choose a JPG, PNG, GIF, or WEBP image.');
      return;
    }
    if (file.size > AVATAR_MAX_BYTES) {
      setError('Photo must be smaller than 10 MB.');
      return;
    }
    setError(null);
    setBusy(true);
    try {
      const uploaded = await uploadMedia(file);
      await updateMyAvatar(uploaded.url);
      await refreshSession();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to upload photo.');
    } finally {
      setBusy(false);
    }
  };

  const handleRemove = async () => {
    setError(null);
    setBusy(true);
    try {
      await updateMyAvatar(null);
      await refreshSession();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to remove photo.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card padding="none">
      <div className="flex flex-col gap-4 p-4 sm:flex-row sm:items-center sm:p-5">
        <div className="flex min-w-0 flex-1 items-center gap-4">
          <UserAvatar avatarUrl={user?.avatarUrl} displayName={user?.displayName} className="h-14 w-14 text-base" />
          <div className="min-w-0 flex-1">
            <p className="text-base font-semibold text-navy">Profile photo</p>
            <p className="mt-1 max-w-xl text-sm leading-relaxed text-muted">
              Shown across your account. JPG, PNG, GIF, or WEBP up to 10 MB. Optional — a picture isn&apos;t required.
            </p>
            {error ? <p id="avatar-upload-error" className="mt-2 text-sm font-semibold text-danger-strong">{error}</p> : null}
          </div>
        </div>
        <div className="flex shrink-0 flex-wrap items-center gap-3">
          <label
            className={cn(
              buttonClassName({ className: 'pressable cursor-pointer focus-within:ring-2 focus-within:ring-primary focus-within:ring-offset-2' }),
              busy && 'pointer-events-none opacity-60',
            )}
          >
            {busy ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : null}
            {user?.avatarUrl ? 'Change photo' : 'Upload photo'}
            <input
              type="file"
              accept={AVATAR_ALLOWED_TYPES.join(',')}
              className="sr-only"
              disabled={busy}
              aria-invalid={Boolean(error)}
              aria-describedby={error ? 'avatar-upload-error' : undefined}
              onChange={(event) => { void handleFileSelected(event); }}
            />
          </label>
          {user?.avatarUrl ? (
            <Button
              type="button"
              variant="ghost"
              className="text-danger-strong hover:bg-danger/10"
              disabled={busy}
              onClick={() => { void handleRemove(); }}
            >
              <Trash2 className="h-4 w-4" aria-hidden="true" />
              Remove
            </Button>
          ) : null}
        </div>
      </div>
    </Card>
  );
}

/**
 * Privacy section body. The learner's recordings, consent, and reviewer access
 * are governed by the compliance system (consent records + retention workers +
 * audited access), so instead of disconnected preference toggles this points to
 * the real controls: the My-Recordings page (review + delete individual
 * recordings) and account deletion (full erasure).
 */
function PrivacyControlsCard() {
  return (
    <Card padding="lg">
      <div className="flex items-start gap-4">
        <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-danger/10 text-danger-strong">
          <ShieldCheck className="h-5 w-5" aria-hidden="true" />
        </div>
        <div className="min-w-0 flex-1 space-y-4">
          <div>
            <h3 className="text-lg font-bold tracking-tight text-navy">Your recordings &amp; data</h3>
            <p className="mt-1.5 max-w-2xl text-sm leading-relaxed text-muted">
              Speaking recordings from live tutor sessions are automatically deleted on a retention schedule, and
              any reviewer access is logged with a reason. You can review or delete individual recordings yourself,
              and request full erasure of your account and data whenever you like.
            </p>
          </div>
          <div className="flex flex-col gap-3 sm:flex-row sm:flex-wrap">
            <Button asChild>
              <Link href="/speaking/recordings">
                <Database className="h-4 w-4" aria-hidden="true" />
                Manage my recordings
              </Link>
            </Button>
            <Button asChild variant="outline" className="border-danger/30 bg-danger/10 text-danger-strong hover:border-danger/40 hover:bg-danger/20">
              <Link href="/settings/danger-zone">
                <Trash2 className="h-4 w-4" aria-hidden="true" />
                Delete my account &amp; data
              </Link>
            </Button>
          </div>
        </div>
      </div>
    </Card>
  );
}

function DangerZoneDeleteSection() {
  const { signOut } = useAuth();
  const router = useRouter();
  const [password, setPassword] = useState('');
  const [reason, setReason] = useState('');
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const handleDelete = async () => {
    if (!password.trim()) return;

    const confirmed = window.confirm(
      'Are you sure you want to delete your account? This action cannot be undone. Your data will be permanently removed after 30 days.'
    );
    if (!confirmed) return;

    setDeleting(true);
    setDeleteError(null);
    try {
      await deleteAccount(password, reason || undefined);
      await signOut();
      router.push('/sign-in');
    } catch (err) {
      setDeleteError(
        err instanceof Error ? err.message : 'Failed to delete account. Please check your password and try again.'
      );
    } finally {
      setDeleting(false);
    }
  };

  return (
    <Card padding="lg" className="border-danger/30 bg-danger/10">
      <div className="flex items-start gap-4">
        <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-danger/30 bg-danger/10">
          <Trash2 className="h-5 w-5 text-danger-strong" aria-hidden="true" />
        </div>
        <div className="flex-1">
          <h3 className="text-lg font-bold text-danger-strong">Delete your account</h3>
          <p className="mt-2 max-w-2xl text-sm leading-6 text-danger-strong">
            This action cannot be undone. After 30 days, your account and all associated data will be permanently deleted. During the grace period you can contact support to cancel the deletion.
          </p>
        </div>
      </div>

      <div className="mt-6 space-y-4">
        <div>
          <label htmlFor="delete-password" className="block text-sm font-semibold text-danger-strong">
            Confirm your password
          </label>
          <input
            id="delete-password"
            type="password"
            required
            autoComplete="current-password"
            placeholder="Enter your password to confirm"
            value={password}
            aria-invalid={Boolean(deleteError)}
            aria-describedby={deleteError ? 'delete-account-error' : undefined}
            onChange={(e) => setPassword(e.target.value)}
            className="mt-1.5 w-full rounded-2xl border border-danger/30 bg-surface px-4 py-3 text-sm text-navy outline-none transition-shadow focus:border-danger focus:ring-2 focus:ring-danger/10"
          />
        </div>

        <div>
          <label htmlFor="delete-reason" className="block text-sm font-semibold text-danger-strong">
            Reason for leaving <span className="font-normal text-danger-strong">(optional)</span>
          </label>
          <textarea
            id="delete-reason"
            placeholder="Why are you leaving? (optional)"
            value={reason}
            aria-invalid={false}
            onChange={(e) => setReason(e.target.value)}
            className="mt-1.5 min-h-24 w-full rounded-2xl border border-danger/30 bg-surface px-4 py-3 text-sm text-navy outline-none transition-shadow focus:border-danger focus:ring-2 focus:ring-danger/10"
          />
        </div>

        {deleteError ? (
          <div id="delete-account-error">
            <InlineAlert variant="error">{deleteError}</InlineAlert>
          </div>
        ) : null}

        <Button
          type="button"
          variant="destructive"
          size="lg"
          loading={deleting}
          disabled={!password.trim()}
          onClick={handleDelete}
        >
          {deleting ? null : <Trash2 className="h-4 w-4" aria-hidden="true" />}
          {deleting ? 'Deleting account...' : 'Delete my account'}
        </Button>
      </div>
    </Card>
  );
}

export default function LearnerSettingsSectionPage() {
  const params = useParams<{ section: string }>();
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const section = toSectionId(params?.section);
  const queryUserId = user?.userId ?? 'current';
  const draftKey = `${queryUserId}:${section ?? 'invalid'}`;
  const loadsRemoteData = Boolean(section && section !== 'notifications' && section !== 'danger-zone' && section !== 'privacy');
  const sectionQuery = useQuery({
    queryKey: queryKeys.settings.section(queryUserId, section ?? 'invalid'),
    queryFn: () => fetchSettingsSection(section as EditableSettingsSectionId),
    staleTime: 60_000,
    enabled: loadsRemoteData,
  });

  const [draft, setDraft] = useState<{ key: string; data: SettingsSectionData } | null>(null);
  // Sticky for the page session: the lock is only discoverable from a rejected save, and
  // it can never lift on its own — a purchase is not undone.
  const [professionLocked, setProfessionLocked] = useState(false);
  // H1 (security): only used to confirm a profile email change — see handleSave and
  // LearnerService.PatchSettingsSectionAsync. Cleared on a successful save.
  const [emailPassword, setEmailPassword] = useState('');
  const [feedback, setFeedback] = useState<{
    key: string;
    error: string | null;
    successMessage: string | null;
  } | null>(null);
  const data = draft?.key === draftKey ? draft.data : sectionQuery.data ?? null;
  const actionError = feedback?.key === draftKey ? feedback.error : null;
  const successMessage = feedback?.key === draftKey ? feedback.successMessage : null;
  const storedEmail = section === 'profile' ? String(sectionQuery.data?.values.email ?? '') : '';
  const draftEmail = section === 'profile' && data ? String(data.values.email ?? '') : '';
  const emailChangeRequiresPassword = section === 'profile'
    && draftEmail.trim().toLowerCase() !== storedEmail.trim().toLowerCase();
  const updateMutation = useMutation({
    mutationFn: ({ targetSection, values, currentPassword }: {
      userId: string;
      draftKey: string;
      targetSection: EditableSettingsSectionId;
      values: SettingsSectionData['values'];
      currentPassword?: string;
    }) =>
      updateSettingsSection(targetSection, values, currentPassword),
    onSuccess: async (response, variables) => {
      const nextData = {
        section: variables.targetSection,
        values: response.values ?? variables.values,
      };
      queryClient.setQueryData(
        queryKeys.settings.section(variables.userId, variables.targetSection),
        nextData,
      );
      setDraft((current) => current?.key === variables.draftKey ? null : current);
      setEmailPassword('');
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.settings.home(variables.userId) }),
        queryClient.invalidateQueries({
          queryKey: queryKeys.settings.section(variables.userId, variables.targetSection),
        }),
      ]);
    },
  });
  const loading = loadsRemoteData && sectionQuery.isPending;
  const saving = updateMutation.isPending;
  const loadError = !section
    ? 'This settings section does not exist.'
    : sectionQuery.error instanceof Error
      ? sectionQuery.error.message
      : sectionQuery.error
        ? 'Failed to load this settings section.'
        : null;
  const error = actionError ?? loadError;

  const config = useMemo(() => (section ? SECTION_CONFIG[section] : null), [section]);
  const configuredFieldCount = useMemo(() => {
    if (!config || !data) return 0;
    return config.fields.filter((field) => isFieldConfigured(field, fieldValue(data.values, field))).length;
  }, [config, data]);

  useEffect(() => {
    if (section) analytics.track('content_view', { page: 'settings-section', section });
  }, [section]);

  const handleChange = (key: string, value: string | boolean) => {
    if (!data) return;
    setDraft({
      key: draftKey,
      data: { ...data, values: setNestedValue(data.values, key, value) },
    });
    setFeedback((current) => ({
      key: draftKey,
      error: current?.key === draftKey ? current.error : null,
      successMessage: null,
    }));
  };

  const handleSave = async () => {
    if (!section || !data) return;
    const activeDraftKey = draftKey;
    setFeedback({ key: activeDraftKey, error: null, successMessage: null });
    try {
      await updateMutation.mutateAsync({
        userId: queryUserId,
        draftKey: activeDraftKey,
        targetSection: section as EditableSettingsSectionId,
        values: data.values,
        currentPassword: emailChangeRequiresPassword ? emailPassword : undefined,
      });
      setFeedback({ key: activeDraftKey, error: null, successMessage: 'Settings saved.' });
    } catch (err) {
      // The whole profile section is PATCHed on every save, so a locked learner editing
      // only their name would be blocked by their own unchanged profession — the backend
      // treats a no-op re-send as no change, so reaching here means the pick really did
      // move. Roll it back to the stored value (keeping the rest of the draft) and swap
      // the control for the locked state + support route; retrying can never succeed.
      if (err instanceof ApiError && err.code === 'profession_locked') {
        setProfessionLocked(true);
        const storedProfession = sectionQuery.data?.values.professionId ?? '';
        setDraft((current) =>
          current?.key === activeDraftKey
            ? {
                key: current.key,
                data: {
                  ...current.data,
                  values: setNestedValue(current.data.values, 'professionId', storedProfession),
                },
              }
            : current,
        );
      }
      setFeedback({
        key: activeDraftKey,
        error: err instanceof Error ? err.message : 'Failed to save this settings section.',
        successMessage: null,
      });
    }
  };

  const backLink = (
    <Button asChild variant="outline" size="sm">
      <Link href="/settings">
        <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
        Back to Settings
      </Link>
    </Button>
  );

  const sectionHeader = config ? (
    <LearnerSurfaceSectionHeader
      eyebrow={config.helperBadge}
      icon={config.icon}
      title={config.helperCardTitle}
      description={config.helperCardBody}
      action={config.fields.length > 0 ? (
        <Badge variant="muted" className="self-start tabular-nums sm:self-auto">
          {configuredFieldCount}/{config.fields.length} configured
        </Badge>
      ) : undefined}
    />
  ) : null;

  return (
    <>
      {config ? (
        <LearnerPageHero
          eyebrow={config.eyebrow}
          icon={config.icon}
          accent={config.accent}
          title={config.heroTitle ?? config.title}
          description={`Review and update your ${config.title.toLowerCase()} settings.`}
          aside={backLink}
        />
      ) : backLink}

      {loading ? (
        <div className="space-y-4">
          {[1, 2, 3].map((item) => <Skeleton key={item} className="h-40 rounded-2xl" />)}
        </div>
      ) : null}

      {!loading && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
      {!loading && successMessage ? <InlineAlert variant="success">{successMessage}</InlineAlert> : null}

      {!loading && (section === 'privacy' || section === 'danger-zone') && config ? (
        <MotionSection className="space-y-4">
          {sectionHeader}
          {section === 'privacy' ? <PrivacyControlsCard /> : <DangerZoneDeleteSection />}
        </MotionSection>
      ) : null}

      {!loading && data && config ? (
        <>
          <MotionSection className="space-y-4">
            {sectionHeader}
            {section === 'profile' ? <AvatarUploadCard /> : null}
            <SettingsSectionForm
              accent={config.accent}
              data={data}
              onChange={handleChange}
              professionLocked={professionLocked}
              originalEmail={section === 'profile' ? storedEmail : undefined}
              emailPassword={emailPassword}
              onEmailPasswordChange={setEmailPassword}
              invalid={Boolean(actionError)}
            />
          </MotionSection>

          <MotionSection delayIndex={1}>
            <Card>
              <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                <div className="space-y-2">
                  <Badge variant={successMessage ? 'success' : 'muted'}>
                    {saving ? 'Saving...' : successMessage ? 'Saved' : 'Ready to save'}
                  </Badge>
                  <p className="max-w-xl text-sm leading-relaxed text-muted">
                    Save when you are ready. Low-bandwidth, transcript, and reminder preferences will be used by the learner app after this update.
                  </p>
                </div>
                <Button onClick={handleSave} loading={saving} size="lg" className="shrink-0">
                  <Save className="h-4 w-4" aria-hidden="true" />
                  Save changes
                </Button>
              </div>
            </Card>
          </MotionSection>
        </>
      ) : null}
    </>
  );
}
