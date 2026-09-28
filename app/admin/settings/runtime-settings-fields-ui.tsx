import { useId, useMemo, useState, type ReactNode } from 'react';
import { ChevronDown, ChevronRight, Eye, EyeOff, Lock, Plus, Search, Trash2 } from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';
import type { RuntimeSettingsIntegrationTestResponse } from './runtime-settings-types';
import type { SectionId } from './runtime-settings-fields';
import { SECTION_META } from './runtime-settings-defaults';
import { MASKED } from './runtime-settings-transforms';

/* ───────────────────────── SecretField ───────────────────────── */

export interface SecretFieldProps {
  label: string;
  hint?: string;
  /** Original server value: '' = unset, '********' = set, anything else = literal. */
  serverValue: string;
  /** Working draft value (same conventions as serverValue). */
  draftValue: string;
  onChange: (next: string) => void;
}

export function SecretField({ label, hint, serverValue, draftValue, onChange }: SecretFieldProps) {
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
        <label htmlFor={inputId} className="text-sm font-semibold tracking-tight text-admin-fg-strong">
          {label}
        </label>
        {isSetOnServer ? (
          <Badge variant="success">Set</Badge>
        ) : (
          <Badge variant="default">Not set</Badge>
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
            className="w-full rounded-admin border border-admin-border bg-admin-bg-surface px-4 py-3 pr-12 text-sm text-admin-fg-default shadow-sm transition-[border-color,box-shadow] focus:border-[var(--admin-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
          />
          <button
            type="button"
            onClick={() => setReveal((v) => !v)}
            className="absolute inset-y-0 right-2 my-auto flex h-9 w-9 items-center justify-center rounded-admin text-admin-fg-muted hover:bg-[var(--admin-state-hover)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]"
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
      {hint ? <p className="text-xs leading-5 text-admin-fg-muted">{hint}</p> : null}
    </div>
  );
}

/* ───────────────────────── PlainField ───────────────────────── */

export interface PlainFieldProps {
  label: string;
  hint?: string;
  type?: 'text' | 'number' | 'url' | 'checkbox' | 'select';
  options?: { value: string; label: string }[];
  value: string | number | boolean | null | undefined;
  onChange: (next: string | boolean) => void;
}

export function PlainField({ label, hint, type = 'text', options, value, onChange }: PlainFieldProps) {
  const reactId = useId();
  const inputId = `plain-${reactId}`;
  if (type === 'select') {
    return (
      <div className="flex flex-col gap-1.5">
        <label htmlFor={inputId} className="text-sm font-semibold tracking-tight text-admin-fg-strong">
          {label}
        </label>
        <select
          id={inputId}
          value={value === null || value === undefined ? '' : String(value)}
          onChange={(event) => onChange(event.target.value)}
          className="w-full rounded-admin border border-admin-border bg-admin-bg-surface px-4 py-3 text-sm text-admin-fg-default shadow-sm transition-[border-color,box-shadow] focus:border-[var(--admin-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
        >
          {(options ?? []).map((opt) => (
            <option key={opt.value} value={opt.value}>
              {opt.label}
            </option>
          ))}
        </select>
        {hint ? <p className="text-xs leading-5 text-admin-fg-muted">{hint}</p> : null}
      </div>
    );
  }
  if (type === 'checkbox') {
    return (
      <div className="flex flex-col gap-1.5">
        <label htmlFor={inputId} className="flex items-center gap-3 text-sm font-semibold tracking-tight text-admin-fg-strong">
          <input
            id={inputId}
            type="checkbox"
            checked={Boolean(value)}
            onChange={(event) => onChange(event.target.checked)}
            className="h-4 w-4 rounded border-admin-border text-[var(--admin-primary)] focus:ring-[var(--admin-primary)]"
          />
          {label}
        </label>
        {hint ? <p className="text-xs leading-5 text-admin-fg-muted">{hint}</p> : null}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={inputId} className="text-sm font-semibold tracking-tight text-admin-fg-strong">
        {label}
      </label>
      <input
        id={inputId}
        type={type}
        value={value === null || value === undefined ? '' : String(value)}
        onChange={(event) => onChange(event.target.value)}
        spellCheck={false}
        className="w-full rounded-admin border border-admin-border bg-admin-bg-surface px-4 py-3 text-sm text-admin-fg-default shadow-sm transition-[border-color,box-shadow] focus:border-[var(--admin-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
      />
      {hint ? <p className="text-xs leading-5 text-admin-fg-muted">{hint}</p> : null}
    </div>
  );
}

/* ───────────────────────── Section ───────────────────────── */

export const DEVICE_EXEMPTION_EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function parseDeviceExemptionEmails(value: string | null | undefined): string[] {
  const seen = new Set<string>();
  return (value ?? '')
    .split(/[,;\r\n]+/)
    .map((email) => email.trim().toLowerCase())
    .filter((email) => {
      if (!email || seen.has(email)) return false;
      seen.add(email);
      return true;
    });
}

export interface DeviceExemptionEmailTableProps {
  value: string | null;
  onChange: (next: string) => void;
}

export function DeviceExemptionEmailTable({ value, onChange }: DeviceExemptionEmailTableProps) {
  const reactId = useId();
  const searchId = `device-exemption-search-${reactId}`;
  const addId = `device-exemption-add-${reactId}`;
  const [search, setSearch] = useState('');
  const [newEmail, setNewEmail] = useState('');
  const [addError, setAddError] = useState<string | null>(null);

  const emails = useMemo(() => parseDeviceExemptionEmails(value), [value]);
  const visibleEmails = useMemo(() => {
    const query = search.trim().toLowerCase();
    return query ? emails.filter((email) => email.includes(query)) : emails;
  }, [emails, search]);

  function replaceEmails(next: string[]) {
    onChange(next.join(','));
  }

  function addEmail() {
    const email = newEmail.trim().toLowerCase();
    if (!DEVICE_EXEMPTION_EMAIL_PATTERN.test(email)) {
      setAddError('Enter a valid email address.');
      return;
    }
    if (emails.includes(email)) {
      setAddError('That email is already on the exemption list.');
      return;
    }

    replaceEmails([...emails, email]);
    setNewEmail('');
    setAddError(null);
  }

  return (
    <div className="md:col-span-2 flex flex-col gap-4 rounded-admin border border-admin-border bg-admin-bg-subtle p-4">
      <div className="flex flex-col gap-1 sm:flex-row sm:items-start sm:justify-between sm:gap-4">
        <div>
          <h3 className="text-sm font-semibold tracking-tight text-admin-fg-strong">Device Verification Exemption List</h3>
          <p className="mt-1 max-w-3xl text-xs leading-5 text-admin-fg-muted">
            These addresses skip trusted-device OTP and risk step-up checks on every device and location. Changes are staged until you click Save all runtime settings.
          </p>
        </div>
        <Badge variant="default">{emails.length} email{emails.length === 1 ? '' : 's'}</Badge>
      </div>

      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <div className="flex flex-col gap-1.5">
          <label htmlFor={searchId} className="text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
            Search list
          </label>
          <div className="relative">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-admin-fg-muted" aria-hidden="true" />
            <input
              id={searchId}
              type="search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Search by email"
              aria-label="Search exempt email addresses"
              spellCheck={false}
              className="w-full rounded-admin border border-admin-border bg-admin-bg-surface py-3 pl-10 pr-4 text-sm text-admin-fg-default shadow-sm transition-[border-color,box-shadow] focus:border-[var(--admin-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
            />
          </div>
        </div>

        <form
          className="flex flex-col gap-1.5"
          onSubmit={(event) => {
            event.preventDefault();
            addEmail();
          }}
        >
          <label htmlFor={addId} className="text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
            Add email address
          </label>
          <div className="flex items-stretch gap-2">
            <input
              id={addId}
              type="email"
              value={newEmail}
              onChange={(event) => {
                setNewEmail(event.target.value);
                if (addError) setAddError(null);
              }}
              placeholder="owner@example.com"
              aria-label="Add exempt email address"
              spellCheck={false}
              className="min-w-0 flex-1 rounded-admin border border-admin-border bg-admin-bg-surface px-4 py-3 text-sm text-admin-fg-default shadow-sm transition-[border-color,box-shadow] focus:border-[var(--admin-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
            />
            <Button type="submit" variant="outline" size="sm" startIcon={<Plus className="h-4 w-4" />}>
              Add
            </Button>
          </div>
          {addError ? <p className="text-xs text-admin-danger" role="alert">{addError}</p> : null}
        </form>
      </div>

      <div className="overflow-x-auto rounded-admin border border-admin-border bg-admin-bg-surface">
        <table className="min-w-full text-left text-sm" aria-label="Device Verification Exemption List">
          <caption className="sr-only">Device Verification Exemption List</caption>
          <thead className="border-b border-admin-border bg-admin-bg-subtle text-xs uppercase tracking-wide text-admin-fg-muted">
            <tr>
              <th scope="col" className="w-16 px-4 py-3">#</th>
              <th scope="col" className="px-4 py-3">Email address</th>
              <th scope="col" className="w-32 px-4 py-3 text-right">Action</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-admin-border">
            {visibleEmails.length > 0 ? (
              visibleEmails.map((email, index) => (
                <tr key={email} className="align-middle">
                  <td className="px-4 py-3 tabular-nums text-admin-fg-muted">{index + 1}</td>
                  <td className="px-4 py-3 font-medium text-admin-fg-strong">{email}</td>
                  <td className="px-4 py-3 text-right">
                    <Button
                      type="button"
                      variant="destructive"
                      size="sm"
                      startIcon={<Trash2 className="h-3.5 w-3.5" />}
                      aria-label={`Delete ${email}`}
                      onClick={() => replaceEmails(emails.filter((candidate) => candidate !== email))}
                    >
                      Delete
                    </Button>
                  </td>
                </tr>
              ))
            ) : (
              <tr>
                <td colSpan={3} className="px-4 py-8 text-center text-sm text-admin-fg-muted">
                  {emails.length === 0 ? 'No exempt email addresses configured.' : 'No addresses match this search.'}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export interface SectionProps {
  id: SectionId;
  title: string;
  description: string;
  open: boolean;
  onToggle: () => void;
  testing: boolean;
  testStatus?: RuntimeSettingsIntegrationTestResponse;
  onTest: () => void;
  children: ReactNode;
}

export function Section({ id, title, description, open, onToggle, testing, testStatus, onTest, children }: SectionProps) {
  const headingId = `runtime-settings-${id}-heading`;
  return (
    <Card
      role="region"
      aria-labelledby={headingId}
      id={`runtime-settings-${id}`}
      className="scroll-mt-24"
    >
      <button
        type="button"
        onClick={onToggle}
        aria-expanded={open}
        aria-controls={`runtime-settings-${id}-body`}
        className="flex w-full items-center justify-between gap-4 px-5 py-4 text-left rounded-admin-lg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]"
      >
        <span>
          <span id={headingId} className="block text-base font-semibold text-admin-fg-strong">
            {title}
          </span>
          <span className="mt-0.5 block text-xs text-admin-fg-muted">{description}</span>
        </span>
        {open ? (
          <ChevronDown className="h-5 w-5 text-admin-fg-muted" aria-hidden="true" />
        ) : (
          <ChevronRight className="h-5 w-5 text-admin-fg-muted" aria-hidden="true" />
        )}
      </button>
      {open && (
        <div id={`runtime-settings-${id}-body`} className="border-t border-admin-border px-5 py-5">
          <div className="mb-5 flex flex-col gap-3 rounded-admin border border-admin-border bg-admin-bg-subtle p-4 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <div className="flex items-center gap-2">
                <span className="text-sm font-semibold text-admin-fg-strong">Connection check</span>
                {testStatus ? (
                  <Badge variant={testStatus.status === 'ok' ? 'success' : 'danger'}>
                    {testStatus.status === 'ok' ? 'Green' : 'Failed'}
                  </Badge>
                ) : (
                  <Badge variant="default">Not tested</Badge>
                )}
              </div>
              <p className="mt-1 text-xs leading-5 text-admin-fg-muted">
                {testStatus?.message ?? 'Runs a non-mutating sandbox/format check; no live learner data is sent.'}
              </p>
            </div>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={onTest}
              disabled={testing}
              loading={testing}
              loadingText="Testing..."
              aria-label={`Test ${title}`}
            >
              Test
            </Button>
          </div>
          <div className="grid grid-cols-1 gap-5 md:grid-cols-2">{children}</div>
        </div>
      )}
    </Card>
  );
}

/* ───────────────────────── Loading skeleton ───────────────────────── */

export function LoadingState() {
  return (
    <div className="space-y-4" aria-busy="true" aria-live="polite">
      {SECTION_META.map((s) => (
        <Card key={s.id}>
          <CardContent className="p-5 pt-5">
            <Skeleton className="h-5 w-1/3" />
            <Skeleton className="mt-3 h-4 w-2/3" />
            <div className="mt-4 grid grid-cols-1 gap-3 md:grid-cols-2">
              <Skeleton className="h-12 w-full" />
              <Skeleton className="h-12 w-full" />
            </div>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

/* ───────────────────────── Locked state ───────────────────────── */

export function LockedState() {
  return (
    <Card surface="tinted-warning">
      <CardContent className="p-8 pt-8">
        <EmptyState
          variant="error"
          size="md"
          illustration={<Lock aria-hidden="true" />}
          title="System administrator access required"
          description="Runtime Settings exposes production secrets and is gated behind the system_admin permission. Ask a super-admin to grant access if you need to change Brevo, Stripe, Sentry, OAuth, push, Zoom, or backup credentials from this UI."
        />
      </CardContent>
    </Card>
  );
}
