'use client';

import { useEffect, useState } from 'react';
import {
  Wand2,
  Loader2,
  CheckCircle2,
  AlertCircle,
  Copy,
  RefreshCw,
  Clock3,
  FileJson2,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRoutePanelFooter,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { MotionItem } from '@/components/ui/motion-primitives';
import { fetchContentGenerationJob, fetchContentGenerationJobs, queueContentGeneration } from '@/lib/api';

type GenerationRequest = {
  contentType: 'grammar_lesson' | 'strategy_guide' | 'pronunciation_drill' | 'vocabulary_term' | 'mock_question';
  examTypeCode: string;
  subtestCode: string;
  difficulty: string;
  topic: string;
  additionalContext: string;
};

type GenerationResult = {
  id: string;
  contentType: string;
  status: 'pending' | 'processing' | 'done' | 'error';
  title: string;
  summary: string;
  previewJson: string;
  error?: string;
  createdAt?: string;
};

type ContentGenerationJob = {
  jobId: string;
  requestedBy?: string;
  examTypeCode: string;
  subtestCode: string;
  taskTypeId?: string | null;
  professionId?: string | null;
  difficulty: string;
  requestedCount: number;
  generatedCount: number;
  state: string;
  errorMessage?: string | null;
  promptConfigJson?: string | null;
  generatedContentIdsJson?: string | null;
  createdAt: string;
  completedAt?: string | null;
};

const CONTENT_TYPES = [
  { value: 'grammar_lesson', label: 'Grammar Lesson' },
  { value: 'strategy_guide', label: 'Strategy Guide' },
  { value: 'pronunciation_drill', label: 'Pronunciation Drill' },
  { value: 'vocabulary_term', label: 'Vocabulary Term' },
  { value: 'mock_question', label: 'Mock Question' },
];

const EXAM_OPTIONS = [
  { value: 'oet', label: 'OET' },
  { value: 'ielts', label: 'IELTS' },
  { value: 'pte', label: 'PTE' },
];

const SUBTEST_OPTIONS = [
  { value: 'writing', label: 'Writing' },
  { value: 'speaking', label: 'Speaking' },
  { value: 'reading', label: 'Reading' },
  { value: 'listening', label: 'Listening' },
  { value: 'general', label: 'General' },
];

const DIFFICULTY_OPTIONS = [
  { value: 'beginner', label: 'Beginner' },
  { value: 'intermediate', label: 'Intermediate' },
  { value: 'advanced', label: 'Advanced' },
];

function prettyContentType(value: string | null | undefined) {
  if (!value) return 'Content Job';
  const match = CONTENT_TYPES.find((item) => item.value === value);
  if (match) return match.label;
  return value
    .split('_')
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

function mapJobState(state: string): GenerationResult['status'] {
  switch (state.toLowerCase()) {
    case 'completed':
      return 'done';
    case 'failed':
      return 'error';
    case 'generating':
      return 'processing';
    default:
      return 'pending';
  }
}

function mapJobToResult(job: ContentGenerationJob): GenerationResult {
  return {
    id: job.jobId,
    contentType: job.taskTypeId ?? 'content_job',
    status: mapJobState(job.state),
    title: `${prettyContentType(job.taskTypeId)} for ${job.examTypeCode.toUpperCase()} ${job.subtestCode}`,
    summary: `${job.difficulty} · ${job.generatedCount}/${job.requestedCount} generated`,
    previewJson: JSON.stringify(job, null, 2),
    error: job.errorMessage ?? undefined,
    createdAt: job.createdAt,
  };
}

function buildCustomInstructions(topic: string, additionalContext: string) {
  return [topic.trim(), additionalContext.trim()].filter(Boolean).join('\n\n');
}

export default function AdminContentGenerationPage() {
  const [form, setForm] = useState<GenerationRequest>({
    contentType: 'grammar_lesson',
    examTypeCode: 'oet',
    subtestCode: 'writing',
    difficulty: 'intermediate',
    topic: '',
    additionalContext: '',
  });
  const [submitting, setSubmitting] = useState(false);
  const [results, setResults] = useState<GenerationResult[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState<string | null>(null);
  const [loadingJobs, setLoadingJobs] = useState(false);
  const [jobsTotal, setJobsTotal] = useState(0);

  async function loadJobs() {
    setLoadingJobs(true);
    try {
      const response = (await fetchContentGenerationJobs(1, 12)) as {
        total?: number;
        items?: ContentGenerationJob[];
      };
      const jobs = Array.isArray(response?.items) ? response.items : [];
      setResults(jobs.map(mapJobToResult));
      setJobsTotal(typeof response?.total === 'number' ? response.total : jobs.length);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load generation jobs.');
    } finally {
      setLoadingJobs(false);
    }
  }

  useEffect(() => {
    void loadJobs();
  }, []);

  async function handleGenerate(e: React.FormEvent) {
    e.preventDefault();
    if (!form.topic.trim() || submitting) return;
    setSubmitting(true);
    setError(null);

    try {
      const request = {
        examTypeCode: form.examTypeCode,
        subtestCode: form.subtestCode,
        taskTypeId: form.contentType,
        difficulty: form.difficulty,
        count: 1,
        customInstructions: buildCustomInstructions(form.topic, form.additionalContext),
      };

      const response = (await queueContentGeneration(request)) as {
        jobId?: string;
        state?: string;
        examTypeCode?: string;
        subtestCode?: string;
        requestedCount?: number;
        createdAt?: string;
      };

      const queued: GenerationResult = {
        id: response.jobId ?? `gen-${Date.now()}`,
        contentType: form.contentType,
        status: mapJobState(response.state ?? 'pending'),
        title: `Queued: ${form.topic}`,
        summary: `${form.examTypeCode.toUpperCase()} · ${form.subtestCode} · ${form.difficulty} · 1 requested`,
        previewJson: JSON.stringify({ request, response }, null, 2),
        createdAt: response.createdAt,
      };

      setResults((prev) => [queued, ...prev.filter((item) => item.id !== queued.id)]);
      await loadJobs();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Content generation failed.');
    } finally {
      setSubmitting(false);
    }
  }

  async function inspectJob(jobId: string) {
    setCopied(null);
    try {
      const detail = (await fetchContentGenerationJob(jobId)) as ContentGenerationJob;
      const mapped = mapJobToResult(detail);
      setResults((prev) => prev.map((result) => (result.id === jobId ? mapped : result)));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load job details.');
    }
  }

  function copyToClipboard(text: string, id: string) {
    void navigator.clipboard.writeText(text).then(() => {
      setCopied(id);
      setTimeout(() => setCopied(null), 2000);
    });
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="AI content generation">
      <AdminRouteHero
        eyebrow="Content · AI"
        icon={Wand2}
        accent="purple"
        title="AI Content Generation"
        description="Queue AI-generated learning content for the OET Prep platform. All generations route through the grounded AI gateway."
        highlights={[
          { label: 'Queued jobs', value: jobsTotal.toLocaleString() },
          { label: 'Exam', value: form.examTypeCode.toUpperCase() },
          { label: 'Subtest', value: form.subtestCode },
        ]}
      />

      {error ? <InlineAlert variant="warning">{error}</InlineAlert> : null}

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <AdminRoutePanel
          eyebrow="Request"
          title="Generation parameters"
          description="Tune the prompt. Topic is required; additional context is optional."
        >
          <form onSubmit={handleGenerate} className="space-y-4">
            <Select
              label="Content type"
              value={form.contentType}
              onChange={(e) =>
                setForm((p) => ({ ...p, contentType: e.target.value as GenerationRequest['contentType'] }))
              }
              options={CONTENT_TYPES}
            />

            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <Select
                label="Exam"
                value={form.examTypeCode}
                onChange={(e) => setForm((p) => ({ ...p, examTypeCode: e.target.value }))}
                options={EXAM_OPTIONS}
              />
              <Select
                label="Subtest"
                value={form.subtestCode}
                onChange={(e) => setForm((p) => ({ ...p, subtestCode: e.target.value }))}
                options={SUBTEST_OPTIONS}
              />
            </div>

            <Select
              label="Difficulty"
              value={form.difficulty}
              onChange={(e) => setForm((p) => ({ ...p, difficulty: e.target.value }))}
              options={DIFFICULTY_OPTIONS}
            />

            <Input
              label="Topic / prompt"
              value={form.topic}
              onChange={(e) => setForm((p) => ({ ...p, topic: e.target.value }))}
              required
              placeholder="e.g. Passive voice in clinical reports"
            />

            <Textarea
              label="Additional context"
              rows={3}
              value={form.additionalContext}
              onChange={(e) => setForm((p) => ({ ...p, additionalContext: e.target.value }))}
              placeholder="Any additional requirements or context…"
            />

            <Button type="submit" fullWidth disabled={!form.topic.trim()} loading={submitting}>
              <Wand2 className="h-4 w-4" /> Generate Content
            </Button>
          </form>
        </AdminRoutePanel>

        <AdminRoutePanel
          eyebrow="History"
          title="Generation history"
          description={`${jobsTotal.toLocaleString()} queued jobs in the live admin catalogue.`}
          actions={
            <Button variant="outline" size="sm" onClick={() => void loadJobs()} disabled={loadingJobs}>
              <RefreshCw className={`h-3.5 w-3.5 ${loadingJobs ? 'animate-spin' : ''}`} /> Refresh
            </Button>
          }
        >
          {results.length === 0 ? (
            <EmptyState
              icon={<Wand2 className="h-6 w-6" aria-hidden />}
              title="No generation jobs yet"
              description="Queued generation jobs will appear here with preview JSON and refresh controls."
            />
          ) : (
            <div className="space-y-3">
              {results.map((result) => (
                <MotionItem
                  key={result.id}
                  className="rounded-xl border border-border bg-surface p-4 shadow-sm"
                >
                  <div className="mb-2 flex items-start gap-3">
                    <span className="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-background-light">
                      {result.status === 'processing' && <Loader2 className="h-4 w-4 animate-spin text-primary" />}
                      {result.status === 'done' && <CheckCircle2 className="h-4 w-4 text-success" />}
                      {result.status === 'error' && <AlertCircle className="h-4 w-4 text-danger" />}
                      {result.status === 'pending' && <Clock3 className="h-4 w-4 text-muted" />}
                    </span>
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm font-semibold text-navy">{result.title}</p>
                      <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-muted">
                        <Badge variant="muted">{prettyContentType(result.contentType)}</Badge>
                        <span>{result.summary}</span>
                        {result.createdAt && (
                          <span className="inline-flex items-center gap-1">
                            <Clock3 className="h-3 w-3" /> {new Date(result.createdAt).toLocaleString()}
                          </span>
                        )}
                      </div>
                      {result.error ? <p className="mt-1 text-xs text-danger">{result.error}</p> : null}
                    </div>
                    <div className="flex shrink-0 items-center gap-1">
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        onClick={() => void inspectJob(result.id)}
                        aria-label="Refresh job details"
                      >
                        <FileJson2 className="h-4 w-4" />
                      </Button>
                      {result.previewJson && (
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          onClick={() => copyToClipboard(result.previewJson, result.id)}
                          aria-label="Copy JSON"
                        >
                          {copied === result.id ? (
                            <CheckCircle2 className="h-4 w-4 text-success" />
                          ) : (
                            <Copy className="h-4 w-4" />
                          )}
                        </Button>
                      )}
                    </div>
                  </div>
                  {result.previewJson ? (
                    <pre className="max-h-48 overflow-x-auto rounded-lg bg-background-light p-3 text-xs text-navy">
                      {result.previewJson}
                    </pre>
                  ) : null}
                </MotionItem>
              ))}
            </div>
          )}
          <AdminRoutePanelFooter source="AI gateway" />
        </AdminRoutePanel>
      </div>
    </AdminRouteWorkspace>
  );
}
