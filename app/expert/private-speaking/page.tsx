'use client';

import { useEffect, useState } from 'react';
import { ExpertRouteHero, ExpertRouteSectionHeader } from '@/components/domain/expert-route-surface';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Calendar, Clock, Video, Star, Plus, Trash2, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  fetchExpertPrivateSpeakingProfile,
  fetchExpertPrivateSpeakingSessions,
  fetchExpertPrivateSpeakingAvailability,
  updateExpertPrivateSpeakingAvailability,
  deleteExpertPrivateSpeakingAvailability,
  cancelExpertPrivateSpeakingSession,
} from '@/lib/api';

type TutorProfile = {
  id: string; displayName: string; bio: string | null; timezone: string;
  priceOverrideMinorUnits: number | null; slotDurationOverrideMinutes: number | null;
  specialtiesJson: string; isActive: boolean; averageRating: number; totalSessions: number;
};

type ExpertSession = {
  id: string; learnerUserId: string; status: string; sessionStartUtc: string;
  durationMinutes: number; zoomJoinUrl: string | null; zoomStartUrl: string | null;
  zoomStatus: string; learnerRating: number | null; learnerFeedback: string | null;
};

type AvailabilityRule = {
  id: string; dayOfWeek: number; startTime: string; endTime: string;
  effectiveFrom: string | null; effectiveTo: string | null; isActive: boolean;
};

type ExpertTab = 'sessions' | 'availability';
const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

function StatusBadge({ status }: { status: string }) {
  const colors: Record<string, string> = {
    Confirmed: 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300',
    ZoomCreated: 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300',
    InProgress: 'bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300',
    Completed: 'bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400',
    Cancelled: 'bg-red-100 text-red-600 dark:bg-red-900/30 dark:text-red-300',
  };
  return (
    <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${colors[status] ?? 'bg-gray-100 text-gray-600'}`}>
      {status}
    </span>
  );
}

export default function ExpertPrivateSpeakingPage() {
  const [tab, setTab] = useState<ExpertTab>('sessions');
  const [profile, setProfile] = useState<TutorProfile | null>(null);
  const [sessions, setSessions] = useState<ExpertSession[]>([]);
  const [availability, setAvailability] = useState<AvailabilityRule[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // New availability rule form
  const [newRule, setNewRule] = useState({ dayOfWeek: 1, startTime: '09:00', endTime: '17:00' });

  // Cancel session state
  const [cancelTarget, setCancelTarget] = useState<ExpertSession | null>(null);
  const [cancelReason, setCancelReason] = useState('');
  const [cancelling, setCancelling] = useState(false);

  useEffect(() => {
    loadData();
  }, []);

  async function loadData() {
    setLoading(true);
    try {
      const [profileData, sessionData] = await Promise.all([
        fetchExpertPrivateSpeakingProfile(),
        fetchExpertPrivateSpeakingSessions(),
      ]);
      // Backend returns `null` when the expert has not yet created a tutor profile.
      setProfile(profileData ? (profileData as TutorProfile) : null);
      setSessions(sessionData as ExpertSession[]);
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
      const rules = await fetchExpertPrivateSpeakingAvailability();
      setAvailability(rules as AvailabilityRule[]);
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
          className="rounded-2xl border border-dashed border-gray-200 dark:border-gray-800 bg-white dark:bg-gray-900 p-5 text-sm text-gray-600 dark:text-gray-400"
        >
          You are not currently available for private speaking sessions.
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
          <button onClick={() => setError(null)} className="ml-2 underline text-sm">Dismiss</button>
        </InlineAlert>
      )}

      {/* Profile summary */}
      <div className="rounded-2xl border border-gray-200 dark:border-gray-800 bg-white dark:bg-gray-900 p-5">
        <div className="flex items-center justify-between">
          <div>
            <h3 className="font-medium text-gray-900 dark:text-white">{profile.displayName}</h3>
            <p className="text-xs text-gray-400">{profile.timezone} · {profile.totalSessions} sessions · {profile.averageRating.toFixed(1)} avg rating</p>
          </div>
          <span className={`text-xs px-2.5 py-1 rounded-full ${profile.isActive ? 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300' : 'bg-gray-100 text-gray-500'}`}>
            {profile.isActive ? 'Active' : 'Inactive'}
          </span>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex gap-1 border-b border-gray-200 dark:border-gray-700">
        {(['sessions', 'availability'] as ExpertTab[]).map(t => (
          <button key={t} onClick={() => setTab(t)}
            className={`px-4 py-2.5 text-sm font-medium border-b-2 transition-colors capitalize ${
              tab === t ? 'border-indigo-500 text-indigo-600 dark:text-indigo-400' : 'border-transparent text-gray-500 hover:text-gray-700'
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
            <p className="text-sm text-gray-400 text-center py-6">No upcoming sessions.</p>
          ) : (
            <div className="space-y-3">
              {upcomingSessions.map(session => {
                const start = new Date(session.sessionStartUtc);
                const isStartingSoon = start.getTime() - Date.now() < 15 * 60 * 1000;
                return (
                  <div key={session.id} className="rounded-2xl border border-gray-200 dark:border-gray-800 bg-white dark:bg-gray-900 p-5">
                    <div className="flex items-center justify-between">
                      <div>
                        <div className="flex items-center gap-2 mb-1">
                          <StatusBadge status={session.status} />
                          {isStartingSoon && <span className="text-xs text-amber-600 font-medium animate-pulse">Starting soon</span>}
                        </div>
                        <p className="text-sm font-medium text-gray-900 dark:text-white">
                          {start.toLocaleDateString('en-AU', { weekday: 'short', month: 'short', day: 'numeric' })}
                          {' '}at {start.toLocaleTimeString('en-AU', { hour: '2-digit', minute: '2-digit' })}
                        </p>
                        <p className="text-xs text-gray-400 mt-0.5">
                          <Clock className="w-3 h-3 inline mr-1" />{session.durationMinutes} min
                          · Learner: {session.learnerUserId.slice(0, 10)}…
                        </p>
                      </div>
                      <div className="flex items-center gap-2">
                        {session.zoomStartUrl && (
                          <a href={session.zoomStartUrl} target="_blank" rel="noopener noreferrer"
                            className="flex items-center gap-1.5 px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg text-sm font-medium">
                            <Video className="w-4 h-4" /> Start Zoom
                          </a>
                        )}
                        <button
                          onClick={() => setCancelTarget(session)}
                          className="flex items-center gap-1.5 px-3 py-2 border border-red-200 dark:border-red-800 text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg text-sm font-medium transition-colors"
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
            <p className="text-sm text-gray-400 text-center py-6">No past sessions yet.</p>
          ) : (
            <div className="space-y-3">
              {pastSessions.map(session => {
                const start = new Date(session.sessionStartUtc);
                return (
                  <div key={session.id} className="rounded-2xl border border-gray-200 dark:border-gray-800 bg-white dark:bg-gray-900 p-5">
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
                        <p className="text-sm text-gray-700 dark:text-gray-300">
                          {start.toLocaleDateString('en-AU', { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' })}
                          {' '}at {start.toLocaleTimeString('en-AU', { hour: '2-digit', minute: '2-digit' })}
                        </p>
                        {session.learnerFeedback && (
                          <p className="text-xs text-gray-500 mt-1 italic">&ldquo;{session.learnerFeedback}&rdquo;</p>
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
            <p className="text-sm text-gray-400 text-center py-4">No availability rules configured yet.</p>
          )}

          <div className="space-y-2">
            {availability.map(rule => (
              <div key={rule.id} className="rounded-2xl border border-gray-200 dark:border-gray-800 bg-white dark:bg-gray-900 p-4">
                <div className="flex items-center justify-between">
                  <div>
                    <span className="text-sm font-medium text-gray-900 dark:text-white">{DAY_NAMES[rule.dayOfWeek]}</span>
                    <span className="text-sm text-gray-500 ml-2">{rule.startTime} – {rule.endTime}</span>
                    {rule.effectiveFrom && <span className="text-xs text-gray-400 ml-2">from {rule.effectiveFrom}</span>}
                  </div>
                  <button onClick={() => handleDeleteRule(rule.id)} className="text-red-400 hover:text-red-600 p-2.5 -m-1">
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
              </div>
            ))}
          </div>

          <div className="rounded-2xl border border-gray-200 dark:border-gray-800 bg-white dark:bg-gray-900 p-5">
            <h4 className="text-sm font-medium text-gray-700 dark:text-gray-300 mb-3">Add New Rule</h4>
            <div className="flex items-center gap-3 flex-wrap">
              <select value={newRule.dayOfWeek} onChange={e => setNewRule(r => ({ ...r, dayOfWeek: Number(e.target.value) }))}
                className="px-3 py-2 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900">
                {DAY_NAMES.map((name, i) => <option key={i} value={i}>{name}</option>)}
              </select>
              <input type="time" value={newRule.startTime} onChange={e => setNewRule(r => ({ ...r, startTime: e.target.value }))}
                className="px-3 py-2 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900" />
              <span className="text-sm text-gray-400">to</span>
              <input type="time" value={newRule.endTime} onChange={e => setNewRule(r => ({ ...r, endTime: e.target.value }))}
                className="px-3 py-2 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900" />
              <Button onClick={handleAddRule} size="sm">
                <Plus className="w-4 h-4 mr-1" /> Add Rule
              </Button>
            </div>
          </div>
        </div>
      )}

      {/* ── Cancel Confirmation Dialog ────────────── */}
      {cancelTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm">
          <div className="w-full max-w-md rounded-2xl border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-900 p-6 shadow-xl">
            <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">Cancel Session</h3>
            <p className="text-sm text-gray-500 dark:text-gray-400 mb-4">
              Are you sure you want to cancel the session on{' '}
              <span className="font-medium text-gray-700 dark:text-gray-300">
                {new Date(cancelTarget.sessionStartUtc).toLocaleDateString('en-AU', { weekday: 'short', month: 'short', day: 'numeric' })}
                {' '}at {new Date(cancelTarget.sessionStartUtc).toLocaleTimeString('en-AU', { hour: '2-digit', minute: '2-digit' })}
              </span>?
              This action cannot be undone.
            </p>
            <label className="block text-sm font-medium text-gray-600 dark:text-gray-400 mb-1">
              Reason (optional)
            </label>
            <textarea
              value={cancelReason}
              onChange={e => setCancelReason(e.target.value)}
              rows={2}
              maxLength={500}
              className="w-full px-3 py-2 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-800 text-gray-900 dark:text-white mb-4 resize-none"
              placeholder="e.g. Schedule conflict"
            />
            <div className="flex justify-end gap-2">
              <button
                onClick={() => { setCancelTarget(null); setCancelReason(''); }}
                disabled={cancelling}
                className="px-4 py-2 text-sm font-medium text-gray-600 dark:text-gray-400 hover:text-gray-800 dark:hover:text-gray-200 transition-colors"
              >
                Keep Session
              </button>
              <button
                onClick={handleCancelSession}
                disabled={cancelling}
                className="px-4 py-2 text-sm font-medium text-white bg-red-600 hover:bg-red-700 disabled:opacity-50 rounded-lg transition-colors"
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
