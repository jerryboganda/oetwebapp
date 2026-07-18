'use client';

import { useEffect, useState } from 'react';
import { Users, Calendar, BarChart3, Settings, Plus, Trash2, RefreshCw, X, CheckCircle2, CreditCard, ListChecks, Video, Pencil, CalendarClock, Banknote, UserX, Download } from 'lucide-react';
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
  adminUpdatePrivateSpeakingAvailabilityRule,
  fetchAdminPrivateSpeakingBookings,
  cancelAdminPrivateSpeakingBooking,
  completeAdminPrivateSpeakingBooking,
  retryAdminPrivateSpeakingZoom,
  adminOverridePrivateSpeakingRefund,
  adminManualReschedulePrivateSpeaking,
  adminEditPrivateSpeakingBooking,
  adminMarkPrivateSpeakingNoShow,
  downloadAdminPrivateSpeakingBookingsCsv,
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
  reminderOffsetsMinutesJson: string; rescheduleFreeWindowHours: number;
  rescheduleSameDayPenaltyPercent: number;
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
  professionTrack?: string | null;
  paymentStatus: string; zoomStatus: string; createdAt: string;
  refundIssued?: boolean; refundAmountMinorUnits?: number | null;
  penaltyAmountMinorUnits?: number | null;
  entitlementConsumed?: boolean; entitlementRestoredAt?: string | null;
  googleCalendarSyncStatus?: string | null;
};

type AuditLog = {
  id: string; bookingId: string | null; actorId: string; actorRole: string;
  action: string; details: string | null; createdAt: string;
};

const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

// Matches PrivateSpeakingBookingStatus on the backend.
const BOOKING_STATUS_FILTERS = [
  'Reserved', 'PendingPayment', 'Confirmed', 'ZoomPending', 'ZoomCreated',
  'InProgress', 'Completed', 'Cancelled', 'Refunded', 'NoShow', 'Expired', 'Failed',
] as const;

type AdminTab = 'overview' | 'config' | 'tutors' | 'bookings' | 'audit';

function formatPrice(minorUnits: number, currency = 'aud') {
  return new Intl.NumberFormat('en-AU', { style: 'currency', currency }).format(minorUnits / 100);
}

// Convert a UTC ISO timestamp to the value expected by <input type="datetime-local"> (local time, no zone).
function isoToLocalInput(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
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
  const [success, setSuccess] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  // Create tutor form
  const [showCreateTutor, setShowCreateTutor] = useState(false);
  const [newTutor, setNewTutor] = useState({ expertUserId: '', displayName: '', timezone: 'Australia/Sydney', bio: '' });

  // Selected tutor for availability management
  const [selectedTutorId, setSelectedTutorId] = useState<string | null>(null);
  const [availability, setAvailability] = useState<AvailabilityRule[]>([]);
  const [newRule, setNewRule] = useState({ dayOfWeek: 1, startTime: '09:00', endTime: '17:00' });
  const [editingRule, setEditingRule] = useState<AvailabilityRule | null>(null);

  // Bookings filters + export
  const [bookingStatusFilter, setBookingStatusFilter] = useState<string>('');
  const [exporting, setExporting] = useState(false);

  // Booking action modals (one booking selected at a time)
  type BookingModalKind = 'refund' | 'reschedule' | 'edit' | null;
  const [bookingModal, setBookingModal] = useState<BookingModalKind>(null);
  const [activeBooking, setActiveBooking] = useState<AdminBooking | null>(null);
  const [refundForm, setRefundForm] = useState<{ amount: string; reason: string }>({ amount: '', reason: '' });
  const [rescheduleForm, setRescheduleForm] = useState<{ newStart: string; reason: string }>({ newStart: '', reason: '' });
  const [editForm, setEditForm] = useState<{ sessionStart: string; durationMinutes: string; professionTrack: string; tutorNotes: string }>({ sessionStart: '', durationMinutes: '', professionTrack: '', tutorNotes: '' });

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

  async function handleSaveRuleEdit() {
    if (!selectedTutorId || !editingRule) return;
    setSaving(true);
    try {
      await adminUpdatePrivateSpeakingAvailabilityRule(selectedTutorId, editingRule.id, {
        dayOfWeek: editingRule.dayOfWeek,
        startTime: editingRule.startTime,
        endTime: editingRule.endTime,
        effectiveFrom: editingRule.effectiveFrom,
        effectiveTo: editingRule.effectiveTo,
        isActive: editingRule.isActive,
      });
      const rules = await fetchAdminPrivateSpeakingAvailability(selectedTutorId) as AvailabilityRule[];
      setAvailability(rules);
      setEditingRule(null);
      setSuccess('Availability rule updated.');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to update availability rule.');
    } finally {
      setSaving(false);
    }
  }

  async function loadBookings() {
    const data = await fetchAdminPrivateSpeakingBookings(
      bookingStatusFilter ? { status: bookingStatusFilter } : undefined,
    ) as { items: AdminBooking[] };
    setBookings(data.items);
  }

  async function handleExportBookings() {
    setExporting(true);
    setError(null);
    try {
      const blob = await downloadAdminPrivateSpeakingBookingsCsv(
        bookingStatusFilter ? { status: bookingStatusFilter } : undefined,
      );
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'speaking-bookings.csv';
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to export bookings.');
    } finally {
      setExporting(false);
    }
  }

  function openRefundModal(b: AdminBooking) {
    setActiveBooking(b);
    setRefundForm({ amount: '', reason: '' });
    setBookingModal('refund');
  }

  function openRescheduleModal(b: AdminBooking) {
    setActiveBooking(b);
    setRescheduleForm({ newStart: isoToLocalInput(b.sessionStartUtc), reason: '' });
    setBookingModal('reschedule');
  }

  function openEditModal(b: AdminBooking) {
    setActiveBooking(b);
    setEditForm({
      sessionStart: isoToLocalInput(b.sessionStartUtc),
      durationMinutes: String(b.durationMinutes ?? ''),
      professionTrack: b.professionTrack ?? '',
      tutorNotes: '',
    });
    setBookingModal('edit');
  }

  function closeBookingModal() {
    setBookingModal(null);
    setActiveBooking(null);
  }

  async function handleSubmitRefund() {
    if (!activeBooking) return;
    setSaving(true);
    setError(null);
    try {
      const trimmedAmount = refundForm.amount.trim();
      await adminOverridePrivateSpeakingRefund(activeBooking.id, {
        amountMinorUnits: trimmedAmount === '' ? null : Number(trimmedAmount),
        reason: refundForm.reason.trim() || null,
      });
      closeBookingModal();
      await loadBookings();
      setSuccess('Refund override applied.');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to override refund.');
    } finally {
      setSaving(false);
    }
  }

  async function handleSubmitReschedule() {
    if (!activeBooking || !rescheduleForm.newStart) return;
    setSaving(true);
    setError(null);
    try {
      await adminManualReschedulePrivateSpeaking(activeBooking.id, {
        newSessionStartUtc: new Date(rescheduleForm.newStart).toISOString(),
        reason: rescheduleForm.reason.trim() || null,
      });
      closeBookingModal();
      await loadBookings();
      setSuccess('Session rescheduled.');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to reschedule session.');
    } finally {
      setSaving(false);
    }
  }

  async function handleSubmitEdit() {
    if (!activeBooking) return;
    setSaving(true);
    setError(null);
    try {
      const duration = editForm.durationMinutes.trim();
      await adminEditPrivateSpeakingBooking(activeBooking.id, {
        sessionStartUtc: editForm.sessionStart ? new Date(editForm.sessionStart).toISOString() : null,
        durationMinutes: duration === '' ? null : Number(duration),
        professionTrack: editForm.professionTrack.trim() || null,
        tutorNotes: editForm.tutorNotes.trim() || null,
      });
      closeBookingModal();
      await loadBookings();
      setSuccess('Booking updated.');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to update booking.');
    } finally {
      setSaving(false);
    }
  }

  async function handleMarkNoShow(b: AdminBooking) {
    if (!window.confirm('Mark this booking as a no-show? No refund will be issued and a penalty may apply.')) return;
    setError(null);
    try {
      await adminMarkPrivateSpeakingNoShow(b.id);
      await loadBookings();
      setSuccess('Booking marked as no-show.');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to mark no-show.');
    }
  }

  async function loadAuditLogs() {
    const data = await fetchAdminPrivateSpeakingAuditLogs() as AuditLog[];
    setAuditLogs(data);
  }

  useEffect(() => {
    if (tab === 'bookings') loadBookings();
    if (tab === 'audit') loadAuditLogs();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab, bookingStatusFilter]);

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
      {success && <InlineAlert variant="success">{success}<button onClick={() => setSuccess(null)} className="ml-2"><X className="w-4 h-4 inline" /></button></InlineAlert>}

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
              <div>
                <label className="text-xs text-admin-fg-muted mb-1 block">Reminder Offsets (minutes JSON)</label>
                <input type="text" value={config.reminderOffsetsMinutesJson}
                  placeholder="[1440, 60, 15]"
                  onChange={e => setConfig(c => c ? { ...c, reminderOffsetsMinutesJson: e.target.value } : c)}
                  className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
              </div>
              <div>
                <label className="text-xs text-admin-fg-muted mb-1 block">Reschedule Free Window (hours)</label>
                <input type="number" value={config.rescheduleFreeWindowHours}
                  onChange={e => setConfig(c => c ? { ...c, rescheduleFreeWindowHours: Number(e.target.value) } : c)}
                  className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
              </div>
              <div>
                <label className="text-xs text-admin-fg-muted mb-1 block">Reschedule Same-Day Penalty (%)</label>
                <input type="number" value={config.rescheduleSameDayPenaltyPercent}
                  onChange={e => setConfig(c => c ? { ...c, rescheduleSameDayPenaltyPercent: Number(e.target.value) } : c)}
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

          {/* Cross-tutor overview — read-only summary. Per-rule day grids load on demand
              via each tutor's Availability panel below. */}
          {tutors.length > 0 && (
            <Card className="mb-4">
              <CardHeader><CardTitle>Tutor availability overview</CardTitle></CardHeader>
              <CardContent>
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b border-admin-border text-left text-xs text-admin-fg-muted uppercase">
                        <th scope="col" className="pb-2 pr-3">Tutor</th>
                        <th scope="col" className="pb-2 pr-3">Status</th>
                        <th scope="col" className="pb-2 pr-3">Timezone</th>
                        <th scope="col" className="pb-2 pr-3">Sessions</th>
                        <th scope="col" className="pb-2">Rating</th>
                      </tr>
                    </thead>
                    <tbody>
                      {tutors.map(t => (
                        <tr key={t.id} className="border-b border-admin-border">
                          <td className="py-2 pr-3 text-admin-fg-strong">{t.displayName}</td>
                          <td className="py-2 pr-3">
                            <Badge variant={t.isActive ? 'success' : 'default'} intensity="tinted" size="sm">
                              {t.isActive ? 'Active' : 'Inactive'}
                            </Badge>
                          </td>
                          <td className="py-2 pr-3 text-xs text-admin-fg-muted">{t.timezone}</td>
                          <td className="py-2 pr-3 text-xs">{t.totalSessions}</td>
                          <td className="py-2 text-xs">{t.averageRating.toFixed(1)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
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
                        editingRule?.id === rule.id ? (
                          <div key={rule.id} className="flex flex-wrap items-center gap-2 bg-admin-bg-subtle rounded-admin-md px-3 py-2">
                            <select value={editingRule.dayOfWeek} onChange={e => setEditingRule(r => r ? { ...r, dayOfWeek: Number(e.target.value) } : r)}
                              className="px-2 py-1.5 border border-admin-border rounded text-xs bg-admin-bg-surface text-admin-fg-strong">
                              {DAY_NAMES.map((name, i) => <option key={i} value={i}>{name}</option>)}
                            </select>
                            <input type="time" value={editingRule.startTime} onChange={e => setEditingRule(r => r ? { ...r, startTime: e.target.value } : r)}
                              className="px-2 py-1.5 border border-admin-border rounded text-xs bg-admin-bg-surface text-admin-fg-strong" />
                            <span className="text-xs text-admin-fg-muted">to</span>
                            <input type="time" value={editingRule.endTime} onChange={e => setEditingRule(r => r ? { ...r, endTime: e.target.value } : r)}
                              className="px-2 py-1.5 border border-admin-border rounded text-xs bg-admin-bg-surface text-admin-fg-strong" />
                            <label className="flex items-center gap-1.5 text-xs text-admin-fg-strong">
                              <input type="checkbox" checked={editingRule.isActive} onChange={e => setEditingRule(r => r ? { ...r, isActive: e.target.checked } : r)}
                                className="w-3.5 h-3.5 rounded" />
                              Active
                            </label>
                            <Button size="sm" variant="primary" onClick={handleSaveRuleEdit} loading={saving}>Save</Button>
                            <Button size="sm" variant="ghost" onClick={() => setEditingRule(null)}>Cancel</Button>
                          </div>
                        ) : (
                          <div key={rule.id} className="flex items-center justify-between bg-admin-bg-subtle rounded-admin-md px-3 py-2">
                            <span className="text-sm text-admin-fg-strong">
                              {DAY_NAMES[rule.dayOfWeek]} {rule.startTime} – {rule.endTime}
                              {!rule.isActive && <Badge variant="default" intensity="tinted" size="sm" className="ml-2">Inactive</Badge>}
                            </span>
                            <div className="flex items-center gap-2">
                              <button onClick={() => setEditingRule(rule)} className="text-admin-fg-muted hover:text-admin-fg-strong" aria-label="Edit rule">
                                <Pencil className="w-3.5 h-3.5" />
                              </button>
                              <button onClick={() => handleDeleteRule(rule.id)} className="text-[var(--admin-danger)] hover:opacity-80" aria-label="Delete rule">
                                <Trash2 className="w-3.5 h-3.5" />
                              </button>
                            </div>
                          </div>
                        )
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
          <CardHeader>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <CardTitle>All bookings</CardTitle>
              <div className="flex items-center gap-2">
                <select value={bookingStatusFilter} onChange={e => setBookingStatusFilter(e.target.value)}
                  aria-label="Filter by status"
                  className="px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong">
                  <option value="">All statuses</option>
                  {BOOKING_STATUS_FILTERS.map(s => (
                    <option key={s} value={s}>{s === 'NoShow' ? 'No-show' : s}</option>
                  ))}
                </select>
                <Button size="sm" variant="outline" onClick={handleExportBookings} loading={exporting}>
                  <Download className="w-3.5 h-3.5 mr-1" /> Export CSV
                </Button>
              </div>
            </div>
          </CardHeader>
          <CardContent>
          {bookings.length === 0 ? (
            <p className="text-sm text-admin-fg-muted text-center py-8">No bookings found.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-admin-border text-left text-xs text-admin-fg-muted uppercase">
                    <th scope="col" className="pb-2 pr-3">Booking</th>
                    <th scope="col" className="pb-2 pr-3">Tutor</th>
                    <th scope="col" className="pb-2 pr-3">Session</th>
                    <th scope="col" className="pb-2 pr-3">Track</th>
                    <th scope="col" className="pb-2 pr-3">Status</th>
                    <th scope="col" className="pb-2 pr-3">Payment</th>
                    <th scope="col" className="pb-2 pr-3">Refund</th>
                    <th scope="col" className="pb-2 pr-3">Penalty</th>
                    <th scope="col" className="pb-2 pr-3">Entitlement</th>
                    <th scope="col" className="pb-2 pr-3">Zoom</th>
                    <th scope="col" className="pb-2 pr-3">Calendar</th>
                    <th scope="col" className="pb-2">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {bookings.map(b => (
                    <tr key={b.id} className="border-b border-admin-border">
                      <td className="py-2 pr-3 font-mono text-xs">{b.id.slice(0, 12)}…</td>
                      <td className="py-2 pr-3">{b.tutorName ?? '-'}</td>
                      <td className="py-2 pr-3">{new Date(b.sessionStartUtc).toLocaleString('en-AU', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })}</td>
                      <td className="py-2 pr-3 text-xs">
                        {b.professionTrack
                          ? <Badge variant="default" intensity="tinted" size="sm">{b.professionTrack}</Badge>
                          : <span className="text-admin-fg-muted">-</span>}
                      </td>
                      <td className="py-2 pr-3">
                        <Badge variant={b.status === 'NoShow' ? 'warning' : 'default'} intensity="tinted" size="sm">
                          {b.status === 'NoShow' ? 'No-show' : b.status}
                        </Badge>
                      </td>
                      <td className="py-2 pr-3 text-xs">{b.paymentStatus}</td>
                      <td className="py-2 pr-3 text-xs">
                        {b.refundIssued
                          ? <Badge variant="success" intensity="tinted" size="sm">{b.refundAmountMinorUnits != null ? formatPrice(b.refundAmountMinorUnits, b.currency) : 'Issued'}</Badge>
                          : <span className="text-admin-fg-muted">-</span>}
                      </td>
                      <td className="py-2 pr-3 text-xs">
                        {b.penaltyAmountMinorUnits != null && b.penaltyAmountMinorUnits > 0
                          ? <Badge variant="warning" intensity="tinted" size="sm">{formatPrice(b.penaltyAmountMinorUnits, b.currency)}</Badge>
                          : <span className="text-admin-fg-muted">-</span>}
                      </td>
                      <td className="py-2 pr-3 text-xs">
                        {b.entitlementConsumed ? (b.entitlementRestoredAt ? 'Restored' : 'Consumed') : 'Legacy payment'}
                      </td>
                      <td className="py-2 pr-3 text-xs">{b.zoomStatus ?? '-'}</td>
                      <td className="py-2 pr-3 text-xs">{b.googleCalendarSyncStatus ?? '-'}</td>
                      <td className="py-2">
                        <div className="flex flex-wrap items-center gap-1.5">
                          {(b.status === 'Confirmed' || b.status === 'ZoomCreated') && (
                            <>
                              <Button size="sm" variant="outline" onClick={async () => { await completeAdminPrivateSpeakingBooking(b.id); loadBookings(); }}>Complete</Button>
                              <Button size="sm" variant="destructive" onClick={async () => { await cancelAdminPrivateSpeakingBooking(b.id, 'Admin cancelled'); loadBookings(); }}>Cancel</Button>
                              <Button size="sm" variant="ghost" onClick={() => handleMarkNoShow(b)}>
                                <UserX className="w-3 h-3 mr-1" /> No-show
                              </Button>
                            </>
                          )}
                          {b.zoomStatus === 'Failed' && (
                            <Button size="sm" variant="ghost" onClick={async () => { await retryAdminPrivateSpeakingZoom(b.id); loadBookings(); }}>
                              <RefreshCw className="w-3 h-3 mr-1" /> Retry Zoom
                            </Button>
                          )}
                          <Button size="sm" variant="ghost" onClick={() => openEditModal(b)}>
                            <Pencil className="w-3 h-3 mr-1" /> Edit
                          </Button>
                          <Button size="sm" variant="ghost" onClick={() => openRescheduleModal(b)}>
                            <CalendarClock className="w-3 h-3 mr-1" /> Reschedule
                          </Button>
                          <Button size="sm" variant="ghost" onClick={() => openRefundModal(b)}>
                            <Banknote className="w-3 h-3 mr-1" /> Refund
                          </Button>
                        </div>
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

      {/* ── Booking action modals ─────────────────── */}
      {bookingModal && activeBooking && (
        <div
          className="overlay-safe-area fixed inset-0 z-50 flex items-center justify-center bg-navy/50 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          onClick={closeBookingModal}
        >
          <div
            className="max-h-[calc(100dvh-2rem-env(safe-area-inset-top)-env(safe-area-inset-bottom))] w-full max-w-lg overflow-y-auto rounded-admin-lg border border-admin-border bg-admin-bg-surface shadow-lg p-6"
            onClick={e => e.stopPropagation()}
          >
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-lg font-semibold text-admin-fg-strong">
                {bookingModal === 'refund' && 'Override refund'}
                {bookingModal === 'reschedule' && 'Manual reschedule'}
                {bookingModal === 'edit' && 'Edit booking'}
              </h3>
              <button onClick={closeBookingModal} className="text-admin-fg-muted hover:text-admin-fg-strong" aria-label="Close">
                <X className="w-4 h-4" />
              </button>
            </div>
            <p className="text-xs text-admin-fg-muted mb-4 font-mono">Booking {activeBooking.id.slice(0, 12)}…</p>

            {bookingModal === 'refund' && (
              <div className="space-y-3">
                <div>
                  <label className="text-xs text-admin-fg-muted mb-1 block">Amount (minor units, optional)</label>
                  <input type="number" value={refundForm.amount} placeholder="Leave blank for full refund"
                    onChange={e => setRefundForm(f => ({ ...f, amount: e.target.value }))}
                    className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
                </div>
                <div>
                  <label className="text-xs text-admin-fg-muted mb-1 block">Reason (optional)</label>
                  <textarea value={refundForm.reason} rows={3}
                    onChange={e => setRefundForm(f => ({ ...f, reason: e.target.value }))}
                    className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
                </div>
                <div className="flex justify-end gap-2 pt-2">
                  <Button variant="ghost" onClick={closeBookingModal}>Cancel</Button>
                  <Button variant="primary" onClick={handleSubmitRefund} loading={saving}>Apply refund</Button>
                </div>
              </div>
            )}

            {bookingModal === 'reschedule' && (
              <div className="space-y-3">
                <div>
                  <label className="text-xs text-admin-fg-muted mb-1 block">New session start</label>
                  <input type="datetime-local" value={rescheduleForm.newStart}
                    onChange={e => setRescheduleForm(f => ({ ...f, newStart: e.target.value }))}
                    className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
                </div>
                <div>
                  <label className="text-xs text-admin-fg-muted mb-1 block">Reason (optional)</label>
                  <textarea value={rescheduleForm.reason} rows={3}
                    onChange={e => setRescheduleForm(f => ({ ...f, reason: e.target.value }))}
                    className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
                </div>
                <div className="flex justify-end gap-2 pt-2">
                  <Button variant="ghost" onClick={closeBookingModal}>Cancel</Button>
                  <Button variant="primary" onClick={handleSubmitReschedule} loading={saving} disabled={!rescheduleForm.newStart}>Reschedule</Button>
                </div>
              </div>
            )}

            {bookingModal === 'edit' && (
              <div className="space-y-3">
                <div>
                  <label className="text-xs text-admin-fg-muted mb-1 block">Session start</label>
                  <input type="datetime-local" value={editForm.sessionStart}
                    onChange={e => setEditForm(f => ({ ...f, sessionStart: e.target.value }))}
                    className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
                </div>
                <div>
                  <label className="text-xs text-admin-fg-muted mb-1 block">Duration (minutes)</label>
                  <input type="number" value={editForm.durationMinutes}
                    onChange={e => setEditForm(f => ({ ...f, durationMinutes: e.target.value }))}
                    className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
                </div>
                <div>
                  <label className="text-xs text-admin-fg-muted mb-1 block">Profession track</label>
                  <input type="text" value={editForm.professionTrack}
                    onChange={e => setEditForm(f => ({ ...f, professionTrack: e.target.value }))}
                    className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
                </div>
                <div>
                  <label className="text-xs text-admin-fg-muted mb-1 block">Tutor notes</label>
                  <textarea value={editForm.tutorNotes} rows={3}
                    onChange={e => setEditForm(f => ({ ...f, tutorNotes: e.target.value }))}
                    className="w-full px-3 py-2 border border-admin-border rounded-admin-md text-sm bg-admin-bg-surface text-admin-fg-strong" />
                </div>
                <div className="flex justify-end gap-2 pt-2">
                  <Button variant="ghost" onClick={closeBookingModal}>Cancel</Button>
                  <Button variant="primary" onClick={handleSubmitEdit} loading={saving}>Save changes</Button>
                </div>
              </div>
            )}
          </div>
        </div>
      )}
      </AdminPageShell>
    </AdminRouteWorkspace>
  );
}
