'use client';

import { useEffect, useState } from 'react';
import {
  Users,
  Calendar,
  BarChart3,
  Settings,
  Plus,
  Trash2,
  RefreshCw,
  Mic,
  CheckCircle2,
  Clock,
  XCircle,
  DollarSign,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Switch } from '@/components/ui/switch';
import { Tabs } from '@/components/ui/tabs';
import { DataTable, type Column } from '@/components/ui/data-table';
import { InlineAlert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { StickyActionBar } from '@/components/ui/sticky-action-bar';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRoutePanelFooter,
  AdminRouteSummaryCard,
  AdminRouteStatRow,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
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
  totalBookings: number;
  confirmedBookings: number;
  completedBookings: number;
  cancelledBookings: number;
  failedPayments: number;
  zoomFailures: number;
  activeTutors: number;
  upcomingSessions: number;
  revenueMinorUnitsLast30Days: number;
};

type Config = {
  isEnabled: boolean;
  defaultPriceMinorUnits: number;
  currency: string;
  defaultSlotDurationMinutes: number;
  bufferMinutesBetweenSlots: number;
  minBookingLeadTimeHours: number;
  maxBookingAdvanceDays: number;
  cancellationWindowHours: number;
  rescheduleWindowHours: number;
  reservationTimeoutMinutes: number;
  reminderOffsetsHoursJson: string;
};

type TutorProfile = {
  id: string;
  expertUserId: string;
  displayName: string;
  bio: string | null;
  timezone: string;
  priceOverrideMinorUnits: number | null;
  slotDurationOverrideMinutes: number | null;
  specialtiesJson: string;
  isActive: boolean;
  averageRating: number;
  totalSessions: number;
};

type AvailabilityRule = {
  id: string;
  dayOfWeek: number;
  startTime: string;
  endTime: string;
  effectiveFrom: string | null;
  effectiveTo: string | null;
  isActive: boolean;
};

type AdminBooking = {
  id: string;
  tutorProfileId: string;
  tutorName: string | null;
  learnerUserId: string;
  status: string;
  sessionStartUtc: string;
  durationMinutes: number;
  priceMinorUnits: number;
  currency: string;
  paymentStatus: string;
  zoomStatus: string;
  createdAt: string;
};

type AuditLog = {
  id: string;
  bookingId: string | null;
  actorId: string;
  actorRole: string;
  action: string;
  details: string | null;
  createdAt: string;
};

const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const DAY_OPTIONS = DAY_NAMES.map((label, value) => ({ value: String(value), label }));

type AdminTab = 'overview' | 'config' | 'tutors' | 'bookings' | 'audit';

const TABS = [
  { id: 'overview', label: 'Overview' },
  { id: 'config', label: 'Configuration' },
  { id: 'tutors', label: 'Tutors' },
  { id: 'bookings', label: 'Bookings' },
  { id: 'audit', label: 'Audit' },
];

type Status = 'loading' | 'error' | 'success';

function formatPrice(minorUnits: number, currency = 'aud') {
  return new Intl.NumberFormat('en-AU', { style: 'currency', currency: currency.toUpperCase() }).format(minorUnits / 100);
}

function statusBadgeVariant(status: string): BadgeProps['variant'] {
  const lower = status.toLowerCase();
  if (lower === 'completed') return 'success';
  if (lower === 'confirmed' || lower === 'zoomcreated') return 'info';
  if (lower === 'cancelled' || lower === 'failed') return 'danger';
  if (lower === 'reserved' || lower === 'pending') return 'warning';
  return 'muted';
}

export default function AdminPrivateSpeakingPage() {
  const [tab, setTab] = useState<AdminTab>('overview');
  const [stats, setStats] = useState<Stats | null>(null);
  const [config, setConfig] = useState<Config | null>(null);
  const [tutors, setTutors] = useState<TutorProfile[]>([]);
  const [bookings, setBookings] = useState<AdminBooking[]>([]);
  const [auditLogs, setAuditLogs] = useState<AuditLog[]>([]);
  const [status, setStatus] = useState<Status>('loading');
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [configDirty, setConfigDirty] = useState(false);

  const [showCreateTutor, setShowCreateTutor] = useState(false);
  const [newTutor, setNewTutor] = useState({ expertUserId: '', displayName: '', timezone: 'Australia/Sydney', bio: '' });

  const [selectedTutorId, setSelectedTutorId] = useState<string | null>(null);
  const [availability, setAvailability] = useState<AvailabilityRule[]>([]);
  const [newRule, setNewRule] = useState({ dayOfWeek: '1', startTime: '09:00', endTime: '17:00' });

  useEffect(() => {
    loadOverview();
  }, []);

  async function loadOverview() {
    setStatus('loading');
    try {
      const [s, c, t] = await Promise.all([
        fetchAdminPrivateSpeakingStats(),
        fetchAdminPrivateSpeakingConfig(),
        fetchAdminPrivateSpeakingTutors(),
      ]);
      setStats(s as Stats);
      setConfig(c as Config);
      setTutors(t as TutorProfile[]);
      setStatus('success');
    } catch {
      setError('Failed to load private speaking data.');
      setStatus('error');
    }
  }

  async function handleSaveConfig() {
    if (!config) return;
    setSaving(true);
    try {
      const updated = (await updateAdminPrivateSpeakingConfig(
        config as unknown as Record<string, unknown>,
      )) as Config;
      setConfig(updated);
      setError(null);
      setConfigDirty(false);
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
      const updated = (await fetchAdminPrivateSpeakingTutors()) as TutorProfile[];
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
    setTutors((prev) => prev.map((t) => (t.id === profileId ? { ...t, isActive: !isActive } : t)));
  }

  async function handleLoadAvailability(profileId: string) {
    setSelectedTutorId(profileId);
    const rules = (await fetchAdminPrivateSpeakingAvailability(profileId)) as AvailabilityRule[];
    setAvailability(rules);
  }

  async function handleAddRule() {
    if (!selectedTutorId) return;
    await createAdminPrivateSpeakingAvailabilityRule(selectedTutorId, {
      dayOfWeek: Number(newRule.dayOfWeek),
      startTime: newRule.startTime,
      endTime: newRule.endTime,
    });
    const rules = (await fetchAdminPrivateSpeakingAvailability(selectedTutorId)) as AvailabilityRule[];
    setAvailability(rules);
  }

  async function handleDeleteRule(ruleId: string) {
    if (!selectedTutorId) return;
    await deleteAdminPrivateSpeakingAvailabilityRule(selectedTutorId, ruleId);
    setAvailability((prev) => prev.filter((r) => r.id !== ruleId));
  }

  async function loadBookings() {
    const data = (await fetchAdminPrivateSpeakingBookings()) as { items: AdminBooking[] };
    setBookings(data.items);
  }

  async function loadAuditLogs() {
    const data = (await fetchAdminPrivateSpeakingAuditLogs()) as AuditLog[];
    setAuditLogs(data);
  }

  useEffect(() => {
    if (tab === 'bookings') void loadBookings();
    if (tab === 'audit') void loadAuditLogs();
  }, [tab]);

  const bookingColumns: Column<AdminBooking>[] = [
    {
      key: 'id',
      header: 'Booking',
      render: (row) => (
        <div className="min-w-0">
          <p className="font-mono text-xs text-navy">{row.id.slice(0, 12)}…</p>
          <p className="text-[10px] text-muted">{row.tutorName ?? '—'}</p>
        </div>
      ),
    },
    {
      key: 'session',
      header: 'Session',
      render: (row) =>
        new Date(row.sessionStartUtc).toLocaleString('en-AU', {
          month: 'short',
          day: 'numeric',
          hour: '2-digit',
          minute: '2-digit',
        }),
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => <Badge variant={statusBadgeVariant(row.status)}>{row.status}</Badge>,
    },
    { key: 'payment', header: 'Payment', render: (row) => row.paymentStatus, hideOnMobile: true },
    { key: 'zoom', header: 'Zoom', render: (row) => row.zoomStatus ?? '—', hideOnMobile: true },
    {
      key: 'actions',
      header: 'Actions',
      render: (row) => (
        <div className="flex items-center gap-1.5">
          {(row.status === 'Confirmed' || row.status === 'ZoomCreated') && (
            <>
              <Button
                variant="outline"
                size="sm"
                onClick={async () => {
                  await completeAdminPrivateSpeakingBooking(row.id);
                  await loadBookings();
                }}
              >
                Complete
              </Button>
              <Button
                variant="destructive"
                size="sm"
                onClick={async () => {
                  await cancelAdminPrivateSpeakingBooking(row.id, 'Admin cancelled');
                  await loadBookings();
                }}
              >
                Cancel
              </Button>
            </>
          )}
          {row.zoomStatus === 'Failed' && (
            <Button
              variant="outline"
              size="sm"
              onClick={async () => {
                await retryAdminPrivateSpeakingZoom(row.id);
                await loadBookings();
              }}
            >
              <RefreshCw className="h-3 w-3" /> Retry
            </Button>
          )}
        </div>
      ),
    },
  ];

  return (
    <AdminRouteWorkspace role="main" aria-label="Private speaking admin">
      <AdminRouteHero
        eyebrow="People & Billing"
        icon={Mic}
        accent="purple"
        title="Private Speaking Sessions"
        description="Manage tutors, availability, bookings, and session configuration across the paid 1:1 speaking program."
        highlights={
          stats
            ? [
                { icon: Users, label: 'Active tutors', value: String(stats.activeTutors) },
                { icon: Calendar, label: 'Upcoming', value: String(stats.upcomingSessions) },
                {
                  icon: DollarSign,
                  label: 'Revenue (30d)',
                  value: formatPrice(stats.revenueMinorUnitsLast30Days, config?.currency),
                },
              ]
            : undefined
        }
      />

      {error ? (
        <InlineAlert variant="warning" dismissible>
          {error}
        </InlineAlert>
      ) : null}

      <Tabs
        tabs={TABS}
        activeTab={tab}
        onChange={(id) => setTab(id as AdminTab)}
      />

      <AsyncStateWrapper status={status} onRetry={() => void loadOverview()}>
        {tab === 'overview' && stats ? (
          <>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <AdminRouteSummaryCard
                label="Active Tutors"
                value={stats.activeTutors}
                icon={<Users className="h-5 w-5" />}
              />
              <AdminRouteSummaryCard
                label="Upcoming"
                value={stats.upcomingSessions}
                icon={<Calendar className="h-5 w-5" />}
                tone="info"
              />
              <AdminRouteSummaryCard
                label="Completed"
                value={stats.completedBookings}
                icon={<CheckCircle2 className="h-5 w-5" />}
                tone="success"
              />
              <AdminRouteSummaryCard
                label="Cancelled"
                value={stats.cancelledBookings}
                icon={<XCircle className="h-5 w-5" />}
                tone={stats.cancelledBookings > 0 ? 'danger' : 'default'}
              />
            </div>

            <AdminRoutePanel
              eyebrow="Reliability"
              title="Session reliability signals"
              description="Aggregate booking, payment, and Zoom pipeline signals over the operational window."
            >
              <AdminRouteStatRow
                items={[
                  { label: 'Total bookings', value: stats.totalBookings.toLocaleString() },
                  { label: 'Confirmed', value: stats.confirmedBookings.toLocaleString() },
                  {
                    label: 'Payment failures',
                    value: stats.failedPayments.toLocaleString(),
                    tone: stats.failedPayments > 0 ? 'danger' : 'success',
                  },
                  {
                    label: 'Zoom failures',
                    value: stats.zoomFailures.toLocaleString(),
                    tone: stats.zoomFailures > 0 ? 'danger' : 'success',
                  },
                ]}
              />
              <AdminRoutePanelFooter source="Booking + Zoom pipelines" />
            </AdminRoutePanel>
          </>
        ) : null}

        {tab === 'config' && config ? (
          <AdminRoutePanel
            eyebrow="Settings"
            title="Module configuration"
            description="Enable the module and tune pricing, scheduling windows, and reservation policy."
          >
            <Switch
              checked={config.isEnabled}
              onCheckedChange={(checked) => {
                setConfig((c) => (c ? { ...c, isEnabled: checked } : c));
                setConfigDirty(true);
              }}
              label="Module enabled"
              description="When disabled, learners cannot book new sessions. Existing sessions remain valid."
            />

            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <Input
                label="Default price (minor units)"
                type="number"
                value={config.defaultPriceMinorUnits}
                onChange={(e) => {
                  setConfig((c) => (c ? { ...c, defaultPriceMinorUnits: Number(e.target.value) } : c));
                  setConfigDirty(true);
                }}
              />
              <Input
                label="Currency"
                value={config.currency}
                onChange={(e) => {
                  setConfig((c) => (c ? { ...c, currency: e.target.value } : c));
                  setConfigDirty(true);
                }}
              />
              <Input
                label="Slot duration (minutes)"
                type="number"
                value={config.defaultSlotDurationMinutes}
                onChange={(e) => {
                  setConfig((c) => (c ? { ...c, defaultSlotDurationMinutes: Number(e.target.value) } : c));
                  setConfigDirty(true);
                }}
              />
              <Input
                label="Buffer between slots (minutes)"
                type="number"
                value={config.bufferMinutesBetweenSlots}
                onChange={(e) => {
                  setConfig((c) => (c ? { ...c, bufferMinutesBetweenSlots: Number(e.target.value) } : c));
                  setConfigDirty(true);
                }}
              />
              <Input
                label="Min lead time (hours)"
                type="number"
                value={config.minBookingLeadTimeHours}
                onChange={(e) => {
                  setConfig((c) => (c ? { ...c, minBookingLeadTimeHours: Number(e.target.value) } : c));
                  setConfigDirty(true);
                }}
              />
              <Input
                label="Max advance (days)"
                type="number"
                value={config.maxBookingAdvanceDays}
                onChange={(e) => {
                  setConfig((c) => (c ? { ...c, maxBookingAdvanceDays: Number(e.target.value) } : c));
                  setConfigDirty(true);
                }}
              />
              <Input
                label="Cancellation window (hours)"
                type="number"
                value={config.cancellationWindowHours}
                onChange={(e) => {
                  setConfig((c) => (c ? { ...c, cancellationWindowHours: Number(e.target.value) } : c));
                  setConfigDirty(true);
                }}
              />
              <Input
                label="Reservation timeout (minutes)"
                type="number"
                value={config.reservationTimeoutMinutes}
                onChange={(e) => {
                  setConfig((c) => (c ? { ...c, reservationTimeoutMinutes: Number(e.target.value) } : c));
                  setConfigDirty(true);
                }}
              />
            </div>
          </AdminRoutePanel>
        ) : null}

        {tab === 'tutors' ? (
          <AdminRoutePanel
            eyebrow="Roster"
            title="Tutor profiles"
            description="Manage speaking tutors, activation state, and weekly availability."
            actions={
              <Button size="sm" onClick={() => setShowCreateTutor((v) => !v)}>
                <Plus className="h-4 w-4" /> Add tutor
              </Button>
            }
          >
            {showCreateTutor ? (
              <div className="rounded-2xl border border-border bg-background-light p-4">
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <Input
                    label="Expert user ID"
                    value={newTutor.expertUserId}
                    onChange={(e) => setNewTutor((p) => ({ ...p, expertUserId: e.target.value }))}
                  />
                  <Input
                    label="Display name"
                    value={newTutor.displayName}
                    onChange={(e) => setNewTutor((p) => ({ ...p, displayName: e.target.value }))}
                  />
                  <Input
                    label="Timezone"
                    value={newTutor.timezone}
                    onChange={(e) => setNewTutor((p) => ({ ...p, timezone: e.target.value }))}
                  />
                  <Textarea
                    label="Bio (optional)"
                    value={newTutor.bio}
                    onChange={(e) => setNewTutor((p) => ({ ...p, bio: e.target.value }))}
                  />
                </div>
                <div className="mt-3 flex gap-2">
                  <Button onClick={handleCreateTutor} disabled={saving || !newTutor.expertUserId || !newTutor.displayName} loading={saving}>
                    Create
                  </Button>
                  <Button variant="outline" onClick={() => setShowCreateTutor(false)}>
                    Cancel
                  </Button>
                </div>
              </div>
            ) : null}

            {tutors.length === 0 ? (
              <EmptyState
                icon={<Users className="h-6 w-6" aria-hidden />}
                title="No tutors"
                description="Add a tutor profile to start offering private speaking sessions."
              />
            ) : (
              <div className="space-y-3">
                {tutors.map((tutor) => (
                  <div key={tutor.id} className="rounded-2xl border border-border bg-surface p-4 shadow-sm">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div>
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="font-semibold text-navy">{tutor.displayName}</span>
                          <Badge variant={tutor.isActive ? 'success' : 'muted'}>
                            {tutor.isActive ? 'Active' : 'Inactive'}
                          </Badge>
                        </div>
                        <p className="mt-0.5 text-xs text-muted">
                          {tutor.timezone} · {tutor.totalSessions} sessions · Rating {tutor.averageRating.toFixed(1)}
                        </p>
                      </div>
                      <div className="flex flex-wrap items-center gap-2">
                        <Button
                          variant={tutor.isActive ? 'destructive' : 'primary'}
                          size="sm"
                          onClick={() => void handleToggleTutor(tutor.id, tutor.isActive)}
                        >
                          {tutor.isActive ? 'Deactivate' : 'Activate'}
                        </Button>
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() =>
                            selectedTutorId === tutor.id
                              ? setSelectedTutorId(null)
                              : void handleLoadAvailability(tutor.id)
                          }
                        >
                          <Settings className="h-3.5 w-3.5" /> Availability
                        </Button>
                      </div>
                    </div>

                    {selectedTutorId === tutor.id ? (
                      <div className="mt-4 border-t border-border pt-4">
                        <h4 className="mb-3 text-sm font-semibold text-navy">Weekly availability rules</h4>
                        {availability.length === 0 ? (
                          <p className="mb-3 text-xs text-muted">No availability rules yet.</p>
                        ) : (
                          <div className="mb-3 space-y-2">
                            {availability.map((rule) => (
                              <div
                                key={rule.id}
                                className="flex items-center justify-between rounded-lg bg-background-light px-3 py-2"
                              >
                                <span className="text-sm text-navy">
                                  {DAY_NAMES[rule.dayOfWeek]} · {rule.startTime} – {rule.endTime}
                                </span>
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  onClick={() => void handleDeleteRule(rule.id)}
                                  aria-label="Delete rule"
                                >
                                  <Trash2 className="h-3.5 w-3.5 text-danger" />
                                </Button>
                              </div>
                            ))}
                          </div>
                        )}
                        <div className="grid grid-cols-1 gap-2 sm:grid-cols-4 sm:items-end">
                          <Select
                            label="Day"
                            value={newRule.dayOfWeek}
                            onChange={(e) => setNewRule((r) => ({ ...r, dayOfWeek: e.target.value }))}
                            options={DAY_OPTIONS}
                          />
                          <Input
                            label="From"
                            type="time"
                            value={newRule.startTime}
                            onChange={(e) => setNewRule((r) => ({ ...r, startTime: e.target.value }))}
                          />
                          <Input
                            label="To"
                            type="time"
                            value={newRule.endTime}
                            onChange={(e) => setNewRule((r) => ({ ...r, endTime: e.target.value }))}
                          />
                          <Button onClick={() => void handleAddRule()}>
                            <Plus className="h-3.5 w-3.5" /> Add
                          </Button>
                        </div>
                      </div>
                    ) : null}
                  </div>
                ))}
              </div>
            )}
          </AdminRoutePanel>
        ) : null}

        {tab === 'bookings' ? (
          <AdminRoutePanel
            eyebrow="Sessions"
            title="All bookings"
            description="Live booking ledger with complete / cancel / retry Zoom actions."
          >
            {bookings.length === 0 ? (
              <EmptyState
                icon={<Calendar className="h-6 w-6" aria-hidden />}
                title="No bookings"
                description="Booked and recently-cancelled sessions will appear here."
              />
            ) : (
              <DataTable
                density="compact"
                data={bookings}
                columns={bookingColumns}
                keyExtractor={(row) => row.id}
                aria-label="Private speaking bookings"
              />
            )}
          </AdminRoutePanel>
        ) : null}

        {tab === 'audit' ? (
          <AdminRoutePanel
            eyebrow="Compliance"
            title="Audit logs"
            description="Every admin action on the private speaking module is recorded here."
          >
            {auditLogs.length === 0 ? (
              <EmptyState
                icon={<BarChart3 className="h-6 w-6" aria-hidden />}
                title="No audit events"
                description="Audit entries will appear as admins manage tutors, bookings, and config."
              />
            ) : (
              <div className="space-y-2">
                {auditLogs.map((log) => (
                  <div key={log.id} className="rounded-2xl border border-border bg-background-light px-4 py-3 text-sm">
                    <div className="flex flex-wrap items-center gap-2 text-xs text-muted">
                      <Clock className="h-3 w-3" aria-hidden />
                      <span>{new Date(log.createdAt).toLocaleString('en-AU')}</span>
                      <Badge variant="muted">
                        {log.actorRole}/{log.actorId.slice(0, 10)}
                      </Badge>
                    </div>
                    <div className="mt-1 text-navy">
                      <span className="font-semibold">{log.action}</span>
                      {log.bookingId ? (
                        <span className="ml-2 text-xs text-muted">Booking {log.bookingId.slice(0, 12)}</span>
                      ) : null}
                    </div>
                    {log.details ? <p className="mt-0.5 text-xs text-muted">{log.details}</p> : null}
                  </div>
                ))}
              </div>
            )}
            <AdminRoutePanelFooter source="Audit ledger" />
          </AdminRoutePanel>
        ) : null}
      </AsyncStateWrapper>

      {tab === 'config' && config ? (
        <StickyActionBar
          description={configDirty ? 'Unsaved changes' : 'Configuration synced'}
        >
          <Button onClick={handleSaveConfig} disabled={saving || !configDirty} loading={saving}>
            Save configuration
          </Button>
        </StickyActionBar>
      ) : null}
    </AdminRouteWorkspace>
  );
}
