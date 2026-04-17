'use client';

import { useEffect, useState, useCallback } from 'react';
import { UserPlus, Trash2, Mail, Loader2, Users } from 'lucide-react';
import {
  fetchSponsoredLearners,
  inviteSponsoredLearner,
  removeSponsoredLearner,
  isApiError,
  type SponsoredLearner,
} from '@/lib/api';

function StatusBadge({ status }: { status: string }) {
  const colors: Record<string, string> = {
    Active: 'bg-emerald-100 text-emerald-800',
    Pending: 'bg-amber-100 text-amber-800',
    Revoked: 'bg-red-100 text-red-800',
  };

  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${colors[status] ?? 'bg-gray-100 text-gray-800'}`}>
      {status}
    </span>
  );
}

export default function SponsorLearnersPage() {
  const [learners, setLearners] = useState<SponsoredLearner[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [inviteEmail, setInviteEmail] = useState('');
  const [inviteLoading, setInviteLoading] = useState(false);
  const [inviteError, setInviteError] = useState<string | null>(null);
  const [inviteSuccess, setInviteSuccess] = useState<string | null>(null);

  const loadLearners = useCallback(async () => {
    try {
      setLoading(true);
      const result = await fetchSponsoredLearners({ pageSize: 50 });
      setLearners(result.items);
      setTotal(result.total);
      setError(null);
    } catch (err) {
      setError(isApiError(err) ? err.userMessage : 'Failed to load learners.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadLearners();
  }, [loadLearners]);

  const handleInvite = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!inviteEmail.trim()) return;

    setInviteLoading(true);
    setInviteError(null);
    setInviteSuccess(null);

    try {
      await inviteSponsoredLearner(inviteEmail.trim());
      setInviteSuccess(`Invitation sent to ${inviteEmail.trim()}`);
      setInviteEmail('');
      void loadLearners();
    } catch (err) {
      setInviteError(isApiError(err) ? err.userMessage : 'Failed to send invitation.');
    } finally {
      setInviteLoading(false);
    }
  };

  const handleRemove = async (id: string) => {
    try {
      await removeSponsoredLearner(id);
      void loadLearners();
    } catch (err) {
      setError(isApiError(err) ? err.userMessage : 'Failed to remove sponsorship.');
    }
  };

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold text-navy">Sponsored Learners</h1>

      {/* Invite form */}
      <div className="page-surface rounded-2xl p-6 shadow-sm">
        <h2 className="text-lg font-semibold text-navy mb-4">Invite a Learner</h2>
        <form onSubmit={handleInvite} className="flex flex-col gap-3 sm:flex-row sm:items-end">
          <div className="flex-1">
            <label htmlFor="invite-email" className="block text-sm font-medium text-muted mb-1">
              Email address
            </label>
            <div className="relative">
              <Mail className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted" />
              <input
                id="invite-email"
                type="email"
                required
                value={inviteEmail}
                onChange={(e) => setInviteEmail(e.target.value)}
                placeholder="learner@example.com"
                className="w-full rounded-xl border border-border bg-transparent py-2.5 pl-10 pr-4 text-sm focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
              />
            </div>
          </div>
          <button
            type="submit"
            disabled={inviteLoading}
            className="inline-flex items-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-sm font-medium text-white hover:bg-primary/90 disabled:opacity-50 transition-colors"
          >
            {inviteLoading ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <UserPlus className="h-4 w-4" />
            )}
            Invite
          </button>
        </form>
        {inviteError && <p className="mt-2 text-sm text-red-600">{inviteError}</p>}
        {inviteSuccess && <p className="mt-2 text-sm text-emerald-600">{inviteSuccess}</p>}
      </div>

      {/* Learners list */}
      {loading ? (
        <div className="space-y-3">
          {[1, 2, 3].map((i) => (
            <div key={i} className="page-surface h-16 animate-pulse rounded-2xl" />
          ))}
        </div>
      ) : error ? (
        <div className="page-surface rounded-2xl p-6 text-center">
          <p className="text-sm text-red-600">{error}</p>
        </div>
      ) : learners.length === 0 ? (
        <div className="page-surface rounded-2xl p-8 text-center">
          <Users className="mx-auto h-10 w-10 text-muted mb-3" />
          <p className="text-sm text-muted">No sponsored learners yet. Use the form above to invite your first learner.</p>
        </div>
      ) : (
        <div className="page-surface rounded-2xl shadow-sm overflow-hidden">
          <div className="px-6 py-4 border-b border-border">
            <p className="text-sm text-muted">{total} learner{total !== 1 ? 's' : ''}</p>
          </div>
          <div className="divide-y divide-border">
            {learners.map((learner) => (
              <div key={learner.id} className="flex items-center justify-between px-6 py-4">
                <div className="min-w-0 flex-1">
                  <p className="text-sm font-medium text-navy truncate">{learner.learnerEmail}</p>
                  <p className="text-xs text-muted mt-0.5">
                    Added {new Date(learner.createdAt).toLocaleDateString()}
                  </p>
                </div>
                <div className="flex items-center gap-3 ml-4">
                  <StatusBadge status={learner.status} />
                  {learner.status !== 'Revoked' && (
                    <button
                      onClick={() => handleRemove(learner.id)}
                      className="rounded-lg p-2.5 -m-1 text-muted hover:text-red-600 hover:bg-red-50 transition-colors"
                      title="Remove sponsorship"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
