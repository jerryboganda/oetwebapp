'use client';

import { useEffect, useState, useCallback } from 'react';
import { Store, Search, Upload, Filter, BookOpen, Mic, Pen, Headphones, Clock, CheckCircle2, XCircle, Loader2 } from 'lucide-react';
import { LearnerPageHero, ExamTypeBadge } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Tabs, TabPanel, type Tab } from '@/components/ui/tabs';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem } from '@/components/ui/motion-primitives';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

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

const STATUS_CONFIG: Record<string, { label: string; icon: typeof CheckCircle2; variant: BadgeProps['variant'] }> = {
  pending: { label: 'Pending Review', icon: Clock, variant: 'warning' },
  in_review: { label: 'In Review', icon: Loader2, variant: 'info' },
  approved: { label: 'Approved', icon: CheckCircle2, variant: 'success' },
  rejected: { label: 'Rejected', icon: XCircle, variant: 'danger' },
};

type MarketplaceTab = 'browse' | 'submit' | 'my';

const MARKETPLACE_TABS: Tab[] = [
  { id: 'browse', label: 'Browse', icon: <Search className="h-4 w-4 shrink-0" aria-hidden="true" /> },
  { id: 'submit', label: 'Submit Content', icon: <Upload className="h-4 w-4 shrink-0" aria-hidden="true" /> },
  { id: 'my', label: 'My Submissions', icon: <BookOpen className="h-4 w-4 shrink-0" aria-hidden="true" /> },
];

const SUBTEST_OPTIONS = [
  { value: 'writing', label: 'Writing' },
  { value: 'speaking', label: 'Speaking' },
  { value: 'reading', label: 'Reading' },
  { value: 'listening', label: 'Listening' },
];

const DIFFICULTY_OPTIONS = [
  { value: 'easy', label: 'Easy' },
  { value: 'medium', label: 'Medium' },
  { value: 'hard', label: 'Hard' },
];

const apiFetch = apiClient.request;

export default function MarketplacePage() {
  const [tab, setTab] = useState<MarketplaceTab>('browse');
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
    <>
      <LearnerPageHero
        title="Content Marketplace"
        description="Browse community-contributed OET practice content or submit your own."
        icon={Store}
        accent="blue"
      />

      {error && <InlineAlert variant="warning">{error}</InlineAlert>}

      <Tabs
        tabs={MARKETPLACE_TABS}
        activeTab={tab}
        onChange={(id) => setTab(id as MarketplaceTab)}
      />

      {/* Browse Tab */}
      <TabPanel id="browse" activeTab={tab} className="space-y-4">
        <div className="flex flex-wrap gap-3">
          <div className="relative min-w-[12rem] flex-1">
            <Search className="absolute start-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden="true" />
            <input type="text" placeholder="Search content..." aria-label="Search marketplace content" value={searchQ}
              onChange={e => setSearchQ(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && loadBrowse()}
              className="min-h-11 w-full rounded-xl border border-border bg-surface py-2.5 ps-10 pe-4 text-sm text-navy outline-none focus:ring-2 focus:ring-primary" />
          </div>
          <select value={filterSubtest} aria-label="Filter by subtest" onChange={e => { setFilterSubtest(e.target.value); }}
            className="min-h-11 rounded-xl border border-border bg-surface px-4 py-2.5 text-sm text-navy outline-none focus:ring-2 focus:ring-primary">
            <option value="">All Subtests</option>
            <option value="writing">Writing</option>
            <option value="speaking">Speaking</option>
            <option value="reading">Reading</option>
            <option value="listening">Listening</option>
          </select>
          <Button onClick={loadBrowse}>
            <Filter className="h-4 w-4" aria-hidden="true" /> Filter
          </Button>
        </div>

        {loading ? (
          <div className="space-y-3">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-2xl" />)}</div>
        ) : browseItems.length === 0 ? (
          <LearnerEmptyState
            icon={Store}
            title="No marketplace content yet"
            description="Be the first to submit practice content!"
            primaryAction={{ label: 'Submit Content', onClick: () => setTab('submit') }}
          />
        ) : (
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            {browseItems.map((item, i) => {
              const SubIcon = SUBTEST_ICONS[item.subtestCode] ?? BookOpen;
              return (
                <MotionItem key={item.id} delayIndex={Math.min(i, 5)}>
                  <Card className="h-full">
                    <div className="flex items-start gap-3">
                      <div className="rounded-lg bg-primary/10 p-2 text-primary">
                        <SubIcon className="h-5 w-5" aria-hidden="true" />
                      </div>
                      <div className="min-w-0 flex-1">
                        <div className="mb-1 flex items-center gap-2">
                          <h3 className="min-w-0 truncate text-sm font-bold text-navy">{item.title}</h3>
                          <ExamTypeBadge examType={item.examFamilyCode} size="sm" />
                        </div>
                        {item.description && <p className="mb-2 line-clamp-2 text-xs text-muted">{item.description}</p>}
                        <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-muted">
                          <span className="capitalize">{item.subtestCode}</span>
                          <span aria-hidden="true">•</span>
                          <span className="capitalize">{item.difficulty}</span>
                          {item.approvedAt && <><span aria-hidden="true">•</span><span className="tabular-nums">{dateFormatter.format(new Date(item.approvedAt))}</span></>}
                        </div>
                      </div>
                    </div>
                  </Card>
                </MotionItem>
              );
            })}
          </div>
        )}
      </TabPanel>

      {/* Submit Tab */}
      <TabPanel id="submit" activeTab={tab}>
        <Card padding="lg">
          <form onSubmit={handleSubmit} className="max-w-2xl space-y-4">
            {submitSuccess && (
              <InlineAlert variant="success">
                Content submitted successfully! It will be reviewed by our team.
              </InlineAlert>
            )}
            <Input
              id="mp-title"
              label="Title *"
              type="text"
              required
              value={submitForm.title}
              onChange={e => setSubmitForm(f => ({ ...f, title: e.target.value }))}
            />
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <Select
                id="mp-subtest"
                label="Subtest *"
                value={submitForm.subtestCode}
                onChange={e => setSubmitForm(f => ({ ...f, subtestCode: e.target.value }))}
                options={SUBTEST_OPTIONS}
              />
              <Select
                id="mp-difficulty"
                label="Difficulty"
                value={submitForm.difficulty}
                onChange={e => setSubmitForm(f => ({ ...f, difficulty: e.target.value }))}
                options={DIFFICULTY_OPTIONS}
              />
            </div>
            <Textarea
              id="mp-description"
              label="Description"
              rows={3}
              value={submitForm.description}
              onChange={e => setSubmitForm(f => ({ ...f, description: e.target.value }))}
              className="resize-none"
            />
            <Textarea
              id="mp-content"
              label="Content (JSON)"
              rows={6}
              value={submitForm.contentPayloadJson}
              onChange={e => setSubmitForm(f => ({ ...f, contentPayloadJson: e.target.value }))}
              placeholder='{"caseNotes": "...", "instructions": "..."}'
              className="resize-none font-mono"
            />
            <Input
              id="mp-tags"
              label="Tags (comma-separated)"
              type="text"
              value={submitForm.tags}
              onChange={e => setSubmitForm(f => ({ ...f, tags: e.target.value }))}
              placeholder="nursing, referral, cardiology"
            />
            <Button type="submit" size="lg" fullWidth loading={submitting} disabled={!submitForm.title.trim()}>
              {submitting ? 'Submitting...' : <><Upload className="h-4 w-4" aria-hidden="true" /> Submit for Review</>}
            </Button>
          </form>
        </Card>
      </TabPanel>

      {/* My Submissions Tab */}
      <TabPanel id="my" activeTab={tab} className="space-y-4">
        {/* Profile Summary */}
        {profile && (
          <Card>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="min-w-0">
                <h3 className="text-sm font-bold text-navy">{profile.displayName}</h3>
                <p className="text-xs capitalize text-muted">{profile.verificationStatus}</p>
              </div>
              <div className="flex gap-4 text-center">
                <div>
                  <p className="text-lg font-bold text-primary"><CountUp value={profile.submissionCount} /></p>
                  <p className="tile-label text-muted">Submitted</p>
                </div>
                <div>
                  <p className="text-lg font-bold text-success-strong"><CountUp value={profile.approvedCount} /></p>
                  <p className="tile-label text-muted">Approved</p>
                </div>
              </div>
            </div>
          </Card>
        )}

        {myItems.length === 0 ? (
          <LearnerEmptyState
            icon={Upload}
            title="No submissions yet"
            description="Switch to the Submit tab to contribute content"
            primaryAction={{ label: 'Submit Content', onClick: () => setTab('submit') }}
          />
        ) : (
          <div className="space-y-3">
            {myItems.map((item, i) => {
              const statusInfo = STATUS_CONFIG[item.status] ?? STATUS_CONFIG.pending;
              const StatusIcon = statusInfo.icon;
              return (
                <MotionItem key={item.id} delayIndex={Math.min(i, 5)}>
                  <Card>
                    <div className="flex items-center justify-between gap-3">
                      <div className="min-w-0">
                        <div className="mb-0.5 flex items-center gap-2">
                          <span className="min-w-0 truncate text-sm font-bold text-navy">{item.title}</span>
                          <ExamTypeBadge examType={item.examFamilyCode} size="sm" />
                        </div>
                        <div className="flex items-center gap-2 text-xs text-muted">
                          <span className="capitalize">{item.subtestCode}</span>
                          <span aria-hidden="true">•</span>
                          <span className="tabular-nums">{dateFormatter.format(new Date(item.submittedAt))}</span>
                        </div>
                      </div>
                      <Badge variant={statusInfo.variant} className="shrink-0 gap-1 px-2.5 py-1">
                        <StatusIcon className="h-3 w-3" aria-hidden="true" /> {statusInfo.label}
                      </Badge>
                    </div>
                  </Card>
                </MotionItem>
              );
            })}
          </div>
        )}
      </TabPanel>
    </>
  );
}
