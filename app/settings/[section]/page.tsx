'use client';

import { useEffect, useMemo, useState } from 'react';
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
import { LearnerDashboardShell } from '@/components/layout';
import { NotificationPreferencesPanel } from '@/components/layout/notification-preferences-panel';
import { InlineAlert } from '@/components/ui/alert';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import { ApiError, fetchSettingsSection, updateSettingsSection } from '@/lib/api';
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

const accentStyles: Record<LearnerSurfaceAccent, {
  icon: string;
  badge: string;
  softBadge: string;
  helperSurface: string;
  helperGlow: string;
  inputFocus: string;
  toggleOn: string;
}> = {
  primary: {
    icon: 'bg-primary/10 text-primary',
    badge: 'border-primary/20 bg-primary/10 text-primary',
    softBadge: 'border-primary/15 bg-primary/10 text-primary',
    helperSurface: 'border-primary/15 bg-surface',
    helperGlow: 'from-primary/10 via-white to-white',
    inputFocus: 'focus:border-primary focus:ring-2 focus:ring-primary/10',
    toggleOn: 'bg-primary',
  },
  navy: {
    icon: 'bg-navy/10 text-navy',
    badge: 'border-navy/20 bg-navy/10 text-navy',
    softBadge: 'border-navy/15 bg-navy/10 text-navy',
    helperSurface: 'border-navy/15 bg-surface',
    helperGlow: 'from-navy/10 via-white to-white',
    inputFocus: 'focus:border-navy focus:ring-2 focus:ring-navy/10',
    toggleOn: 'bg-navy',
  },
  amber: {
    icon: 'bg-amber-50 text-amber-700',
    badge: 'border-amber-200 bg-amber-50 text-amber-700',
    softBadge: 'border-amber-100 bg-amber-50 text-amber-700',
    helperSurface: 'border-amber-200/70 bg-surface',
    helperGlow: 'from-amber-50 via-white to-white',
    inputFocus: 'focus:border-amber-400 focus:ring-2 focus:ring-amber-100',
    toggleOn: 'bg-amber-500',
  },
  blue: {
    icon: 'bg-blue-50 text-blue-700',
    badge: 'border-blue-200 bg-blue-50 text-blue-700',
    softBadge: 'border-blue-100 bg-blue-50 text-blue-700',
    helperSurface: 'border-blue-200/70 bg-surface',
    helperGlow: 'from-blue-50 via-white to-white',
    inputFocus: 'focus:border-blue-400 focus:ring-2 focus:ring-blue-100',
    toggleOn: 'bg-blue-600',
  },
  indigo: {
    icon: 'bg-indigo-50 text-indigo-700',
    badge: 'border-indigo-200 bg-indigo-50 text-indigo-700',
    softBadge: 'border-indigo-100 bg-indigo-50 text-indigo-700',
    helperSurface: 'border-indigo-200/70 bg-surface',
    helperGlow: 'from-indigo-50 via-white to-white',
    inputFocus: 'focus:border-indigo-400 focus:ring-2 focus:ring-indigo-100',
    toggleOn: 'bg-indigo-600',
  },
  purple: {
    icon: 'bg-purple-50 text-purple-700',
    badge: 'border-purple-200 bg-purple-50 text-purple-700',
    softBadge: 'border-purple-100 bg-purple-50 text-purple-700',
    helperSurface: 'border-purple-200/70 bg-surface',
    helperGlow: 'from-purple-50 via-white to-white',
    inputFocus: 'focus:border-purple-400 focus:ring-2 focus:ring-purple-100',
    toggleOn: 'bg-purple-600',
  },
  rose: {
    icon: 'bg-rose-50 text-rose-700',
    badge: 'border-rose-200 bg-rose-50 text-rose-700',
    softBadge: 'border-rose-100 bg-rose-50 text-rose-700',
    helperSurface: 'border-rose-200/70 bg-surface',
    helperGlow: 'from-rose-50 via-white to-white',
    inputFocus: 'focus:border-rose-400 focus:ring-2 focus:ring-rose-100',
    toggleOn: 'bg-rose-600',
  },
  emerald: {
    icon: 'bg-emerald-50 text-emerald-700',
    badge: 'border-emerald-200 bg-emerald-50 text-emerald-700',
    softBadge: 'border-emerald-100 bg-emerald-50 text-emerald-700',
    helperSurface: 'border-emerald-200/70 bg-surface',
    helperGlow: 'from-emerald-50 via-white to-white',
    inputFocus: 'focus:border-emerald-400 focus:ring-2 focus:ring-emerald-100',
    toggleOn: 'bg-emerald-600',
  },
  slate: {
    icon: 'bg-muted text-navy',
    badge: 'border-border bg-muted text-navy',
    softBadge: 'border-border bg-muted text-navy',
    helperSurface: 'border-border bg-surface',
    helperGlow: 'from-slate-100 via-white to-white',
    inputFocus: 'focus:border-slate-400 focus:ring-2 focus:ring-slate-100',
    toggleOn: 'bg-slate-700',
  },
};

const tagToneStyles: Record<Exclude<FieldTagTone, 'section'>, string> = {
  muted: 'border-border bg-background-light text-muted',
  writing: 'border-rose-200 bg-rose-50 text-rose-700',
  speaking: 'border-purple-200 bg-purple-50 text-purple-700',
  reading: 'border-blue-200 bg-blue-50 text-blue-700',
  listening: 'border-emerald-200 bg-emerald-50 text-emerald-700',
  study: 'border-amber-200 bg-amber-50 text-amber-700',
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
    <Badge key={`${label}-${tone}`} className={cn('rounded-full px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em]', className)}>
      {label}
    </Badge>
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

function inputClasses(accent: LearnerSurfaceAccent) {
  return cn(
    'w-full rounded-2xl border border-border bg-surface px-5 py-4 text-base font-bold text-navy outline-none transition-[border-color,box-shadow] duration-200 shadow-inner focus:border-primary focus:ring-4 focus:ring-primary/20',
    accentStyles[accent].inputFocus,
  );
}

function toggleFocusClasses(accent: LearnerSurfaceAccent) {
  return accentStyles[accent].inputFocus
    .replace('focus:border-', 'focus-visible:border-')
    .replace('focus:ring-', 'focus-visible:ring-');
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
    <div className="rounded-2xl border border-amber-200 bg-amber-50 p-5 shadow-sm">
      <div className="flex items-start gap-4">
        <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl border border-amber-200 bg-amber-100">
          <Lock className="h-5 w-5 text-amber-700" />
        </div>
        <div className="min-w-0 flex-1 space-y-3">
          <p className="text-sm font-black uppercase tracking-widest text-amber-800">Profession locked</p>
          <p className="max-w-2xl text-sm font-medium leading-relaxed text-amber-900/80">
            Your videos and materials are tied to the profession you registered with, so changing it now would
            re-point every package on your account. That is why it locks after your first purchase — our team can
            move you across and re-point your access for you.
          </p>
          <a
            href={href}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center justify-center gap-2 rounded-full bg-[#25D366] px-5 py-2.5 text-sm font-black text-white shadow-sm transition hover:brightness-95"
          >
            <MessageCircle className="h-4 w-4" />
            Request a change on WhatsApp
          </a>
        </div>
      </div>
    </div>
  );
}

function SettingsSectionHelperCard({
  accent,
  helperBadge,
  icon: Icon,
  title,
  body,
  configuredFieldCount,
  totalFieldCount,
}: {
  accent: LearnerSurfaceAccent;
  helperBadge: string;
  icon: LucideIcon;
  title: string;
  body: string;
  configuredFieldCount: number;
  totalFieldCount: number;
}) {
  const palette = accentStyles[accent];

  return (
    <section className={cn('overflow-hidden rounded-[2rem] border shadow-sm bg-surface relative', palette.helperSurface)}>
      <div className="p-6 sm:p-8 relative z-10">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex items-start gap-5">
            <div className={cn('flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl border', palette.softBadge)}>
              <Icon className="h-6 w-6" />
            </div>
            <div>
              <div className="flex flex-wrap items-center gap-2">
                <Badge className={cn('rounded-full px-3 py-1 text-xs font-black uppercase tracking-widest shadow-sm', palette.badge)}>
                  {helperBadge}
                </Badge>
                <Badge variant="muted" className="rounded-full px-3 py-1 text-xs font-bold uppercase tracking-widest">
                  {configuredFieldCount}/{totalFieldCount} configured
                </Badge>
              </div>
              <h2 className="mt-4 text-xl font-black text-navy tracking-tight">{title}</h2>
              <p className="mt-1.5 max-w-3xl text-sm leading-relaxed text-muted font-bold">{body}</p>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}

function SettingsSectionForm({
  accent,
  data,
  onChange,
  professionLocked = false,
}: {
  accent: LearnerSurfaceAccent;
  data: SettingsSectionData;
  onChange: (key: string, value: string | boolean) => void;
  /** Set once the backend has rejected a change with `profession_locked`. */
  professionLocked?: boolean;
}) {
  const { options: professionOptions } = useProfessions();
  const config = SECTION_CONFIG[data.section];
  const palette = accentStyles[accent];

  if (data.section === 'profile') {
    return (
      <div className="rounded-[2.5rem] border border-border bg-surface shadow-sm relative overflow-hidden">
        <div className="relative z-10 flex flex-col">
          {config.fields.map((field, i) => {
            const value = fieldValue(data.values, field);
            const status = fieldStatus(field, value);
            const FieldIcon = field.icon;
            const isProfessionField = field.key === 'professionId';
            const isLocked = isProfessionField && professionLocked;
            const selectOptions: SelectOption[] = isProfessionField
              ? professionSelectOptions(professionOptions, String(value))
              : (field.options ?? []);

            return (
              <div key={field.key} className={cn('transition-colors duration-300 hover:bg-background-light', i !== config.fields.length - 1 && 'border-b border-border')}>
                <div className="p-6 sm:p-8 flex flex-col xl:flex-row xl:items-start gap-6">
                  <div className="flex min-w-0 flex-1 items-start gap-5">
                    <div className={cn('flex h-16 w-16 shrink-0 items-center justify-center rounded-2xl border', palette.softBadge)}>
                      <FieldIcon className={cn('h-7 w-7', status.label === 'Set' ? 'text-primary' : '')} />
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-2 mb-1.5">
                        <label className="text-xl font-black text-navy tracking-tight" htmlFor={field.key}>{field.label}</label>
                        <Badge className={cn('rounded-full px-3 py-1 font-bold text-[10px] uppercase tracking-widest', palette.badge)}>
                          Personal Info
                        </Badge>
                        <Badge variant={isLocked ? 'muted' : status.variant} className="rounded-full px-2.5 py-1 text-[10px] font-black uppercase tracking-widest shadow-sm">
                          {isLocked ? 'Locked' : status.label}
                        </Badge>
                      </div>
                      <p className="max-w-xl text-sm leading-relaxed text-navy/70 font-medium">{field.description}</p>
                    </div>
                  </div>
                  <div className="w-full xl:w-96 shrink-0">
                    {field.type === 'select' ? (
                      <select
                        id={field.key}
                        className={cn(
                          inputClasses(accent),
                          'appearance-none cursor-pointer bg-no-repeat bg-[right_1rem_center] bg-[length:1.2em] font-bold h-14',
                          isLocked && 'cursor-not-allowed opacity-60',
                        )}
                        value={String(value)}
                        disabled={isLocked}
                        aria-describedby={isLocked ? `${field.key}-lock` : undefined}
                        onChange={(event) => onChange(field.key, event.target.value)}
                      >
                        <option value="" disabled className="text-navy/30">Select an option…</option>
                        {selectOptions.map((option) => (
                          <option key={option.value} value={option.value} disabled={option.disabled} className="text-navy font-bold">{option.label}</option>
                        ))}
                      </select>
                    ) : (
                      <input
                        id={field.key}
                        type={field.type}
                        className={cn(inputClasses(accent), 'font-bold h-14')}
                        value={String(value)}
                        onChange={(event) => onChange(field.key, event.target.value)}
                        placeholder={`Enter your ${field.label.toLowerCase()}`}
                      />
                    )}
                  </div>
                </div>
                {isLocked ? (
                  <div id={`${field.key}-lock`} className="px-6 pb-6 sm:px-8 sm:pb-8">
                    <ProfessionLockNotice />
                  </div>
                ) : null}
              </div>
            );
          })}
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {config.fields.map((field) => {
        const value = fieldValue(data.values, field);
        const status = fieldStatus(field, value);
        const FieldIcon = field.icon;

        return (
          <div key={field.key} className={cn('rounded-[2rem] bg-surface p-6 sm:p-8 shadow-sm border transition-[box-shadow,border-color,transform] duration-300 hover:shadow-clinical hover:border-border-hover hoverable:-translate-y-1 group relative', status.label === 'Not set' ? 'border-dashed border-border' : 'border-border')}>
            <div className="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between relative z-10">
              <div className="flex min-w-0 flex-1 items-start gap-5">
                <div className={cn('flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl border transition-transform duration-300 ease-out group-hoverable:scale-110', palette.softBadge)}>
                  <FieldIcon className={cn('h-6 w-6 transition-colors duration-300', Boolean(value) || status.label === 'Set' ? 'text-primary' : '')} />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <label className="text-xl font-black text-navy tracking-tight group-hover:text-primary transition-colors" htmlFor={field.key}>{field.label}</label>
                    <div className="inline-flex overflow-hidden rounded-full shadow-sm border border-border">
                      {renderTag(field.primaryTag, accent, field.primaryTagTone ?? 'section')}
                    </div>
                    {field.secondaryTag ? (
                      <div className="inline-flex overflow-hidden rounded-full shadow-sm border border-border">
                        {renderTag(field.secondaryTag, accent, field.secondaryTagTone ?? 'muted')}
                      </div>
                    ) : null}
                  </div>
                  <p className="mt-2.5 max-w-2xl text-sm leading-relaxed text-muted font-medium">{field.description}</p>
                </div>
              </div>

              {field.type === 'toggle' ? (
                <button
                  id={field.key}
                  type="button"
                  onClick={() => onChange(field.key, !Boolean(value))}
                  className={cn(
                    'relative inline-flex h-10 w-20 shrink-0 items-center rounded-full transition-colors duration-200 focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-offset-2 overflow-hidden shadow-inner border border-border',
                    Boolean(value) ? palette.toggleOn : 'bg-navy/10 hover:bg-navy/15',
                    toggleFocusClasses(accent),
                  )}
                  aria-checked={Boolean(value)}
                  aria-label={`Toggle ${field.label}`}
                  role="switch"
                >
                  <span className={cn('inline-block h-8 w-8 transform rounded-full bg-surface shadow-sm transition-transform duration-200 ease-out', Boolean(value) ? 'translate-x-10' : 'translate-x-1')} />
                </button>
              ) : null}
            </div>

            {field.type !== 'toggle' ? (
              <div className="mt-8 relative z-10">
                {field.type === 'select' ? (
                  <select
                    id={field.key}
                    className={cn(inputClasses(accent), 'appearance-none cursor-pointer bg-no-repeat bg-[right_1rem_center] bg-[length:1.2em] font-bold')}
                    value={String(value)}
                    onChange={(event) => onChange(field.key, event.target.value)}
                  >
                    <option value="" disabled className="text-navy/30">Select an option…</option>
                    {(field.key === 'professionId'
                      ? professionSelectOptions(professionOptions, String(value))
                      : (field.options ?? [])
                    ).map((option) => (
                      <option key={option.value} value={option.value} disabled={option.disabled} className="text-navy font-bold">{option.label}</option>
                    ))}
                  </select>
                ) : field.type === 'textarea' ? (
                  <textarea
                    id={field.key}
                    className={cn('min-h-32 resize-y font-bold p-6', inputClasses(accent))}
                    value={String(value)}
                    onChange={(event) => onChange(field.key, event.target.value)}
                  />
                ) : (
                  <input
                    id={field.key}
                    type={field.type}
                    min={field.min}
                    max={field.max}
                    className={cn(inputClasses(accent), 'font-bold h-14')}
                    value={String(value)}
                    onChange={(event) => onChange(field.key, event.target.value)}
                  />
                )}
              </div>
            ) : null}
          </div>
        );
      })}
    </div>
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
    <div className="rounded-[2rem] border border-border bg-surface p-6 sm:p-8 shadow-sm relative overflow-hidden">
      <div className="flex items-start gap-5 relative z-10">
        <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl border border-rose-200 bg-rose-50">
          <ShieldCheck className="h-6 w-6 text-rose-700" />
        </div>
        <div className="min-w-0 flex-1 space-y-4">
          <div>
            <h3 className="text-xl font-black tracking-tight text-navy">Your recordings &amp; data</h3>
            <p className="mt-1.5 max-w-2xl text-sm font-medium leading-relaxed text-muted">
              Speaking recordings from live tutor sessions are automatically deleted on a retention schedule, and
              any reviewer access is logged with a reason. You can review or delete individual recordings yourself,
              and request full erasure of your account and data whenever you like.
            </p>
          </div>
          <div className="flex flex-col gap-3 sm:flex-row">
            <Link
              href="/speaking/recordings"
              className="inline-flex items-center justify-center gap-2 rounded-full bg-primary px-5 py-2.5 text-sm font-black text-white shadow-sm transition hover:bg-primary-dark"
            >
              <Database className="h-4 w-4" />
              Manage my recordings
            </Link>
            <Link
              href="/settings/danger-zone"
              className="inline-flex items-center justify-center gap-2 rounded-full border border-danger/30 bg-danger/10 px-5 py-2.5 text-sm font-black text-danger shadow-sm transition hover:bg-danger/20"
            >
              <Trash2 className="h-4 w-4" />
              Delete my account &amp; data
            </Link>
          </div>
        </div>
      </div>
    </div>
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
    <div className="space-y-6">
      <div className="rounded-2xl border-2 border-danger/30 bg-danger/10 p-6 shadow-sm">
        <div className="flex items-start gap-4">
          <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl border border-danger/30 bg-danger/10">
            <Trash2 className="h-5 w-5 text-danger" />
          </div>
          <div className="flex-1">
            <h3 className="text-lg font-bold text-danger">Delete your account</h3>
            <p className="mt-2 max-w-2xl text-sm leading-6 text-danger">
              This action cannot be undone. After 30 days, your account and all associated data will be permanently deleted. During the grace period you can contact support to cancel the deletion.
            </p>
          </div>
        </div>

        <div className="mt-6 space-y-4">
          <div>
            <label htmlFor="delete-password" className="block text-sm font-semibold text-danger">
              Confirm your password
            </label>
            <input
              id="delete-password"
              type="password"
              required
              autoComplete="current-password"
              placeholder="Enter your password to confirm"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="mt-1.5 w-full rounded-2xl border border-danger/30 bg-surface px-4 py-3 text-sm text-navy outline-none transition-shadow focus:border-danger focus:ring-2 focus:ring-danger/10"
            />
          </div>

          <div>
            <label htmlFor="delete-reason" className="block text-sm font-semibold text-danger">
              Reason for leaving <span className="font-normal text-danger">(optional)</span>
            </label>
            <textarea
              id="delete-reason"
              placeholder="Why are you leaving? (optional)"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              className="mt-1.5 min-h-24 w-full rounded-2xl border border-danger/30 bg-surface px-4 py-3 text-sm text-navy outline-none transition-shadow focus:border-danger focus:ring-2 focus:ring-danger/10"
            />
          </div>

          {deleteError ? <InlineAlert variant="error">{deleteError}</InlineAlert> : null}

          <button
            type="button"
            disabled={!password.trim() || deleting}
            onClick={handleDelete}
            className={cn(
              'inline-flex items-center gap-2 rounded-2xl px-6 py-3 text-sm font-bold text-white transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-danger focus-visible:ring-offset-2',
              password.trim() && !deleting
                ? 'bg-danger hover:bg-danger/90 cursor-pointer'
                : 'bg-danger/40 cursor-not-allowed',
            )}
          >
            {deleting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Trash2 className="h-4 w-4" />}
            {deleting ? 'Deleting account...' : 'Delete my account'}
          </button>
        </div>
      </div>
    </div>
  );
}

export default function LearnerSettingsSectionPage() {
  const params = useParams<{ section: string }>();
  const router = useRouter();
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
  const [feedback, setFeedback] = useState<{
    key: string;
    error: string | null;
    successMessage: string | null;
  } | null>(null);
  const data = draft?.key === draftKey ? draft.data : sectionQuery.data ?? null;
  const actionError = feedback?.key === draftKey ? feedback.error : null;
  const successMessage = feedback?.key === draftKey ? feedback.successMessage : null;
  const updateMutation = useMutation({
    mutationFn: ({ targetSection, values }: {
      userId: string;
      draftKey: string;
      targetSection: EditableSettingsSectionId;
      values: SettingsSectionData['values'];
    }) =>
      updateSettingsSection(targetSection, values),
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

  return (
    <LearnerDashboardShell pageTitle={config?.title ?? 'Settings'} subtitle={config?.description ?? 'Manage learner settings'} backHref="/settings">
      <div className="relative min-h-[calc(100dvh-4rem)]">
        <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_top,_var(--tw-gradient-stops))] from-primary/10 via-transparent to-transparent pointer-events-none -z-10 blur-3xl opacity-70" />
        
        <div className="space-y-6 sm:space-y-10 relative z-10 px-4 sm:px-0 pb-20">
          <Button variant="ghost" className="gap-2 rounded-full hover:bg-navy/5 font-bold mt-4" onClick={() => router.push('/settings')}>
            <ArrowLeft className="h-4 w-4" />
            Back to Settings
          </Button>

          {config ? (
            <div className="bg-surface p-2 sm:p-2 border border-border shadow-sm overflow-hidden relative rounded-3xl">
              <LearnerPageHero
                eyebrow={config.eyebrow}
                icon={config.icon}
                accent={config.accent}
                title={config.heroTitle ?? config.title}
                description={`Review and update your ${config.title.toLowerCase()} settings.`}
                highlights={[
                  { icon: config.icon, label: 'Controls', value: `${config.fields.length} settings` },
                  { icon: Save, label: 'Configured', value: `${configuredFieldCount} set` },
                  { icon: Settings2, label: 'Save state', value: saving ? 'Saving...' : successMessage ? 'Saved' : 'Ready to edit' },
                ]}
              />
            </div>
          ) : null}

          {loading ? (
            <div className="space-y-4">
              {[1, 2, 3].map((item) => <Skeleton key={item} className="h-40 rounded-3xl" />)}
            </div>
          ) : null}

          {!loading && error ? <InlineAlert variant="error" className="shadow-sm">{error}</InlineAlert> : null}
          {!loading && successMessage ? <InlineAlert variant="success" className="shadow-sm">{successMessage}</InlineAlert> : null}

          {!loading && section === 'notifications' && config ? (
            <div className="space-y-5 sm:space-y-8 relative z-20">
              <SettingsSectionHelperCard
                accent={config.accent}
                helperBadge={config.helperBadge}
                icon={config.icon}
                title={config.helperCardTitle}
                body={config.helperCardBody}
                configuredFieldCount={config.fields.length}
                totalFieldCount={config.fields.length}
              />

              <NotificationPreferencesPanel
                description="These controls now use the shared notification service for learners, experts, and admins while preserving learner compatibility settings during rollout."
              />
            </div>
          ) : null}

          {!loading && section === 'danger-zone' && config ? (
            <div className="space-y-5 sm:space-y-8 relative z-20">
              <SettingsSectionHelperCard
                accent={config.accent}
                helperBadge={config.helperBadge}
                icon={config.icon}
                title={config.helperCardTitle}
                body={config.helperCardBody}
                configuredFieldCount={0}
                totalFieldCount={0}
              />

              <DangerZoneDeleteSection />
            </div>
          ) : null}

          {!loading && section === 'privacy' && config ? (
            <div className="space-y-5 sm:space-y-8 relative z-20">
              <SettingsSectionHelperCard
                accent={config.accent}
                helperBadge={config.helperBadge}
                icon={config.icon}
                title={config.helperCardTitle}
                body={config.helperCardBody}
                configuredFieldCount={0}
                totalFieldCount={0}
              />

              <PrivacyControlsCard />
            </div>
          ) : null}

          {!loading && data && config ? (
            <div className="space-y-5 sm:space-y-8 relative z-20">
              <SettingsSectionHelperCard
                accent={config.accent}
                helperBadge={config.helperBadge}
                icon={config.icon}
                title={config.helperCardTitle}
                body={config.helperCardBody}
                configuredFieldCount={configuredFieldCount}
                totalFieldCount={config.fields.length}
              />

              <SettingsSectionForm
                accent={config.accent}
                data={data}
                onChange={handleChange}
                professionLocked={professionLocked}
              />

              <div className="rounded-[2rem] border border-border bg-surface px-6 py-5 shadow-sm relative overflow-hidden mt-10">
                <div className="flex flex-col gap-5 sm:flex-row sm:items-center sm:justify-between relative z-10">
                  <div className="space-y-3">
                    <div className="flex flex-wrap items-center gap-3">
                      <Badge className={cn('rounded-full px-3 py-1.5 text-[10px] font-black uppercase tracking-[0.14em] shadow-sm', accentStyles[config.accent].badge)}>
                        {configuredFieldCount}/{config.fields.length} configured
                      </Badge>
                      <Badge variant={successMessage ? 'success' : 'muted'} className="rounded-full px-3 py-1.5 text-[10px] font-black uppercase tracking-[0.14em] bg-surface shadow-sm border border-border">
                        {saving ? 'Saving...' : successMessage ? 'Saved' : 'Ready to save'}
                      </Badge>
                    </div>
                    <p className="text-sm font-medium text-muted leading-relaxed max-w-xl">
                      Save when you are ready. Low-bandwidth, transcript, and reminder preferences will be used by the learner app after this update.
                    </p>
                  </div>
                  <Button onClick={handleSave} loading={saving} size="lg" className="gap-2 rounded-full font-black px-8 shadow-sm hoverable:scale-105 transition-[background-color,box-shadow,transform] duration-200 shrink-0">
                    <Save className="h-4 w-4" />
                    Save changes
                  </Button>
                </div>
              </div>
            </div>
          ) : null}
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
