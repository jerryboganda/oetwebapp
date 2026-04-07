'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { GraduationCap, Calendar, Star, Plus } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchTutoringSessions, bookTutoringSession, rateTutoringSession } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type TutoringSession = {
  id: string; expertUserId: string; examTypeCode: string; subtestFocus: string | null;
  scheduledAt: string; durationMinutes: number; state: string; price: number;
  learnerRating: number | null;
};

const STATE_COLORS: Record<string, string> = {
  booked: 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300',
  completed: 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300',
  cancelled: 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300',
};

function formatDate(iso: string) {
  return new Date(iso).toLocaleString('en-AU', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

export default function TutoringPage() {
  const [sessions, setSessions] = useState<TutoringSession[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showBook, setShowBook] = useState(false);
  const [booking, setBooking] = useState(false);
  const [ratingSession, setRatingSession] = useState<string | null>(null);
  const [ratingValue, setRatingValue] = useState(5);
  const [bookForm, setBookForm] = useState({
    expertUserId: '',
    examTypeCode: 'oet',
    subtestFocus: '',
    scheduledAt: '',
    durationMinutes: 60,
    learnerNotes: '',
    price: 50,
  });

  useEffect(() => {
    analytics.track('tutoring_page_viewed');
    fetchTutoringSessions().then(data => {
      setSessions(data as TutoringSession[]);
      setLoading(false);
    }).catch(() => {
      setError('Could not load sessions.');
      setLoading(false);
    });
  }, []);

  async function handleBook(e: React.FormEvent) {
    e.preventDefault();
    if (!bookForm.expertUserId || !bookForm.scheduledAt || booking) return;
    setBooking(true);
    try {
      await bookTutoringSession({ expertUserId: bookForm.expertUserId, examTypeCode: bookForm.examTypeCode, subtestFocus: bookForm.subtestFocus || undefined, scheduledAt: bookForm.scheduledAt, durationMinutes: bookForm.durationMinutes, learnerNotes: bookForm.learnerNotes || undefined, price: bookForm.price });
      const data = await fetchTutoringSessions() as TutoringSession[];
      setSessions(data);
      setShowBook(false);
    } catch {
      setError('Could not book session.');
    } finally {
      setBooking(false);
    }
  }

  async function handleRate(sessionId: string) {
    try {
      await rateTutoringSession(sessionId, ratingValue);
      setSessions(prev => prev.map(s => s.id === sessionId ? { ...s, learnerRating: ratingValue } : s));
      setRatingSession(null);
    } catch {
      setError('Could not submit rating.');
    }
  }

  return (
    <LearnerDashboardShell>
      <div className="flex items-center justify-between mb-6">
        <LearnerPageHero
          title="Tutoring Sessions"
          description="Book 1-on-1 sessions with OET expert tutors"
          icon={GraduationCap}
        />
        <button
          onClick={() => setShowBook(true)}
          className="flex items-center gap-2 px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white rounded-xl text-sm font-medium transition-colors"
        >
          <Plus className="w-4 h-4" /> Book Session
        </button>
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* Book form */}
      {showBook && (
        <motion.div initial={{ opacity: 0, y: -10 }} animate={{ opacity: 1, y: 0 }} className="bg-white dark:bg-gray-800 rounded-xl border border-indigo-200 dark:border-indigo-700 p-5 mb-6">
          <h3 className="font-semibold text-gray-900 dark:text-white mb-4">Book a Tutoring Session</h3>
          <form onSubmit={handleBook} className="space-y-3">
            <input
              type="text"
              placeholder="Expert User ID or username"
              value={bookForm.expertUserId}
              onChange={e => setBookForm(p => ({ ...p, expertUserId: e.target.value }))}
              required
              className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200 focus:outline-none focus:ring-2 focus:ring-indigo-400"
            />
            <div className="grid grid-cols-2 gap-3">
              <select value={bookForm.examTypeCode} onChange={e => setBookForm(p => ({ ...p, examTypeCode: e.target.value }))} className="px-3 py-2 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-700 dark:text-gray-300">
                <option value="oet">OET</option>
                <option value="ielts">IELTS</option>
                <option value="pte">PTE</option>
              </select>
              <select value={bookForm.subtestFocus} onChange={e => setBookForm(p => ({ ...p, subtestFocus: e.target.value }))} className="px-3 py-2 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-700 dark:text-gray-300">
                <option value="">Any subtest</option>
                <option value="writing">Writing</option>
                <option value="speaking">Speaking</option>
                <option value="reading">Reading</option>
                <option value="listening">Listening</option>
              </select>
            </div>
            <input
              type="datetime-local"
              value={bookForm.scheduledAt}
              onChange={e => setBookForm(p => ({ ...p, scheduledAt: e.target.value }))}
              required
              className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200 focus:outline-none focus:ring-2 focus:ring-indigo-400"
            />
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="text-xs text-gray-500 mb-1 block">Duration (minutes)</label>
                <select value={bookForm.durationMinutes} onChange={e => setBookForm(p => ({ ...p, durationMinutes: Number(e.target.value) }))} className="w-full px-3 py-2 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-700 dark:text-gray-300">
                  <option value={30}>30 min</option>
                  <option value={60}>60 min</option>
                  <option value={90}>90 min</option>
                </select>
              </div>
              <div>
                <label className="text-xs text-gray-500 mb-1 block">Price (credits)</label>
                <input type="number" min={0} value={bookForm.price} onChange={e => setBookForm(p => ({ ...p, price: Number(e.target.value) }))} className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200" />
              </div>
            </div>
            <textarea
              placeholder="Notes for the tutor (optional)"
              value={bookForm.learnerNotes}
              onChange={e => setBookForm(p => ({ ...p, learnerNotes: e.target.value }))}
              rows={2}
              className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200 resize-none focus:outline-none"
            />
            <div className="flex gap-2 pt-1">
              <button type="submit" disabled={booking} className="px-5 py-2 bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg text-sm font-medium disabled:opacity-50">
                {booking ? 'Booking...' : 'Confirm Booking'}
              </button>
              <button type="button" onClick={() => setShowBook(false)} className="px-5 py-2 border border-gray-300 dark:border-gray-600 rounded-lg text-sm text-gray-600 dark:text-gray-400">
                Cancel
              </button>
            </div>
          </form>
        </motion.div>
      )}

      <LearnerSurfaceSectionHeader title="Your Sessions" />
      {loading ? (
        <div className="space-y-3">{Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div>
      ) : sessions.length === 0 ? (
        <div className="text-center py-12 text-gray-400">
          <GraduationCap className="w-10 h-10 mx-auto mb-3 opacity-30" />
          <p>No tutoring sessions yet.</p>
        </div>
      ) : (
        <div className="space-y-3">
          {sessions.map((session, i) => (
            <motion.div key={session.id} initial={{ opacity: 0, y: 6 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: i * 0.04 }}
              className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-4 flex flex-col sm:flex-row sm:items-center gap-3"
            >
              <div className="flex-1">
                <div className="flex items-center gap-2 mb-1">
                  <span className="font-medium text-gray-900 dark:text-white text-sm">{session.examTypeCode.toUpperCase()} {session.subtestFocus ? `— ${session.subtestFocus}` : ''}</span>
                  <span className={`text-xs px-2 py-0.5 rounded-full capitalize ${STATE_COLORS[session.state] ?? 'bg-gray-100 text-gray-500'}`}>{session.state}</span>
                </div>
                <div className="flex items-center gap-3 text-xs text-gray-400">
                  <span className="flex items-center gap-1"><Calendar className="w-3.5 h-3.5" />{formatDate(session.scheduledAt)}</span>
                  <span>{session.durationMinutes} min</span>
                  <span>{session.price} credits</span>
                </div>
              </div>
              {session.state === 'completed' && session.learnerRating === null && (
                ratingSession === session.id ? (
                  <div className="flex items-center gap-2">
                    {[1, 2, 3, 4, 5].map(v => (
                      <button key={v} onClick={() => setRatingValue(v)}
                        className={`w-7 h-7 rounded-full text-sm ${ratingValue >= v ? 'text-yellow-400' : 'text-gray-300'}`}>
                        ★
                      </button>
                    ))}
                    <button onClick={() => handleRate(session.id)} className="text-xs px-3 py-1.5 bg-yellow-500 hover:bg-yellow-600 text-white rounded-lg">Submit</button>
                    <button onClick={() => setRatingSession(null)} className="text-xs text-gray-400">Cancel</button>
                  </div>
                ) : (
                  <button onClick={() => setRatingSession(session.id)} className="flex items-center gap-1.5 text-sm text-yellow-500 hover:text-yellow-600 font-medium">
                    <Star className="w-4 h-4" /> Rate
                  </button>
                )
              )}
              {session.learnerRating !== null && (
                <div className="flex items-center gap-1 text-yellow-400 text-sm">
                  {'★'.repeat(session.learnerRating)}{'☆'.repeat(5 - session.learnerRating)}
                </div>
              )}
            </motion.div>
          ))}
        </div>
      )}
    </LearnerDashboardShell>
  );
}
