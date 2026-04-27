'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { ArrowLeft, Wand2 } from 'lucide-react';
import { adminGenerateGrammarAiDraft, adminListGrammarTopics } from '@/lib/api';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import type { AdminGrammarTopic } from '@/lib/grammar/types';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function GrammarAiDraftPage() {
  const router = useRouter();
  const [topics, setTopics] = useState<AdminGrammarTopic[]>([]);
  const [examType, setExamType] = useState('oet');
  const [topicSlug, setTopicSlug] = useState('');
  const [level, setLevel] = useState('intermediate');
  const [count, setCount] = useState(6);
  const [prompt, setPrompt] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  useEffect(() => {
    (async () => {
      try {
        const t = (await adminListGrammarTopics({ examTypeCode: examType })) as AdminGrammarTopic[];
        setTopics(t || []);
      } catch {/* noop */}
    })();
  }, [examType]);

  const onGenerate = useCallback(async () => {
    if (!prompt.trim()) {
      setToast({ variant: 'error', message: 'Provide a prompt.' });
      return;
    }
    setSubmitting(true);
    try {
      const res = (await adminGenerateGrammarAiDraft({
        examTypeCode: examType,
        topicSlug: topicSlug || undefined,
        prompt,
        level,
        targetExerciseCount: count,
      })) as { lessonId: string; title: string; contentBlockCount: number; exerciseCount: number; warning?: string | null };
      setToast({
        variant: res.warning ? 'error' : 'success',
        message: res.warning
          ? `Draft created with warning: ${res.warning}`
          : `Draft "${res.title}" created (${res.contentBlockCount} blocks, ${res.exerciseCount} exercises).`,
      });
      setTimeout(() => router.push(`/admin/grammar/lessons/${encodeURIComponent(res.lessonId)}`), 700);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Draft generation failed.' });
    } finally {
      setSubmitting(false);
    }
  }, [examType, topicSlug, level, count, prompt, router]);

  return (
    <AdminRouteWorkspace role="main" aria-label="AI grammar draft">
      <Link href="/admin/grammar" className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy" aria-label="Back">
        <ArrowLeft className="h-4 w-4" /> Back to Grammar CMS
      </Link>

      <AdminRouteHero
        eyebrow="CMS"
        icon={Wand2}
        accent="navy"
        title="AI grammar draft"
        description="Generate a draft lesson via the grounded AI gateway. Drafts are always stored as draft and must be reviewed before publishing."
      />

      <AdminRoutePanel>
        <p className="text-sm leading-6 text-muted">
          This generates a draft lesson via the grounded AI gateway. Drafts are always stored as <strong>draft</strong> — review and edit before publishing. The gateway physically refuses ungrounded prompts.
        </p>

        <div className="grid gap-3 sm:grid-cols-3">
          <Select
            label="Exam"
            value={examType}
            onChange={(e) => setExamType(e.target.value)}
            options={[
              { value: 'oet', label: 'OET' },
              { value: 'ielts', label: 'IELTS' },
              { value: 'pte', label: 'PTE' },
            ]}
          />
          <Select
            label="Topic"
            value={topicSlug}
            onChange={(e) => setTopicSlug(e.target.value)}
            options={[{ value: '', label: 'No topic (attach later)' }, ...topics.map((t) => ({ value: t.slug, label: t.name }))]}
          />
          <Select
            label="Level"
            value={level}
            onChange={(e) => setLevel(e.target.value)}
            options={[
              { value: 'beginner', label: 'Beginner' },
              { value: 'intermediate', label: 'Intermediate' },
              { value: 'advanced', label: 'Advanced' },
            ]}
          />
          <Input label="Target exercise count" type="number" value={String(count)} onChange={(e) => setCount(Math.max(3, Math.min(12, Number(e.target.value) || 6)))} />
        </div>

        <Textarea
          label="Admin prompt"
          value={prompt}
          onChange={(e) => setPrompt(e.target.value)}
          rows={6}
          placeholder="Describe the grammar pattern you want to drill, the medical context, the typical OET candidate mistake, and the level of formality expected. The grounded AI gateway will produce a structured draft."
        />

        <div className="flex justify-end">
          <Button disabled={submitting} onClick={onGenerate}>
            {submitting ? 'Generating…' : 'Generate draft'}
          </Button>
        </div>
      </AdminRoutePanel>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </AdminRouteWorkspace>
  );
}
