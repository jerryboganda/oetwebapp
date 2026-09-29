'use client';

import { useEffect, useState } from 'react';
import { CalendarDays, Plus, Trash2, ExternalLink } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { Input, Select } from '@/components/ui/form-controls';
import { fetchExamBookings, createExamBooking, deleteExamBooking } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type ExamBooking = {
  id: string; examTypeCode: string; examDate: string; status: string;
  testCenter: string | null; bookingReference: string | null; externalUrl: string | null;
  createdAt: string;
};

const EXAM_TYPE_OPTIONS = [
  { value: 'oet', label: 'OET' },
  { value: 'ielts', label: 'IELTS' },
  { value: 'pte', label: 'PTE' },
  { value: 'cambridge', label: 'Cambridge' },
  { value: 'toefl', label: 'TOEFL' },
];

const STATUS_COLORS: Record<string, string> = {
  planned: 'bg-info/10 text-info',
  confirmed: 'bg-success/10 text-success',
  completed: 'bg-background-light text-muted',
  cancelled: 'bg-danger/10 text-danger',
};

function daysUntil(dateStr: string) {
  const date = new Date(dateStr);
  const today = new Date();
  const diff = Math.ceil((date.getTime() - today.getTime()) / (1000 * 60 * 60 * 24));
  return diff;
}

export default function ExamBookingPage() {
  const [bookings, setBookings] = useState<ExamBooking[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [creating, setCreating] = useState(false);
  const [deleting, setDeleting] = useState<string | null>(null);
  const [form, setForm] = useState({
    examTypeCode: 'oet',
    examDate: '',
    testCenter: '',
    bookingReference: '',
    externalUrl: '',
  });

  function loadBookings() {
    setLoading(true);
    setLoadFailed(false);
    fetchExamBookings().then(data => {
      setBookings(data as ExamBooking[]);
      setLoading(false);
    }).catch(() => {
      setLoadFailed(true);
      setLoading(false);
    });
  }

  useEffect(() => {
    analytics.track('exam_booking_page_viewed');
    loadBookings();
  }, []);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    if (!form.examDate || creating) return;
    setCreating(true);
    try {
      await createExamBooking({ examTypeCode: form.examTypeCode, examDate: form.examDate, bookingReference: form.bookingReference || undefined, externalUrl: form.externalUrl || undefined, testCenter: form.testCenter || undefined });
      const data = await fetchExamBookings() as ExamBooking[];
      setBookings(data);
      setShowCreate(false);
      setForm({ examTypeCode: 'oet', examDate: '', testCenter: '', bookingReference: '', externalUrl: '' });
    } catch {
      setError('Could not save booking.');
    } finally {
      setCreating(false);
    }
  }

  async function handleDelete(bookingId: string) {
    if (deleting) return;
    if (!window.confirm('Remove this exam booking?')) return;
    setDeleting(bookingId);
    try {
      await deleteExamBooking(bookingId);
      setBookings(prev => prev.filter(b => b.id !== bookingId));
    } catch {
      setError('Could not delete booking.');
    } finally {
      setDeleting(null);
    }
  }

  const upcoming = bookings.filter(b => b.status !== 'completed' && b.status !== 'cancelled');
  const past = bookings.filter(b => b.status === 'completed' || b.status === 'cancelled');

  return (
    <>
      <div className="mb-6">
        <LearnerPageHero
          title="Exam Bookings"
          description="Track your upcoming English exam dates"
          icon={CalendarDays}
          aside={
            <Button onClick={() => setShowCreate(true)}>
              <Plus className="w-4 h-4" aria-hidden="true" /> Add Booking
            </Button>
          }
        />
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* Create form */}
      {showCreate && (
        <MotionSection className="mb-6 rounded-2xl border border-primary/30 bg-surface p-5 shadow-sm">
          <h2 className="mb-4 font-semibold text-navy">Add Exam Booking</h2>
          <form onSubmit={handleCreate} className="space-y-3">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <Select id="exam-booking-type" label="Exam" options={EXAM_TYPE_OPTIONS} value={form.examTypeCode} onChange={e => setForm(p => ({ ...p, examTypeCode: e.target.value }))} />
              <Input id="exam-booking-date" label="Exam date" type="date" value={form.examDate} onChange={e => setForm(p => ({ ...p, examDate: e.target.value }))} required />
            </div>
            <Input id="exam-booking-center" label="Test center" type="text" placeholder="Test center (optional)" value={form.testCenter} onChange={e => setForm(p => ({ ...p, testCenter: e.target.value }))} />
            <Input id="exam-booking-reference" label="Booking reference" type="text" placeholder="Booking reference (optional)" value={form.bookingReference} onChange={e => setForm(p => ({ ...p, bookingReference: e.target.value }))} />
            <Input id="exam-booking-url" label="External booking URL" type="url" placeholder="External booking URL (optional)" value={form.externalUrl} onChange={e => setForm(p => ({ ...p, externalUrl: e.target.value }))} />
            <div className="flex flex-wrap gap-2 pt-1">
              <Button type="submit" loading={creating}>
                {creating ? 'Saving...' : 'Save Booking'}
              </Button>
              <Button type="button" variant="outline" onClick={() => setShowCreate(false)}>Cancel</Button>
            </div>
          </form>
        </MotionSection>
      )}

      {loading ? (
        <div className="space-y-3">{Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div>
      ) : loadFailed ? (
        <ErrorState title="Could not load bookings" message="Check your connection and try again." onRetry={loadBookings} />
      ) : bookings.length === 0 ? (
        <EmptyState
          icon={<CalendarDays className="h-7 w-7" aria-hidden="true" />}
          title="No exam bookings yet"
          description="Add your upcoming exam date to count down!"
          action={{ label: 'Add Booking', onClick: () => setShowCreate(true) }}
        />
      ) : (
        <>
          {upcoming.length > 0 && (
            <>
              <LearnerSurfaceSectionHeader title="Upcoming" />
              <div className="space-y-3 mb-8">
                {upcoming.map((booking, i) => {
                  const days = daysUntil(booking.examDate);
                  return (
                    <MotionItem key={booking.id} delayIndex={i}
                      className="flex items-center gap-3 rounded-2xl border border-border bg-surface p-4 shadow-sm sm:gap-4"
                    >
                      <div className={`flex-shrink-0 w-14 h-14 rounded-xl flex flex-col items-center justify-center text-white text-xs font-bold ${days <= 7 ? 'bg-danger' : days <= 30 ? 'bg-warning' : 'bg-primary dark:bg-primary-700'}`}>
                        <div className="text-2xl font-bold leading-none">{days > 0 ? days : 'N/A'}</div>
                        <div>days</div>
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="mb-0.5 flex flex-wrap items-center gap-2">
                          <span className="font-semibold text-navy">{booking.examTypeCode.toUpperCase()}</span>
                          <span className={`text-xs px-2 py-0.5 rounded-full capitalize ${STATUS_COLORS[booking.status] ?? 'bg-background-light text-muted'}`}>{booking.status}</span>
                        </div>
                        <div className="text-sm text-muted">{new Date(booking.examDate).toLocaleDateString('en-AU', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })}</div>
                        {booking.testCenter && <div className="text-xs text-muted/60">{booking.testCenter}</div>}
                        {booking.bookingReference && <div className="text-xs text-muted/60">Ref: {booking.bookingReference}</div>}
                      </div>
                      <div className="flex shrink-0 items-center">
                        {booking.externalUrl && (
                          <a href={booking.externalUrl} target="_blank" rel="noopener noreferrer" aria-label={`Open ${booking.examTypeCode.toUpperCase()} booking page`} className="inline-flex h-11 w-11 items-center justify-center rounded-lg text-muted transition-colors hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary">
                            <ExternalLink className="w-4 h-4" aria-hidden="true" />
                          </a>
                        )}
                        <button type="button" onClick={() => handleDelete(booking.id)} disabled={deleting === booking.id} aria-label={`Remove ${booking.examTypeCode.toUpperCase()} booking`} className="inline-flex h-11 w-11 items-center justify-center rounded-lg text-muted transition-colors hover:text-danger focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-40">
                          <Trash2 className="w-4 h-4" aria-hidden="true" />
                        </button>
                      </div>
                    </MotionItem>
                  );
                })}
              </div>
            </>
          )}

          {past.length > 0 && (
            <>
              <LearnerSurfaceSectionHeader title="Past" />
              <div className="space-y-2">
                {past.map((booking, _i) => (
                  <div key={booking.id} className="bg-background-light rounded-xl border border-border px-4 py-3 flex items-center gap-3 opacity-70">
                    <span className="font-medium text-navy text-sm">{booking.examTypeCode.toUpperCase()}</span>
                    <span className="text-sm text-muted">{booking.examDate}</span>
                    <span className={`text-xs px-2 py-0.5 rounded-full capitalize ml-auto ${STATUS_COLORS[booking.status] ?? 'bg-background-light text-muted'}`}>{booking.status}</span>
                  </div>
                ))}
              </div>
            </>
          )}
        </>
      )}
    </>
  );
}
