'use client';

import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Activity, AlertTriangle, BarChart3, Download, ListChecks, Plus, RefreshCw, Target, TrendingUp, UserPlus, Users } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { StatCard } from '@/components/ui/stat-card';
import { exportToCsv } from '@/lib/csv-export';
import { teacherClassApi, type ListeningClassAnalyticsDto, type TeacherClassDto } from '@/lib/listening/v2-api';

type AsyncStatus = 'loading' | 'error' | 'success';

const DAYS_OPTIONS = [
  { value: '7', label: 'Last 7 days' },
  { value: '30', label: 'Last 30 days' },
  { value: '90', label: 'Last 90 days' },
];

function pct(value: number | null | undefined) {
  if (value == null) return '--';
  return Number.isInteger(value) ? `${value}%` : `${value.toFixed(1)}%`;
}

function errorMessage(error: unknown, fallback: string) {
  return error instanceof Error && error.message ? error.message : fallback;
}

function filenameSegment(value: string) {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 64) || 'class';
}

function analyticsExportRows(result: ListeningClassAnalyticsDto): Record<string, unknown>[] {
  const summaryRows = [
    { section: 'summary', metric: 'Class name', value: result.className },
    { section: 'summary', metric: 'Members', value: result.memberCount },
    { section: 'summary', metric: 'Window days', value: result.analytics.days },
    { section: 'summary', metric: 'Completed attempts', value: result.analytics.completedAttempts },
    { section: 'summary', metric: 'Average scaled score', value: result.analytics.averageScaledScore ?? '' },
    { section: 'summary', metric: 'Likely passing percent', value: result.analytics.percentLikelyPassing },
  ];

  const partRows = result.analytics.classPartAverages.map((part) => ({
    section: 'part_accuracy',
    partCode: part.partCode,
    earned: part.earned,
    max: part.max,
    accuracyPercent: part.accuracyPercent,
  }));

  const questionRows = result.analytics.hardestQuestions.map((question) => ({
    section: 'hardest_questions',
    paperId: question.paperId,
    paperTitle: question.paperTitle,
    questionNumber: question.questionNumber,
    partCode: question.partCode,
    attemptCount: question.attemptCount,
    accuracyPercent: question.accuracyPercent,
  }));

  const distractorRows = result.analytics.distractorHeat.map((item) => ({
    section: 'distractor_heat',
    paperId: item.paperId,
    questionNumber: item.questionNumber,
    correctAnswer: item.correctAnswer,
    wrongAnswerCount: item.wrongAnswerCount,
  }));

  return [...summaryRows, ...partRows, ...questionRows, ...distractorRows];
}

export default function ListeningTeacherClassesPage() {
  const [classes, setClasses] = useState<TeacherClassDto[]>([]);
  const [selectedClassId, setSelectedClassId] = useState('');
  const [days, setDays] = useState('30');
  const [classesStatus, setClassesStatus] = useState<AsyncStatus>('loading');
  const [analyticsStatus, setAnalyticsStatus] = useState<AsyncStatus>('loading');
  const [analytics, setAnalytics] = useState<ListeningClassAnalyticsDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [analyticsError, setAnalyticsError] = useState<string | null>(null);
  const [classReloadKey, setClassReloadKey] = useState(0);
  const [analyticsReloadKey, setAnalyticsReloadKey] = useState(0);
  const [newClassName, setNewClassName] = useState('');
  const [newClassDescription, setNewClassDescription] = useState('');
  const [memberUserId, setMemberUserId] = useState('');
  const [busyAction, setBusyAction] = useState<'create' | 'member' | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setClassesStatus('loading');
        setError(null);
        const result = await teacherClassApi.list();
        if (cancelled) return;
        setClasses(result);
        setSelectedClassId((current) => {
          if (current && result.some((item) => item.id === current)) return current;
          return result[0]?.id ?? '';
        });
        setClassesStatus('success');
      } catch (loadError) {
        if (!cancelled) {
          setError(errorMessage(loadError, 'Unable to load teacher classes.'));
          setClassesStatus('error');
        }
      }
    })();

    return () => { cancelled = true; };
  }, [classReloadKey]);

  useEffect(() => {
    if (!selectedClassId) {
      setAnalytics(null);
      setAnalyticsStatus('success');
      return;
    }

    let cancelled = false;
    (async () => {
      try {
        setAnalyticsStatus('loading');
        setAnalyticsError(null);
        setAnalytics(null);
        const result = await teacherClassApi.analytics(selectedClassId, Number.parseInt(days, 10));
        if (!cancelled) {
          setAnalytics(result);
          setAnalyticsStatus('success');
        }
      } catch (loadError) {
        if (!cancelled) {
          setAnalytics(null);
          setAnalyticsError(errorMessage(loadError, 'Unable to load class analytics.'));
          setAnalyticsStatus('error');
        }
      }
    })();

    return () => { cancelled = true; };
  }, [analyticsReloadKey, days, selectedClassId]);

  const selectedClass = useMemo(
    () => classes.find((item) => item.id === selectedClassId) ?? null,
    [classes, selectedClassId],
  );

  const classOptions = useMemo(
    () => classes.map((item) => ({ value: item.id, label: `${item.name} (${item.memberCount})` })),
    [classes],
  );

  async function handleCreateClass(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmedName = newClassName.trim();
    if (!trimmedName) return;

    try {
      setBusyAction('create');
      setError(null);
      const created = await teacherClassApi.create(trimmedName, newClassDescription.trim() || null);
      setNewClassName('');
      setNewClassDescription('');
      setSelectedClassId(created.id);
      setClassReloadKey((current) => current + 1);
    } catch (createError) {
      setError(errorMessage(createError, 'Unable to create class.'));
    } finally {
      setBusyAction(null);
    }
  }

  async function handleAddMember(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmedMember = memberUserId.trim();
    if (!selectedClassId || !trimmedMember) return;

    try {
      setBusyAction('member');
      setError(null);
      await teacherClassApi.addMember(selectedClassId, trimmedMember);
      setMemberUserId('');
      setClassReloadKey((current) => current + 1);
      setAnalyticsReloadKey((current) => current + 1);
    } catch (memberError) {
      setError(errorMessage(memberError, 'Unable to add learner to class.'));
    } finally {
      setBusyAction(null);
    }
  }

  function handleExportAnalytics() {
    if (!analytics) return;
    exportToCsv(
      analyticsExportRows(analytics),
      `listening-class-${filenameSegment(analytics.className)}-${analytics.analytics.days}d.csv`,
    );
  }

  const completedAttempts = analytics?.analytics.completedAttempts ?? 0;

  return (
    <LearnerDashboardShell pageTitle="Listening Class Analytics" subtitle="Owner-scoped class reporting for Listening V2." backHref="/listening">
      <div className="space-y-8 pb-24">
        <LearnerPageHero
          eyebrow="Listening · Teacher Classes"
          title="Class Listening analytics"
          description="Roster-scoped score signals, part accuracy, hardest questions, and distractor patterns for the classes you own."
          icon={Users}
          accent="blue"
          highlights={[
            { icon: Users, label: 'Classes', value: String(classes.length) },
            { icon: UserPlus, label: 'Members', value: String(selectedClass?.memberCount ?? analytics?.memberCount ?? 0) },
            { icon: Activity, label: 'Window', value: DAYS_OPTIONS.find((item) => item.value === days)?.label ?? `${days} days` },
          ]}
        />

        {error ? <InlineAlert variant="warning" title="Class workspace unavailable">{error}</InlineAlert> : null}

        <section className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_380px]">
          <Card className="space-y-5 p-5">
            <LearnerSurfaceSectionHeader
              eyebrow="Class Scope"
              title="Select analytics window"
              description="Only classes owned by your account are listed."
              action={classes.length > 0 ? (
                <div className="flex flex-wrap items-center gap-2">
                  <Button variant="outline" size="sm" onClick={() => setAnalyticsReloadKey((current) => current + 1)} disabled={analyticsStatus === 'loading'}>
                    <RefreshCw className="h-4 w-4" aria-hidden />
                    Refresh
                  </Button>
                  <Button variant="outline" size="sm" onClick={handleExportAnalytics} disabled={!analytics || analyticsStatus !== 'success'}>
                    <Download className="h-4 w-4" aria-hidden />
                    Export CSV
                  </Button>
                </div>
              ) : null}
            />

            {classesStatus === 'loading' ? (
              <div className="grid gap-3 sm:grid-cols-2">
                <Skeleton className="h-16 rounded-2xl" />
                <Skeleton className="h-16 rounded-2xl" />
              </div>
            ) : null}

            {classesStatus === 'success' && classes.length > 0 ? (
              <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_220px]">
                <Select
                  label="Class"
                  value={selectedClassId}
                  onChange={(event) => setSelectedClassId(event.target.value)}
                  options={classOptions}
                  aria-label="Select teacher class"
                />
                <Select
                  label="Date window"
                  value={days}
                  onChange={(event) => setDays(event.target.value)}
                  options={DAYS_OPTIONS}
                  aria-label="Select analytics date window"
                />
              </div>
            ) : null}

            {classesStatus === 'success' && classes.length === 0 ? (
              <LearnerSurfaceCard
                card={{
                  kind: 'navigation',
                  sourceType: 'frontend_navigation',
                  accent: 'amber',
                  eyebrow: 'No classes yet',
                  eyebrowIcon: Users,
                  title: 'Create your first teacher class',
                  description: 'Start a class, add learner user ids, then return here to review the Listening evidence for that roster.',
                }}
              />
            ) : null}
          </Card>

          <Card className="space-y-5 p-5">
            <LearnerSurfaceSectionHeader
              eyebrow="Roster"
              title="Class setup"
              description="Add a class or attach a learner to the selected roster."
            />
            <form className="space-y-3" onSubmit={handleCreateClass}>
              <Input
                label="Class name"
                value={newClassName}
                onChange={(event) => setNewClassName(event.target.value)}
                placeholder="Clinical Listening Cohort"
              />
              <Textarea
                label="Description"
                value={newClassDescription}
                onChange={(event) => setNewClassDescription(event.target.value)}
                placeholder="Optional cohort note"
                rows={3}
              />
              <Button type="submit" variant="primary" fullWidth loading={busyAction === 'create'} disabled={!newClassName.trim()}>
                <Plus className="h-4 w-4" aria-hidden />
                Create class
              </Button>
            </form>
            <form className="space-y-3 border-t border-border pt-4" onSubmit={handleAddMember}>
              <Input
                label="Learner user id"
                value={memberUserId}
                onChange={(event) => setMemberUserId(event.target.value)}
                placeholder="learner-user-id"
                disabled={!selectedClassId}
              />
              <Button type="submit" variant="outline" fullWidth loading={busyAction === 'member'} disabled={!selectedClassId || !memberUserId.trim()}>
                <UserPlus className="h-4 w-4" aria-hidden />
                Add learner
              </Button>
            </form>
          </Card>
        </section>

        {analyticsError ? <InlineAlert variant="warning" title="Analytics unavailable">{analyticsError}</InlineAlert> : null}

        {analyticsStatus === 'loading' && selectedClassId ? (
          <div className="grid gap-4 md:grid-cols-3">
            {[0, 1, 2].map((item) => <Skeleton key={item} className="h-32 rounded-2xl" />)}
          </div>
        ) : null}

        {analyticsStatus === 'success' && analytics && completedAttempts === 0 ? (
          <LearnerSurfaceCard
            card={{
              kind: 'evidence',
              sourceType: 'backend_summary',
              accent: 'slate',
              eyebrow: 'No attempts yet',
              eyebrowIcon: BarChart3,
              title: `${analytics.className} has no submitted Listening attempts in this window`,
              description: 'Class analytics will populate after roster members submit Listening V2 attempts in the selected date range.',
              metaItems: [
                { icon: Users, label: `${analytics.memberCount} member${analytics.memberCount === 1 ? '' : 's'}` },
                { icon: Activity, label: `${analytics.analytics.days} day window` },
              ],
            }}
          />
        ) : null}

        {analyticsStatus === 'success' && analytics && completedAttempts > 0 ? (
          <div className="space-y-8">
            <section className="grid grid-cols-2 gap-3 md:grid-cols-4">
              <StatCard icon={<Users />} label="Members" value={analytics.memberCount} hint={analytics.className} tone="info" />
              <StatCard icon={<ListChecks />} label="Completed" value={completedAttempts} hint={`${analytics.analytics.days} day window`} tone="default" />
              <StatCard icon={<TrendingUp />} label="Average score" value={analytics.analytics.averageScaledScore ?? '--'} hint="Scaled /500 estimate" tone="success" />
              <StatCard icon={<Target />} label="Likely passing" value={pct(analytics.analytics.percentLikelyPassing)} hint="Backend pass projection" tone="warning" />
            </section>

            <section className="grid gap-5 lg:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]">
              <Card className="p-5">
                <LearnerSurfaceSectionHeader
                  eyebrow="Part Accuracy"
                  title="A/B/C breakdown"
                  description={`${analytics.analytics.classPartAverages.length} scored part${analytics.analytics.classPartAverages.length === 1 ? '' : 's'} in this window.`}
                  className="mb-5"
                />
                <div className="space-y-4">
                  {analytics.analytics.classPartAverages.map((part) => {
                    const width = Math.max(0, Math.min(100, part.accuracyPercent));
                    return (
                      <div key={part.partCode} className="space-y-2">
                        <div className="flex items-center justify-between gap-3 text-sm">
                          <span className="font-bold text-navy">Part {part.partCode}</span>
                          <span className="font-semibold text-muted">{part.earned}/{part.max} · {pct(part.accuracyPercent)}</span>
                        </div>
                        <div className="h-2 overflow-hidden rounded-full bg-lavender/70">
                          <div className="h-full rounded-full bg-primary" style={{ width: `${width}%` }} />
                        </div>
                      </div>
                    );
                  })}
                </div>
              </Card>

              <Card className="p-5">
                <LearnerSurfaceSectionHeader
                  eyebrow="Question Evidence"
                  title="Hardest questions"
                  description="Questions with enough class attempts, sorted by lowest accuracy."
                  className="mb-5"
                />
                {analytics.analytics.hardestQuestions.length > 0 ? (
                  <div className="space-y-3">
                    {analytics.analytics.hardestQuestions.slice(0, 6).map((item) => (
                      <div key={`${item.paperId}-${item.questionNumber}`} className="rounded-2xl border border-border bg-background-light p-4">
                        <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                          <div>
                            <p className="text-sm font-bold text-navy">Question {item.questionNumber} · Part {item.partCode}</p>
                            <p className="mt-1 text-xs text-muted">{item.paperTitle}</p>
                          </div>
                          <div className="text-left sm:text-right">
                            <p className="text-sm font-black text-primary">{pct(item.accuracyPercent)}</p>
                            <p className="text-xs text-muted">{item.attemptCount} attempts</p>
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                ) : (
                  <div className="rounded-2xl border border-dashed border-border bg-background-light p-5 text-sm text-muted">
                    No question has reached the minimum class-attempt threshold yet.
                  </div>
                )}
              </Card>
            </section>

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Distractor Heat"
                title="Most-missed MCQ items"
                description="Wrong-answer text stays out of the teacher view; this panel shows miss volume and the authored correct answer."
                className="mb-5"
              />
              {analytics.analytics.distractorHeat.length > 0 ? (
                <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
                  {analytics.analytics.distractorHeat.map((item) => (
                    <Card key={`${item.paperId}-${item.questionNumber}`} className="p-5">
                      <div className="flex items-start justify-between gap-3">
                        <div>
                          <p className="text-xs font-black uppercase tracking-widest text-muted">Question {item.questionNumber}</p>
                          <h3 className="mt-2 text-lg font-bold text-navy">Correct answer {item.correctAnswer}</h3>
                        </div>
                        <div className="rounded-2xl bg-warning/10 px-3 py-2 text-right text-warning">
                          <p className="text-lg font-black">{item.wrongAnswerCount}</p>
                          <p className="text-[10px] font-bold uppercase tracking-widest">misses</p>
                        </div>
                      </div>
                    </Card>
                  ))}
                </div>
              ) : (
                <div className="rounded-2xl border border-dashed border-border bg-surface p-6 text-sm text-muted">
                  No distractor heatmap data is available for this class window.
                </div>
              )}
            </section>
          </div>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}