'use client';

import { useEffect, useState } from 'react';
import { Users, Calendar, BarChart3, Settings, Plus, Trash2, RefreshCw, X, CheckCircle2, CreditCard, ListChecks, Video } from 'lucide-react';
import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { PageHeader } from '@/components/admin/ui/page-header';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
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
  cancellationWindowHours: number; allowReschedule: boolean; rescheduleWindowHours: number;
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
  entitlementConsumed?: boolean; entitlementRestoredAt?: string | null;
  googleCalendarSyncStatus?: string | null;
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
  useAdminAuth();
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
      <AdminPageShell>
        <div className="space-y-4">
          <Skeleton className="h-20 rounded-admin-lg" />
          <Skeleton className="h-48 rounded-admin-lg" />
        </div>
      </AdminPageShell>
    );
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Private speaking">
      <AdminPageShell>
        <PageHeader
          title="Private speaking sessions"
          description="Manage tutors, availability, bookings, and session configuration."
          breadcrumbs={[
            { label: 'Admin', href: '/admin' },
            { label: 'Private Speaking' },
          ]}
          icon={<Calendar className="h-5 w-5" aria-hidden="true" />}
        />

      {error && <InlineAlert variant="warning">{error}<button onClick={() => setError(null)} className="ml-2"><X className="w-4 h-4 inline" /></button></InlineAlert>}

      {/* Tab navigation */}
      <div className="flex gap-1 border-b border-admin-border">
        {(['overview', 'config', 'tutors', 'bookings', 'audit'] as AdminTab[]).map(t => (          <button key={t} onClick={() => setTab(t)}
            className={`px-4 py-2.5 text-sm font-medium border-b-2 transition-colors capitalize ${
              tab === t ? 'border-[var(--admin-primary)] text-[var(--admin-primary)]' : 'border-transparent text-admin-fg-muted hover:text-admin-fg-strong'
            }`}>
            {t}
          </button>
        ))}
      </div>

      {/* ── Overview Tab ────────────────────────────── */}
      {tab === 'overview' && stats && (
        <>
          <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-3 mb-6">
            <KpiTile label="Active Tutors" value={String(stats.activeTutors)} icon={<Users className="h-4 w-4" />} size="sm" />
            <KpiTile label="Upcoming" value={String(stats.upcomingSessions)} icon={<Calendar className="h-4 w-4" />} size="sm" />
            <KpiTile label="Completed" value={String(stats.completedBookings)} icon={<BarChart3 className="h-4 w-4" />} size="sm" />
            <KpiTile label="Cancelled" value={String(stats.cancelledBookings)} tone={stats.cancelledBookings > 0 ? 'danger' : 'default'} size="sm" />
            <KpiTile label="Revenue (30d)" value={formatPrice(stats.revenueMinorUnitsLast30Days, config?.currency)} size="sm" />
          </div>
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            <KpiTile label="Total Bookings" value={String(stats.totalBookings)} icon={<ListChecks className="h-4 w-4" />} size="sm" />
            <KpiTile label="Confirmed" value={String(stats.confirmedBookings)} icon={<CheckCircle2 className="h-4 w-4" />} size="sm" />
            <KpiTile label="Payment Failures" value={String(stats.failedPayments)} icon={<CreditCard className="h-4 w-4" />} tone={stats.failedPayments > 0 ? 'warning' : 'default'} size="sm" />
            <KpiTile label="Zoom Failures" value={String(stats.zoomFailures)} icon={<Video className="h-4 w-4" />} tone={stats.zoomFailures > 0 ? 'warning' : 'default'} size="sm" />
          </div>
        </>
      )}

      {/* ── Config Tab ──────────────────────────────── */}
      {tab === 'config' && config && (
          <Card>
            <CardHeader><CardTitle>Module Configuration</CardTitle></CardHeader>
            <CardContent>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <label className="flex items-center gap-3">
                <input type="checkbox" checked={config.isEnabled} onChange={e => setConfig(c => c ? { ...c, isEnabled: e.target.checked } : c)}
                  className="w-4 h-4 rounded text-primary" />
                <span className="text-sm text-admin-fg-strong">Module Enabled</span>
              </label>
              <label className="flex items-center gap-3">
                <input type="checkbox" checked={config.allowReschedule} onChange={e => setConfig(c => c ? { ...c, allowReschedule: e.target.checked } : c)}
                  className="w-4 h-4 rounded text-primary" />
                <span className="text-sm text-admin-fg-strong">Learner Reschedule Enabled</span>
              </label>
              <div>
                <label className="text-xs text-admin-fg-muted mb-1 block">Default Price (minor units)</label>
                <input type="number" value={config.defaultPriceMinorUnits}
                  onChange={e => setConfig(c => c ? { ...c, defaultPriceMinorUnits: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
              </div>
              <div>
                <label className="text-xs text-admin-fg-muted mb-1 block">Currency</label>
                <input type="text" value={config.currency}
                  onChange={e => setConfig(c => c ? { ...c, currency: e.target.value } : c)}
                  className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
              </div>
              <div>
                <label className="text-xs text-admin-fg-muted mb-1 block">Slot Duration (minutes)</label>
                <input type="number" value={config.defaultSlotDurationMinutes}
                  onChange={e => setConfig(c => c ? { ...c, defaultSlotDurationMinutes: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
              </div>
              <div>
                <label className="text-xs text-admin-fg-muted mb-1 block">Buffer Between Slots (minutes)</label>
                <input type="number" value={config.bufferMinutesBetweenSlots}
                  onChange={e => setConfig(c => c ? { ...c, bufferMinutesBetweenSlots: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
              </div>
              <div>
                <label className="text-xs text-admin-fg-muted mb-1 block">Min Lead Time (hours)</label>
                <input type="number" value={config.minBookingLeadTimeHours}
                  onChange={e => setConfig(c => c ? { ...c, minBookingLeadTimeHours: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
              </div>
              <div>
                <label className="text-xs text-admin-fg-muted mb-1 block">Max Advance Days</label>
                <input type="number" value={config.maxBookingAdvanceDays}
                  onChange={e => setConfig(c => c ? { ...c, maxBookingAdvanceDays: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
              </div>
              <div>
                <label className="text-xs text-admin-fg-muted mb-1 block">Cancellation Window (hours)</label>
                <input type="number" value={config.cancellationWindowHours}
                  onChange={e => setConfig(c => c ? { ...c, cancellationWindowHours: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
              </div>
              <div>
                <label className="text-xs text-admin-fg-muted mb-1 block">Reschedule Window (hours)</label>
                <input type="number" value={config.rescheduleWindowHours}
                  onChange={e => setConfig(c => c ? { ...c, rescheduleWindowHours: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
              </div>
              <div>
                <label className="text-xs text-admin-fg-muted mb-1 block">Reservation Timeout (minutes)</label>
                <input type="number" value={config.reservationTimeoutMinutes}
                  onChange={e => setConfig(c => c ? { ...c, reservationTimeoutMinutes: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
              </div>
            </div>
            <Button variant="primary" onClick={handleSaveConfig} loading={saving} className="mt-4">
              {saving ? 'Saving...' : 'Save Configuration'}
            </Button>
            </CardContent>
          </Card>
      )}

      {/* ── Tutors Tab ───────────────────────────── */}
      {tab === 'tutors' && (
        <>
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-lg font-semibold text-admin-fg-strong">Tutor Profiles</h3>
            <Button variant="primary" onClick={() => setShowCreateTutor(true)}>
              <Plus className="w-4 h-4" /> Add Tutor
            </Button>
          </div>

          {showCreateTutor && (
            <Card className="mb-4">
              <CardHeader><CardTitle>Create Tutor Profile</CardTitle></CardHeader>
              <CardContent>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <input type="text" placeholder="Expert User ID" value={newTutor.expertUserId}
                  onChange={e => setNewTutor(p => ({ ...p, expertUserId: e.target.value }))}
                  className="px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
                <input type="text" placeholder="Display Name" value={newTutor.displayName}
                  onChange={e => setNewTutor(p => ({ ...p, displayName: e.target.value }))}
                  className="px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
                <input type="text" placeholder="Timezone (e.g. Australia/Sydney)" value={newTutor.timezone}
                  onChange={e => setNewTutor(p => ({ ...p, timezone: e.target.value }))}
                  className="px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
                <input type="text" placeholder="Bio (optional)" value={newTutor.bio}
                  onChange={e => setNewTutor(p => ({ ...p, bio: e.target.value }))}
                  className="px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
              </div>
              <div className="flex gap-2 mt-3">
                <Button variant="primary" onClick={handleCreateTutor} loading={saving} disabled={!newTutor.expertUserId || !newTutor.displayName}>
                  {saving ? 'Creating...' : 'Create'}
                </Button>
                <Button variant="ghost" onClick={() => setShowCreateTutor(false)}>Cancel</Button>
              </div>
              </CardContent>
            </Card>
          )}

          <div className="space-y-3">
            {tutors.map(tutor => (
              <Card key={tutor.id}><CardContent className="p-4">
                <div className="flex items-center justify-between">
                  <div>
                    <div className="flex items-center gap-2">
                      <span className="font-medium text-admin-fg-strong">{tutor.displayName}</span>
                      <Badge variant={tutor.isActive ? 'success' : 'default'} intensity="tinted" size="sm">
                        {tutor.isActive ? 'Active' : 'Inactive'}
                      </Badge>
                    </div>
                    <div className="text-xs text-admin-fg-muted mt-0.5">
                      {tutor.timezone} · {tutor.totalSessions} sessions · Rating: {tutor.averageRating.toFixed(1)}
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    <Button size="sm" variant={tutor.isActive ? 'destructive' : 'primary'} onClick={() => handleToggleTutor(tutor.id, tutor.isActive)}>
                      {tutor.isActive ? 'Deactivate' : 'Activate'}
                    </Button>
                    <Button size="sm" variant="outline" onClick={() => handleLoadAvailability(tutor.id)}>
                      <Settings className="w-3.5 h-3.5 mr-1" /> Availability
                    </Button>
                  </div>
                </div>

                {/* Availability panel */}
                {selectedTutorId === tutor.id && (
                  <div className="mt-4 pt-4 border-t border-admin-border">
                    <h4 className="text-sm font-medium text-admin-fg-strong mb-3">Weekly Availability Rules</h4>
                    {availability.length === 0 && <p className="text-xs text-admin-fg-muted mb-3">No availability rules yet.</p>}
                    <div className="space-y-2 mb-3">
                      {availability.map(rule => (
                        <div key={rule.id} className="flex items-center justify-between bg-admin-bg-subtle rounded-admin-md px-3 py-2">
                          <span className="text-sm text-admin-fg-strong">
                            {DAY_NAMES[rule.dayOfWeek]} {rule.startTime} – {rule.endTime}
                          </span>
                          <button onClick={() => handleDeleteRule(rule.id)} className="text-[var(--admin-danger)] hover:opacity-80">
                            <Trash2 className="w-3.5 h-3.5" />
                          </button>
                        </div>
                      ))}
                    </div>
                    <div className="flex items-center gap-2">
                      <select value={newRule.dayOfWeek} onChange={e => setNewRule(r => ({ ...r, dayOfWeek: Number(e.target.value) }))}
                        className="px-2 py-1.5 border border-admin-border rounded text-xs bg-admin-bg-surface text-admin-fg-strong">
                        {DAY_NAMES.map((name, i) => <option key={i} value={i}>{name}</option>)}
                      </select>
                      <input type="time" value={newRule.startTime} onChange={e => setNewRule(r => ({ ...r, startTime: e.target.value }))}
                        className="px-2 py-1.5 border border-admin-border rounded text-xs bg-admin-bg-surface text-admin-fg-strong" />
                      <span className="text-xs text-admin-fg-muted">to</span>
                      <input type="time" value={newRule.endTime} onChange={e => setNewRule(r => ({ ...r, endTime: e.target.value }))}
                        className="px-2 py-1.5 border border-admin-border rounded text-xs bg-admin-bg-surface text-admin-fg-strong" />
                      <Button size="sm" variant="primary" onClick={handleAddRule}>
                        <Plus className="w-3.5 h-3.5" /> Add
                      </Button>
                    </div>
                  </div>
                )}
              </CardContent></Card>
            ))}
          </div>
        </>
      )}

      {/* ── Bookings Tab ─────────────────────────── */}
      {tab === 'bookings' && (
        <Card>
          <CardHeader><CardTitle>All bookings</CardTitle></CardHeader>
          <CardContent>
          {bookings.length === 0 ? (
            <p className="text-sm text-admin-fg-muted text-center py-8">No bookings found.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-admin-border text-left text-xs text-admin-fg-muted uppercase">
                    <th className="pb-2 pr-3">Booking</th>
                    <th className="pb-2 pr-3">Tutor</th>
                    <th className="pb-2 pr-3">Session</th>
                    <th className="pb-2 pr-3">Status</th>
                    <th className="pb-2 pr-3">Payment</th>
                    <th className="pb-2 pr-3">Entitlement</th>
                    <th className="pb-2 pr-3">Zoom</th>
                    <th className="pb-2 pr-3">Calendar</th>
                    <th className="pb-2">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {bookings.map(b => (
                    <tr key={b.id} className="border-b border-admin-border">
                      <td className="py-2 pr-3 font-mono text-xs">{b.id.slice(0, 12)}…</td>
                      <td className="py-2 pr-3">{b.tutorName ?? '—'}</td>
                      <td className="py-2 pr-3">{new Date(b.sessionStartUtc).toLocaleString('en-AU', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })}</td>
                      <td className="py-2 pr-3"><Badge variant="default" intensity="tinted" size="sm">{b.status}</Badge></td>
                      <td className="py-2 pr-3 text-xs">{b.paymentStatus}</td>
                      <td className="py-2 pr-3 text-xs">
                        {b.entitlementConsumed ? (b.entitlementRestoredAt ? 'Restored' : 'Consumed') : 'Legacy payment'}
                      </td>
                      <td className="py-2 pr-3 text-xs">{b.zoomStatus ?? '—'}</td>
                      <td className="py-2 pr-3 text-xs">{b.googleCalendarSyncStatus ?? '—'}</td>
                      <td className="py-2 flex items-center gap-1.5">
                        {(b.status === 'Confirmed' || b.status === 'ZoomCreated') && (
                          <>
                            <Button size="sm" variant="outline" onClick={async () => { await completeAdminPrivateSpeakingBooking(b.id); loadBookings(); }}>Complete</Button>
                            <Button size="sm" variant="destructive" onClick={async () => { await cancelAdminPrivateSpeakingBooking(b.id, 'Admin cancelled'); loadBookings(); }}>Cancel</Button>
                          </>
                        )}
                        {b.zoomStatus === 'Failed' && (
                          <Button size="sm" variant="ghost" onClick={async () => { await retryAdminPrivateSpeakingZoom(b.id); loadBookings(); }}>
                            <RefreshCw className="w-3 h-3" /> Retry Zoom
                          </Button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          </CardContent>
        </Card>
      )}

      {/* ── Audit Logs Tab ──────────────────────── */}
      {tab === 'audit' && (
        <Card>
          <CardHeader><CardTitle>Audit logs</CardTitle></CardHeader>
          <CardContent>
          {auditLogs.length === 0 ? (
            <p className="text-sm text-admin-fg-muted text-center py-8">No audit logs found.</p>
          ) : (
            <div className="space-y-2">
              {auditLogs.map(log => (
                <div key={log.id} className="bg-admin-bg-subtle rounded-admin-md px-4 py-2 text-sm">
                  <div className="flex items-center gap-2 text-xs text-admin-fg-muted">
                    <span>{new Date(log.createdAt).toLocaleString('en-AU')}</span>
                    <span className="font-mono">{log.actorRole}/{log.actorId.slice(0, 10)}</span>
                  </div>
                  <div className="text-admin-fg-strong mt-0.5">
                    <span className="font-medium">{log.action}</span>
                    {log.bookingId && <span className="text-xs ml-2 text-admin-fg-muted">Booking: {log.bookingId.slice(0, 12)}</span>}
                  </div>
                  {log.details && <p className="text-xs text-admin-fg-muted mt-0.5">{log.details}</p>}
                </div>
              ))}
            </div>
          )}
          </CardContent>
        </Card>
      )}
      </AdminPageShell>
    </AdminRouteWorkspace>
  );
}
