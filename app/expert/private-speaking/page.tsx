'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { ExpertRouteHero, ExpertRouteSectionHeader } from '@/components/domain/expert-route-surface';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Calendar, Clock, Video, Star, Plus, Trash2, Pencil, X, Link2, Unlink, Download, UserX } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import {
  type PrivateSpeakingCalendarStatus,
  fetchExpertPrivateSpeakingProfile,
  fetchExpertPrivateSpeakingSessions,
  fetchExpertPrivateSpeakingAvailability,
  updateExpertPrivateSpeakingAvailability,
  updateExpertPrivateSpeakingAvailabilityRule,
  deleteExpertPrivateSpeakingAvailability,
  cancelExpertPrivateSpeakingSession,
  fetchExpertPrivateSpeakingCalendarStatus,
  connectExpertPrivateSpeakingGoogleCalendar,
  disconnectExpertPrivateSpeakingCalendar,
  downloadExpertPrivateSpeakingCalendarInvite,
  markExpertPrivateSpeakingNoShow,
} from '@/lib/api';
import { createSpeakingExamFromBookingAsTutor } from '@/lib/api/speaking-exams';

type TutorProfile = {
  id: string; displayName: string; bio: string | null; timezone: string;
  priceOverrideMinorUnits: number | null; slotDurationOverrideMinutes: number | null;
  specialtiesJson: string; isActive: boolean; averageRating: number; totalSessions: number;
};

type ExpertSession = {
  id: string; learnerUserId: string; status: string; sessionStartUtc: string;
  durationMinutes: number;
  learnerRating: number | null; learnerFeedback: string | null;
};

type AvailabilityRule = {
  id: string; dayOfWeek: number; startTime: string; endTime: string;
  effectiveFrom: string | null; effectiveTo: string | null; isActive: boolean;
};

type ExpertTab = 'sessions' | 'availability';
const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

const SESSION_STATUS_VARIANTS: Record<string, BadgeProps['variant']> = {
  Confirmed: 'info',
  InProgress: 'warning',
  Completed: 'muted',
  Cancelled: 'danger',
  NoShow: 'danger',
};

function StatusBadge({ status }: { status: string }) {
  return <Badge variant={SESSION_STATUS_VARIANTS[status] ?? 'muted'}>{status}</Badge>;
}

export default function ExpertPrivateSpeakingPage() {
  const [tab, setTab] = useState<ExpertTab>('sessions');
  const [profile, setProfile] = useState<TutorProfile | null>(null);
  const [sessions, setSessions] = useState<ExpertSession[]>([]);
  const [availability, setAvailability] = useState<AvailabilityRule[]>([]);
  const [calendarStatus, setCalendarStatus] = useState<PrivateSpeakingCalendarStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  // Distinguishes "profile GET failed" from "profile GET succeeded with null"
  // so a null profile renders the empty state, never the failure banner.
  const [profileLoadFailed, setProfileLoadFailed] = useState(false);
  const [calendarBusy, setCalendarBusy] = useState(false);
  // True while we wait for the tutor to finish Google's OAuth consent in the
  // separate tab; drives a short poll of the calendar status.
  const [calendarPolling, setCalendarPolling] = useState(false);
  const [startingSessionId, setStartingSessionId] = useState<string | null>(null);

  // New availability rule form
  const [newRule, setNewRule] = useState({ dayOfWeek: 1, startTime: '09:00', endTime: '17:00' });

  // Edit availability rule state
  const [editingRuleId, setEditingRuleId] = useState<string | null>(null);
  const [editRule, setEditRule] = useState({ dayOfWeek: 1, startTime: '09:00', endTime: '17:00', isActive: true });
  const [savingRule, setSavingRule] = useState(false);

  // Cancel session state
  const [cancelTarget, setCancelTarget] = useState<ExpertSession | null>(null);
  const [cancelReason, setCancelReason] = useState('');
  const [cancelling, setCancelling] = useState(false);

  // Mark learner no-show state
  const [markingNoShowId, setMarkingNoShowId] = useState<string | null>(null);

  useEffect(() => {
    loadData();
  }, []);

  // The OAuth callback redirects back here with ?calendar=connected|error
  // (this tab when the pop-up was blocked, or the new tab on success). Surface
  // the outcome, refresh status, and strip the query param from the URL.
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const calendarResult = params.get('calendar');
    if (calendarResult === 'connected') {
      void fetchExpertPrivateSpeakingCalendarStatus().then(setCalendarStatus).catch(() => {});
    } else if (calendarResult === 'error') {
      setError('Google Calendar authorization did not complete. Please try connecting again.');
    }
    if (calendarResult) {
      params.delete('calendar');
      const query = params.toString();
      window.history.replaceState(null, '', `${window.location.pathname}${query ? `?${query}` : ''}`);
    }
  }, []);

  // While the tutor authorises Google Calendar in a separate tab, poll the
  // connection status (and refresh the moment they switch back to this tab)
  // until it reports connected, then stop. Bounded so it never polls forever.
  useEffect(() => {
    if (!calendarPolling) return;
    let cancelled = false;
    const startedAt = Date.now();
    const POLL_INTERVAL_MS = 3000;
    const POLL_TIMEOUT_MS = 5 * 60 * 1000;

    async function check() {
      try {
        const status = await fetchExpertPrivateSpeakingCalendarStatus();
        if (cancelled) return;
        setCalendarStatus(status);
        if (status?.connected) {
          setCalendarPolling(false);
        }
      } catch {
        // Transient — keep polling until the timeout.
      }
    }

    const interval = setInterval(() => {
      if (cancelled) return;
      if (Date.now() - startedAt > POLL_TIMEOUT_MS) {
        setCalendarPolling(false);
        return;
      }
      void check();
    }, POLL_INTERVAL_MS);

    const onFocus = () => void check();
    window.addEventListener('focus', onFocus);

    return () => {
      cancelled = true;
      clearInterval(interval);
      window.removeEventListener('focus', onFocus);
    };
  }, [calendarPolling]);

  async function loadData() {
    setLoading(true);
    // Settle each request independently: a null profile is a valid 200 (the
    // expert has no tutor profile yet), and one failing call must not poison
    // the others or masquerade as a missing profile.
    const [profileResult, sessionResult, calendarResult] = await Promise.allSettled([
      fetchExpertPrivateSpeakingProfile(),
      fetchExpertPrivateSpeakingSessions(),
      fetchExpertPrivateSpeakingCalendarStatus(),
    ]);

    if (profileResult.status === 'fulfilled') {
      // Backend returns `null` (empty 200) when the expert has not yet been
      // provisioned a tutor profile.
      setProfile(profileResult.value ? (profileResult.value as TutorProfile) : null);
      setProfileLoadFailed(false);
    } else {
      setProfileLoadFailed(true);
    }
    if (sessionResult.status === 'fulfilled') {
      setSessions((sessionResult.value ?? []) as ExpertSession[]);
    }
    if (calendarResult.status === 'fulfilled') {
      setCalendarStatus(calendarResult.value ?? null);
    }

    const anyFailed = [profileResult, sessionResult, calendarResult].some(
      (result) => result.status === 'rejected'
    );
    if (anyFailed) {
      setError('Failed to load your private speaking data.');
    }
    setLoading(false);
  }

  async function loadAvailability() {
    try {
      const data = await fetchExpertPrivateSpeakingProfile();
      setProfile(data ? (data as TutorProfile) : null);
      const [rules, calendarData] = await Promise.all([
        fetchExpertPrivateSpeakingAvailability(),
        fetchExpertPrivateSpeakingCalendarStatus(),
      ]);
      setAvailability(rules as AvailabilityRule[]);
      setCalendarStatus(calendarData);
    } catch {
      setError('Failed to load availability.');
    }
  }

  async function handleAddRule() {
    try {
      await updateExpertPrivateSpeakingAvailability(newRule);
      await loadAvailability();
    } catch {
      setError('Failed to add availability rule.');
    }
  }

  async function handleDeleteRule(ruleId: string) {
    try {
      await deleteExpertPrivateSpeakingAvailability(ruleId);
      setAvailability(prev => prev.filter(r => r.id !== ruleId));
    } catch {
      setError('Failed to delete rule.');
    }
  }

  function startEditRule(rule: AvailabilityRule) {
    setEditingRuleId(rule.id);
    setEditRule({
      dayOfWeek: rule.dayOfWeek,
      startTime: rule.startTime,
      endTime: rule.endTime,
      isActive: rule.isActive,
    });
  }

  function cancelEditRule() {
    setEditingRuleId(null);
  }

  async function handleSaveRule(rule: AvailabilityRule) {
    setSavingRule(true);
    setError(null);
    try {
      await updateExpertPrivateSpeakingAvailabilityRule(rule.id, {
        dayOfWeek: editRule.dayOfWeek,
        startTime: editRule.startTime,
        endTime: editRule.endTime,
        effectiveFrom: rule.effectiveFrom,
        effectiveTo: rule.effectiveTo,
        isActive: editRule.isActive,
      });
      setEditingRuleId(null);
      await loadAvailability();
    } catch {
      setError('Failed to update availability rule.');
    } finally {
      setSavingRule(false);
    }
  }

  async function handleCancelSession() {
    if (!cancelTarget) return;
    setCancelling(true);
    try {
      await cancelExpertPrivateSpeakingSession(cancelTarget.id, cancelReason || undefined);
      setCancelTarget(null);
      setCancelReason('');
      await loadData();
    } catch {
      setError('Failed to cancel session.');
    } finally {
      setCancelling(false);
    }
  }

  // Mirrors the admin mark-no-show flow (window.confirm gate). Only offered
  // for scheduled sessions whose slot is already in the past — the backend
  // additionally enforces the Confirmed/InProgress state gate.
  async function handleMarkNoShow(session: ExpertSession) {
    if (!window.confirm('Mark the learner as a no-show for this session? The session is forfeited per policy (no refund) and this cannot be undone.')) return;
    setMarkingNoShowId(session.id);
    setError(null);
    setNotice(null);
    try {
      await markExpertPrivateSpeakingNoShow(session.id);
      setNotice('Session marked as a learner no-show.');
      await loadData();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to mark the session as a no-show.');
    } finally {
      setMarkingNoShowId(null);
    }
  }

  async function handleStartSession(session: ExpertSession) {
    setStartingSessionId(session.id);
    setError(null);
    try {
      const exam = await createSpeakingExamFromBookingAsTutor(session.id);
      window.location.href = `/expert/speaking/exam/${exam.examId}`;
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not prepare the LiveKit tutor room.');
    } finally {
      setStartingSessionId(null);
    }
  }

  async function handleConnectCalendar() {
    setCalendarBusy(true);
    setError(null);
    // Open the tab synchronously inside the click gesture so pop-up blockers
    // allow it (a window.open after the await below would be blocked). We
    // navigate this blank tab to Google's consent screen once the
    // authorization URL arrives, keeping this dashboard in place and polling
    // status (see effect) until the tutor finishes authorising.
    const popup = window.open('', '_blank');
    try {
      const result = await connectExpertPrivateSpeakingGoogleCalendar();
      if (popup && !popup.closed) {
        popup.location.href = result.authorizationUrl;
        setCalendarPolling(true);
      } else {
        // Pop-up blocked or closed — fall back to a same-tab redirect.
        window.location.href = result.authorizationUrl;
        return;
      }
    } catch (err: unknown) {
      if (popup && !popup.closed) popup.close();
      setError(err instanceof Error ? err.message : 'Could not start Google Calendar connection.');
    } finally {
      setCalendarBusy(false);
    }
  }

  async function handleDisconnectCalendar() {
    setCalendarBusy(true);
    setError(null);
    try {
      await disconnectExpertPrivateSpeakingCalendar();
      setCalendarStatus(await fetchExpertPrivateSpeakingCalendarStatus());
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not disconnect Google Calendar.');
    } finally {
      setCalendarBusy(false);
    }
  }

  async function handleDownloadInvite(sessionId: string) {
    try {
      const blob = await downloadExpertPrivateSpeakingCalendarInvite(sessionId);
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `oet-private-speaking-${sessionId}.ics`;
      anchor.click();
      URL.revokeObjectURL(url);
    } catch {
      setError('Could not download the calendar invite.');
    }
  }

  useEffect(() => {
    if (tab === 'availability') loadAvailability();
  }, [tab]);

  const upcomingSessions = sessions.filter(s =>
    ['Confirmed', 'InProgress'].includes(s.status)
  ).sort((a, b) => new Date(a.sessionStartUtc).getTime() - new Date(b.sessionStartUtc).getTime());

  const pastSessions = sessions.filter(s =>
    ['Completed', 'Cancelled', 'NoShow'].includes(s.status)
  ).sort((a, b) => new Date(b.sessionStartUtc).getTime() - new Date(a.sessionStartUtc).getTime());

  if (loading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-24 rounded-xl" />
        <Skeleton className="h-48 rounded-xl" />
      </div>
    );
  }

  if (!profile && profileLoadFailed) {
    // The profile request itself failed — we don't know whether a profile
    // exists, so show a retryable error instead of the "not set up" state.
    return (
      <div className="space-y-4">
        <ExpertRouteHero
          title="Private Speaking Sessions"
          description="Manage your private speaking sessions and availability."
        />
        <InlineAlert variant="error">
          {error ?? 'Failed to load your private speaking data.'}
          <button type="button" onClick={() => void loadData()} className="ml-2 underline text-sm">Try again</button>
        </InlineAlert>
      </div>
    );
  }

  if (!profile) {
    return (
      <div className="space-y-4">
        <ExpertRouteHero
          title="Private Speaking Sessions"
          description="Offer one-to-one speaking practice once your tutor profile is set up."
        />
        <div role="status" className="rounded-2xl border border-dashed border-border bg-surface p-6">
          <h2 className="text-base font-bold text-navy">Your tutor profile is not set up yet</h2>
          <p className="mt-2 text-sm text-navy">
            You are not currently bookable for private speaking sessions. To get started:
          </p>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-navy">
            <li>
              <Link
                href="/expert/onboarding"
                className="font-semibold text-primary underline underline-offset-2 hover:opacity-80"
              >
                Complete your tutor onboarding (profile + rates)
              </Link>
            </li>
            <li>Then contact an admin to activate your private speaking tutor profile.</li>
          </ul>
        </div>
        {error && <InlineAlert variant="warning">{error}</InlineAlert>}
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <ExpertRouteHero
        title="Private Speaking Sessions"
        description={`Welcome, ${profile.displayName}. Manage your sessions and availability.`}
      />

      {error && (
        <InlineAlert variant="warning" className="mb-2">
          {error}
          <button type="button" onClick={() => setError(null)} className="ml-2 underline text-sm">Dismiss</button>
        </InlineAlert>
      )}

      {notice && (
        <InlineAlert variant="success" className="mb-2">
          {notice}
          <button type="button" onClick={() => setNotice(null)} className="ml-2 underline text-sm">Dismiss</button>
        </InlineAlert>
      )}

      {/* Profile summary */}
      <Card padding="none" className="p-5">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h3 className="font-bold text-navy">{profile.displayName}</h3>
            <p className="text-xs text-muted">{profile.timezone} · {profile.totalSessions} sessions · {profile.averageRating.toFixed(1)} avg rating</p>
            <p className="mt-1 text-xs text-muted">
              Calendar: {calendarStatus?.connected ? `Connected${calendarStatus.connectedEmail ? ` as ${calendarStatus.connectedEmail}` : ''}` : 'Not connected'}
              {calendarStatus?.lastError ? ` · Last sync issue: ${calendarStatus.lastError}` : ''}
            </p>
            {calendarPolling ? (
              <p className="mt-1 text-xs text-muted">Waiting for Google authorization in the new tab… this updates automatically once you finish.</p>
            ) : null}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <span className={`text-xs px-2.5 py-1 rounded-full ${profile.isActive ? 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300' : 'bg-background-light text-muted'}`}>
              {profile.isActive ? 'Active' : 'Inactive'}
            </span>
            {calendarStatus?.connected ? (
              <Button type="button" variant="outline" size="sm" onClick={handleDisconnectCalendar} disabled={calendarBusy}>
                <Unlink className="w-4 h-4 mr-1" /> Disconnect Calendar
              </Button>
            ) : (
              <Button type="button" variant="primary" size="sm" onClick={handleConnectCalendar} disabled={calendarBusy || calendarPolling}>
                <Link2 className="w-4 h-4 mr-1" /> {calendarPolling ? 'Waiting for Google…' : 'Connect Google Calendar'}
              </Button>
            )}
          </div>
        </div>
      </Card>

      {/* Tabs */}
      <div className="flex gap-1 border-b border-border">
        {(['sessions', 'availability'] as ExpertTab[]).map(t => (
          <button key={t} type="button" onClick={() => setTab(t)} aria-pressed={tab === t}
            className={`px-4 py-2.5 text-sm font-medium border-b-2 transition-colors capitalize ${
              tab === t ? 'border-primary text-primary' : 'border-transparent text-muted hover:text-navy'
            }`}>
            {t === 'sessions' ? 'My Sessions' : 'Availability'}
          </button>
        ))}
      </div>

      {/* ── Sessions Tab ────────────────────────────── */}
      {tab === 'sessions' && (
        <div className="space-y-6">
          <ExpertRouteSectionHeader title="Upcoming Sessions" icon={Calendar} />
          {upcomingSessions.length === 0 ? (
            <p className="text-sm text-muted text-center py-6">No upcoming sessions.</p>
          ) : (
            <div className="space-y-3">
              {upcomingSessions.map(session => {
                const start = new Date(session.sessionStartUtc);
                const isStartingSoon = start.getTime() - Date.now() < 15 * 60 * 1000;
                // Scheduled slot fully elapsed but the booking never completed —
                // offer the tutor-side "learner no-show" action (backend also
                // gates on Confirmed/InProgress).
                const slotEnded = start.getTime() + session.durationMinutes * 60_000 < Date.now();
                const canMarkNoShow = slotEnded
                  && ['Confirmed', 'InProgress'].includes(session.status);
                return (
                  <Card key={session.id} padding="none" className="p-5">
                    <div className="flex items-center justify-between">
                      <div>
                        <div className="flex items-center gap-2 mb-1">
                          <StatusBadge status={session.status} />
                          {isStartingSoon && <span className="text-xs text-warning font-medium animate-pulse">Starting soon</span>}
                        </div>
                        <p className="text-sm font-medium text-navy">
                          {start.toLocaleDateString('en-AU', { weekday: 'short', month: 'short', day: 'numeric' })}
                          {' '}at {start.toLocaleTimeString('en-AU', { hour: '2-digit', minute: '2-digit' })}
                        </p>
                        <p className="text-xs text-muted mt-0.5">
                          <Clock className="w-3 h-3 inline mr-1" />{session.durationMinutes} min
                          · Learner: {session.learnerUserId.slice(0, 10)}…
                        </p>
                      </div>
                      <div className="flex items-center gap-2">
                        {(session.status === 'Confirmed' || session.status === 'InProgress') && (
                          <Button type="button" size="sm" onClick={() => handleStartSession(session)} disabled={startingSessionId === session.id}>
                            <Video className="w-4 h-4" aria-hidden="true" /> {startingSessionId === session.id ? 'Opening...' : 'Open LiveKit'}
                          </Button>
                        )}
                        <Button type="button" variant="outline" size="sm" onClick={() => handleDownloadInvite(session.id)}>
                          <Download className="w-4 h-4" aria-hidden="true" /> Calendar
                        </Button>
                        {canMarkNoShow && (
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            onClick={() => handleMarkNoShow(session)}
                            disabled={markingNoShowId === session.id}
                            className="border-danger/30 text-danger hover:bg-danger/10"
                          >
                            <UserX className="w-4 h-4" aria-hidden="true" /> {markingNoShowId === session.id ? 'Marking…' : 'Mark learner no-show'}
                          </Button>
                        )}
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          onClick={() => setCancelTarget(session)}
                          className="border-danger/30 text-danger hover:bg-danger/10"
                        >
                          <X className="w-4 h-4" aria-hidden="true" /> Cancel
                        </Button>
                      </div>
                    </div>
                  </Card>
                );
              })}
            </div>
          )}

          <ExpertRouteSectionHeader title="Past Sessions" icon={Star} />
          {pastSessions.length === 0 ? (
            <p className="text-sm text-muted text-center py-6">No past sessions yet.</p>
          ) : (
            <div className="space-y-3">
              {pastSessions.map(session => {
                const start = new Date(session.sessionStartUtc);
                return (
                  <Card key={session.id} padding="none" className="p-5">
                    <div className="flex items-center justify-between">
                      <div>
                        <div className="flex items-center gap-2 mb-1">
                          <StatusBadge status={session.status} />
                          {session.learnerRating != null && (
                            <span className="text-xs text-amber-500">
                              {'★'.repeat(session.learnerRating)}{'☆'.repeat(5 - session.learnerRating)}
                            </span>
                          )}
                        </div>
                        <p className="text-sm text-navy">
                          {start.toLocaleDateString('en-AU', { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' })}
                          {' '}at {start.toLocaleTimeString('en-AU', { hour: '2-digit', minute: '2-digit' })}
                        </p>
                        {session.learnerFeedback && (
                          <p className="text-xs text-muted mt-1 italic">&ldquo;{session.learnerFeedback}&rdquo;</p>
                        )}
                      </div>
                    </div>
                  </Card>
                );
              })}
            </div>
          )}
        </div>
      )}

      {/* ── Availability Tab ────────────────────────── */}
      {tab === 'availability' && (
        <div className="space-y-4">
          <ExpertRouteSectionHeader title="Weekly Availability Rules" icon={Clock} />

          {availability.length === 0 && (
            <p className="text-sm text-muted text-center py-4">No availability rules configured yet.</p>
          )}

          <div className="space-y-2">
            {availability.map(rule => (
              <Card key={rule.id} padding="none" className="p-4">
                {editingRuleId === rule.id ? (
                  <div className="flex items-center gap-3 flex-wrap">
                    <select aria-label="Day of week" value={editRule.dayOfWeek} onChange={e => setEditRule(r => ({ ...r, dayOfWeek: Number(e.target.value) }))}
                      disabled={savingRule}
                      className="px-3 py-2 border border-border rounded-lg text-sm bg-surface text-navy focus:outline-none focus:ring-2 focus:ring-primary/20">
                      {DAY_NAMES.map((name, i) => <option key={i} value={i}>{name}</option>)}
                    </select>
                    <input type="time" aria-label="Start time" value={editRule.startTime} onChange={e => setEditRule(r => ({ ...r, startTime: e.target.value }))}
                      disabled={savingRule}
                      className="px-3 py-2 border border-border rounded-lg text-sm bg-surface text-navy focus:outline-none focus:ring-2 focus:ring-primary/20" />
                    <span className="text-sm text-muted">to</span>
                    <input type="time" aria-label="End time" value={editRule.endTime} onChange={e => setEditRule(r => ({ ...r, endTime: e.target.value }))}
                      disabled={savingRule}
                      className="px-3 py-2 border border-border rounded-lg text-sm bg-surface text-navy focus:outline-none focus:ring-2 focus:ring-primary/20" />
                    <label className="flex items-center gap-1.5 text-sm text-muted">
                      <input type="checkbox" checked={editRule.isActive} onChange={e => setEditRule(r => ({ ...r, isActive: e.target.checked }))}
                        disabled={savingRule}
                        className="rounded border-border" />
                      Active
                    </label>
                    <div className="flex items-center gap-2 ml-auto">
                      <Button onClick={() => handleSaveRule(rule)} size="sm" disabled={savingRule}>
                        {savingRule ? 'Saving…' : 'Save'}
                      </Button>
                      <Button onClick={cancelEditRule} size="sm" variant="outline" disabled={savingRule}>
                        Cancel
                      </Button>
                    </div>
                  </div>
                ) : (
                  <div className="flex items-center justify-between">
                    <div>
                      <span className="text-sm font-medium text-navy">{DAY_NAMES[rule.dayOfWeek]}</span>
                      <span className="text-sm text-muted ml-2">{rule.startTime} – {rule.endTime}</span>
                      {rule.effectiveFrom && <span className="text-xs text-muted ml-2">from {rule.effectiveFrom}</span>}
                      {!rule.isActive && <span className="text-xs text-muted ml-2 italic">(inactive)</span>}
                    </div>
                    <div className="flex items-center gap-1">
                      <button type="button" onClick={() => startEditRule(rule)} className="text-muted hover:text-navy p-2.5 -m-1" aria-label="Edit rule">
                        <Pencil className="w-4 h-4" aria-hidden="true" />
                      </button>
                      <button type="button" onClick={() => handleDeleteRule(rule.id)} className="text-danger/80 hover:text-danger p-2.5 -m-1" aria-label="Delete rule">
                        <Trash2 className="w-4 h-4" aria-hidden="true" />
                      </button>
                    </div>
                  </div>
                )}
              </Card>
            ))}
          </div>

          <Card padding="none" className="p-5">
            <h4 className="text-sm font-bold text-navy mb-3">Add New Rule</h4>
            <div className="flex items-center gap-3 flex-wrap">
              <select aria-label="New rule day of week" value={newRule.dayOfWeek} onChange={e => setNewRule(r => ({ ...r, dayOfWeek: Number(e.target.value) }))}
                className="px-3 py-2 border border-border rounded-lg text-sm bg-surface text-navy focus:outline-none focus:ring-2 focus:ring-primary/20">
                {DAY_NAMES.map((name, i) => <option key={i} value={i}>{name}</option>)}
              </select>
              <input type="time" aria-label="New rule start time" value={newRule.startTime} onChange={e => setNewRule(r => ({ ...r, startTime: e.target.value }))}
                className="px-3 py-2 border border-border rounded-lg text-sm bg-surface text-navy focus:outline-none focus:ring-2 focus:ring-primary/20" />
              <span className="text-sm text-muted">to</span>
              <input type="time" aria-label="New rule end time" value={newRule.endTime} onChange={e => setNewRule(r => ({ ...r, endTime: e.target.value }))}
                className="px-3 py-2 border border-border rounded-lg text-sm bg-surface text-navy focus:outline-none focus:ring-2 focus:ring-primary/20" />
              <Button onClick={handleAddRule} size="sm">
                <Plus className="w-4 h-4 mr-1" aria-hidden="true" /> Add Rule
              </Button>
            </div>
          </Card>
        </div>
      )}

      {/* ── Cancel Confirmation Dialog ────────────── */}
      {cancelTarget && (
        <div className="overlay-safe-area fixed inset-0 z-50 flex items-center justify-center bg-navy/40 backdrop-blur-sm">
          <div role="dialog" aria-modal="true" aria-labelledby="cancel-session-title" className="max-h-[calc(100dvh-2rem-env(safe-area-inset-top)-env(safe-area-inset-bottom))] w-full max-w-md overflow-y-auto rounded-2xl border border-border bg-surface p-6 shadow-xl">
            <h3 id="cancel-session-title" className="text-lg font-bold text-navy mb-2">Cancel Session</h3>
            <p className="text-sm text-muted mb-4">
              Are you sure you want to cancel the session on{' '}
              <span className="font-medium text-navy">
                {new Date(cancelTarget.sessionStartUtc).toLocaleDateString('en-AU', { weekday: 'short', month: 'short', day: 'numeric' })}
                {' '}at {new Date(cancelTarget.sessionStartUtc).toLocaleTimeString('en-AU', { hour: '2-digit', minute: '2-digit' })}
              </span>?
              This action cannot be undone.
            </p>
            <label htmlFor="cancel-session-reason" className="block text-sm font-medium text-muted mb-1">
              Reason (optional)
            </label>
            <textarea
              id="cancel-session-reason"
              value={cancelReason}
              onChange={e => setCancelReason(e.target.value)}
              rows={2}
              maxLength={500}
              className="w-full px-3 py-2 border border-border rounded-lg text-sm bg-surface text-navy mb-4 resize-none focus:outline-none focus:ring-2 focus:ring-primary/20"
              placeholder="e.g. Schedule conflict"
            />
            <div className="flex justify-end gap-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => { setCancelTarget(null); setCancelReason(''); }}
                disabled={cancelling}
              >
                Keep Session
              </Button>
              <Button
                type="button"
                variant="destructive"
                onClick={handleCancelSession}
                disabled={cancelling}
              >
                {cancelling ? 'Cancelling…' : 'Confirm Cancel'}
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
