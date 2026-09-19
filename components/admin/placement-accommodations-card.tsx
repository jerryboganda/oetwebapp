'use client';

import { Fragment, useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { AlertTriangle, ChevronDown, ChevronRight, RefreshCw } from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Input } from '@/components/admin/ui/input';
import { NativeSelect } from '@/components/admin/ui/native-select';
import { InlineAlert } from '@/components/ui/alert';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { isApiError } from '@/lib/api/client';
import { readErrorMessage } from '@/lib/read-error-message';
import {
  fetchPlacementAccommodations,
  grantPlacementAccommodation,
  revokePlacementAccommodation,
  type GrantPlacementAccommodationInput,
  type PlacementAccommodation,
} from '@/lib/api/admin-placement';

/**
 * Extra-time accommodations for the placement test. Extra time is granted here
 * by an administrator only — a candidate can never switch it on for themselves.
 * Every grant records who approved it, when, the percentage, and which attempts
 * used it, so this card is both the control and the audit view.
 */

const PERCENT_PRESETS: readonly number[] = [25, 50, 75, 100];
const REFERENCE_MAX = 200;
const REASON_MAX = 200;
const TABLE_COLUMNS = 7;
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const REFERENCE_WARNING_ID = 'placement-accommodation-reference-warning';

interface GrantFormErrors {
  learner?: string;
  percent?: string;
  reference?: string;
}

/** Validates the grant form. `input` is null whenever any field is invalid. */
function parseGrantForm(
  learnerText: string,
  percentText: string,
  referenceText: string,
): { errors: GrantFormErrors; input: GrantPlacementAccommodationInput | null } {
  const errors: GrantFormErrors = {};
  const learner = learnerText.trim();
  const reference = referenceText.trim();
  const percent = Number(percentText);

  if (!learner) {
    errors.learner = "Enter the learner's email address or user ID.";
  } else if (learner.includes('@') ? !EMAIL_PATTERN.test(learner) : /\s/.test(learner)) {
    errors.learner = 'Enter a valid email address, or a user ID without spaces.';
  }
  if (!percentText || !PERCENT_PRESETS.includes(percent)) {
    errors.percent = 'Choose an extra-time percentage.';
  }
  if (reference.length > REFERENCE_MAX) {
    errors.reference = `Keep the reference to ${REFERENCE_MAX} characters or fewer.`;
  }
  if (errors.learner || errors.percent || errors.reference) return { errors, input: null };

  return {
    errors,
    input: {
      ...(learner.includes('@') ? { learnerEmail: learner } : { learnerUserId: learner }),
      extraTimePercent: percent,
      ...(reference ? { reference } : {}),
    },
  };
}

/** Local date and time with the zone, so the audit trail is unambiguous. */
function formatWhen(iso: string | null): string {
  if (!iso) return '—';
  // The API can emit more than three fractional digits, which Safari rejects.
  const parsed = new Date(iso.replace(/(\.\d{3})\d+/, '$1'));
  if (Number.isNaN(parsed.getTime())) return iso;
  return parsed.toLocaleString(undefined, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    timeZoneName: 'short',
  });
}

export function PlacementAccommodationsCard() {
  // The page is gated by review_ops, but these endpoints need learner:read / learner:write.
  const { user } = useAuth();
  const canRead = hasPermission(user?.adminPermissions, AdminPermission.LearnerRead);
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.LearnerWrite);
  const [grants, setGrants] = useState<PlacementAccommodation[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [loadForbidden, setLoadForbidden] = useState(false);
  const [filter, setFilter] = useState('');
  const [showRevoked, setShowRevoked] = useState(false);
  const [expanded, setExpanded] = useState<string[]>([]);
  const [notice, setNotice] = useState<string | null>(null);

  const [learner, setLearner] = useState('');
  const [percent, setPercent] = useState('');
  const [reference, setReference] = useState('');
  const [errors, setErrors] = useState<GrantFormErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [granting, setGranting] = useState(false);

  const [revoking, setRevoking] = useState<{ id: string; email: string; reason: string } | null>(null);
  const [revokeError, setRevokeError] = useState<string | null>(null);
  const [revokeBusy, setRevokeBusy] = useState(false);
  const revokeTriggerRef = useRef<HTMLButtonElement | null>(null);
  const reasonRef = useRef<HTMLInputElement | null>(null);
  const loadSeq = useRef(0);

  // ponytail: the learner filter runs client-side over the loaded page (the API
  // returns the newest grants only); wire the `learner` query if that page is outgrown.
  const load = useCallback(async () => {
    if (!canRead) return;
    const seq = ++loadSeq.current;
    try {
      const rows = await fetchPlacementAccommodations({ includeRevoked: showRevoked });
      if (seq !== loadSeq.current) return;
      setGrants(rows);
      setLoadError(null);
      setLoadForbidden(false);
    } catch (err) {
      if (seq !== loadSeq.current) return;
      setLoadError(readErrorMessage(err, 'Could not load extra-time accommodations.'));
      // A 403 means the permission claim was stale; retrying cannot help.
      setLoadForbidden(isApiError(err) && err.status === 403);
    }
  }, [canRead, showRevoked]);

  useEffect(() => {
    void load();
  }, [load]);

  const revokingId = revoking?.id ?? null;
  useEffect(() => {
    if (revokingId) reasonRef.current?.focus();
  }, [revokingId]);

  const handleGrant = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (granting) return;
    setNotice(null);
    setFormError(null);
    const { errors: found, input } = parseGrantForm(learner, percent, reference);
    setErrors(found);
    if (!input) return;

    setGranting(true);
    try {
      const created = await grantPlacementAccommodation(input);
      const who = created.learnerEmail || input.learnerEmail || input.learnerUserId;
      setNotice(`Extra time granted: +${input.extraTimePercent}% for ${who}.`);
      setLearner('');
      setPercent('');
      setReference('');
      await load();
    } catch (err) {
      setFormError(readErrorMessage(err, 'Could not grant extra time. Nothing was changed.'));
    } finally {
      setGranting(false);
    }
  };

  const openRevoke = (grant: PlacementAccommodation, trigger: HTMLButtonElement) => {
    revokeTriggerRef.current = trigger;
    setNotice(null);
    setRevokeError(null);
    setRevoking({ id: grant.id, email: grant.learnerEmail, reason: '' });
  };

  const cancelRevoke = () => {
    if (revokeBusy) return;
    setRevoking(null);
    setRevokeError(null);
    revokeTriggerRef.current?.focus();
  };

  const confirmRevoke = async () => {
    if (!revoking || revokeBusy) return;
    setRevokeBusy(true);
    setRevokeError(null);
    try {
      await revokePlacementAccommodation(revoking.id, revoking.reason.trim() || undefined);
      setNotice(`Extra time revoked for ${revoking.email}.`);
      setRevoking(null);
      await load();
    } catch (err) {
      setRevokeError(readErrorMessage(err, 'Could not revoke the accommodation. Nothing was changed.'));
    } finally {
      setRevokeBusy(false);
    }
  };

  const toggleExpanded = (id: string) =>
    setExpanded((current) => (current.includes(id) ? current.filter((entry) => entry !== id) : [...current, id]));

  const needle = filter.trim().toLowerCase();
  const visible = (grants ?? []).filter(
    (grant) =>
      (showRevoked || grant.status === 'active') &&
      (!needle ||
        [grant.learnerName ?? '', grant.learnerEmail, grant.learnerUserId].some((value) =>
          value.toLowerCase().includes(needle),
        )),
  );
  const activeCount = (grants ?? []).filter((grant) => grant.status === 'active').length;
  const revokedCount = (grants?.length ?? 0) - activeCount;
  const columnCount = canWrite ? TABLE_COLUMNS : TABLE_COLUMNS - 1;

  if (!canRead) {
    return (
      <Card role="region" aria-labelledby="placement-accommodations-title">
        <CardHeader>
          <CardTitle id="placement-accommodations-title">Extra-time accommodations</CardTitle>
        </CardHeader>
        <CardContent>
          <InlineAlert variant="info" role="note">
            Extra-time accommodations need the learner:read permission.
          </InlineAlert>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card role="region" aria-labelledby="placement-accommodations-title">
      <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <CardTitle id="placement-accommodations-title">Extra-time accommodations</CardTitle>
          <CardDescription>
            Extra time is approved by an administrator only; candidates cannot switch it on for themselves. Each grant
            records who approved it, when, the percentage, and which attempts used it.
          </CardDescription>
        </div>
        <Button
          variant="secondary"
          size="sm"
          startIcon={<RefreshCw className="h-3.5 w-3.5" />}
          onClick={() => void load()}
        >
          Refresh
        </Button>
      </CardHeader>

      <CardContent className="space-y-4">
        {notice ? (
          <InlineAlert variant="success" role="status">
            {notice}
          </InlineAlert>
        ) : null}

        {canWrite ? (
          <form
            noValidate
            aria-label="Grant extra time"
            onSubmit={(event) => void handleGrant(event)}
            className="space-y-3 rounded-admin border border-admin-border bg-admin-bg-subtle p-3"
          >
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              <Input
                label="Learner email or user ID"
                size="sm"
                value={learner}
                onChange={(event) => setLearner(event.target.value)}
                error={errors.learner}
                disabled={granting}
                autoComplete="off"
                aria-required="true"
                placeholder="name@example.com"
              />
              <NativeSelect
                label="Extra time percentage"
                value={percent}
                onChange={(event) => setPercent(event.target.value)}
                error={errors.percent}
                disabled={granting}
                aria-required="true"
                placeholder="Select percentage"
                options={PERCENT_PRESETS.map((preset) => ({ value: String(preset), label: `+${preset}%` }))}
                className="h-8 px-2.5 text-xs"
              />
              <div className="space-y-1.5">
                <Input
                  label="Administrative reference (optional)"
                  size="sm"
                  value={reference}
                  onChange={(event) => setReference(event.target.value)}
                  error={errors.reference}
                  disabled={granting}
                  maxLength={REFERENCE_MAX}
                  autoComplete="off"
                  aria-describedby={REFERENCE_WARNING_ID}
                />
                <div
                  id={REFERENCE_WARNING_ID}
                  role="note"
                  className="flex items-start gap-1.5 text-xs font-medium text-admin-fg-default"
                >
                  <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-admin-warning" aria-hidden="true" />
                  <span>Administrative reference only - do not enter medical or personal health details</span>
                </div>
              </div>
            </div>
            {formError ? <InlineAlert variant="error">{formError}</InlineAlert> : null}
            <div className="flex flex-wrap items-center justify-between gap-3">
              <p className="text-xs text-admin-fg-muted">
                A new grant replaces the learner&apos;s current active one, which stays on record as revoked.
              </p>
              <Button type="submit" size="sm" loading={granting} loadingText="Granting…">
                Grant extra time
              </Button>
            </div>
          </form>
        ) : (
          <InlineAlert variant="info" role="note">
            Granting or revoking extra time needs the learner:write permission.
          </InlineAlert>
        )}

        {loadError ? (
          <InlineAlert
            variant="error"
            action={
              loadForbidden ? undefined : (
                <Button variant="secondary" size="sm" onClick={() => void load()}>
                  Retry
                </Button>
              )
            }
          >
            {loadError}
          </InlineAlert>
        ) : null}

        {grants === null ? (
          loadError ? null : (
            <p role="status" className="text-sm text-admin-fg-muted">
              Loading accommodations…
            </p>
          )
        ) : (
          <>
            <div className="flex flex-wrap items-end justify-between gap-3">
              <Input
                label="Filter by learner"
                size="sm"
                type="search"
                value={filter}
                onChange={(event) => setFilter(event.target.value)}
                wrapperClassName="w-full sm:w-72"
                placeholder="Name, email or user ID"
                autoComplete="off"
              />
              <div className="flex items-center gap-4 pb-1.5 text-sm text-admin-fg-default">
                <span className="text-xs text-admin-fg-muted">
                  {showRevoked ? `${activeCount} active · ${revokedCount} revoked` : `${activeCount} active`}
                </span>
                <label className="flex cursor-pointer items-center gap-2">
                  <input
                    type="checkbox"
                    checked={showRevoked}
                    onChange={(event) => setShowRevoked(event.target.checked)}
                    className="h-4 w-4 accent-[var(--admin-primary)]"
                  />
                  Show revoked
                </label>
              </div>
            </div>

            {visible.length === 0 ? (
              <EmptyState
                size="sm"
                headingLevel="h4"
                title={
                  grants.length > 0
                    ? 'No accommodations match the filter'
                    : showRevoked
                      ? 'No accommodations yet'
                      : 'No active accommodations'
                }
                description={
                  grants.length > 0
                    ? 'Try a different name, email or user ID.'
                    : `Grants you approve appear here with who approved them and which attempts used them.${showRevoked ? '' : ' Turn on Show revoked to see past grants.'}`
                }
              />
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full min-w-[760px] border-collapse text-sm text-admin-fg-default">
                  <caption className="sr-only">Extra-time accommodations, newest first</caption>
                  <thead>
                    <tr className="border-b border-admin-border text-left text-xs text-admin-fg-muted">
                      <th scope="col" className="py-2 pr-3 font-medium">Learner</th>
                      <th scope="col" className="py-2 pr-3 font-medium">Extra time</th>
                      <th scope="col" className="py-2 pr-3 font-medium">Status</th>
                      <th scope="col" className="py-2 pr-3 font-medium">Approved by</th>
                      <th scope="col" className="py-2 pr-3 font-medium">Reference</th>
                      <th scope="col" className="py-2 pr-3 font-medium">Attempts used</th>
                      {canWrite ? <th scope="col" className="py-2 font-medium">Action</th> : null}
                    </tr>
                  </thead>
                  <tbody>
                    {visible.map((grant) => {
                      const isActive = grant.status === 'active';
                      const isExpanded = expanded.includes(grant.id);
                      const usesId = `accommodation-uses-${grant.id}`;
                      const confirming = revoking && revoking.id === grant.id ? revoking : null;
                      return (
                        <Fragment key={grant.id}>
                          <tr className="border-b border-admin-border align-top last:border-0">
                            <td className="py-2 pr-3">
                              <p className="font-medium text-admin-fg-strong">{grant.learnerName ?? grant.learnerEmail}</p>
                              <p className="break-all text-xs text-admin-fg-muted">
                                {grant.learnerName ? (
                                  <>
                                    <span>{grant.learnerEmail}</span> ·{' '}
                                  </>
                                ) : null}
                                <span className="font-mono">{grant.learnerUserId}</span>
                              </p>
                            </td>
                            <td className="whitespace-nowrap py-2 pr-3 font-medium tabular-nums">+{grant.extraTimePercent}%</td>
                            <td className="py-2 pr-3">
                              <Badge variant={isActive ? 'success' : 'muted'}>{isActive ? 'Active' : 'Revoked'}</Badge>
                              {isActive ? null : (
                                <div className="mt-1 text-xs text-admin-fg-muted">
                                  <p>
                                    Revoked by {grant.revokedByName ?? grant.revokedByUserId ?? 'unknown'} on{' '}
                                    {formatWhen(grant.revokedAt)}
                                  </p>
                                  {grant.revokedReason ? <p className="break-words">Reason: {grant.revokedReason}</p> : null}
                                </div>
                              )}
                            </td>
                            <td className="py-2 pr-3">
                              <p className="font-medium text-admin-fg-strong">
                                {grant.approvedByName || grant.approvedByUserId || '—'}
                              </p>
                              <p className="text-xs text-admin-fg-muted">{formatWhen(grant.approvedAt)}</p>
                            </td>
                            <td className="max-w-[16rem] break-words py-2 pr-3">
                              {grant.reference ?? <span className="text-admin-fg-muted">—</span>}
                            </td>
                            <td className="py-2 pr-3">
                              {grant.uses.length === 0 ? (
                                <span className="text-admin-fg-muted">None yet</span>
                              ) : (
                                <button
                                  type="button"
                                  onClick={() => toggleExpanded(grant.id)}
                                  aria-expanded={isExpanded}
                                  aria-controls={usesId}
                                  className="inline-flex items-center gap-1 rounded text-admin-primary underline-offset-2 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]"
                                >
                                  {isExpanded ? (
                                    <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />
                                  ) : (
                                    <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
                                  )}
                                  {grant.uses.length} attempt{grant.uses.length === 1 ? '' : 's'}
                                </button>
                              )}
                            </td>
                            {canWrite ? (
                              <td className="py-2">
                                {isActive ? (
                                  <Button
                                    variant="destructive"
                                    size="sm"
                                    aria-label={`Revoke extra time for ${grant.learnerEmail}`}
                                    disabled={revokeBusy}
                                    onClick={(event) => openRevoke(grant, event.currentTarget)}
                                  >
                                    Revoke
                                  </Button>
                                ) : null}
                              </td>
                            ) : null}
                          </tr>

                          {isExpanded ? (
                            <tr id={usesId} className="border-b border-admin-border bg-admin-bg-subtle">
                              <td colSpan={columnCount} className="px-3 py-2">
                                <p className="text-xs font-medium text-admin-fg-muted">Attempts that used this accommodation</p>
                                <ul className="mt-1 space-y-0.5 text-xs">
                                  {grant.uses.map((use, index) => (
                                    <li key={`${use.sessionId}:${use.appliedAt}:${index}`}>
                                      <span className="font-mono">{use.sessionId}</span> · applied {formatWhen(use.appliedAt)} · +
                                      {use.extraTimePercent}%
                                    </li>
                                  ))}
                                </ul>
                              </td>
                            </tr>
                          ) : null}

                          {confirming ? (
                            <tr className="border-b border-admin-border bg-admin-bg-subtle">
                              <td colSpan={columnCount} className="px-3 py-3">
                                <form
                                  aria-label={`Confirm revoking extra time for ${grant.learnerEmail}`}
                                  className="space-y-2"
                                  onSubmit={(event) => {
                                    event.preventDefault();
                                    void confirmRevoke();
                                  }}
                                  onKeyDown={(event) => {
                                    if (event.key === 'Escape') cancelRevoke();
                                  }}
                                >
                                  <p className="text-sm font-medium text-admin-fg-strong">
                                    Revoke +{grant.extraTimePercent}% extra time for {grant.learnerEmail}?
                                  </p>
                                  <p className="text-xs text-admin-fg-muted">
                                    Revoking ends this accommodation. The grant and its usage history stay on record.
                                  </p>
                                  <Input
                                    ref={reasonRef}
                                    label="Reason (optional)"
                                    size="sm"
                                    value={confirming.reason}
                                    onChange={(event) =>
                                      setRevoking((current) => (current ? { ...current, reason: event.target.value } : current))
                                    }
                                    maxLength={REASON_MAX}
                                    disabled={revokeBusy}
                                    autoComplete="off"
                                    hint="Administrative reason only - no medical or personal health details."
                                    wrapperClassName="w-full sm:w-96"
                                  />
                                  <div className="flex items-center gap-2">
                                    <Button type="submit" variant="destructive" size="sm" loading={revokeBusy} loadingText="Revoking…">
                                      Confirm revoke
                                    </Button>
                                    <Button variant="secondary" size="sm" disabled={revokeBusy} onClick={cancelRevoke}>
                                      Cancel
                                    </Button>
                                  </div>
                                  {revokeError ? (
                                    <p role="alert" className="text-xs text-admin-danger">
                                      {revokeError}
                                    </p>
                                  ) : null}
                                </form>
                              </td>
                            </tr>
                          ) : null}
                        </Fragment>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
