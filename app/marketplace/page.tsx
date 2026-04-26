'use client';

import { ExamTypeBadge } from "@/components/domain/exam-type-badge";
import { LearnerPageHero } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';
import { BookOpen, CheckCircle2, ChevronRight, Clock, Filter, Headphones, Loader2, Mic, Pen, Search, Store, Upload, XCircle } from 'lucide-react';
import { AnimatePresence } from 'motion/react';
import { useCallback, useEffect, useState } from 'react';

type Submission = {
  id: string;
  contributorId: string;
  examFamilyCode: string;
  subtestCode: string;
  title: string;
  description: string | null;
  contentType: string;
  difficulty: string;
  tags: string | null;
  status: string;
  submittedAt: string;
  approvedAt: string | null;
};

type ContributorProfile = {
  id: string;
  displayName: string;
  bio: string | null;
  verificationStatus: string;
  submissionCount: number;
  approvedCount: number;
  rating: number;
};

const SUBTEST_ICONS: Record<string, typeof BookOpen> = {
  writing: Pen,
  speaking: Mic,
  reading: BookOpen,
  listening: Headphones,
};

const STATUS_CONFIG: Record<string, { label: string; icon: typeof CheckCircle2; color: string }> = {
  pending: { label: 'Pending Review', icon: Clock, color: 'bg-warning/10 text-warning' },
  in_review: { label: 'In Review', icon: Loader2, color: 'bg-info/10 text-info' },
  approved: { label: 'Approved', icon: CheckCircle2, color: 'bg-success/10 text-success' },
  rejected: { label: 'Rejected', icon: XCircle, color: 'bg-danger/10 text-danger' },
};

const apiFetch = apiClient.request;

export default function MarketplacePage() {
  const [tab, setTab] = useState<'browse' | 'submit' | 'my'>('browse');
  const [profile, setProfile] = useState<ContributorProfile | null>(null);
  const [browseItems, setBrowseItems] = useState<Submission[]>([]);
  const [myItems, setMyItems] = useState<Submission[]>([]);
  const [loading, setLoading] = useState(true);
  const [searchQ, setSearchQ] = useState('');
  const [filterSubtest, setFilterSubtest] = useState('');
  const [error, setError] = useState<string | null>(null);

  // Submit form
  const [submitForm, setSubmitForm] = useState({
    title: '', subtestCode: 'writing', description: '', difficulty: 'medium',
    contentType: 'practice_task', tags: '', examFamilyCode: 'oet',
    contentPayloadJson: '{}',
  });
  const [submitting, setSubmitting] = useState(false);
  const [submitSuccess, setSubmitSuccess] = useState(false);

  const loadBrowse = useCallback(async () => {
    try {
      const params = new URLSearchParams({ page: '1', pageSize: '20' });
      if (searchQ) params.set('search', searchQ);
      if (filterSubtest) params.set('subtest', filterSubtest);
      const data = await apiFetch(`/v1/marketplace/browse?${params}`);
      setBrowseItems(Array.isArray(data.items) ? data.items : []);
    } catch { setError('Failed to load marketplace content.'); }
  }, [searchQ, filterSubtest]);

  const loadMy = useCallback(async () => {
    try {
      const data = await apiFetch('/v1/marketplace/submissions?page=1&pageSize=50');
      setMyItems(Array.isArray(data.items) ? data.items : []);
    } catch { /* Ignore - user may not have submissions */ }
  }, []);

  useEffect(() => {
    analytics.track('marketplace_page_viewed');
    const init = async () => {
      try {
        const p = await apiFetch('/v1/marketplace/profile');
        setProfile(p);
      } catch { /* first visit */ }
      await loadBrowse();
      await loadMy();
      setLoading(false);
    };
    void init();
  }, [loadBrowse, loadMy]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (submitting || !submitForm.title.trim()) return;
    setSubmitting(true);
    setSubmitSuccess(false);
    setError(null);
    try {
      // Ensure profile exists
      if (!profile) {
        const p = await apiFetch('/v1/marketplace/profile');
        setProfile(p);
      }
      await apiFetch('/v1/marketplace/submissions', {
        method: 'POST',
        body: JSON.stringify(submitForm),
      });
      analytics.track('marketplace_submission_created');
      setSubmitSuccess(true);
      setSubmitForm(f => ({ ...f, title: '', description: '', tags: '', contentPayloadJson: '{}' }));
      await loadMy();
    } catch {
      setError('Failed to submit content. Please try again.');
    } finally {
      setSubmitting(false);
    }
  }

  const dateFormatter = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', year: 'numeric' });

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Content Marketplace"
        description="Browse community-contributed OET practice content or submit your own."
        icon={Store}
        accent="blue"
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* Tabs */}
      <div className="flex gap-1 mb-6 bg-background-light rounded-xl p-1">
        {[
          { key: 'browse' as const, label: 'Browse', icon: Search },
          { key: 'submit' as const, label: 'Submit Content', icon: Upload },
          { key: 'my' as const, label: 'My Submissions', icon: BookOpen },
        ].map(t => (
          <button key={t.key} onClick={() => setTab(t.key)}
            className={`flex-1 flex items-center justify-center gap-2 py-2.5 rounded-lg text-sm font-semibold transition-colors ${
              tab === t.key ? 'bg-surface text-navy shadow-sm' : 'text-muted hover:text-navy'
            }`}>
            <t.icon className="w-4 h-4" /> {t.label}
          </button>
        ))}
      </div>

      {/* Browse Tab */}
      {tab === 'browse' && (
        <section>
          <div className="flex gap-3 mb-4">
            <div className="flex-1 relative">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted/60" />
              <input type="text" placeholder="Search content..." value={searchQ}
                onChange={e => setSearchQ(e.target.value)}
                onKeyDown={e => e.key === 'Enter' && loadBrowse()}
                className="w-full pl-10 pr-4 py-2.5 bg-surface border border-border rounded-xl text-sm text-navy focus:ring-2 focus:ring-primary outline-none" />
            </div>
            <select value={filterSubtest} onChange={e => { setFilterSubtest(e.target.value); }}
              className="px-4 py-2.5 bg-surface border border-border rounded-xl text-sm text-navy focus:ring-2 focus:ring-primary outline-none">
              <option value="">All Subtests</option>
              <option value="writing">Writing</option>
              <option value="speaking">Speaking</option>
              <option value="reading">Reading</option>
              <option value="listening">Listening</option>
            </select>
            <button onClick={loadBrowse} className="px-4 py-2.5 bg-primary hover:bg-primary/90 text-white rounded-xl text-sm font-semibold flex items-center gap-1">
              <Filter className="w-4 h-4" /> Filter
            </button>
          </div>

          {loading ? (
            <div className="space-y-3">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div>
          ) : browseItems.length === 0 ? (
            <div className="text-center py-16 bg-background-light rounded-2xl border border-dashed border-border">
              <Store className="w-10 h-10 text-muted/40 mx-auto mb-3" />
              <p className="text-sm font-semibold text-muted">No marketplace content yet</p>
              <p className="text-xs text-muted/60 mt-1">Be the first to submit practice content!</p>
            </div>
          ) : (
            <AnimatePresence>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {browseItems.map((item, i) => {
                  const SubIcon = SUBTEST_ICONS[item.subtestCode] ?? BookOpen;
                  return (
                    <MotionItem key={item.id} delayIndex={i}
                      className="bg-surface rounded-xl border border-border p-4 hover:border-primary/30 hover:shadow-sm transition-all">
                      <div className="flex items-start gap-3">
                        <div className="p-2 rounded-lg bg-primary/10 text-primary">
                          <SubIcon className="w-5 h-5" />
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2 mb-1">
                            <h3 className="text-sm font-semibold text-navy truncate">{item.title}</h3>
                            <ExamTypeBadge examType={item.examFamilyCode} size="sm" />
                          </div>
                          {item.description && <p className="text-xs text-muted line-clamp-2 mb-2">{item.description}</p>}
                          <div className="flex items-center gap-2 text-xs text-muted/60">
                            <span className="capitalize">{item.subtestCode}</span>
                            <span>•</span>
                            <span className="capitalize">{item.difficulty}</span>
                            {item.approvedAt && <><span>•</span><span>{dateFormatter.format(new Date(item.approvedAt))}</span></>}
                          </div>
                        </div>
                        <ChevronRight className="w-4 h-4 text-muted/40 mt-1" />
                      </div>
                    </MotionItem>
                  );
                })}
              </div>
            </AnimatePresence>
          )}
        </section>
      )}

      {/* Submit Tab */}
      {tab === 'submit' && (
        <section className="max-w-xl">
          {submitSuccess && (
            <InlineAlert variant="success" className="mb-4">
              Content submitted successfully! It will be reviewed by our team.
            </InlineAlert>
          )}
          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <label className="block text-sm font-semibold text-navy mb-1">Title *</label>
              <input type="text" required value={submitForm.title} onChange={e => setSubmitForm(f => ({ ...f, title: e.target.value }))}
                className="w-full px-4 py-2.5 bg-surface border border-border rounded-xl text-sm text-navy focus:ring-2 focus:ring-primary outline-none" />
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-semibold text-navy mb-1">Subtest *</label>
                <select value={submitForm.subtestCode} onChange={e => setSubmitForm(f => ({ ...f, subtestCode: e.target.value }))}
                  className="w-full px-4 py-2.5 bg-surface border border-border rounded-xl text-sm text-navy focus:ring-2 focus:ring-primary outline-none">
                  <option value="writing">Writing</option>
                  <option value="speaking">Speaking</option>
                  <option value="reading">Reading</option>
                  <option value="listening">Listening</option>
                </select>
              </div>
              <div>
                <label className="block text-sm font-semibold text-navy mb-1">Difficulty</label>
                <select value={submitForm.difficulty} onChange={e => setSubmitForm(f => ({ ...f, difficulty: e.target.value }))}
                  className="w-full px-4 py-2.5 bg-surface border border-border rounded-xl text-sm text-navy focus:ring-2 focus:ring-primary outline-none">
                  <option value="easy">Easy</option>
                  <option value="medium">Medium</option>
                  <option value="hard">Hard</option>
                </select>
              </div>
            </div>
            <div>
              <label className="block text-sm font-semibold text-navy mb-1">Description</label>
              <textarea rows={3} value={submitForm.description} onChange={e => setSubmitForm(f => ({ ...f, description: e.target.value }))}
                className="w-full px-4 py-2.5 bg-surface border border-border rounded-xl text-sm text-navy focus:ring-2 focus:ring-primary outline-none resize-none" />
            </div>
            <div>
              <label className="block text-sm font-semibold text-navy mb-1">Content (JSON)</label>
              <textarea rows={6} value={submitForm.contentPayloadJson} onChange={e => setSubmitForm(f => ({ ...f, contentPayloadJson: e.target.value }))}
                placeholder='{"caseNotes": "...", "instructions": "..."}'
                className="w-full px-4 py-2.5 bg-surface border border-border rounded-xl text-sm text-navy font-mono focus:ring-2 focus:ring-primary outline-none resize-none" />
            </div>
            <div>
              <label className="block text-sm font-semibold text-navy mb-1">Tags (comma-separated)</label>
              <input type="text" value={submitForm.tags} onChange={e => setSubmitForm(f => ({ ...f, tags: e.target.value }))}
                placeholder="nursing, referral, cardiology"
                className="w-full px-4 py-2.5 bg-surface border border-border rounded-xl text-sm text-navy focus:ring-2 focus:ring-primary outline-none" />
            </div>
            <button type="submit" disabled={submitting || !submitForm.title.trim()}
              className="w-full py-3 bg-primary hover:bg-primary/90 disabled:opacity-50 text-white rounded-xl font-semibold flex items-center justify-center gap-2 transition-colors">
              {submitting ? <><Loader2 className="w-4 h-4 animate-spin" /> Submitting...</> : <><Upload className="w-4 h-4" /> Submit for Review</>}
            </button>
          </form>
        </section>
      )}

      {/* My Submissions Tab */}
      {tab === 'my' && (
        <section>
          {/* Profile Summary */}
          {profile && (
            <div className="bg-primary/5 rounded-xl border border-primary/20 p-4 mb-4">
              <div className="flex items-center justify-between">
                <div>
                  <h3 className="text-sm font-bold text-navy">{profile.displayName}</h3>
                  <p className="text-xs text-muted capitalize">{profile.verificationStatus}</p>
                </div>
                <div className="flex gap-4 text-center">
                  <div><div className="text-lg font-bold text-primary">{profile.submissionCount}</div><div className="text-xs text-muted/60">Submitted</div></div>
                  <div><div className="text-lg font-bold text-success">{profile.approvedCount}</div><div className="text-xs text-muted/60">Approved</div></div>
                </div>
              </div>
            </div>
          )}

          {myItems.length === 0 ? (
            <div className="text-center py-12 bg-background-light rounded-2xl border border-dashed border-border">
              <Upload className="w-10 h-10 text-muted/40 mx-auto mb-3" />
              <p className="text-sm font-semibold text-muted">No submissions yet</p>
              <p className="text-xs text-muted/60 mt-1">Switch to the Submit tab to contribute content</p>
            </div>
          ) : (
            <div className="space-y-3">
              {myItems.map((item, i) => {
                const statusInfo = STATUS_CONFIG[item.status] ?? STATUS_CONFIG.pending;
                const StatusIcon = statusInfo.icon;
                return (
                  <MotionItem key={item.id} delayIndex={i}
                    className="bg-surface rounded-xl border border-border p-4">
                    <div className="flex items-center justify-between gap-3">
                      <div className="min-w-0">
                        <div className="flex items-center gap-2 mb-0.5">
                          <span className="text-sm font-semibold text-navy truncate">{item.title}</span>
                          <ExamTypeBadge examType={item.examFamilyCode} size="sm" />
                        </div>
                        <div className="flex items-center gap-2 text-xs text-muted/60">
                          <span className="capitalize">{item.subtestCode}</span>
                          <span>•</span>
                          <span>{dateFormatter.format(new Date(item.submittedAt))}</span>
                        </div>
                      </div>
                      <span className={`text-xs font-medium px-2.5 py-1 rounded-full flex items-center gap-1 ${statusInfo.color}`}>
                        <StatusIcon className="w-3 h-3" /> {statusInfo.label}
                      </span>
                    </div>
                  </MotionItem>
                );
              })}
            </div>
          )}
        </section>
      )}
    </LearnerDashboardShell>
  );
}
