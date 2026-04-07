'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { CalendarDays, Plus, Trash2, ExternalLink } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchExamBookings, createExamBooking, deleteExamBooking } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type ExamBooking = {
  id: string; examTypeCode: string; examDate: string; status: string;
  testCenter: string | null; bookingReference: string | null; externalUrl: string | null;
  createdAt: string;
};

const STATUS_COLORS: Record<string, string> = {
  planned: 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300',
  confirmed: 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300',
  completed: 'bg-gray-100 text-gray-600 dark:bg-gray-700 dark:text-gray-300',
  cancelled: 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300',
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

  useEffect(() => {
    analytics.track('exam_booking_page_viewed');
    fetchExamBookings().then(data => {
      setBookings(data as ExamBooking[]);
      setLoading(false);
    }).catch(() => {
      setError('Could not load bookings.');
      setLoading(false);
    });
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
    <LearnerDashboardShell>
      <div className="flex items-center justify-between mb-6">
        <LearnerPageHero
          title="Exam Bookings"
          description="Track your upcoming English exam dates"
          icon={CalendarDays}
        />
        <button
          onClick={() => setShowCreate(true)}
          className="flex items-center gap-2 px-4 py-2 bg-teal-600 hover:bg-teal-700 text-white rounded-xl text-sm font-medium transition-colors"
        >
          <Plus className="w-4 h-4" /> Add Booking
        </button>
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* Create form */}
      {showCreate && (
        <motion.div initial={{ opacity: 0, y: -10 }} animate={{ opacity: 1, y: 0 }} className="bg-white dark:bg-gray-800 rounded-xl border border-teal-200 dark:border-teal-700 p-5 mb-6">
          <h3 className="font-semibold text-gray-900 dark:text-white mb-4">Add Exam Booking</h3>
          <form onSubmit={handleCreate} className="space-y-3">
            <div className="grid grid-cols-2 gap-3">
              <select value={form.examTypeCode} onChange={e => setForm(p => ({ ...p, examTypeCode: e.target.value }))} className="px-3 py-2 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-700 dark:text-gray-300">
                <option value="oet">OET</option>
                <option value="ielts">IELTS</option>
                <option value="pte">PTE</option>
                <option value="cambridge">Cambridge</option>
                <option value="toefl">TOEFL</option>
              </select>
              <input type="date" value={form.examDate} onChange={e => setForm(p => ({ ...p, examDate: e.target.value }))} required className="px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200" />
            </div>
            <input type="text" placeholder="Test center (optional)" value={form.testCenter} onChange={e => setForm(p => ({ ...p, testCenter: e.target.value }))} className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200" />
            <input type="text" placeholder="Booking reference (optional)" value={form.bookingReference} onChange={e => setForm(p => ({ ...p, bookingReference: e.target.value }))} className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200" />
            <input type="url" placeholder="External booking URL (optional)" value={form.externalUrl} onChange={e => setForm(p => ({ ...p, externalUrl: e.target.value }))} className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200" />
            <div className="flex gap-2 pt-1">
              <button type="submit" disabled={creating} className="px-5 py-2 bg-teal-600 hover:bg-teal-700 text-white rounded-lg text-sm font-medium disabled:opacity-50">
                {creating ? 'Saving...' : 'Save Booking'}
              </button>
              <button type="button" onClick={() => setShowCreate(false)} className="px-5 py-2 border border-gray-300 dark:border-gray-600 rounded-lg text-sm text-gray-600 dark:text-gray-400">Cancel</button>
            </div>
          </form>
        </motion.div>
      )}

      {loading ? (
        <div className="space-y-3">{Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div>
      ) : bookings.length === 0 ? (
        <div className="text-center py-12 text-gray-400">
          <CalendarDays className="w-10 h-10 mx-auto mb-3 opacity-30" />
          <p>No exam bookings yet. Add your upcoming exam date to count down!</p>
        </div>
      ) : (
        <>
          {upcoming.length > 0 && (
            <>
              <LearnerSurfaceSectionHeader title="Upcoming" />
              <div className="space-y-3 mb-8">
                {upcoming.map((booking, i) => {
                  const days = daysUntil(booking.examDate);
                  return (
                    <motion.div key={booking.id} initial={{ opacity: 0, y: 6 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: i * 0.05 }}
                      className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-4 flex items-center gap-4"
                    >
                      <div className={`flex-shrink-0 w-14 h-14 rounded-xl flex flex-col items-center justify-center text-white text-xs font-bold ${days <= 7 ? 'bg-red-500' : days <= 30 ? 'bg-orange-500' : 'bg-teal-500'}`}>
                        <div className="text-2xl font-bold leading-none">{days > 0 ? days : '—'}</div>
                        <div>days</div>
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-0.5">
                          <span className="font-semibold text-gray-900 dark:text-white">{booking.examTypeCode.toUpperCase()}</span>
                          <span className={`text-xs px-2 py-0.5 rounded-full capitalize ${STATUS_COLORS[booking.status] ?? 'bg-gray-100 text-gray-500'}`}>{booking.status}</span>
                        </div>
                        <div className="text-sm text-gray-500">{new Date(booking.examDate).toLocaleDateString('en-AU', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })}</div>
                        {booking.testCenter && <div className="text-xs text-gray-400">{booking.testCenter}</div>}
                        {booking.bookingReference && <div className="text-xs text-gray-400">Ref: {booking.bookingReference}</div>}
                      </div>
                      <div className="flex items-center gap-2">
                        {booking.externalUrl && (
                          <a href={booking.externalUrl} target="_blank" rel="noopener noreferrer" className="p-1.5 text-gray-400 hover:text-teal-600 transition-colors">
                            <ExternalLink className="w-4 h-4" />
                          </a>
                        )}
                        <button onClick={() => handleDelete(booking.id)} disabled={deleting === booking.id} className="p-1.5 text-gray-400 hover:text-red-500 transition-colors disabled:opacity-40">
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                    </motion.div>
                  );
                })}
              </div>
            </>
          )}

          {past.length > 0 && (
            <>
              <LearnerSurfaceSectionHeader title="Past" />
              <div className="space-y-2">
                {past.map((booking, i) => (
                  <div key={booking.id} className="bg-gray-50 dark:bg-gray-900 rounded-xl border border-gray-200 dark:border-gray-700 px-4 py-3 flex items-center gap-3 opacity-70">
                    <span className="font-medium text-gray-700 dark:text-gray-300 text-sm">{booking.examTypeCode.toUpperCase()}</span>
                    <span className="text-sm text-gray-400">{booking.examDate}</span>
                    <span className={`text-xs px-2 py-0.5 rounded-full capitalize ml-auto ${STATUS_COLORS[booking.status] ?? 'bg-gray-100 text-gray-500'}`}>{booking.status}</span>
                  </div>
                ))}
              </div>
            </>
          )}
        </>
      )}
    </LearnerDashboardShell>
  );
}
