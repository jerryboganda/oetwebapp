'use client';

import { useState } from 'react';
import { Sparkles, Copy } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { requestWritingParaphrase } from '@/lib/writing/api';
import type { WritingParaphraseResultDto } from '@/lib/writing/types';

export default function WritingParaphraseToolPage() {
  const [input, setInput] = useState('');
  const [result, setResult] = useState<WritingParaphraseResultDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState<string | null>(null);

  const onSubmit = async () => {
    if (input.trim().length === 0) return;
    setLoading(true);
    setError(null);
    setResult(null);
    try {
      const r = await requestWritingParaphrase({ text: input.trim() });
      setResult(r);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Paraphrase failed.');
    } finally {
      setLoading(false);
    }
  };

  const onCopy = async (text: string, key: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(key);
      window.setTimeout(() => setCopied(null), 1500);
    } catch {
      /* swallow */
    }
  };

  return (
    <LearnerDashboardShell pageTitle="Paraphrase tool">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Tools"
          icon={Sparkles}
          accent="amber"
          title="Paraphrase a sentence three ways"
          description="Get three alternative phrasings at different formality levels. Useful when a sentence sounds wrong but you can't say why."
          highlights={[]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <label className="flex flex-col gap-2 rounded-2xl border border-border bg-surface p-4 shadow-sm">
          <span className="text-sm font-bold text-navy">Sentence to paraphrase</span>
          <textarea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            rows={3}
            maxLength={500}
            placeholder="e.g. The patient was admitted with chest pain and was discharged after two days."
            className="min-h-24 rounded-lg border border-border bg-background p-3 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            aria-describedby="paraphrase-helper"
          />
          <span id="paraphrase-helper" className="text-xs text-muted">Max 500 characters.</span>
          <div className="flex justify-end">
            <Button onClick={() => void onSubmit()} loading={loading} disabled={input.trim().length === 0}>
              Get paraphrases
            </Button>
          </div>
        </label>

        {result ? (
          <section aria-labelledby="paraphrase-results" className="space-y-3">
            <h2 id="paraphrase-results" className="text-base font-bold text-navy">Alternatives</h2>
            <ul className="space-y-2">
              {result.alternatives.map((alt, idx) => (
                <li key={idx}>
                  <Card padding="md">
                    <CardContent>
                      <div className="flex items-center justify-between gap-2">
                        <Badge variant="info" size="sm">{alt.formality}</Badge>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => void onCopy(alt.text, `alt-${idx}`)}
                          aria-label={`Copy ${alt.formality} alternative`}
                        >
                          <Copy className="h-3 w-3" aria-hidden="true" />
                          {copied === `alt-${idx}` ? 'Copied!' : 'Copy'}
                        </Button>
                      </div>
                      <p className="mt-2 text-sm text-navy">{alt.text}</p>
                    </CardContent>
                  </Card>
                </li>
              ))}
            </ul>
          </section>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
