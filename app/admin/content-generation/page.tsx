'use client';

import { useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Wand2, Loader2, CheckCircle2, AlertCircle, Copy, RefreshCw, Clock3, FileJson2 } from 'lucide-react';
import { AdminDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
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
      const response = await fetchContentGenerationJobs(1, 12) as { total?: number; items?: ContentGenerationJob[] };
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

      const response = await queueContentGeneration(request) as {
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
      const detail = await fetchContentGenerationJob(jobId) as ContentGenerationJob;
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
    <AdminDashboardShell>
      <div className="max-w-5xl mx-auto">
        <div className="flex items-center gap-3 mb-6">
          <div className="p-2.5 bg-indigo-100 dark:bg-indigo-900/30 rounded-xl">
            <Wand2 className="w-6 h-6 text-indigo-600 dark:text-indigo-400" />
          </div>
          <div>
            <h1 className="text-2xl font-bold text-gray-900 dark:text-white">AI Content Generation</h1>
            <p className="text-sm text-gray-500">Generate learning content using AI for the OET platform</p>
          </div>
        </div>

        {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {/* Form */}
          <div className="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700 p-6">
            <h2 className="font-semibold text-gray-900 dark:text-white mb-4">Generation Parameters</h2>
            <form onSubmit={handleGenerate} className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">Content Type</label>
                <select
                  value={form.contentType}
                  onChange={e => setForm(p => ({ ...p, contentType: e.target.value as GenerationRequest['contentType'] }))}
                  className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-xl bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-400"
                >
                  {CONTENT_TYPES.map(ct => <option key={ct.value} value={ct.value}>{ct.label}</option>)}
                </select>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">Exam</label>
                  <select
                    value={form.examTypeCode}
                    onChange={e => setForm(p => ({ ...p, examTypeCode: e.target.value }))}
                    className="w-full px-3 py-2 border border-gray-200 dark:border-gray-700 rounded-xl bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200 text-sm"
                  >
                    <option value="oet">OET</option>
                    <option value="ielts">IELTS</option>
                    <option value="pte">PTE</option>
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">Subtest</label>
                  <select
                    value={form.subtestCode}
                    onChange={e => setForm(p => ({ ...p, subtestCode: e.target.value }))}
                    className="w-full px-3 py-2 border border-gray-200 dark:border-gray-700 rounded-xl bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200 text-sm"
                  >
                    <option value="writing">Writing</option>
                    <option value="speaking">Speaking</option>
                    <option value="reading">Reading</option>
                    <option value="listening">Listening</option>
                    <option value="general">General</option>
                  </select>
                </div>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">Difficulty</label>
                <select
                  value={form.difficulty}
                  onChange={e => setForm(p => ({ ...p, difficulty: e.target.value }))}
                  className="w-full px-3 py-2 border border-gray-200 dark:border-gray-700 rounded-xl bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200 text-sm"
                >
                  <option value="beginner">Beginner</option>
                  <option value="intermediate">Intermediate</option>
                  <option value="advanced">Advanced</option>
                </select>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">Topic / Prompt</label>
                <input
                  type="text"
                  value={form.topic}
                  onChange={e => setForm(p => ({ ...p, topic: e.target.value }))}
                  required
                  placeholder="e.g. Passive voice in clinical reports"
                  className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-xl bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-400"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">Additional Context</label>
                <textarea
                  value={form.additionalContext}
                  onChange={e => setForm(p => ({ ...p, additionalContext: e.target.value }))}
                  rows={3}
                  placeholder="Any additional requirements or context..."
                  className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-xl bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200 text-sm resize-none focus:outline-none focus:ring-2 focus:ring-indigo-400"
                />
              </div>

              <button
                type="submit"
                disabled={submitting || !form.topic.trim()}
                className="w-full flex items-center justify-center gap-2 py-3 bg-indigo-600 hover:bg-indigo-700 text-white rounded-xl font-semibold text-sm disabled:opacity-50 transition-colors"
              >
                {submitting ? (
                  <><Loader2 className="w-4 h-4 animate-spin" /> Generating...</>
                ) : (
                  <><Wand2 className="w-4 h-4" /> Generate Content</>
                )}
              </button>
            </form>
          </div>

          {/* Results */}
          <div>
            <div className="mb-4 flex items-center justify-between gap-3">
              <div>
                <h2 className="font-semibold text-gray-900 dark:text-white">Generation History</h2>
                <p className="text-xs text-gray-500">{jobsTotal.toLocaleString()} queued jobs in the live admin catalogue.</p>
              </div>
              <button
                type="button"
                onClick={() => void loadJobs()}
                className="inline-flex items-center gap-2 rounded-xl border border-gray-200 bg-white px-3 py-2 text-xs font-semibold text-gray-700 transition-colors hover:border-indigo-300 hover:text-indigo-700 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-200"
              >
                <RefreshCw className={`h-3.5 w-3.5 ${loadingJobs ? 'animate-spin' : ''}`} />
                Refresh jobs
              </button>
            </div>
            {results.length === 0 ? (
              <div className="bg-gray-50 dark:bg-gray-900 rounded-2xl border border-dashed border-gray-300 dark:border-gray-700 p-8 text-center text-gray-400">
                <Wand2 className="w-8 h-8 mx-auto mb-2 opacity-40" />
                <p className="text-sm">Queued generation jobs will appear here</p>
              </div>
            ) : (
              <div className="space-y-3">
                {results.map((result) => (
                  <MotionItem
                    key={result.id}
                    className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-4"
                  >
                    <div className="flex items-start gap-3 mb-2">
                      <div className="flex-shrink-0 mt-0.5">
                        {result.status === 'processing' && <Loader2 className="w-4 h-4 text-indigo-500 animate-spin" />}
                        {result.status === 'done' && <CheckCircle2 className="w-4 h-4 text-green-500" />}
                        {result.status === 'error' && <AlertCircle className="w-4 h-4 text-red-500" />}
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="font-medium text-sm text-gray-900 dark:text-white truncate">{result.title}</div>
                        <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-gray-400">
                          <span className="rounded-full bg-gray-100 px-2 py-0.5 font-semibold text-gray-600 dark:bg-gray-700 dark:text-gray-300">
                            {prettyContentType(result.contentType)}
                          </span>
                          <span>{result.summary}</span>
                          {result.createdAt && (
                            <span className="inline-flex items-center gap-1"><Clock3 className="h-3 w-3" /> {new Date(result.createdAt).toLocaleString()}</span>
                          )}
                        </div>
                        {result.error && <div className="text-xs text-red-500 mt-1">{result.error}</div>}
                      </div>
                      <div className="flex flex-shrink-0 items-center gap-1.5">
                        <button
                          type="button"
                          onClick={() => void inspectJob(result.id)}
                          className="rounded-lg p-1.5 text-gray-400 transition-colors hover:bg-gray-100 hover:text-indigo-600 dark:hover:bg-gray-700"
                          title="Refresh job details"
                        >
                          <FileJson2 className="w-4 h-4" />
                        </button>
                        {result.previewJson && (
                          <button
                            type="button"
                            onClick={() => copyToClipboard(result.previewJson, result.id)}
                            className="rounded-lg p-1.5 text-gray-400 transition-colors hover:bg-gray-100 hover:text-indigo-600 dark:hover:bg-gray-700"
                            title="Copy JSON"
                          >
                            {copied === result.id ? <CheckCircle2 className="w-4 h-4 text-green-500" /> : <Copy className="w-4 h-4" />}
                          </button>
                        )}
                      </div>
                    </div>
                    {result.previewJson && (
                      <pre className="text-xs bg-gray-50 dark:bg-gray-900 rounded-lg p-3 overflow-x-auto max-h-48 text-gray-700 dark:text-gray-300">
                        {result.previewJson}
                      </pre>
                    )}
                  </MotionItem>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </AdminDashboardShell>
  );
}
