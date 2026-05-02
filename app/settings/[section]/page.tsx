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
import { TARGET_COUNTRY_OPTIONS } from '@/lib/auth/target-countries';
import { deleteAccount } from '@/lib/auth-client';
import { useProfessions } from '@/lib/hooks/use-professions';
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
    heroTitle: 'Keep profile settings clear before you change them',
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
        description: 'Controls profession-specific writing and speaking material.',
        icon: BriefcaseMedical,
        primaryTag: '',
        secondaryTag: '',
        secondaryTagTone: 'muted',
        options: [
          { value: 'medicine', label: 'Medicine' },
          { value: 'nursing', label: 'Nursing' },
          { value: 'pharmacy', label: 'Pharmacy' },
          { value: 'dentistry', label: 'Dentistry' },
          { value: 'physiotherapy', label: 'Physiotherapy' },
        ],
      },
    ],
  },
  privacy: {
    title: 'Privacy',
    description: 'Manage how recordings, transcripts, and learner evidence are stored or shared.',
    eyebrow: 'Privacy Controls',
    icon: Shield,
    accent: 'rose',
    helperBadge: 'Sensitive Data',
    helperCardTitle: 'Evidence privacy',
    helperCardBody: 'Control how your writing and speaking evidence is stored and who can access it.',
    fields: [
      {
        key: 'storeRecordings',
        label: 'Store speaking recordings',
        type: 'toggle',
        description: 'Keep recordings available for transcript review and tutor feedback.',
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
        label: 'Allow tutor reviewers to access evidence',
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
    'w-full rounded-2xl border border-black/5 bg-white/60 backdrop-blur-xl px-5 py-4 text-base font-bold text-navy outline-none transition-all shadow-inner focus:bg-white focus:shadow-[0_4px_20px_rgb(0,0,0,0.08)] focus:border-primary focus:ring-4 focus:ring-primary/20',
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
    <section className={cn('overflow-hidden rounded-[2rem] border/50 shadow-[0_8px_30px_rgb(0,0,0,0.04)] ring-1 ring-black/5 bg-white/60 backdrop-blur-3xl relative group', palette.helperSurface)}>
      <div className="absolute inset-0 bg-[linear-gradient(45deg,transparent,rgba(255,255,255,0.4),transparent)] -translate-x-[150%] group-hover:translate-x-[150%] transition-transform duration-1000 ease-in-out pointer-events-none" />
      <div className={cn('p-6 sm:p-8 relative z-10', palette.helperGlow)}>
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex items-start gap-5">
            <div className={cn('flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl border shadow-md ring-1 ring-white/50 backdrop-blur-md', palette.softBadge)}>
              <Icon className="h-6 w-6 drop-shadow-sm" />
            </div>
            <div>
              <div className="flex flex-wrap items-center gap-2">
                <Badge className={cn('rounded-full px-3 py-1 text-xs font-black uppercase tracking-widest shadow-sm', palette.badge)}>
                  {helperBadge}
                </Badge>
                <Badge variant="muted" className="rounded-full px-3 py-1 text-xs font-bold uppercase tracking-widest bg-white/50 backdrop-blur-sm">
                  {configuredFieldCount}/{totalFieldCount} configured
                </Badge>
              </div>
              <h2 className="mt-4 text-xl font-black text-navy tracking-tight">{title}</h2>
              <p className="mt-1.5 max-w-3xl text-sm leading-relaxed text-navy/70 font-bold">{body}</p>
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
  const { options: professionOptions } = useProfessions();
  const config = SECTION_CONFIG[data.section];
  const palette = accentStyles[accent];

  if (data.section === 'profile') {
    return (
      <div className="rounded-[2.5rem] border border-black/5 bg-white/70 backdrop-blur-3xl shadow-[0_8px_40px_rgb(0,0,0,0.06)] relative overflow-hidden group">
        <div className="absolute inset-0 bg-gradient-to-br from-white/60 to-transparent pointer-events-none" />
        <div className="relative z-10 flex flex-col">
          {config.fields.map((field, i) => {
            const value = fieldValue(data.values, field);
            const status = fieldStatus(field, value);
            const FieldIcon = field.icon;

            return (
              <div key={field.key} className={cn('p-6 sm:p-8 flex flex-col xl:flex-row xl:items-start gap-6 transition-colors duration-500 hover:bg-white/40', i !== config.fields.length - 1 && 'border-b border-black/5')}>
                <div className="flex min-w-0 flex-1 items-start gap-5">
                  <div className={cn('flex h-16 w-16 shrink-0 items-center justify-center rounded-2xl border shadow-sm ring-1 ring-white/50 backdrop-blur-md', palette.softBadge)}>
                    <FieldIcon className={cn('h-7 w-7 drop-shadow-sm', status.label === 'Set' ? 'text-primary' : '')} />
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2 mb-1.5">
                      <label className="text-xl font-black text-navy tracking-tight" htmlFor={field.key}>{field.label}</label>
                      <Badge className={cn('rounded-full px-3 py-1 font-bold text-[10px] uppercase tracking-widest', palette.badge)}>
                        Personal Info
                      </Badge>
                      <Badge variant={status.variant} className="rounded-full px-2.5 py-1 text-[10px] font-black uppercase tracking-widest shadow-sm">
                        {status.label}
                      </Badge>
                    </div>
                    <p className="max-w-xl text-sm leading-relaxed text-navy/70 font-medium">{field.description}</p>
                  </div>
                </div>
                <div className="w-full xl:w-96 shrink-0">
                  {field.type === 'select' ? (
                    <select
                      id={field.key}
                      className={cn(inputClasses(accent), 'appearance-none cursor-pointer bg-no-repeat bg-[right_1rem_center] bg-[length:1.2em] font-bold h-14')}
                      value={String(value)}
                      onChange={(event) => onChange(field.key, event.target.value)}
                    >
                      <option value="" disabled className="text-navy/30">Select an option…</option>
                      {(field.key === 'professionId' && professionOptions.length > 0 ? professionOptions : (field.options ?? [])).map((option) => (
                        <option key={option.value} value={option.value} className="text-navy font-bold">{option.label}</option>
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
        
        const isStorageOrAudio = field.primaryTag === 'Storage' || field.secondaryTag === 'Audio evidence';

        return (
          <div key={field.key} className={cn('rounded-[2rem] border/50 bg-white/70 backdrop-blur-2xl p-6 sm:p-8 shadow-[0_8px_30px_rgb(0,0,0,0.04)] ring-1 ring-black/5 transition-all duration-500 hover:shadow-[0_8px_40px_rgb(0,0,0,0.08)] hover:-translate-y-1 group relative', status.label === 'Not set' ? 'border-dashed border-gray-300 ring-transparent' : 'border-black/5', data.section === 'privacy' && 'backdrop-blur-xl bg-white/60')}>
            <div className="absolute inset-0 bg-gradient-to-br from-white/40 to-transparent pointer-events-none rounded-[2rem]" />
            <div className="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between relative z-10">
              <div className="flex min-w-0 flex-1 items-start gap-5">
                <div className={cn('flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl border shadow-sm transition-all duration-500 ease-out group-hover:scale-110 group-hover:shadow-md ring-1 ring-white/50 backdrop-blur-md', palette.softBadge)}>
                  <FieldIcon className={cn('h-6 w-6 drop-shadow-sm transition-colors duration-500', Boolean(value) || status.label === 'Set' ? 'text-primary' : '')} />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <label className="text-xl font-black text-navy tracking-tight group-hover:text-primary transition-colors" htmlFor={field.key}>{field.label}</label>
                    <div className={cn("inline-flex overflow-hidden rounded-full shadow-sm ring-1 ring-black/5", isStorageOrAudio && "animate-pulse")}>
                      {renderTag(field.primaryTag, accent, field.primaryTagTone ?? 'section')}
                    </div>
                    {field.secondaryTag ? (
                      <div className={cn("inline-flex overflow-hidden rounded-full shadow-sm ring-1 ring-black/5", isStorageOrAudio && "animate-pulse")}>
                        {renderTag(field.secondaryTag, accent, field.secondaryTagTone ?? 'muted')}
                      </div>
                    ) : null}
                  </div>
                  <p className="mt-2.5 max-w-2xl text-sm leading-relaxed text-navy/70 font-medium">{field.description}</p>
                </div>
              </div>

              {field.type === 'toggle' ? (
                <button
                  id={field.key}
                  type="button"
                  onClick={() => onChange(field.key, !Boolean(value))}
                  className={cn(
                    'relative inline-flex h-10 w-20 shrink-0 items-center rounded-full transition-all duration-500 focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-offset-2 overflow-hidden shadow-inner ring-1 ring-black/5',
                    Boolean(value) ? palette.toggleOn : 'bg-navy/10 hover:bg-navy/15',
                    toggleFocusClasses(accent),
                  )}
                  aria-checked={Boolean(value)}
                  aria-label={`Toggle ${field.label}`}
                  role="switch"
                >
                  <span className={cn('inline-block h-8 w-8 transform rounded-full bg-white shadow-[0_2px_10px_rgba(0,0,0,0.1)] transition-transform duration-500 ease-spring', Boolean(value) ? 'translate-x-10' : 'translate-x-1')} />
                  {Boolean(value) && (
                    <span className="absolute inset-0 bg-white/20 animate-pulse pointer-events-none" />
                  )}
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
                    {(field.key === 'professionId' && professionOptions.length > 0
                      ? professionOptions
                      : (field.options ?? [])
                    ).map((option) => (
                      <option key={option.value} value={option.value} className="text-navy font-bold">{option.label}</option>
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
      <div className="relative min-h-[calc(100dvh-4rem)]">
        <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_top,_var(--tw-gradient-stops))] from-primary/10 via-transparent to-transparent pointer-events-none -z-10 blur-3xl opacity-70" />
        
        <div className="space-y-10 relative z-10 px-4 sm:px-0 pb-20">
          <Button variant="ghost" className="gap-2 rounded-full hover:bg-navy/5 font-bold mt-4" onClick={() => router.push('/settings')}>
            <ArrowLeft className="h-4 w-4" />
            Back to Settings
          </Button>

          {config ? (
            <div className="bg-white/60 backdrop-blur-2xl p-2 sm:p-2 border-0 shadow-[0_8px_30px_rgb(0,0,0,0.04)] ring-1 ring-primary/5 transition-all overflow-hidden relative group rounded-3xl">
              <div className="absolute inset-0 bg-[linear-gradient(45deg,transparent,rgba(255,255,255,0.4),transparent)] -translate-x-[150%] group-hover:translate-x-[150%] transition-transform duration-1000 ease-in-out pointer-events-none" />
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
              {[1, 2, 3].map((item) => <Skeleton key={item} className="h-40 rounded-3xl bg-white/40" />)}
            </div>
          ) : null}

          {!loading && error ? <InlineAlert variant="error" className="shadow-sm">{error}</InlineAlert> : null}
          {!loading && successMessage ? <InlineAlert variant="success" className="shadow-sm">{successMessage}</InlineAlert> : null}

          {!loading && section === 'notifications' && config ? (
            <div className="space-y-8 relative z-20">
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
            <div className="space-y-8 relative z-20">
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

          {!loading && data && config ? (
            <div className="space-y-8 relative z-20">
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

              <div className="rounded-[2rem] border border-black/5 bg-white/70 backdrop-blur-2xl px-6 py-5 shadow-lg relative group overflow-hidden mt-10">
                <div className="absolute inset-0 bg-gradient-to-r from-primary/5 via-transparent to-primary/5 pointer-events-none" />
                <div className="flex flex-col gap-5 sm:flex-row sm:items-center sm:justify-between relative z-10">
                  <div className="space-y-3">
                    <div className="flex flex-wrap items-center gap-3">
                      <Badge className={cn('rounded-full px-3 py-1.5 text-[10px] font-black uppercase tracking-[0.14em] shadow-sm', accentStyles[config.accent].badge)}>
                        {configuredFieldCount}/{config.fields.length} configured
                      </Badge>
                      <Badge variant={successMessage ? 'success' : 'muted'} className="rounded-full px-3 py-1.5 text-[10px] font-black uppercase tracking-[0.14em] bg-white shadow-sm ring-1 ring-black/5">
                        {saving ? 'Saving...' : successMessage ? 'Saved' : 'Ready to save'}
                      </Badge>
                    </div>
                    <p className="text-sm font-medium text-navy/70 leading-relaxed max-w-xl">
                      Save when you are ready. Low-bandwidth, transcript, and reminder preferences will be used by the learner app after this update.
                    </p>
                  </div>
                  <Button onClick={handleSave} loading={saving} size="lg" className="gap-2 rounded-full font-black px-8 shadow-primary/20 shadow-lg group hover:scale-105 transition-all border border-white/20 shrink-0">
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
