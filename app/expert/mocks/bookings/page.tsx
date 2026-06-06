'use client';

import { useEffect, useMemo, useState } from 'react';
import { CalendarClock, ClipboardList, Flame, Gauge, ShieldCheck, Sparkles, Video } from 'lucide-react';
import {
  ExpertRouteHero,
  ExpertRouteSectionHeader,
  ExpertRouteSummaryCard,
  ExpertRouteWorkspace,
} from '@/components/domain/expert-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchExpertMockBookings } from '@/lib/api';
import type { MockBooking } from '@/lib/mock-data';
import { MockBatchHeatmap, type MockBatchHeatmapRow } from '@/components/expert/mock-batch-heatmap';
import { MockMarkingQueue, type MockMarkingQueueItem } from '@/components/expert/mock-marking-queue';

// TODO(api): Replace synthetic data below with live endpoints once the backend
// surface for the teacher batch mocks dashboard is available. Expected APIs:
//   - GET /v1/expert/mock-batches/:batchId/readiness  -> MockBatchHeatmapRow[]
//   - GET /v1/expert/mock-marking-queue              -> MockMarkingQueueItem[]
//   - GET /v1/expert/mock-marking-consistency        -> { sigma, biasBySubtest, calibrationDriftPct }
// Until then we render synthetic samples so reviewers can validate the layout
// and copy without blocking on backend work.

const MOCK_HEATMAP_ROWS: MockBatchHeatmapRow[] = [
  {
    learnerId: 'lrn-001',
    learnerName: 'Aisha Khan',
    cells: [
      { subtest: 'listening', rag: 'green', score: 410 },
      { subtest: 'reading', rag: 'amber', score: 350 },
      { subtest: 'writing', rag: 'green', score: 380 },
      { subtest: 'speaking', rag: 'red', score: 290 },
    ],
  },
  {
    learnerId: 'lrn-002',
    learnerName: 'Daniel Okafor',
    cells: [
      { subtest: 'listening', rag: 'green', score: 430 },
      { subtest: 'reading', rag: 'green', score: 400 },
      { subtest: 'writing', rag: 'amber', score: 340 },
      { subtest: 'speaking', rag: 'amber', score: 330 },
    ],
  },
  {
    learnerId: 'lrn-003',
    learnerName: 'Priya Menon',
    cells: [
      { subtest: 'listening', rag: 'amber', score: 320 },
      { subtest: 'reading', rag: 'red', score: 270 },
      { subtest: 'writing', rag: 'red', score: 250 },
      { subtest: 'speaking', rag: 'amber', score: 310 },
    ],
  },
  {
    learnerId: 'lrn-004',
    learnerName: 'Lucas Almeida',
    cells: [
      { subtest: 'listening', rag: 'green', score: 420 },
      { subtest: 'reading', rag: 'green', score: 390 },
      { subtest: 'writing', rag: 'green', score: 370 },
      { subtest: 'speaking', rag: 'green', score: 380 },
    ],
  },
  {
    learnerId: 'lrn-005',
    learnerName: 'Maya Petrov',
    cells: [
      { subtest: 'listening', rag: 'grey' },
      { subtest: 'reading', rag: 'grey' },
      { subtest: 'writing', rag: 'amber', score: 330 },
      { subtest: 'speaking', rag: 'grey' },
    ],
  },
];

const NOW = Date.now();
const MOCK_QUEUE_ITEMS: MockMarkingQueueItem[] = [
  {
    reviewRequestId: 'rev-mock-7a31',
    learnerName: 'Aisha Khan',
    subtest: 'writing',
    submittedAt: new Date(NOW - 4 * 60 * 60 * 1000).toISOString(),
    ageHours: 4,
    routeHref: '/expert/review/writing/rev-mock-7a31',
  },
  {
    reviewRequestId: 'rev-mock-8c14',
    learnerName: 'Priya Menon',
    subtest: 'speaking',
    submittedAt: new Date(NOW - 28 * 60 * 60 * 1000).toISOString(),
    ageHours: 28,
    routeHref: '/expert/review/speaking/rev-mock-8c14',
  },
  {
    reviewRequestId: 'rev-mock-9d22',
    learnerName: 'Daniel Okafor',
    subtest: 'writing',
    submittedAt: new Date(NOW - 19 * 60 * 60 * 1000).toISOString(),
    ageHours: 19,
    routeHref: '/expert/review/writing/rev-mock-9d22',
  },
  {
    reviewRequestId: 'rev-mock-a045',
    learnerName: 'Maya Petrov',
    subtest: 'writing',
    submittedAt: new Date(NOW - 2 * 60 * 60 * 1000).toISOString(),
    ageHours: 2,
    routeHref: '/expert/review/writing/rev-mock-a045',
  },
];

interface ConsistencySignal {
  label: string;
  delta: number;
  description: string;
}

// TODO(api): Source these signals from the marking-consistency endpoint once
// the analytics service computes per-expert variance against the calibration
// benchmark. The mock values reflect typical drift thresholds (<5% green,
// 5-10% amber, >10% red).
const MOCK_CONSISTENCY_SIGNALS: ConsistencySignal[] = [
  { label: 'Writing band variance', delta: 3.4, description: 'Spread vs. peer reviewers in the last 14 days.' },
  { label: 'Speaking holistic drift', delta: 7.8, description: 'Average shift from calibration benchmark scores.' },
  { label: 'Rubric criterion outliers', delta: 12.1, description: 'Criteria flagged >1.0 band away from consensus.' },
];

function consistencyVariant(delta: number): 'success' | 'warning' | 'danger' {
  if (delta < 5) return 'success';
  if (delta < 10) return 'warning';
  return 'danger';
}

export default function ExpertMockBookingsPage() {
  const [items, setItems] = useState<MockBooking[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    fetchExpertMockBookings()
      .then(setItems)
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load mock bookings.'))
      .finally(() => setLoading(false));
  }, []);

  const totalLearners = MOCK_HEATMAP_ROWS.length;
  const redCellCount = useMemo(
    () =>
      MOCK_HEATMAP_ROWS.reduce(
        (sum, row) => sum + row.cells.filter((cell) => String(cell.rag).toLowerCase() === 'red').length,
        0,
      ),
    [],
  );
  const pendingCount = MOCK_QUEUE_ITEMS.length;
  const overdueCount = MOCK_QUEUE_ITEMS.filter((item) => item.ageHours >= 24).length;

  return (
    <ExpertRouteWorkspace>
      <div className="space-y-8">
        {/* FE-004: the heatmap / queue / consistency figures below are hardcoded
            synthetic data (MOCK_*). Make that unmistakable instead of letting it
            read as live cohort data. */}
        <div role="status" className="rounded-2xl border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning">
          <strong className="font-semibold">Sample data — preview.</strong> These figures are synthetic
          placeholders while the teacher-batch mock endpoints are wired up; they are not live.
        </div>
        <ExpertRouteHero
          eyebrow="Mocks V2"
          title="Teacher batch mocks dashboard"
          description="Track readiness across the cohort, work the marking queue against SLA, and monitor your scoring consistency against calibration."
          icon={Sparkles}
          accent="primary"
          highlights={[
            { icon: ClipboardList, label: 'Pending marking', value: String(pendingCount) },
            { icon: Flame, label: 'Overdue (>24h)', value: String(overdueCount) },
            { icon: Gauge, label: 'Red cells in batch', value: String(redCellCount) },
          ]}
        />

        <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
          <ExpertRouteSummaryCard
            label="Learners in batch"
            value={totalLearners}
            hint="Synthetic preview. Wire to /v1/expert/mock-batches/:id/readiness."
            accent="navy"
            icon={ClipboardList}
          />
          <ExpertRouteSummaryCard
            label="Pending marking"
            value={pendingCount}
            hint="Items awaiting your expert action."
            accent={overdueCount > 0 ? 'amber' : 'emerald'}
            icon={Flame}
          />
          <ExpertRouteSummaryCard
            label="Consistency status"
            value={MOCK_CONSISTENCY_SIGNALS.some((s) => s.delta >= 10) ? 'Needs review' : 'Aligned'}
            hint="Variance vs. calibration benchmark."
            accent={MOCK_CONSISTENCY_SIGNALS.some((s) => s.delta >= 10) ? 'amber' : 'emerald'}
            icon={ShieldCheck}
          />
        </div>

        <InlineAlert variant="info" title="Preview data">
          The readiness heatmap, marking queue, and consistency widgets below use synthetic data. Replace each block with
          its live API once the teacher batch endpoints land. Search the file for
          {' '}<code className="rounded bg-background-light px-1 py-0.5 text-xs">TODO(api)</code> to find the hookup points.
        </InlineAlert>

        <section className="space-y-4">
          <ExpertRouteSectionHeader
            eyebrow="Batch readiness"
            title="Mock batch readiness heatmap"
            description="Each row is a learner in the current mock batch. Cells track sub-test readiness via a RAG signal. Hover for the latest score."
          />
          <MockBatchHeatmap rows={MOCK_HEATMAP_ROWS} />
        </section>

        <section className="space-y-4">
          <ExpertRouteSectionHeader
            eyebrow="Marking workload"
            title="Pending marking queue"
            description="Mock review requests waiting for expert marking. Age is color-coded against the 24h SLA."
          />
          <MockMarkingQueue items={MOCK_QUEUE_ITEMS} slaHours={24} />
        </section>

        <section className="space-y-4">
          <ExpertRouteSectionHeader
            eyebrow="Quality assurance"
            title="Marking consistency QA"
            description="How your marking is drifting against peer reviewers and calibration benchmarks. Red signals trigger a calibration nudge."
          />
          <Card>
            <CardContent className="grid grid-cols-1 gap-4 p-5 md:grid-cols-3">
              {MOCK_CONSISTENCY_SIGNALS.map((signal) => {
                const variant = consistencyVariant(signal.delta);
                return (
                  <div key={signal.label} className="rounded-2xl border border-border bg-background-light p-4">
                    <div className="flex items-center justify-between gap-2">
                      <h3 className="text-sm font-bold text-navy">{signal.label}</h3>
                      <Badge variant={variant}>{signal.delta.toFixed(1)}%</Badge>
                    </div>
                    <p className="mt-2 text-xs text-muted">{signal.description}</p>
                  </div>
                );
              })}
            </CardContent>
          </Card>
        </section>

        <section className="space-y-4">
          <ExpertRouteSectionHeader
            eyebrow="Live mocks"
            title="Scheduled mock sessions"
            description="Candidate cards are visible to learners. Interlocutor cards and live-room controls are tutor-only."
            action={<CalendarClock className="h-5 w-5 text-muted" aria-hidden="true" />}
          />

          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
          {loading ? <Skeleton className="h-48 rounded-2xl" /> : null}

          {!loading ? (
            <div className="grid gap-4">
              {items.map((booking) => (
                <article key={booking.id} className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div>
                      <div className="flex flex-wrap items-center gap-2">
                        <h2 className="font-black text-navy">{booking.title ?? booking.mockBundleId}</h2>
                        <Badge variant={booking.status === 'completed' ? 'success' : booking.status === 'cancelled' ? 'muted' : 'info'}>{booking.status}</Badge>
                        <Badge variant="outline">{booking.liveRoomState ?? 'waiting'}</Badge>
                      </div>
                      <p className="mt-2 text-sm text-muted">{new Date(booking.scheduledStartAt).toLocaleString()} ({booking.timezoneIana})</p>
                      <p className="mt-1 text-xs text-muted">Learner: candidate card only / Tutor: interlocutor card visible</p>
                    </div>
                    {booking.zoomJoinUrl || booking.joinUrl ? (
                      <Button variant="primary" onClick={() => window.location.assign(booking.zoomJoinUrl ?? booking.joinUrl ?? '#')}>
                        <Video className="mr-2 h-4 w-4" /> Open room
                      </Button>
                    ) : null}
                  </div>
                </article>
              ))}
              {items.length === 0 ? <InlineAlert variant="info">No scheduled mock sessions are assigned yet.</InlineAlert> : null}
            </div>
          ) : null}
        </section>
      </div>
    </ExpertRouteWorkspace>
  );
}
