'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTheme } from 'next-themes';
import {
  Bell,
  CalendarClock,
  KeyRound,
  Monitor,
  Moon,
  Settings2,
  ShieldCheck,
  Sun,
  UserRound,
} from 'lucide-react';
import {
  ExpertRouteHero,
  ExpertRouteSectionHeader,
  ExpertRouteWorkspace,
} from '@/components/domain/expert-route-surface';
import { NotificationPreferencesPanel } from '@/components/layout/notification-preferences-panel';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { useExpertAuth } from '@/lib/hooks/use-expert-auth';
import { cn } from '@/lib/utils';

// IMPORTANT: this page must only use surfaces that work for the expert role.
// `/v1/settings/*` is learner-only server-side (experts have no Users row and
// receive a 403), so account preferences here go through
// `/v1/notifications/preferences` (shared across roles, via the notification
// center context) plus the client-side next-themes appearance state. Do NOT
// add `fetchSettingsSection`/`updateSettingsSection` calls to this page.

const THEME_OPTIONS = [
  { value: 'light', label: 'Light', hint: 'Bright surfaces for well-lit rooms.', icon: Sun },
  { value: 'dark', label: 'Dark', hint: 'Low-glare marking during long sessions.', icon: Moon },
  { value: 'system', label: 'System', hint: 'Follow the device preference.', icon: Monitor },
] as const;

function AppearanceCard() {
  const { theme, setTheme } = useTheme();
  const [mounted, setMounted] = useState(false);

  // next-themes resolves on the client only; render the options after mount so
  // the selected state never mismatches the server-rendered markup.
  useEffect(() => {
    const timer = window.setTimeout(() => setMounted(true), 0);
    return () => window.clearTimeout(timer);
  }, []);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Appearance</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="mb-4 text-sm text-muted">
          The same theme control as the header toggle, kept on this device. It applies across the whole expert console.
        </p>
        <div className="grid gap-3 sm:grid-cols-3">
          {THEME_OPTIONS.map((option) => {
            const Icon = option.icon;
            const active = mounted && (theme ?? 'system') === option.value;
            return (
              <button
                key={option.value}
                type="button"
                onClick={() => setTheme(option.value)}
                aria-pressed={active}
                className={cn(
                  'pressable flex items-start gap-3 rounded-2xl border px-4 py-3 text-left transition-colors',
                  active
                    ? 'border-primary/25 bg-primary/5 text-navy'
                    : 'border-border bg-surface text-navy hover:border-primary/40',
                )}
              >
                <Icon className={cn('mt-0.5 h-4 w-4 shrink-0', active ? 'text-primary' : 'text-muted')} aria-hidden="true" />
                <span className="space-y-1">
                  <span className="block text-sm font-semibold">{option.label}</span>
                  <span className="block text-xs text-muted">{option.hint}</span>
                </span>
              </button>
            );
          })}
        </div>
      </CardContent>
    </Card>
  );
}

function LinkCard({
  title,
  description,
  icon: Icon,
  actionLabel,
  onOpen,
  badge,
}: {
  title: string;
  description: string;
  icon: typeof UserRound;
  actionLabel: string;
  onOpen: () => void;
  badge?: React.ReactNode;
}) {
  return (
    <Card>
      <CardContent className="p-5">
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary ring-1 ring-primary/10">
            <Icon className="h-5 w-5" aria-hidden="true" />
          </div>
          <div className="min-w-0 flex-1 space-y-1">
            <div className="flex flex-wrap items-center gap-2">
              <p className="text-sm font-bold text-navy">{title}</p>
              {badge}
            </div>
            <p className="text-sm text-muted">{description}</p>
          </div>
        </div>
        <div className="mt-4">
          <Button variant="outline" size="sm" onClick={onOpen}>
            {actionLabel}
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

export default function ExpertSettingsPage() {
  const router = useRouter();
  const { expert } = useExpertAuth();
  const { user } = useCurrentUser();

  const mfaEnabled = Boolean(user?.isAuthenticatorEnabled);

  return (
    <ExpertRouteWorkspace role="main" aria-label="Expert Settings">
      <ExpertRouteHero
        eyebrow="Expert Settings"
        icon={Settings2}
        accent="primary"
        title="Keep your tutor account tuned"
        description="Notification delivery, appearance, and account security for the expert console. Profile, qualifications, rates, and availability live in their own workspaces below."
        highlights={[
          { icon: Bell, label: 'Notifications', value: 'Shared account' },
          { icon: ShieldCheck, label: 'MFA', value: mfaEnabled ? 'Enabled' : 'Not enabled' },
          { icon: CalendarClock, label: 'Timezone', value: expert?.timezone ?? '-' },
        ]}
      />

      <section className="space-y-4">
        <ExpertRouteSectionHeader
          eyebrow="Delivery"
          title="Notification preferences"
          description="Channels, quiet hours, and per-event overrides are stored once per account and apply across the learner, expert, and admin shells."
        />
        <NotificationPreferencesPanel
          title="Notification Preferences"
          description="Manage delivery channels, quiet hours, and browser push for your tutor account."
        />
      </section>

      <section className="space-y-4">
        <ExpertRouteSectionHeader
          eyebrow="Display"
          title="Appearance"
          description="Choose how the console renders on this device."
        />
        <AppearanceCard />
      </section>

      <section className="space-y-4">
        <ExpertRouteSectionHeader
          eyebrow="Account"
          title="Security, profile & availability"
          description="Deep links into the dedicated workspaces that own these records."
        />
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
          <LinkCard
            title="Multi-factor authentication"
            description={
              mfaEnabled
                ? 'An authenticator app already protects this account. Open setup to review or rotate it.'
                : 'Privileged access is safer with an authenticator app. Setup takes about two minutes.'
            }
            icon={KeyRound}
            actionLabel={mfaEnabled ? 'Manage MFA' : 'Set up MFA'}
            onOpen={() => router.push('/mfa/setup?next=/expert/settings')}
            badge={<Badge variant={mfaEnabled ? 'success' : 'warning'}>{mfaEnabled ? 'Enabled' : 'Not enabled'}</Badge>}
          />
          <LinkCard
            title="Profile, qualifications & rates"
            description="Display name, bio, teaching qualifications, and session rates are managed in the onboarding workspace."
            icon={UserRound}
            actionLabel="Open Profile Workspace"
            onOpen={() => router.push('/expert/onboarding')}
          />
          <LinkCard
            title="Availability & schedule"
            description="Weekly availability windows, timezone, and date exceptions drive assignment and booking expectations."
            icon={CalendarClock}
            actionLabel="Open Schedule"
            onOpen={() => router.push('/expert/schedule')}
          />
        </div>
      </section>
    </ExpertRouteWorkspace>
  );
}
