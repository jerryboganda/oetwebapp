'use client';

import { useCallback, useEffect, useId, useMemo, useState, type ReactNode } from 'react';
import { ChevronDown, ChevronRight, Cog, ExternalLink, Eye, EyeOff, Loader2, Lock, Save } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Toast } from '@/components/ui/alert';
import { apiClient } from '@/lib/api';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';

/* ───────────────────────── Types ───────────────────────── */

const MASKED = '********' as const;

export interface EmailSettings {
  brevoEnabled: boolean;
  brevoApiKey: string;
  brevoEmailVerificationTemplateId: number | null;
  brevoPasswordResetTemplateId: number | null;
  brevoWelcomeTemplateId: number | null;
  brevoPasswordChangedTemplateId: number | null;
  brevoMfaEnabledTemplateId: number | null;
  brevoAdminInviteTemplateId: number | null;
  brevoSecurityAlertTemplateId: number | null;
  brevoReviewCompletedTemplateId: number | null;
  smtpEnabled: boolean;
  smtpHost: string;
  smtpPort: number | null;
  smtpUsername: string;
  smtpPassword: string;
  smtpEnableSsl: boolean;
  smtpFromAddress: string;
  smtpFromName: string;
}

export interface BillingSettings {
  stripeSecretKey: string;
  stripePublishableKey: string;
  stripeWebhookSecret: string;
  stripeSuccessUrl: string;
  stripeCancelUrl: string;
}

export interface SentrySettings {
  dsn: string;
  environment: string;
  sampleRate: number | null;
}

export interface BackupSettings {
  s3Url: string;
  awsAccessKeyId: string;
  awsSecretAccessKey: string;
  gpgPassphrase: string;
  alertWebhook: string;
}

export interface OAuthSettings {
  googleEnabled: boolean;
  googleClientId: string;
  googleClientSecret: string;
  appleClientId: string;
  appleTeamId: string;
  appleKeyId: string;
  applePrivateKey: string;
  facebookEnabled: boolean;
  facebookAppId: string;
  facebookAppSecret: string;
  linkedInEnabled: boolean;
  linkedInClientId: string;
  linkedInClientSecret: string;
}

export interface PushSettings {
  webPushEnabled: boolean;
  webPushSubject: string;
  webPushPublicKey: string;
  webPushPrivateKey: string;
  apnsKeyId: string;
  apnsTeamId: string;
  apnsBundleId: string;
  apnsAuthKey: string;
  fcmServerKey: string;
  fcmProjectId: string;
}

export interface RuntimeSettingsResponse {
  email: EmailSettings;
  billing: BillingSettings;
  sentry: SentrySettings;
  backup: BackupSettings;
  oauth: OAuthSettings;
  push: PushSettings;
  updatedBy: string | null;
  updatedByUserId?: string | null;
  updatedAt: string | null;
}

type SectionId = 'email' | 'billing' | 'sentry' | 'backup' | 'oauth' | 'push';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

/* ───────────────────────── Field metadata ───────────────────────── */

interface FieldDef<TSection> {
  key: keyof TSection & string;
  label: string;
  hint?: string;
  secret?: boolean;
  type?: 'text' | 'number' | 'url' | 'checkbox';
  placeholder?: string;
}

const EMAIL_FIELDS: FieldDef<EmailSettings>[] = [
  { key: 'brevoEnabled', label: 'Enable Brevo', type: 'checkbox', hint: 'Routes transactional email through Brevo when enabled.' },
  { key: 'brevoApiKey', label: 'Brevo API Key', secret: true, hint: 'Server-side Brevo (Sendinblue) API key for transactional email.' },
  { key: 'brevoEmailVerificationTemplateId', label: 'Brevo Email Verification Template ID', type: 'number', hint: 'Numeric template id used for OTP verification emails.' },
  { key: 'brevoPasswordResetTemplateId', label: 'Brevo Password Reset Template ID', type: 'number', hint: 'Numeric template id used for password-reset emails.' },
  { key: 'brevoWelcomeTemplateId', label: 'Brevo Welcome Template ID', type: 'number' },
  { key: 'brevoPasswordChangedTemplateId', label: 'Brevo Password Changed Template ID', type: 'number' },
  { key: 'brevoMfaEnabledTemplateId', label: 'Brevo MFA Enabled Template ID', type: 'number' },
  { key: 'brevoAdminInviteTemplateId', label: 'Brevo Admin Invite Template ID', type: 'number' },
  { key: 'brevoSecurityAlertTemplateId', label: 'Brevo Security Alert Template ID', type: 'number' },
  { key: 'brevoReviewCompletedTemplateId', label: 'Brevo Review Completed Template ID', type: 'number' },
  { key: 'smtpEnabled', label: 'Enable SMTP fallback', type: 'checkbox', hint: 'Routes email through SMTP when Brevo is disabled.' },
  { key: 'smtpHost', label: 'SMTP Host', hint: 'Fallback SMTP server hostname.' },
  { key: 'smtpPort', label: 'SMTP Port', type: 'number', hint: 'Typically 587 (STARTTLS) or 465 (TLS).' },
  { key: 'smtpUsername', label: 'SMTP Username' },
  { key: 'smtpPassword', label: 'SMTP Password', secret: true },
  { key: 'smtpEnableSsl', label: 'SMTP SSL/TLS', type: 'checkbox' },
  { key: 'smtpFromAddress', label: 'From Address', hint: 'Address used in the From header of outgoing email.' },
  { key: 'smtpFromName', label: 'From Name' },
];

const BILLING_FIELDS: FieldDef<BillingSettings>[] = [
  { key: 'stripeSecretKey', label: 'Stripe Secret Key', secret: true, hint: 'Server-side Stripe key (starts with sk_live_ or sk_test_).' },
  { key: 'stripePublishableKey', label: 'Stripe Publishable Key', hint: 'Client-safe Stripe key (starts with pk_).' },
  { key: 'stripeWebhookSecret', label: 'Stripe Webhook Signing Secret', secret: true, hint: 'Used to verify Stripe webhook signatures (starts with whsec_).' },
  { key: 'stripeSuccessUrl', label: 'Checkout Success URL', type: 'url' },
  { key: 'stripeCancelUrl', label: 'Checkout Cancel URL', type: 'url' },
];

const SENTRY_FIELDS: FieldDef<SentrySettings>[] = [
  { key: 'dsn', label: 'Sentry DSN', hint: 'Project DSN for error reporting.' },
  { key: 'environment', label: 'Sentry Environment', hint: 'e.g. production, staging.' },
  { key: 'sampleRate', label: 'Sentry Sample Rate', type: 'number', hint: 'Number between 0 and 1 (e.g. 0.1 = 10%).' },
];

const BACKUP_FIELDS: FieldDef<BackupSettings>[] = [
  { key: 's3Url', label: 'Backup S3 URL', type: 'url', hint: 's3://bucket/prefix or https-style endpoint.' },
  { key: 'awsAccessKeyId', label: 'AWS Access Key ID' },
  { key: 'awsSecretAccessKey', label: 'AWS Secret Access Key', secret: true },
  { key: 'gpgPassphrase', label: 'GPG Passphrase', secret: true, hint: 'Used to encrypt backup archives.' },
  { key: 'alertWebhook', label: 'Backup Alert Webhook', type: 'url', hint: 'POSTed when a backup fails.' },
];

const OAUTH_FIELDS: FieldDef<OAuthSettings>[] = [
  { key: 'googleEnabled', label: 'Enable Google OAuth', type: 'checkbox' },
  { key: 'googleClientId', label: 'Google Client ID' },
  { key: 'googleClientSecret', label: 'Google Client Secret', secret: true },
  { key: 'appleClientId', label: 'Apple Client ID', hint: 'The service identifier (e.g. com.example.web).' },
  { key: 'appleTeamId', label: 'Apple Team ID' },
  { key: 'appleKeyId', label: 'Apple Key ID' },
  { key: 'applePrivateKey', label: 'Apple Private Key (.p8 contents)', secret: true },
  { key: 'facebookEnabled', label: 'Enable Facebook OAuth', type: 'checkbox' },
  { key: 'facebookAppId', label: 'Facebook App ID' },
  { key: 'facebookAppSecret', label: 'Facebook App Secret', secret: true },
  { key: 'linkedInEnabled', label: 'Enable LinkedIn OAuth', type: 'checkbox' },
  { key: 'linkedInClientId', label: 'LinkedIn Client ID' },
  { key: 'linkedInClientSecret', label: 'LinkedIn Client Secret', secret: true },
];

const PUSH_FIELDS: FieldDef<PushSettings>[] = [
  { key: 'webPushEnabled', label: 'Enable Web Push', type: 'checkbox' },
  { key: 'webPushSubject', label: 'Web Push Subject', hint: 'mailto: or https: contact used by push services.' },
  { key: 'webPushPublicKey', label: 'Web Push Public Key' },
  { key: 'webPushPrivateKey', label: 'Web Push Private Key', secret: true },
  { key: 'apnsKeyId', label: 'APNs Key ID' },
  { key: 'apnsTeamId', label: 'APNs Team ID' },
  { key: 'apnsBundleId', label: 'APNs Bundle ID', hint: 'iOS app bundle identifier.' },
  { key: 'apnsAuthKey', label: 'APNs Auth Key (.p8 contents)', secret: true },
  { key: 'fcmServerKey', label: 'FCM Server Key', secret: true },
  { key: 'fcmProjectId', label: 'FCM Project ID' },
];

const SECTION_META: { id: SectionId; title: string; description: string }[] = [
  { id: 'email', title: 'Email (Brevo + SMTP)', description: 'Transactional email delivery via Brevo with SMTP fallback.' },
  { id: 'billing', title: 'Billing (Stripe)', description: 'Stripe Checkout, Customer Portal, and webhook signing.' },
  { id: 'sentry', title: 'Sentry', description: 'Error reporting and performance monitoring.' },
  { id: 'backup', title: 'Backup S3', description: 'Off-site database and media backup destination.' },
  { id: 'oauth', title: 'OAuth (Google + Apple + Facebook + LinkedIn)', description: 'Social sign-in providers, enable flags, and client secrets.' },
  { id: 'push', title: 'Push (Web Push + APNs + FCM)', description: 'Browser and native push notifications via Web Push, Apple, and Firebase.' },
];

const CONFIGURATION_SURFACES = [
  {
    title: 'ElevenLabs realtime STT and voices',
    href: '/admin/content/conversation/settings',
    description:
      'Manage encrypted ElevenLabs STT keys, realtime provider, model, spend cap, topology, consent gates, and transcript controls.',
  },
  {
    title: 'AI providers, keys, failover, and budgets',
    href: '/admin/ai-providers',
    description:
      'Register OpenAI-compatible, Anthropic, Copilot, Azure, ElevenLabs, ASR, TTS, OCR, and PDF providers with key rotation, retries, circuit breakers, model allowlists, and tests.',
  },
  {
    title: 'AI usage quotas and kill switches',
    href: '/admin/ai-usage',
    description:
      'Control platform budget, BYOK policy, feature disable lists, overage behavior, and live spend monitoring.',
  },
  {
    title: 'Notification channels, caps, and test sends',
    href: '/admin/notifications',
    description:
      'Configure learner, expert, and admin notification policy, per-event overrides, rate caps, email mode, and delivery audit/test flows.',
  },
  {
    title: 'Mobile, RevenueCat, desktop, and launch gates',
    href: '/admin/launch-readiness',
    description:
      'Set mobile billing policy, RevenueCat public SDK keys, IAP product IDs, legal/privacy evidence, protected smoke URLs, desktop signing, and rollout readiness.',
  },
] as const;

/* ───────────────────────── Helpers ───────────────────────── */

function emptyResponse(): RuntimeSettingsResponse {
  return {
    email: {
      brevoEnabled: false,
      brevoApiKey: '',
      brevoEmailVerificationTemplateId: null,
      brevoPasswordResetTemplateId: null,
      brevoWelcomeTemplateId: null,
      brevoPasswordChangedTemplateId: null,
      brevoMfaEnabledTemplateId: null,
      brevoAdminInviteTemplateId: null,
      brevoSecurityAlertTemplateId: null,
      brevoReviewCompletedTemplateId: null,
      smtpEnabled: false,
      smtpHost: '',
      smtpPort: null,
      smtpUsername: '',
      smtpPassword: '',
      smtpEnableSsl: false,
      smtpFromAddress: '',
      smtpFromName: '',
    },
    billing: {
      stripeSecretKey: '',
      stripePublishableKey: '',
      stripeWebhookSecret: '',
      stripeSuccessUrl: '',
      stripeCancelUrl: '',
    },
    sentry: { dsn: '', environment: '', sampleRate: null },
    backup: {
      s3Url: '',
      awsAccessKeyId: '',
      awsSecretAccessKey: '',
      gpgPassphrase: '',
      alertWebhook: '',
    },
    oauth: {
      googleEnabled: false,
      googleClientId: '',
      googleClientSecret: '',
      appleClientId: '',
      appleTeamId: '',
      appleKeyId: '',
      applePrivateKey: '',
      facebookEnabled: false,
      facebookAppId: '',
      facebookAppSecret: '',
      linkedInEnabled: false,
      linkedInClientId: '',
      linkedInClientSecret: '',
    },
    push: {
      webPushEnabled: false,
      webPushSubject: '',
      webPushPublicKey: '',
      webPushPrivateKey: '',
      apnsKeyId: '',
      apnsTeamId: '',
      apnsBundleId: '',
      apnsAuthKey: '',
      fcmServerKey: '',
      fcmProjectId: '',
    },
    updatedBy: null,
    updatedByUserId: null,
    updatedAt: null,
  };
}

function normalizeResponse(data: Partial<RuntimeSettingsResponse>): RuntimeSettingsResponse {
  const empty = emptyResponse();
  return {
    ...empty,
    ...data,
    email: { ...empty.email, ...data.email },
    billing: { ...empty.billing, ...data.billing },
    sentry: { ...empty.sentry, ...data.sentry },
    backup: { ...empty.backup, ...data.backup },
    oauth: { ...empty.oauth, ...data.oauth },
    push: { ...empty.push, ...data.push },
  };
}

function parseNullableNumberInput(value: string): number | null {
  if (value.trim() === '') return null;
  return Number(value);
}

function getApiErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message) return err.message;
  if (typeof err === 'string') return err;
  return fallback;
}

function formatTimestamp(value: string | null): string {
  if (!value) return 'unknown time';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString();
}

/* ───────────────────────── SecretField ───────────────────────── */

interface SecretFieldProps {
  label: string;
  hint?: string;
  /** Original server value: '' = unset, '********' = set, anything else = literal. */
  serverValue: string;
  /** Working draft value (same conventions as serverValue). */
  draftValue: string;
  onChange: (next: string) => void;
}

function SecretField({ label, hint, serverValue, draftValue, onChange }: SecretFieldProps) {
  const reactId = useId();
  const inputId = `secret-${reactId}`;
  const [reveal, setReveal] = useState(false);

  const isSetOnServer = serverValue === MASKED;
  const isUntouched = draftValue === serverValue;
  // Display empty when untouched + server-set so user sees the placeholder, not the literal '********'.
  const displayValue = isUntouched && isSetOnServer ? '' : draftValue;

  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-center justify-between gap-2">
        <label htmlFor={inputId} className="text-sm font-semibold tracking-tight text-navy">
          {label}
        </label>
        {isSetOnServer ? (
          <Badge variant="success">Set</Badge>
        ) : (
          <Badge variant="muted">Not set</Badge>
        )}
      </div>
      <div className="flex items-stretch gap-2">
        <div className="relative flex-1">
          <input
            id={inputId}
            type={reveal ? 'text' : 'password'}
            value={displayValue}
            placeholder={isSetOnServer ? MASKED : 'Enter value'}
            onChange={(event) => onChange(event.target.value)}
            autoComplete="off"
            spellCheck={false}
            className="w-full rounded-2xl border border-gray-200 bg-background-light px-4 py-3 pr-12 text-sm text-navy shadow-sm transition focus:border-primary focus:outline-none focus:ring-4 focus:ring-primary/15"
          />
          <button
            type="button"
            onClick={() => setReveal((v) => !v)}
            className="absolute inset-y-0 right-2 my-auto flex h-9 w-9 items-center justify-center rounded-xl text-muted hover:bg-lavender/40"
            aria-label={reveal ? `Hide ${label}` : `Show ${label}`}
          >
            {reveal ? <EyeOff className="h-4 w-4" aria-hidden="true" /> : <Eye className="h-4 w-4" aria-hidden="true" />}
          </button>
        </div>
        {isSetOnServer && (
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => onChange('')}
            aria-label={`Clear ${label}`}
          >
            Clear
          </Button>
        )}
      </div>
      {hint ? <p className="text-xs leading-5 text-muted">{hint}</p> : null}
    </div>
  );
}

/* ───────────────────────── PlainField ───────────────────────── */

interface PlainFieldProps {
  label: string;
  hint?: string;
  type?: 'text' | 'number' | 'url';
  value: string | number | null | undefined;
  onChange: (next: string) => void;
}

function PlainField({ label, hint, type = 'text', value, onChange }: PlainFieldProps) {
  const reactId = useId();
  const inputId = `plain-${reactId}`;
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={inputId} className="text-sm font-semibold tracking-tight text-navy">
        {label}
      </label>
      <input
        id={inputId}
        type={type}
        value={value === null || value === undefined ? '' : String(value)}
        onChange={(event) => onChange(event.target.value)}
        spellCheck={false}
        className="w-full rounded-2xl border border-gray-200 bg-background-light px-4 py-3 text-sm text-navy shadow-sm transition focus:border-primary focus:outline-none focus:ring-4 focus:ring-primary/15"
      />
      {hint ? <p className="text-xs leading-5 text-muted">{hint}</p> : null}
    </div>
  );
}

interface BooleanFieldProps {
  label: string;
  hint?: string;
  checked: boolean;
  onChange: (next: boolean) => void;
}

function BooleanField({ label, hint, checked, onChange }: BooleanFieldProps) {
  const reactId = useId();
  const inputId = `bool-${reactId}`;
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={inputId} className="flex items-center gap-3 rounded-2xl border border-gray-200 bg-background-light px-4 py-3 text-sm font-semibold tracking-tight text-navy shadow-sm">
        <input
          id={inputId}
          type="checkbox"
          checked={checked}
          onChange={(event) => onChange(event.target.checked)}
          className="h-4 w-4 rounded border-gray-300 text-primary focus:ring-primary"
        />
        {label}
      </label>
      {hint ? <p className="text-xs leading-5 text-muted">{hint}</p> : null}
    </div>
  );
}

/* ───────────────────────── Section ───────────────────────── */

interface SectionProps {
  id: SectionId;
  title: string;
  description: string;
  open: boolean;
  onToggle: () => void;
  children: ReactNode;
}

function Section({ id, title, description, open, onToggle, children }: SectionProps) {
  const headingId = `runtime-settings-${id}-heading`;
  return (
    <section
      role="region"
      aria-labelledby={headingId}
      className="rounded-2xl border border-border bg-surface shadow-sm"
    >
      <button
        type="button"
        onClick={onToggle}
        aria-expanded={open}
        aria-controls={`runtime-settings-${id}-body`}
        className="flex w-full items-center justify-between gap-4 px-5 py-4 text-left"
      >
        <span>
          <span id={headingId} className="block text-base font-semibold text-navy">
            {title}
          </span>
          <span className="mt-0.5 block text-xs text-muted">{description}</span>
        </span>
        {open ? (
          <ChevronDown className="h-5 w-5 text-muted" aria-hidden="true" />
        ) : (
          <ChevronRight className="h-5 w-5 text-muted" aria-hidden="true" />
        )}
      </button>
      {open && (
        <div id={`runtime-settings-${id}-body`} className="border-t border-border px-5 py-5">
          <div className="grid grid-cols-1 gap-5 md:grid-cols-2">{children}</div>
        </div>
      )}
    </section>
  );
}

/* ───────────────────────── Loading skeleton ───────────────────────── */

function LoadingState() {
  return (
    <div className="space-y-4" aria-busy="true" aria-live="polite">
      {SECTION_META.map((s) => (
        <div key={s.id} className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <Skeleton className="h-5 w-1/3" />
          <Skeleton className="mt-3 h-4 w-2/3" />
          <div className="mt-4 grid grid-cols-1 gap-3 md:grid-cols-2">
            <Skeleton className="h-12 w-full" />
            <Skeleton className="h-12 w-full" />
          </div>
        </div>
      ))}
    </div>
  );
}

/* ───────────────────────── Locked state ───────────────────────── */

function LockedState() {
  return (
    <section className="mx-auto flex min-h-[40vh] w-full max-w-2xl items-center justify-center px-4 py-12">
      <div className="rounded-[2rem] border border-amber-100 bg-white p-8 text-center shadow-sm">
        <Lock className="mx-auto h-10 w-10 text-amber-500" aria-hidden="true" />
        <h2 className="mt-4 text-xl font-semibold text-navy">System administrator access required</h2>
        <p className="mt-2 text-sm text-muted">
          Runtime Settings exposes production secrets and is gated behind the{' '}
          <code className="rounded bg-gray-100 px-1.5 py-0.5 text-xs">system_admin</code> permission.
          Ask a super-admin to grant access if you need to change Brevo, Stripe, Sentry, OAuth, push, or backup
          credentials from this UI.
        </p>
      </div>
    </section>
  );
}

function ConfigurationCoverage() {
  return (
    <section className="mb-5 rounded-2xl border border-border bg-surface p-4 shadow-sm" aria-labelledby="runtime-settings-coverage-heading">
      <div className="flex flex-col gap-1">
        <h2 id="runtime-settings-coverage-heading" className="text-base font-semibold text-navy">
          Admin-managed provider coverage
        </h2>
        <p className="text-sm leading-6 text-slate-700 dark:text-muted">
          Secret-adjacent runtime settings live here. Feature-specific providers and operational policies are also
          admin-managed from the linked control surfaces below, so operators do not need to edit committed code or
          server env files for ElevenLabs, AI budgets, notifications, RevenueCat, or launch evidence.
        </p>
      </div>
      <div className="mt-4 grid gap-3 md:grid-cols-2">
        {CONFIGURATION_SURFACES.map((surface) => (
          <a
            key={surface.href}
            href={surface.href}
            className="group rounded-2xl border border-border bg-background-light p-4 shadow-sm transition hover:border-primary/40 hover:bg-surface focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            <span className="flex items-start justify-between gap-3">
              <span className="text-sm font-semibold text-navy">{surface.title}</span>
              <ExternalLink className="mt-0.5 h-4 w-4 shrink-0 text-slate-700 transition group-hover:text-primary dark:text-muted" aria-hidden="true" />
            </span>
            <span className="mt-2 block text-xs leading-5 text-slate-700 dark:text-muted">{surface.description}</span>
          </a>
        ))}
      </div>
    </section>
  );
}

/* ───────────────────────── Main client ───────────────────────── */

export function RuntimeSettingsClient() {
  const { user, loading: authLoading } = useAuth();
  const isSystemAdmin = hasPermission(user?.adminPermissions ?? null, AdminPermission.SystemAdmin);

  const [server, setServer] = useState<RuntimeSettingsResponse | null>(null);
  const [draft, setDraft] = useState<RuntimeSettingsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const [openSections, setOpenSections] = useState<Record<SectionId, boolean>>({
    email: true,
    billing: false,
    sentry: false,
    backup: false,
    oauth: false,
    push: false,
  });

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await apiClient.get<RuntimeSettingsResponse>('/v1/admin/runtime-settings');
      // Defensive merge so missing keys never blow up the UI.
      const merged = normalizeResponse(data);
      setServer(merged);
      setDraft(merged);
    } catch (err) {
      setToast({ variant: 'error', message: getApiErrorMessage(err, 'Failed to load runtime settings.') });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!isSystemAdmin) return;
    void load();
  }, [isSystemAdmin, load]);

  const toggleSection = useCallback((id: SectionId) => {
    setOpenSections((prev) => ({ ...prev, [id]: !prev[id] }));
  }, []);

  const updateField = useCallback(
    <S extends SectionId, K extends keyof RuntimeSettingsResponse[S] & string>(
      section: S,
      key: K,
      value: RuntimeSettingsResponse[S][K],
    ) => {
      setDraft((prev) => {
        if (!prev) return prev;
        return {
          ...prev,
          [section]: {
            ...prev[section],
            [key]: value,
          },
        } as RuntimeSettingsResponse;
      });
    },
    [],
  );

  const handleSave = useCallback(async () => {
    if (!draft) return;
    setSaving(true);
    try {
      await apiClient.put('/v1/admin/runtime-settings', draft);
      await load();
      setToast({ variant: 'success', message: 'Runtime settings saved. Changes apply within ~30 seconds.' });
    } catch (err) {
      setToast({ variant: 'error', message: getApiErrorMessage(err, 'Failed to save runtime settings.') });
    } finally {
      setSaving(false);
    }
  }, [draft, load]);

  const updatedLine = useMemo(() => {
    if (!server) return null;
    if (!server.updatedBy && !server.updatedAt) return null;
    return `Last updated by ${server.updatedBy ?? 'unknown'} at ${formatTimestamp(server.updatedAt)}`;
  }, [server]);

  if (authLoading) {
    return (
      <main className="mx-auto w-full max-w-4xl px-4 py-8">
        <LoadingState />
      </main>
    );
  }

  if (!isSystemAdmin) {
    return (
      <main className="mx-auto w-full max-w-4xl px-4 py-8">
        <LockedState />
      </main>
    );
  }

  return (
    <main className="mx-auto w-full max-w-4xl px-4 py-8">
      <header className="mb-6">
        <div className="flex items-center gap-3">
          <span className="flex h-10 w-10 items-center justify-center rounded-2xl bg-primary/10 text-primary">
            <Cog className="h-5 w-5" aria-hidden="true" />
          </span>
          <div>
            <h1 className="text-xl font-semibold text-navy sm:text-2xl">Runtime Settings</h1>
            <p className="mt-1 text-sm text-muted">
              Configure production secrets without editing the server&apos;s .env file. Changes apply within ~30
              seconds.
            </p>
          </div>
        </div>
      </header>

      {loading || !draft || !server ? (
        <LoadingState />
      ) : (
        <div className="space-y-4">
          <ConfigurationCoverage />

          {SECTION_META.map((section) => (
            <Section
              key={section.id}
              id={section.id}
              title={section.title}
              description={section.description}
              open={openSections[section.id]}
              onToggle={() => toggleSection(section.id)}
            >
              {section.id === 'email' &&
                EMAIL_FIELDS.map((field) =>
                  field.type === 'checkbox' ? (
                    <BooleanField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      checked={Boolean(draft.email[field.key])}
                      onChange={(next) => updateField('email', field.key, next as never)}
                    />
                  ) : field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.email[field.key] ?? '')}
                      draftValue={String(draft.email[field.key] ?? '')}
                      onChange={(next) => updateField('email', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.email[field.key] as string | number | null}
                      onChange={(next) =>
                        updateField(
                          'email',
                          field.key,
                          (field.type === 'number' ? parseNullableNumberInput(next) : next) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'billing' &&
                BILLING_FIELDS.map((field) =>
                  field.type === 'checkbox' ? (
                    <BooleanField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      checked={Boolean(draft.billing[field.key])}
                      onChange={(next) => updateField('billing', field.key, next as never)}
                    />
                  ) : field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.billing[field.key] ?? '')}
                      draftValue={String(draft.billing[field.key] ?? '')}
                      onChange={(next) => updateField('billing', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.billing[field.key] as string}
                      onChange={(next) => updateField('billing', field.key, next as never)}
                    />
                  ),
                )}

              {section.id === 'sentry' &&
                SENTRY_FIELDS.map((field) =>
                  field.type === 'checkbox' ? (
                    <BooleanField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      checked={Boolean(draft.sentry[field.key])}
                      onChange={(next) => updateField('sentry', field.key, next as never)}
                    />
                  ) : field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.sentry[field.key] ?? '')}
                      draftValue={String(draft.sentry[field.key] ?? '')}
                      onChange={(next) => updateField('sentry', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.sentry[field.key] as string | number | null}
                      onChange={(next) =>
                        updateField(
                          'sentry',
                          field.key,
                          (field.type === 'number' ? parseNullableNumberInput(next) : next) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'backup' &&
                BACKUP_FIELDS.map((field) =>
                  field.type === 'checkbox' ? (
                    <BooleanField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      checked={Boolean(draft.backup[field.key])}
                      onChange={(next) => updateField('backup', field.key, next as never)}
                    />
                  ) : field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.backup[field.key] ?? '')}
                      draftValue={String(draft.backup[field.key] ?? '')}
                      onChange={(next) => updateField('backup', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.backup[field.key] as string}
                      onChange={(next) => updateField('backup', field.key, next as never)}
                    />
                  ),
                )}

              {section.id === 'oauth' &&
                OAUTH_FIELDS.map((field) =>
                  field.type === 'checkbox' ? (
                    <BooleanField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      checked={Boolean(draft.oauth[field.key])}
                      onChange={(next) => updateField('oauth', field.key, next as never)}
                    />
                  ) : field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.oauth[field.key] ?? '')}
                      draftValue={String(draft.oauth[field.key] ?? '')}
                      onChange={(next) => updateField('oauth', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.oauth[field.key] as string}
                      onChange={(next) => updateField('oauth', field.key, next as never)}
                    />
                  ),
                )}

              {section.id === 'push' &&
                PUSH_FIELDS.map((field) =>
                  field.type === 'checkbox' ? (
                    <BooleanField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      checked={Boolean(draft.push[field.key])}
                      onChange={(next) => updateField('push', field.key, next as never)}
                    />
                  ) : field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.push[field.key] ?? '')}
                      draftValue={String(draft.push[field.key] ?? '')}
                      onChange={(next) => updateField('push', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.push[field.key] as string}
                      onChange={(next) => updateField('push', field.key, next as never)}
                    />
                  ),
                )}
            </Section>
          ))}

          <div className="flex flex-col items-stretch gap-3 pt-2 sm:flex-row sm:items-center sm:justify-end">
            <Button variant="primary" onClick={handleSave} disabled={saving} aria-label="Save all runtime settings">
              {saving ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
              ) : (
                <Save className="mr-2 h-4 w-4" aria-hidden="true" />
              )}
              {saving ? 'Saving…' : 'Save All'}
            </Button>
          </div>

          {updatedLine && (
            <p className="text-right text-xs text-muted" data-testid="runtime-settings-updated-line">
              {updatedLine}
            </p>
          )}
        </div>
      )}

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </main>
  );
}

export default RuntimeSettingsClient;
