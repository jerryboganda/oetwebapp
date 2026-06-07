'use client';

import { useEffect, useState } from 'react';
import { ZoomMeetingEmbed } from '@/components/class/ZoomMeetingEmbed';
import { ExpertRouteHero, ExpertRouteSectionHeader } from '@/components/domain/expert-route-surface';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Calendar, Clock, Video, Star, Plus, Trash2, Pencil, X, Link2, Unlink, Download } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  type LiveClassJoinToken,
  type PrivateSpeakingCalendarStatus,
  fetchExpertPrivateSpeakingProfile,
  fetchExpertPrivateSpeakingSessions,
  fetchExpertPrivateSpeakingAvailability,
  updateExpertPrivateSpeakingAvailability,
  updateExpertPrivateSpeakingAvailabilityRule,
  deleteExpertPrivateSpeakingAvailability,
  cancelExpertPrivateSpeakingSession,
  fetchExpertPrivateSpeakingJoinToken,
  fetchExpertPrivateSpeakingCalendarStatus,
  connectExpertPrivateSpeakingGoogleCalendar,
  disconnectExpertPrivateSpeakingCalendar,
  downloadExpertPrivateSpeakingCalendarInvite,
} from '@/lib/api';
import { safeZoomUrl } from '@/lib/zoom-url';

type TutorProfile = {
  id: string; displayName: string; bio: string | null; timezone: string;
  priceOverrideMinorUnits: number | null; slotDurationOverrideMinutes: number | null;
  specialtiesJson: string; isActive: boolean; averageRating: number; totalSessions: number;
};

type ExpertSession = {
  id: string; learnerUserId: string; status: string; sessionStartUtc: string;
  durationMinutes: number; zoomJoinUrl: string | null;
  zoomStatus: string; learnerRating: number | null; learnerFeedback: string | null;
};

type AvailabilityRule = {
  id: string; dayOfWeek: number; startTime: string; endTime: string;
  effectiveFrom: string | null; effectiveTo: string | null; isActive: boolean;
};

type ExpertTab = 'sessions' | 'availability';
const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

type ActiveMeeting = {
  token: LiveClassJoinToken;
  title: string;
  startsAt: string;
};

function StatusBadge({ status }: { status: string }) {
  const colors: Record<string, string> = {
    Confirmed: 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300',
    ZoomCreated: 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300',
    InProgress: 'bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300',
    Completed: 'bg-background-light text-muted',
    Cancelled: 'bg-red-100 text-red-600',
  };
  return (
    <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${colors[status] ?? 'bg-background-light text-muted'}`}>
      {status}
    </span>
  );
}

export default function ExpertPrivateSpeakingPage() {
  const [tab, setTab] = useState<ExpertTab>('sessions');
  const [profile, setProfile] = useState<TutorProfile | null>(null);
  const [sessions, setSessions] = useState<ExpertSession[]>([]);
  const [availability, setAvailability] = useState<AvailabilityRule[]>([]);
  const [calendarStatus, setCalendarStatus] = useState<PrivateSpeakingCalendarStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [calendarBusy, setCalendarBusy] = useState(false);
  // True while we wait for the tutor to finish Google's OAuth consent in the
  // separate tab; drives a short poll of the calendar status.
  const [calendarPolling, setCalendarPolling] = useState(false);
  const [startingSessionId, setStartingSessionId] = useState<string | null>(null);
  const [activeMeeting, setActiveMeeting] = useState<ActiveMeeting | null>(null);

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
    try {
      const [profileData, sessionData, calendarData] = await Promise.all([
        fetchExpertPrivateSpeakingProfile(),
        fetchExpertPrivateSpeakingSessions(),
        fetchExpertPrivateSpeakingCalendarStatus(),
      ]);
      // Backend returns `null` when the expert has not yet created a tutor profile.
      setProfile(profileData ? (profileData as TutorProfile) : null);
      setSessions(sessionData as ExpertSession[]);
      setCalendarStatus(calendarData);
    } catch {
      setError('Failed to load your private speaking data.');
    } finally {
      setLoading(false);
    }
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

  async function handleStartSession(session: ExpertSession) {
    setStartingSessionId(session.id);
    setError(null);
    try {
      const token = await fetchExpertPrivateSpeakingJoinToken(session.id);
      if (token.sdkKey && token.signature && (token.role === 0 || token.zak)) {
        setActiveMeeting({ token, title: 'Private Speaking Session', startsAt: session.sessionStartUtc });
        return;
      }

      const joinUrl = safeZoomUrl(token.joinUrl);
      if (joinUrl) {
        window.open(joinUrl, '_blank', 'noopener,noreferrer');
        return;
      }

      setError('Zoom host details are not ready for this session yet.');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not prepare the Zoom host room.');
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
    ['Confirmed', 'ZoomCreated', 'ZoomPending', 'InProgress'].includes(s.status)
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

  if (!profile) {
    return (
      <div className="space-y-4">
        <ExpertRouteHero
          title="Private Speaking Sessions"
          description="Your tutor profile is not set up yet. Please contact an admin to get started."
        />
        <div
          role="status"
          className="rounded-2xl border border-dashed border-border bg-surface p-5 text-sm text-muted"
        >
          You are not currently available for private speaking sessions.
        </div>
        {error && <InlineAlert variant="warning">{error}</InlineAlert>}
      </div>
    );
  }

  if (activeMeeting && activeMeeting.token.sdkKey && activeMeeting.token.signature) {
    return (
      <div className="space-y-4">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-primary">Private Speaking</p>
            <h1 className="text-xl font-semibold text-navy">{activeMeeting.title}</h1>
            <p className="text-sm text-muted">{new Date(activeMeeting.startsAt).toLocaleString('en-AU', { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })}</p>
          </div>
          <Button type="button" variant="outline" onClick={() => setActiveMeeting(null)}>
            Close meeting
          </Button>
        </div>
        <ZoomMeetingEmbed joinToken={activeMeeting.token} onLeave={() => setActiveMeeting(null)} />
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
          <button onClick={() => setError(null)} className="ml-2 underline text-sm">Dismiss</button>
        </InlineAlert>
      )}

      {/* Profile summary */}
      <div className="rounded-2xl border border-border bg-surface p-5">
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
      </div>

      {/* Tabs */}
      <div className="flex gap-1 border-b border-border">
        {(['sessions', 'availability'] as ExpertTab[]).map(t => (
          <button key={t} onClick={() => setTab(t)}
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
                return (
                  <div key={session.id} className="rounded-2xl border border-border bg-surface p-5">
                    <div className="flex items-center justify-between">
                      <div>
                        <div className="flex items-center gap-2 mb-1">
                          <StatusBadge status={session.status} />
                          {isStartingSoon && <span className="text-xs text-amber-600 font-medium animate-pulse">Starting soon</span>}
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
                        {(session.status === 'ZoomCreated' || session.status === 'InProgress') && (
                          <button onClick={() => handleStartSession(session)} disabled={startingSessionId === session.id}
                            className="flex items-center gap-1.5 px-4 py-2 bg-primary hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600 text-white rounded-lg text-sm font-medium disabled:opacity-50">
                            <Video className="w-4 h-4" /> {startingSessionId === session.id ? 'Opening...' : 'Start'}
                          </button>
                        )}
                        <button onClick={() => handleDownloadInvite(session.id)}
                          className="flex items-center gap-1.5 px-3 py-2 border border-border text-muted hover:text-navy rounded-lg text-sm font-medium transition-colors">
                          <Download className="w-4 h-4" /> Calendar
                        </button>
                        <button
                          onClick={() => setCancelTarget(session)}
                          className="flex items-center gap-1.5 px-3 py-2 border border-danger/30 text-danger hover:bg-red-50 rounded-lg text-sm font-medium transition-colors"
                        >
                          <X className="w-4 h-4" /> Cancel
                        </button>
                      </div>
                    </div>
                  </div>
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
                  <div key={session.id} className="rounded-2xl border border-border bg-surface p-5">
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
                  </div>
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
              <div key={rule.id} className="rounded-2xl border border-border bg-surface p-4">
                {editingRuleId === rule.id ? (
                  <div className="flex items-center gap-3 flex-wrap">
                    <select value={editRule.dayOfWeek} onChange={e => setEditRule(r => ({ ...r, dayOfWeek: Number(e.target.value) }))}
                      disabled={savingRule}
                      className="px-3 py-2 border border-border rounded-lg text-sm bg-surface">
                      {DAY_NAMES.map((name, i) => <option key={i} value={i}>{name}</option>)}
                    </select>
                    <input type="time" value={editRule.startTime} onChange={e => setEditRule(r => ({ ...r, startTime: e.target.value }))}
                      disabled={savingRule}
                      className="px-3 py-2 border border-border rounded-lg text-sm bg-surface" />
                    <span className="text-sm text-muted">to</span>
                    <input type="time" value={editRule.endTime} onChange={e => setEditRule(r => ({ ...r, endTime: e.target.value }))}
                      disabled={savingRule}
                      className="px-3 py-2 border border-border rounded-lg text-sm bg-surface" />
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
                      <button onClick={() => startEditRule(rule)} className="text-muted hover:text-navy p-2.5 -m-1" aria-label="Edit rule">
                        <Pencil className="w-4 h-4" />
                      </button>
                      <button onClick={() => handleDeleteRule(rule.id)} className="text-red-400 hover:text-red-600 p-2.5 -m-1" aria-label="Delete rule">
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </div>
                  </div>
                )}
              </div>
            ))}
          </div>

          <div className="rounded-2xl border border-border bg-surface p-5">
            <h4 className="text-sm font-bold text-navy mb-3">Add New Rule</h4>
            <div className="flex items-center gap-3 flex-wrap">
              <select value={newRule.dayOfWeek} onChange={e => setNewRule(r => ({ ...r, dayOfWeek: Number(e.target.value) }))}
                className="px-3 py-2 border border-border rounded-lg text-sm bg-surface">
                {DAY_NAMES.map((name, i) => <option key={i} value={i}>{name}</option>)}
              </select>
              <input type="time" value={newRule.startTime} onChange={e => setNewRule(r => ({ ...r, startTime: e.target.value }))}
                className="px-3 py-2 border border-border rounded-lg text-sm bg-surface" />
              <span className="text-sm text-muted">to</span>
              <input type="time" value={newRule.endTime} onChange={e => setNewRule(r => ({ ...r, endTime: e.target.value }))}
                className="px-3 py-2 border border-border rounded-lg text-sm bg-surface" />
              <Button onClick={handleAddRule} size="sm">
                <Plus className="w-4 h-4 mr-1" /> Add Rule
              </Button>
            </div>
          </div>
        </div>
      )}

      {/* ── Cancel Confirmation Dialog ────────────── */}
      {cancelTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-navy/40 backdrop-blur-sm">
          <div className="w-full max-w-md rounded-2xl border border-border bg-surface p-6 shadow-xl">
            <h3 className="text-lg font-bold text-navy mb-2">Cancel Session</h3>
            <p className="text-sm text-muted mb-4">
              Are you sure you want to cancel the session on{' '}
              <span className="font-medium text-navy">
                {new Date(cancelTarget.sessionStartUtc).toLocaleDateString('en-AU', { weekday: 'short', month: 'short', day: 'numeric' })}
                {' '}at {new Date(cancelTarget.sessionStartUtc).toLocaleTimeString('en-AU', { hour: '2-digit', minute: '2-digit' })}
              </span>?
              This action cannot be undone.
            </p>
            <label className="block text-sm font-medium text-muted mb-1">
              Reason (optional)
            </label>
            <textarea
              value={cancelReason}
              onChange={e => setCancelReason(e.target.value)}
              rows={2}
              maxLength={500}
              className="w-full px-3 py-2 border border-border rounded-lg text-sm bg-surface text-navy mb-4 resize-none"
              placeholder="e.g. Schedule conflict"
            />
            <div className="flex justify-end gap-2">
              <button
                onClick={() => { setCancelTarget(null); setCancelReason(''); }}
                disabled={cancelling}
                className="px-4 py-2 text-sm font-medium text-muted hover:text-navy transition-colors"
              >
                Keep Session
              </button>
              <button
                onClick={handleCancelSession}
                disabled={cancelling}
                className="px-4 py-2 text-sm font-medium text-white bg-danger hover:bg-danger/90 disabled:opacity-50 rounded-lg transition-colors"
              >
                {cancelling ? 'Cancelling…' : 'Confirm Cancel'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
