'use client';

import { useEffect, useMemo, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import {
  Accessibility,
  ArrowLeft,
  Bell,
  BellRing,
  BookOpen,
  BriefcaseMedical,
  Calendar,
  CalendarClock,
  Captions,
  Clock3,
  Contrast,
  Database,
  FileText,
  Globe2,
  Headphones,
  Keyboard,
  Loader2,
  Mail,
  MessageSquareMore,
  Mic,
  MonitorSmartphone,
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
import { fetchSettingsSection, updateSettingsSection } from '@/lib/api';
import { deleteAccount } from '@/lib/auth-client';
import type { LearnerSurfaceAccent } from '@/lib/learner-surface';
import type { SettingsSectionData, SettingsSectionId } from '@/lib/mock-data';
import { cn } from '@/lib/utils';
import { useAuth } from '@/contexts/auth-context';

type FieldType = 'text' | 'email' | 'number' | 'date' | 'select' | 'textarea' | 'toggle';
type FieldTagTone = 'section' | 'muted' | 'writing' | 'speaking' | 'reading' | 'listening' | 'study';

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
  options?: Array<{ value: string; label: string }>;
  min?: number;
  max?: number;
}

interface SectionConfig {
  title: string;
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
    icon: 'bg-slate-100 text-slate-700',
    badge: 'border-slate-200 bg-slate-100 text-slate-700',
    softBadge: 'border-slate-200 bg-slate-100 text-slate-700',
    helperSurface: 'border-slate-200 bg-surface',
    helperGlow: 'from-slate-100 via-white to-white',
    inputFocus: 'focus:border-slate-400 focus:ring-2 focus:ring-slate-100',
    toggleOn: 'bg-slate-700',
  },
};

const tagToneStyles: Record<Exclude<FieldTagTone, 'section'>, string> = {
  muted: 'border-gray-200 bg-gray-100 text-gray-600',
  writing: 'border-rose-200 bg-rose-50 text-rose-700',
  speaking: 'border-purple-200 bg-purple-50 text-purple-700',
  reading: 'border-blue-200 bg-blue-50 text-blue-700',
  listening: 'border-emerald-200 bg-emerald-50 text-emerald-700',
  study: 'border-amber-200 bg-amber-50 text-amber-700',
};

const SECTION_CONFIG: Record<SettingsSectionId, SectionConfig> = {
  profile: {
    title: 'Profile',
    description: 'Keep name, email, profession, and account-facing identity details aligned with the learner record.',
    eyebrow: 'Account & Identity',
    icon: User,
    accent: 'blue',
    helperBadge: 'Identity',
    helperCardTitle: 'What changes here',
    helperCardBody: 'Use this page to keep learner identity data accurate before the rest of the app depends on it.',
    fields: [
      {
        key: 'displayName',
        label: 'Display Name',
        type: 'text',
        description: 'Shown across the learner workspace and report surfaces.',
        icon: UserCircle2,
        primaryTag: 'Identity',
        secondaryTag: 'Visible in app',
        secondaryTagTone: 'muted',
      },
      {
        key: 'email',
        label: 'Email',
        type: 'email',
        description: 'Used for sign-in, review updates, and billing communication.',
        icon: Mail,
        primaryTag: 'Account',
        secondaryTag: 'Sign-in',
        secondaryTagTone: 'muted',
      },
      {
        key: 'professionId',
        label: 'Profession',
        type: 'select',
        description: 'Controls profession-specific writing and speaking material.',
        icon: BriefcaseMedical,
        primaryTag: 'Study Content',
        secondaryTag: 'Route context',
        secondaryTagTone: 'muted',
        options: [
          { value: 'medicine', label: 'Medicine' },
          { value: 'nursing', label: 'Nursing' },
          { value: 'pharmacy', label: 'Pharmacy' },
          { value: 'dentistry', label: 'Dentistry' },
          { value: 'physiotherapy', label: 'Physiotherapy' },
        ],
      },
      {
        key: 'deviceVisibility',
        label: 'Session Visibility Label',
        type: 'text',
        description: 'A learner-facing note for active session and device visibility.',
        icon: MonitorSmartphone,
        primaryTag: 'Device Visibility',
        secondaryTag: 'Session note',
        secondaryTagTone: 'muted',
      },
    ],
  },
  privacy: {
    title: 'Privacy',
    description: 'Control how recordings, transcripts, consent, and learner evidence are stored or shared.',
    eyebrow: 'Privacy Controls',
    icon: Shield,
    accent: 'rose',
    helperBadge: 'Sensitive Data',
    helperCardTitle: 'OET evidence privacy',
    helperCardBody: 'Writing and speaking evidence is sensitive. These settings keep that control explicit.',
    fields: [
      {
        key: 'storeRecordings',
        label: 'Store speaking recordings',
        type: 'toggle',
        description: 'Keep recordings available for transcript review and expert feedback.',
        icon: Database,
        primaryTag: 'Storage',
        secondaryTag: 'Audio evidence',
        secondaryTagTone: 'muted',
      },
      {
        key: 'storeTranscripts',
        label: 'Store transcripts',
        type: 'toggle',
        description: 'Retain generated transcripts for later review and comparison.',
        icon: Captions,
        primaryTag: 'Transcript',
        secondaryTag: 'Review support',
        secondaryTagTone: 'muted',
      },
      {
        key: 'allowExpertAccess',
        label: 'Allow expert reviewers to access evidence',
        type: 'toggle',
        description: 'Required when you request human review on writing or speaking attempts.',
        icon: ShieldCheck,
        primaryTag: 'Reviewer Access',
        secondaryTag: 'Human review',
        secondaryTagTone: 'muted',
      },
      {
        key: 'consentHistoryNote',
        label: 'Consent history note',
        type: 'textarea',
        description: 'Short note explaining learner consent or export/delete expectations.',
        icon: FileText,
        primaryTag: 'Consent',
        secondaryTag: 'Learner note',
        secondaryTagTone: 'muted',
      },
    ],
  },
  notifications: {
    title: 'Notifications',
    description: 'Choose how the learner is contacted about study reminders, review updates, and billing actions.',
    eyebrow: 'Learner Notifications',
    icon: Bell,
    accent: 'indigo',
    helperBadge: 'Reminders',
    helperCardTitle: 'Communication policy',
    helperCardBody: 'Keep reminders high-signal. The learner should know exactly what each alert type is for.',
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
        label: 'Expert review updates',
        type: 'toggle',
        description: 'Notify the learner when expert review status changes.',
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
    description: 'Control playback defaults, transcript support, and low-bandwidth behavior for audio-heavy study paths.',
    eyebrow: 'Audio & Playback',
    icon: Volume2,
    accent: 'amber',
    helperBadge: 'Playback',
    helperCardTitle: 'Runtime impact',
    helperCardBody: 'These settings affect listening and speaking surfaces immediately, especially on slower networks.',
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
        key: 'showTranscriptPrompts',
        label: 'Show transcript prompts when available',
        type: 'toggle',
        description: 'Reveal transcript support hints on listening and speaking review surfaces.',
        icon: Captions,
        primaryTag: 'Transcript Help',
        secondaryTag: 'Review cue',
        secondaryTagTone: 'muted',
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
    description: 'Make reading, audio review, and navigation easier to use for different learner needs.',
    eyebrow: 'Accessibility',
    icon: Accessibility,
    accent: 'emerald',
    helperBadge: 'Accessibility',
    helperCardTitle: 'Inclusive defaults',
    helperCardBody: 'Accessibility settings should help instantly, not sit as decorative placeholders.',
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
    description: 'Keep the exam window, study volume, destination regulator, and reminder cadence in one practical surface.',
    eyebrow: 'Study Preferences',
    icon: Calendar,
    accent: 'navy',
    helperBadge: 'Study Planning',
    helperCardTitle: 'Planning impact',
    helperCardBody: 'These values influence readiness, study-plan pacing, and reminder timing throughout the learner app.',
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
        label: 'Target country or regulator',
        type: 'text',
        description: 'Helps describe the exam destination context.',
        icon: Globe2,
        primaryTag: 'Destination',
        secondaryTag: 'Regulator context',
        secondaryTagTone: 'muted',
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
    description: 'Set score targets and weak-skill focus so the app can explain what the learner is aiming for.',
    eyebrow: 'Target Scores',
    icon: Settings2,
    accent: 'purple',
    helperBadge: 'Target Scores',
    helperCardTitle: 'Goal framing',
    helperCardBody: 'Use score targets and weak-skill focus to keep study direction explicit, especially for Writing and Speaking.',
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
    'w-full rounded-2xl border border-gray-200 bg-white px-4 py-3 text-sm text-navy outline-none transition-shadow',
    accentStyles[accent].inputFocus,
  );
}

function toggleFocusClasses(accent: LearnerSurfaceAccent) {
  return accentStyles[accent].inputFocus
    .replace('focus:border-', 'focus-visible:border-')
    .replace('focus:ring-', 'focus-visible:ring-');
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
    <section className={cn('overflow-hidden rounded-[24px] border shadow-sm', palette.helperSurface)}>
      <div className={cn('bg-gradient-to-br p-5 sm:p-6', palette.helperGlow)}>
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex items-start gap-4">
            <div className={cn('flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl border', palette.softBadge)}>
              <Icon className="h-5 w-5" />
            </div>
            <div>
              <div className="flex flex-wrap items-center gap-2">
                <Badge className={cn('rounded-full px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.16em]', palette.badge)}>
                  {helperBadge}
                </Badge>
                <Badge variant="muted" className="rounded-full px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.16em]">
                  {configuredFieldCount}/{totalFieldCount} configured
                </Badge>
              </div>
              <h2 className="mt-3 text-lg font-bold text-navy">{title}</h2>
              <p className="mt-1 max-w-3xl text-sm leading-6 text-muted">{body}</p>
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
}: {
  accent: LearnerSurfaceAccent;
  data: SettingsSectionData;
  onChange: (key: string, value: string | boolean) => void;
}) {
  const config = SECTION_CONFIG[data.section];
  const palette = accentStyles[accent];

  return (
    <div className="space-y-5">
      {config.fields.map((field) => {
        const value = fieldValue(data.values, field);
        const status = fieldStatus(field, value);
        const FieldIcon = field.icon;

        return (
          <div key={field.key} className="rounded-[24px] border border-gray-200 bg-surface p-5 shadow-sm transition-shadow duration-200 hover:shadow-md">
            <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
              <div className="flex min-w-0 flex-1 items-start gap-4">
                <div className={cn('flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl border', palette.softBadge)}>
                  <FieldIcon className="h-5 w-5" />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <label className="text-base font-bold text-navy" htmlFor={field.key}>{field.label}</label>
                    {renderTag(field.primaryTag, accent, field.primaryTagTone ?? 'section')}
                    {field.secondaryTag ? renderTag(field.secondaryTag, accent, field.secondaryTagTone ?? 'muted') : null}
                    <Badge variant={status.variant} className="rounded-full px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em]">
                      {status.label}
                    </Badge>
                  </div>
                  <p className="mt-2 max-w-2xl text-sm leading-6 text-muted">{field.description}</p>
                </div>
              </div>

              {field.type === 'toggle' ? (
                <button
                  id={field.key}
                  type="button"
                  onClick={() => onChange(field.key, !Boolean(value))}
                  className={cn(
                    'relative inline-flex h-7 w-12 shrink-0 items-center rounded-full transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2',
                    Boolean(value) ? palette.toggleOn : 'bg-gray-200',
                    toggleFocusClasses(accent),
                  )}
                  aria-checked={Boolean(value)}
                  aria-label={`Toggle ${field.label}`}
                  role="switch"
                >
                  <span className={cn('inline-block h-5 w-5 transform rounded-full bg-white transition-transform', Boolean(value) ? 'translate-x-6' : 'translate-x-1')} />
                </button>
              ) : null}
            </div>

            {field.type !== 'toggle' ? (
              <div className="mt-4">
                {field.type === 'select' ? (
                  <select
                    id={field.key}
                    className={inputClasses(accent)}
                    value={String(value)}
                    onChange={(event) => onChange(field.key, event.target.value)}
                  >
                    <option value="">Select an option</option>
                    {(field.options ?? []).map((option) => (
                      <option key={option.value} value={option.value}>{option.label}</option>
                    ))}
                  </select>
                ) : field.type === 'textarea' ? (
                  <textarea
                    id={field.key}
                    className={cn('min-h-28', inputClasses(accent))}
                    value={String(value)}
                    onChange={(event) => onChange(field.key, event.target.value)}
                  />
                ) : (
                  <input
                    id={field.key}
                    type={field.type}
                    min={field.min}
                    max={field.max}
                    className={inputClasses(accent)}
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
      <div className="rounded-[24px] border-2 border-red-300 bg-red-50/50 p-6 shadow-sm">
        <div className="flex items-start gap-4">
          <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl border border-red-200 bg-red-100">
            <Trash2 className="h-5 w-5 text-red-600" />
          </div>
          <div className="flex-1">
            <h3 className="text-lg font-bold text-red-900">Delete your account</h3>
            <p className="mt-2 max-w-2xl text-sm leading-6 text-red-800">
              This action cannot be undone. After 30 days, your account and all associated data will be permanently deleted. During the grace period you can contact support to cancel the deletion.
            </p>
          </div>
        </div>

        <div className="mt-6 space-y-4">
          <div>
            <label htmlFor="delete-password" className="block text-sm font-semibold text-red-900">
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
              className="mt-1.5 w-full rounded-2xl border border-red-200 bg-white px-4 py-3 text-sm text-navy outline-none transition-shadow focus:border-red-400 focus:ring-2 focus:ring-red-100"
            />
          </div>

          <div>
            <label htmlFor="delete-reason" className="block text-sm font-semibold text-red-900">
              Reason for leaving <span className="font-normal text-red-600">(optional)</span>
            </label>
            <textarea
              id="delete-reason"
              placeholder="Why are you leaving? (optional)"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              className="mt-1.5 min-h-24 w-full rounded-2xl border border-red-200 bg-white px-4 py-3 text-sm text-navy outline-none transition-shadow focus:border-red-400 focus:ring-2 focus:ring-red-100"
            />
          </div>

          {deleteError ? <InlineAlert variant="error">{deleteError}</InlineAlert> : null}

          <button
            type="button"
            disabled={!password.trim() || deleting}
            onClick={handleDelete}
            className={cn(
              'inline-flex items-center gap-2 rounded-2xl px-6 py-3 text-sm font-bold text-white transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-red-400 focus-visible:ring-offset-2',
              password.trim() && !deleting
                ? 'bg-red-600 hover:bg-red-700 cursor-pointer'
                : 'bg-red-300 cursor-not-allowed',
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
  const section = toSectionId(params?.section);

  const [data, setData] = useState<SettingsSectionData | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const config = useMemo(() => (section ? SECTION_CONFIG[section] : null), [section]);
  const configuredFieldCount = useMemo(() => {
    if (!config || !data) return 0;
    return config.fields.filter((field) => isFieldConfigured(field, fieldValue(data.values, field))).length;
  }, [config, data]);

  useEffect(() => {
    if (!section) {
      setLoading(false);
      setError('This settings section does not exist.');
      return;
    }

    if (section === 'notifications' || section === 'danger-zone') {
      setLoading(false);
      setData(null);
      setError(null);
      analytics.track('content_view', { page: 'settings-section', section });
      return;
    }

    let cancelled = false;
    analytics.track('content_view', { page: 'settings-section', section });

    (async () => {
      try {
        const result = await fetchSettingsSection(section);
        if (!cancelled) {
          setData(result);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load this settings section.');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [section]);

  const handleChange = (key: string, value: string | boolean) => {
    if (!data) return;
    setData((current) => current ? { ...current, values: setNestedValue(current.values, key, value) } : current);
    setSuccessMessage(null);
  };

  const handleSave = async () => {
    if (!section || !data) return;
    setSaving(true);
    setError(null);
    setSuccessMessage(null);
    try {
      const response = await updateSettingsSection(section as Parameters<typeof updateSettingsSection>[0], data.values);
      setData({ section, values: response.values ?? data.values });
      setSuccessMessage('Settings saved.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save this settings section.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <LearnerDashboardShell pageTitle={config?.title ?? 'Settings'} subtitle={config?.description ?? 'Manage learner settings'} backHref="/settings">
      <div className="space-y-8">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/settings')}>
          <ArrowLeft className="h-4 w-4" />
          Back to Settings
        </Button>

        {config ? (
          <LearnerPageHero
            eyebrow={config.eyebrow}
            icon={config.icon}
            accent={config.accent}
            title={`Keep ${config.title.toLowerCase()} settings clear before you change them`}
            description={`Use this section to review ${config.title.toLowerCase()} controls with the outcome of each change kept explicit.`}
            highlights={[
              { icon: config.icon, label: 'Controls', value: `${config.fields.length} settings` },
              { icon: Save, label: 'Configured', value: `${configuredFieldCount} set` },
              { icon: Settings2, label: 'Save state', value: saving ? 'Saving...' : successMessage ? 'Saved' : 'Ready to edit' },
            ]}
          />
        ) : null}

        {loading ? (
          <div className="space-y-4">
            {[1, 2, 3].map((item) => <Skeleton key={item} className="h-40 rounded-[24px]" />)}
          </div>
        ) : null}

        {!loading && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        {!loading && successMessage ? <InlineAlert variant="success">{successMessage}</InlineAlert> : null}

        {!loading && section === 'notifications' && config ? (
          <>
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
          </>
        ) : null}

        {!loading && section === 'danger-zone' && config ? (
          <>
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
          </>
        ) : null}

        {!loading && data && config ? (
          <>
            <SettingsSectionHelperCard
              accent={config.accent}
              helperBadge={config.helperBadge}
              icon={config.icon}
              title={config.helperCardTitle}
              body={config.helperCardBody}
              configuredFieldCount={configuredFieldCount}
              totalFieldCount={config.fields.length}
            />

            <SettingsSectionForm accent={config.accent} data={data} onChange={handleChange} />

            <div className="rounded-[24px] border border-gray-200 bg-surface px-5 py-4 shadow-sm">
              <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                <div className="space-y-2">
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge className={cn('rounded-full px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em]', accentStyles[config.accent].badge)}>
                      {configuredFieldCount}/{config.fields.length} configured
                    </Badge>
                    <Badge variant={successMessage ? 'success' : 'muted'} className="rounded-full px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em]">
                      {saving ? 'Saving...' : successMessage ? 'Saved' : 'Ready to save'}
                    </Badge>
                  </div>
                  <p className="text-sm text-muted">
                    Save when you are ready. Low-bandwidth, transcript, and reminder preferences will be used by the learner app after this update.
                  </p>
                </div>
                <Button onClick={handleSave} loading={saving} className="gap-2">
                  <Save className="h-4 w-4" />
                  Save changes
                </Button>
              </div>
            </div>
          </>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
