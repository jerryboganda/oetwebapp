'use client';

import { useEffect, useState } from 'react';
import { Users, Calendar, BarChart3, Settings, Plus, Trash2, RefreshCw, X } from 'lucide-react';
import { AdminRouteHero, AdminRoutePanel, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  fetchAdminPrivateSpeakingConfig,
  updateAdminPrivateSpeakingConfig,
  fetchAdminPrivateSpeakingStats,
  fetchAdminPrivateSpeakingTutors,
  createAdminPrivateSpeakingTutor,
  updateAdminPrivateSpeakingTutor,
  fetchAdminPrivateSpeakingAvailability,
  createAdminPrivateSpeakingAvailabilityRule,
  deleteAdminPrivateSpeakingAvailabilityRule,
  fetchAdminPrivateSpeakingBookings,
  cancelAdminPrivateSpeakingBooking,
  completeAdminPrivateSpeakingBooking,
  retryAdminPrivateSpeakingZoom,
  fetchAdminPrivateSpeakingAuditLogs,
} from '@/lib/api';

type Stats = {
  totalBookings: number; confirmedBookings: number; completedBookings: number;
  cancelledBookings: number; failedPayments: number; zoomFailures: number;
  activeTutors: number; upcomingSessions: number; revenueMinorUnitsLast30Days: number;
};

type Config = {
  isEnabled: boolean; defaultPriceMinorUnits: number; currency: string;
  defaultSlotDurationMinutes: number; bufferMinutesBetweenSlots: number;
  minBookingLeadTimeHours: number; maxBookingAdvanceDays: number;
  cancellationWindowHours: number; rescheduleWindowHours: number;
  reservationTimeoutMinutes: number; reminderOffsetsHoursJson: string;
};

type TutorProfile = {
  id: string; expertUserId: string; displayName: string; bio: string | null;
  timezone: string; priceOverrideMinorUnits: number | null;
  slotDurationOverrideMinutes: number | null; specialtiesJson: string;
  isActive: boolean; averageRating: number; totalSessions: number;
};

type AvailabilityRule = {
  id: string; dayOfWeek: number; startTime: string; endTime: string;
  effectiveFrom: string | null; effectiveTo: string | null; isActive: boolean;
};

type AdminBooking = {
  id: string; tutorProfileId: string; tutorName: string | null;
  learnerUserId: string; status: string; sessionStartUtc: string;
  durationMinutes: number; priceMinorUnits: number; currency: string;
  paymentStatus: string; zoomStatus: string; createdAt: string;
};

type AuditLog = {
  id: string; bookingId: string | null; actorId: string; actorRole: string;
  action: string; details: string | null; createdAt: string;
};

const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

type AdminTab = 'overview' | 'config' | 'tutors' | 'bookings' | 'audit';

function formatPrice(minorUnits: number, currency = 'aud') {
  return new Intl.NumberFormat('en-AU', { style: 'currency', currency }).format(minorUnits / 100);
}

export default function AdminPrivateSpeakingPage() {
  const [tab, setTab] = useState<AdminTab>('overview');
  const [stats, setStats] = useState<Stats | null>(null);
  const [config, setConfig] = useState<Config | null>(null);
  const [tutors, setTutors] = useState<TutorProfile[]>([]);
  const [bookings, setBookings] = useState<AdminBooking[]>([]);
  const [auditLogs, setAuditLogs] = useState<AuditLog[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  // Create tutor form
  const [showCreateTutor, setShowCreateTutor] = useState(false);
  const [newTutor, setNewTutor] = useState({ expertUserId: '', displayName: '', timezone: 'Australia/Sydney', bio: '' });

  // Selected tutor for availability management
  const [selectedTutorId, setSelectedTutorId] = useState<string | null>(null);
  const [availability, setAvailability] = useState<AvailabilityRule[]>([]);
  const [newRule, setNewRule] = useState({ dayOfWeek: 1, startTime: '09:00', endTime: '17:00' });

  useEffect(() => {
    loadOverview();
  }, []);

  async function loadOverview() {
    setLoading(true);
    try {
      const [s, c, t] = await Promise.all([
        fetchAdminPrivateSpeakingStats(),
        fetchAdminPrivateSpeakingConfig(),
        fetchAdminPrivateSpeakingTutors(),
      ]);
      setStats(s as Stats);
      setConfig(c as Config);
      setTutors(t as TutorProfile[]);
    } catch {
      setError('Failed to load private speaking data.');
    } finally {
      setLoading(false);
    }
  }

  async function handleSaveConfig() {
    if (!config) return;
    setSaving(true);
    try {
      const updated = await updateAdminPrivateSpeakingConfig(config as unknown as Record<string, unknown>) as Config;
      setConfig(updated);
      setError(null);
    } catch {
      setError('Failed to save config.');
    } finally {
      setSaving(false);
    }
  }

  async function handleCreateTutor() {
    setSaving(true);
    try {
      await createAdminPrivateSpeakingTutor({
        expertUserId: newTutor.expertUserId,
        displayName: newTutor.displayName,
        timezone: newTutor.timezone,
        bio: newTutor.bio || undefined,
      });
      const updated = await fetchAdminPrivateSpeakingTutors() as TutorProfile[];
      setTutors(updated);
      setShowCreateTutor(false);
      setNewTutor({ expertUserId: '', displayName: '', timezone: 'Australia/Sydney', bio: '' });
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to create tutor.');
    } finally {
      setSaving(false);
    }
  }

  async function handleToggleTutor(profileId: string, isActive: boolean) {
    await updateAdminPrivateSpeakingTutor(profileId, { isActive: !isActive });
    setTutors(prev => prev.map(t => t.id === profileId ? { ...t, isActive: !isActive } : t));
  }

  async function handleLoadAvailability(profileId: string) {
    setSelectedTutorId(profileId);
    const rules = await fetchAdminPrivateSpeakingAvailability(profileId) as AvailabilityRule[];
    setAvailability(rules);
  }

  async function handleAddRule() {
    if (!selectedTutorId) return;
    await createAdminPrivateSpeakingAvailabilityRule(selectedTutorId, newRule);
    const rules = await fetchAdminPrivateSpeakingAvailability(selectedTutorId) as AvailabilityRule[];
    setAvailability(rules);
  }

  async function handleDeleteRule(ruleId: string) {
    if (!selectedTutorId) return;
    await deleteAdminPrivateSpeakingAvailabilityRule(selectedTutorId, ruleId);
    setAvailability(prev => prev.filter(r => r.id !== ruleId));
  }

  async function loadBookings() {
    const data = await fetchAdminPrivateSpeakingBookings() as { items: AdminBooking[] };
    setBookings(data.items);
  }

  async function loadAuditLogs() {
    const data = await fetchAdminPrivateSpeakingAuditLogs() as AuditLog[];
    setAuditLogs(data);
  }

  useEffect(() => {
    if (tab === 'bookings') loadBookings();
    if (tab === 'audit') loadAuditLogs();
  }, [tab]);

  if (loading) {
    return (
      <div className="space-y-4"><Skeleton className="h-20 rounded-xl" /><Skeleton className="h-48 rounded-xl" /></div>
    );
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Private speaking">
      <AdminRouteHero
        eyebrow="Operations"
        icon={Calendar}
        accent="navy"
        title="Private speaking sessions"
        description="Manage tutors, availability, bookings, and session configuration."
      />

      {error && <InlineAlert variant="warning">{error}<button onClick={() => setError(null)} className="ml-2"><X className="w-4 h-4 inline" /></button></InlineAlert>}

      {/* Tab navigation */}
      <div className="flex gap-1 border-b border-border dark:border-border">
        {(['overview', 'config', 'tutors', 'bookings', 'audit'] as AdminTab[]).map(t => (          <button key={t} onClick={() => setTab(t)}
            className={`px-4 py-2.5 text-sm font-medium border-b-2 transition-colors capitalize ${
              tab === t ? 'border-primary text-primary dark:text-primary' : 'border-transparent text-muted hover:text-navy'
            }`}>
            {t}
          </button>
        ))}
      </div>

      {/* ── Overview Tab ────────────────────────────── */}
      {tab === 'overview' && stats && (
        <>
          <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-3 mb-6">
            <AdminRouteSummaryCard label="Active Tutors" value={String(stats.activeTutors)} icon={Users} />
            <AdminRouteSummaryCard label="Upcoming" value={String(stats.upcomingSessions)} icon={Calendar} />
            <AdminRouteSummaryCard label="Completed" value={String(stats.completedBookings)} icon={BarChart3} />
            <AdminRouteSummaryCard label="Cancelled" value={String(stats.cancelledBookings)} tone={stats.cancelledBookings > 0 ? 'danger' : 'default'} />
            <AdminRouteSummaryCard label="Revenue (30d)" value={formatPrice(stats.revenueMinorUnitsLast30Days, config?.currency)} />
          </div>
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            <AdminRoutePanel title="Total Bookings" className="text-center">
              <span className="text-2xl font-bold text-navy dark:text-navy">{stats.totalBookings}</span>
            </AdminRoutePanel>
            <AdminRoutePanel title="Confirmed" className="text-center">
              <span className="text-2xl font-bold text-primary">{stats.confirmedBookings}</span>
            </AdminRoutePanel>
            <AdminRoutePanel title="Payment Failures" className="text-center">
              <span className={`text-2xl font-bold ${stats.failedPayments > 0 ? 'text-danger' : 'text-success'}`}>{stats.failedPayments}</span>
            </AdminRoutePanel>
            <AdminRoutePanel title="Zoom Failures" className="text-center">
              <span className={`text-2xl font-bold ${stats.zoomFailures > 0 ? 'text-danger' : 'text-success'}`}>{stats.zoomFailures}</span>
            </AdminRoutePanel>
          </div>
        </>
      )}

      {/* ── Config Tab ──────────────────────────────── */}
      {tab === 'config' && config && (
          <AdminRoutePanel title="Module Configuration">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <label className="flex items-center gap-3">
                <input type="checkbox" checked={config.isEnabled} onChange={e => setConfig(c => c ? { ...c, isEnabled: e.target.checked } : c)}
                  className="w-4 h-4 rounded text-primary" />
                <span className="text-sm text-navy dark:text-navy">Module Enabled</span>
              </label>
              <div>
                <label className="text-xs text-muted mb-1 block">Default Price (minor units)</label>
                <input type="number" value={config.defaultPriceMinorUnits}
                  onChange={e => setConfig(c => c ? { ...c, defaultPriceMinorUnits: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-border dark:border-border rounded-lg text-sm bg-surface dark:bg-surface" />
              </div>
              <div>
                <label className="text-xs text-muted mb-1 block">Currency</label>
                <input type="text" value={config.currency}
                  onChange={e => setConfig(c => c ? { ...c, currency: e.target.value } : c)}
                  className="w-full px-3 py-2 border border-border dark:border-border rounded-lg text-sm bg-surface dark:bg-surface" />
              </div>
              <div>
                <label className="text-xs text-muted mb-1 block">Slot Duration (minutes)</label>
                <input type="number" value={config.defaultSlotDurationMinutes}
                  onChange={e => setConfig(c => c ? { ...c, defaultSlotDurationMinutes: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-border dark:border-border rounded-lg text-sm bg-surface dark:bg-surface" />
              </div>
              <div>
                <label className="text-xs text-muted mb-1 block">Buffer Between Slots (minutes)</label>
                <input type="number" value={config.bufferMinutesBetweenSlots}
                  onChange={e => setConfig(c => c ? { ...c, bufferMinutesBetweenSlots: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-border dark:border-border rounded-lg text-sm bg-surface dark:bg-surface" />
              </div>
              <div>
                <label className="text-xs text-muted mb-1 block">Min Lead Time (hours)</label>
                <input type="number" value={config.minBookingLeadTimeHours}
                  onChange={e => setConfig(c => c ? { ...c, minBookingLeadTimeHours: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-border dark:border-border rounded-lg text-sm bg-surface dark:bg-surface" />
              </div>
              <div>
                <label className="text-xs text-muted mb-1 block">Max Advance Days</label>
                <input type="number" value={config.maxBookingAdvanceDays}
                  onChange={e => setConfig(c => c ? { ...c, maxBookingAdvanceDays: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-border dark:border-border rounded-lg text-sm bg-surface dark:bg-surface" />
              </div>
              <div>
                <label className="text-xs text-muted mb-1 block">Cancellation Window (hours)</label>
                <input type="number" value={config.cancellationWindowHours}
                  onChange={e => setConfig(c => c ? { ...c, cancellationWindowHours: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-border dark:border-border rounded-lg text-sm bg-surface dark:bg-surface" />
              </div>
              <div>
                <label className="text-xs text-muted mb-1 block">Reservation Timeout (minutes)</label>
                <input type="number" value={config.reservationTimeoutMinutes}
                  onChange={e => setConfig(c => c ? { ...c, reservationTimeoutMinutes: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-border dark:border-border rounded-lg text-sm bg-surface dark:bg-surface" />
              </div>
            </div>
            <button onClick={handleSaveConfig} disabled={saving}
              className="mt-4 px-5 py-2 bg-primary hover:bg-primary-dark text-white rounded-lg text-sm font-medium disabled:opacity-50">
              {saving ? 'Saving...' : 'Save Configuration'}
            </button>
          </AdminRoutePanel>
      )}

      {/* ── Tutors Tab ───────────────────────────── */}
      {tab === 'tutors' && (
        <>
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-lg font-semibold text-navy dark:text-navy">Tutor Profiles</h3>
            <button onClick={() => setShowCreateTutor(true)} className="flex items-center gap-2 px-4 py-2 bg-primary hover:bg-primary-dark text-white rounded-lg text-sm font-medium">
              <Plus className="w-4 h-4" /> Add Tutor
            </button>
          </div>

          {showCreateTutor && (
            <AdminRoutePanel title="Create Tutor Profile" className="mb-4">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <input type="text" placeholder="Expert User ID" value={newTutor.expertUserId}
                  onChange={e => setNewTutor(p => ({ ...p, expertUserId: e.target.value }))}
                  className="px-3 py-2 border border-border dark:border-border rounded-lg text-sm bg-surface dark:bg-surface" />
                <input type="text" placeholder="Display Name" value={newTutor.displayName}
                  onChange={e => setNewTutor(p => ({ ...p, displayName: e.target.value }))}
                  className="px-3 py-2 border border-border dark:border-border rounded-lg text-sm bg-surface dark:bg-surface" />
                <input type="text" placeholder="Timezone (e.g. Australia/Sydney)" value={newTutor.timezone}
                  onChange={e => setNewTutor(p => ({ ...p, timezone: e.target.value }))}
                  className="px-3 py-2 border border-border dark:border-border rounded-lg text-sm bg-surface dark:bg-surface" />
                <input type="text" placeholder="Bio (optional)" value={newTutor.bio}
                  onChange={e => setNewTutor(p => ({ ...p, bio: e.target.value }))}
                  className="px-3 py-2 border border-border dark:border-border rounded-lg text-sm bg-surface dark:bg-surface" />
              </div>
              <div className="flex gap-2 mt-3">
                <button onClick={handleCreateTutor} disabled={saving || !newTutor.expertUserId || !newTutor.displayName}
                  className="px-4 py-2 bg-primary hover:bg-primary-dark text-white rounded-lg text-sm font-medium disabled:opacity-50">
                  {saving ? 'Creating...' : 'Create'}
                </button>
                <button onClick={() => setShowCreateTutor(false)} className="px-4 py-2 border border-border dark:border-border rounded-lg text-sm text-muted dark:text-muted">Cancel</button>
              </div>
            </AdminRoutePanel>
          )}

          <div className="space-y-3">
            {tutors.map(tutor => (
              <AdminRoutePanel key={tutor.id} title="" className="!p-4">
                <div className="flex items-center justify-between">
                  <div>
                    <div className="flex items-center gap-2">
                      <span className="font-medium text-navy dark:text-navy">{tutor.displayName}</span>
                      <span className={`text-xs px-2 py-0.5 rounded-full ${tutor.isActive ? 'bg-success/10 text-success dark:bg-green-900/30 dark:text-success' : 'bg-lavender/30 text-muted'}`}>
                        {tutor.isActive ? 'Active' : 'Inactive'}
                      </span>
                    </div>
                    <div className="text-xs text-muted mt-0.5">
                      {tutor.timezone} · {tutor.totalSessions} sessions · Rating: {tutor.averageRating.toFixed(1)}
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    <button onClick={() => handleToggleTutor(tutor.id, tutor.isActive)}
                      className={`text-xs px-3 py-2 rounded-lg ${tutor.isActive ? 'bg-danger/10 text-danger hover:bg-danger/15' : 'bg-success/10 text-success hover:bg-success/10'}`}>
                      {tutor.isActive ? 'Deactivate' : 'Activate'}
                    </button>
                    <button onClick={() => handleLoadAvailability(tutor.id)}
                      className="text-xs px-3 py-2 bg-lavender/30 dark:bg-surface rounded-lg text-muted dark:text-navy hover:bg-lavender/60">
                      <Settings className="w-3.5 h-3.5 inline mr-1" /> Availability
                    </button>
                  </div>
                </div>

                {/* Availability panel */}
                {selectedTutorId === tutor.id && (
                  <div className="mt-4 pt-4 border-t border-border dark:border-border">
                    <h4 className="text-sm font-medium text-navy dark:text-navy mb-3">Weekly Availability Rules</h4>
                    {availability.length === 0 && <p className="text-xs text-muted mb-3">No availability rules yet.</p>}
                    <div className="space-y-2 mb-3">
                      {availability.map(rule => (
                        <div key={rule.id} className="flex items-center justify-between bg-background-light dark:bg-surface rounded-lg px-3 py-2">
                          <span className="text-sm text-navy dark:text-navy">
                            {DAY_NAMES[rule.dayOfWeek]} {rule.startTime} – {rule.endTime}
                          </span>
                          <button onClick={() => handleDeleteRule(rule.id)} className="text-danger hover:text-danger">
                            <Trash2 className="w-3.5 h-3.5" />
                          </button>
                        </div>
                      ))}
                    </div>
                    <div className="flex items-center gap-2">
                      <select value={newRule.dayOfWeek} onChange={e => setNewRule(r => ({ ...r, dayOfWeek: Number(e.target.value) }))}
                        className="px-2 py-1.5 border border-border dark:border-border rounded text-xs bg-surface dark:bg-surface">
                        {DAY_NAMES.map((name, i) => <option key={i} value={i}>{name}</option>)}
                      </select>
                      <input type="time" value={newRule.startTime} onChange={e => setNewRule(r => ({ ...r, startTime: e.target.value }))}
                        className="px-2 py-1.5 border border-border dark:border-border rounded text-xs bg-surface dark:bg-surface" />
                      <span className="text-xs text-muted">to</span>
                      <input type="time" value={newRule.endTime} onChange={e => setNewRule(r => ({ ...r, endTime: e.target.value }))}
                        className="px-2 py-1.5 border border-border dark:border-border rounded text-xs bg-surface dark:bg-surface" />
                      <button onClick={handleAddRule} className="px-3 py-1.5 bg-primary text-white rounded text-xs hover:bg-primary-dark">
                        <Plus className="w-3.5 h-3.5 inline" /> Add
                      </button>
                    </div>
                  </div>
                )}
              </AdminRoutePanel>
            ))}
          </div>
        </>
      )}

      {/* ── Bookings Tab ─────────────────────────── */}
      {tab === 'bookings' && (
        <AdminRoutePanel title="All bookings">
          {bookings.length === 0 ? (
            <p className="text-sm text-muted text-center py-8">No bookings found.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border dark:border-border text-left text-xs text-muted uppercase">
                    <th className="pb-2 pr-3">Booking</th>
                    <th className="pb-2 pr-3">Tutor</th>
                    <th className="pb-2 pr-3">Session</th>
                    <th className="pb-2 pr-3">Status</th>
                    <th className="pb-2 pr-3">Payment</th>
                    <th className="pb-2 pr-3">Zoom</th>
                    <th className="pb-2">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {bookings.map(b => (
                    <tr key={b.id} className="border-b border-border dark:border-border">
                      <td className="py-2 pr-3 font-mono text-xs">{b.id.slice(0, 12)}…</td>
                      <td className="py-2 pr-3">{b.tutorName ?? '—'}</td>
                      <td className="py-2 pr-3">{new Date(b.sessionStartUtc).toLocaleString('en-AU', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })}</td>
                      <td className="py-2 pr-3"><span className="text-xs px-2 py-0.5 rounded-full bg-lavender/30 dark:bg-surface">{b.status}</span></td>
                      <td className="py-2 pr-3 text-xs">{b.paymentStatus}</td>
                      <td className="py-2 pr-3 text-xs">{b.zoomStatus ?? '—'}</td>
                      <td className="py-2 flex items-center gap-1.5">
                        {(b.status === 'Confirmed' || b.status === 'ZoomCreated') && (
                          <>
                            <button onClick={async () => { await completeAdminPrivateSpeakingBooking(b.id); loadBookings(); }}
                              className="text-xs px-2 py-2 bg-success/10 text-success rounded hover:bg-success/10">Complete</button>
                            <button onClick={async () => { await cancelAdminPrivateSpeakingBooking(b.id, 'Admin cancelled'); loadBookings(); }}
                              className="text-xs px-2 py-2 bg-danger/10 text-danger rounded hover:bg-danger/15">Cancel</button>
                          </>
                        )}
                        {b.zoomStatus === 'Failed' && (
                          <button onClick={async () => { await retryAdminPrivateSpeakingZoom(b.id); loadBookings(); }}
                            className="text-xs px-2 py-2 bg-lavender text-primary rounded hover:bg-lavender/60 flex items-center gap-1">
                            <RefreshCw className="w-3 h-3" /> Retry Zoom
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </AdminRoutePanel>
      )}

      {/* ── Audit Logs Tab ──────────────────────── */}
      {tab === 'audit' && (
        <AdminRoutePanel title="Audit logs">
          {auditLogs.length === 0 ? (
            <p className="text-sm text-muted text-center py-8">No audit logs found.</p>
          ) : (
            <div className="space-y-2">
              {auditLogs.map(log => (
                <div key={log.id} className="bg-background-light dark:bg-surface rounded-lg px-4 py-2 text-sm">
                  <div className="flex items-center gap-2 text-xs text-muted">
                    <span>{new Date(log.createdAt).toLocaleString('en-AU')}</span>
                    <span className="font-mono">{log.actorRole}/{log.actorId.slice(0, 10)}</span>
                  </div>
                  <div className="text-navy dark:text-navy mt-0.5">
                    <span className="font-medium">{log.action}</span>
                    {log.bookingId && <span className="text-xs ml-2 text-muted">Booking: {log.bookingId.slice(0, 12)}</span>}
                  </div>
                  {log.details && <p className="text-xs text-muted mt-0.5">{log.details}</p>}
                </div>
              ))}
            </div>
          )}
        </AdminRoutePanel>
      )}
    </AdminRouteWorkspace>
  );
}
