'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { ArrowLeft, Wand2 } from 'lucide-react';
import { adminGenerateGrammarAiDraft, adminListGrammarTopics } from '@/lib/api';
import { Card } from '@/components/ui/card';
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
    <div className="space-y-6 p-4 sm:p-6">
      <header className="flex items-center gap-2">
        <Link href="/admin/grammar" className="text-muted hover:text-navy" aria-label="Back">
          <ArrowLeft className="h-5 w-5" />
        </Link>
        <h1 className="flex items-center gap-2 text-2xl font-bold text-navy dark:text-white">
          <Wand2 className="h-5 w-5 text-primary" /> AI grammar draft
        </h1>
      </header>

      <Card className="space-y-4 p-5">
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
      </Card>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </div>
  );
}
