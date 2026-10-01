'use client';

import { useState } from 'react';
import { Sparkles, Check, X, ChevronDown, ChevronUp, BookOpen, BarChart3 } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState } from '@/components/ui/empty-error';
import { LearnerPageHero } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

/* ── types ─────────────────────────────────────── */
interface Suggestion {
  id: string;
  type: 'grammar' | 'vocabulary' | 'tone' | 'conciseness' | 'structure';
  originalText: string;
  suggestedText: string;
  explanation: string;
  confidence: number;
  offsetStart: number;
  offsetEnd: number;
}

interface CoachCheckResponse {
  sessionId: string;
  suggestions: Suggestion[];
  stats: {
    active: number;
    totalGenerated: number;
    accepted: number;
    dismissed: number;
    pending: number;
    acceptanceRate: number;
  };
}

interface CoachStats {
  active: number; totalGenerated: number; accepted: number; dismissed: number;
  pending: number; acceptanceRate: number;
  suggestionBreakdown?: Record<string, number>;
}

/* ── api helper ───────────────────────────────── */
const apiRequest = apiClient.request;

/* ── category config ─────────────────────────── */
const TYPE_CONFIG: Record<string, { label: string; color: string; bg: string }> = {
  grammar:      { label: 'Grammar',      color: 'text-danger-strong',  bg: 'bg-danger/10 border-danger/30' },
  vocabulary:   { label: 'Better Phrase', color: 'text-info',    bg: 'bg-info/10 border-info/30' },
  tone:         { label: 'Tone',         color: 'text-primary', bg: 'bg-primary/10 border-primary/30' },
  conciseness:  { label: 'Conciseness',  color: 'text-warning-strong', bg: 'bg-warning/10 border-warning/30' },
  structure:    { label: 'Structure',    color: 'text-success-strong', bg: 'bg-success/10 border-success/30' },
};

export default function PhraseSuggestionsPage() {
  /* state */
  const [attemptId, setAttemptId] = useState('');
  const [inputText, setInputText] = useState('');
  const [suggestions, setSuggestions] = useState<Suggestion[]>([]);
  const [stats, setStats] = useState<CoachStats | null>(null);
  const [loading, setLoading] = useState(false);
  const [resolving, setResolving] = useState<string | null>(null);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [filter, setFilter] = useState<string | null>(null);

  /* run coach check */
  const runCheck = async () => {
    if (!attemptId.trim()) return;
    setLoading(true);
    analytics.track('phrase_suggestions_check', { attemptId });
    try {
      const data = await apiRequest<CoachCheckResponse>(`/v1/writing/attempts/${attemptId}/coach-check`, {
        method: 'POST', body: JSON.stringify({ text: inputText || undefined }),
      });
      setSuggestions(data.suggestions);
      setStats(data.stats);
    } catch { /* */ }
    setLoading(false);
  };

  /* resolve (accept/dismiss) */
  const resolve = async (suggestionId: string, resolution: 'accepted' | 'dismissed') => {
    setResolving(suggestionId);
    try {
      await apiRequest(`/v1/writing/coach-suggestions/${suggestionId}/resolve`, {
        method: 'POST', body: JSON.stringify({ Resolution: resolution }),
      });
      setSuggestions(prev => prev.filter(s => s.id !== suggestionId));
      if (stats) {
        setStats({
          ...stats,
          [resolution]: stats[resolution as keyof CoachStats] as number + 1,
          pending: Math.max(0, stats.pending - 1),
          acceptanceRate: resolution === 'accepted'
            ? ((stats.accepted + 1) / (stats.accepted + stats.dismissed + 1)) * 100
            : (stats.accepted / (stats.accepted + stats.dismissed + 1)) * 100,
        });
      }
      analytics.track('phrase_suggestion_resolved', { resolution });
    } catch { /* */ }
    setResolving(null);
  };

  /* filtered suggestions */
  const filtered = filter ? suggestions.filter(s => s.type === filter) : suggestions;

  /* ── render ────────────────────────────────── */
  const fieldClassName = 'w-full min-w-0 rounded-control border border-border bg-background-light px-3 text-sm text-navy focus:outline-none focus-visible:ring-2 focus-visible:ring-primary';
  const pillClassName = (selected: boolean, selectedTone: string) => `pressable min-h-11 rounded-full border px-3 py-1.5 text-xs font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ${selected ? selectedTone : 'hover-primary border-border bg-background-light text-navy hover:border-primary/50'}`;

  return (
    <>
      <LearnerPageHero
        icon={Sparkles}
        title="AI Phrase Coach"
        description="Get intelligent suggestions to upgrade your vocabulary, fix grammar, and improve writing tone"
      />

      {/* ── Input Section ─────────────────── */}
      <Card padding="lg">
        <label htmlFor="attempt-id" className="mb-2 block text-sm font-semibold text-navy">Attempt ID</label>
        <div className="mb-4 flex gap-2">
          <input
            id="attempt-id"
            type="text"
            value={attemptId}
            onChange={e => setAttemptId(e.target.value)}
            placeholder="Enter your writing attempt ID"
            className={`${fieldClassName} min-h-11 flex-1 py-2`}
          />
          <Button onClick={runCheck} loading={loading} disabled={!attemptId.trim()} className="shrink-0">
            {loading ? null : <Sparkles className="h-4 w-4" aria-hidden="true" />}
            {loading ? 'Checking…' : 'Check'}
          </Button>
        </div>

        <label htmlFor="input-text" className="mb-2 block text-sm font-semibold text-navy">
          Paste text <span className="font-normal text-muted">(optional, overrides stored attempt)</span>
        </label>
        <textarea
          id="input-text"
          value={inputText}
          onChange={e => setInputText(e.target.value)}
          rows={4}
          placeholder="Optionally paste writing text to check for phrase suggestions…"
          className={`${fieldClassName} resize-none py-3`}
        />
      </Card>

      {/* ── Stats Row ─────────────────────── */}
      {stats && (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          {[
            { label: 'Suggestions', value: stats.totalGenerated, suffix: '', icon: Sparkles },
            { label: 'Accepted', value: stats.accepted, suffix: '', icon: Check },
            { label: 'Dismissed', value: stats.dismissed, suffix: '', icon: X },
            { label: 'Acceptance', value: Math.round(stats.acceptanceRate), suffix: '%', icon: BarChart3 },
          ].map((s, index) => (
            <MotionItem key={s.label} delayIndex={Math.min(index, 5)} className="min-w-0">
              <Card padding="sm" className="h-full text-center">
                <s.icon className="mx-auto mb-1 h-4 w-4 text-muted" aria-hidden="true" />
                <p className="text-lg font-bold text-navy"><CountUp value={s.value} suffix={s.suffix} /></p>
                <p className="tile-label text-muted">{s.label}</p>
              </Card>
            </MotionItem>
          ))}
        </div>
      )}

      {/* ── Filter Pills ──────────────────── */}
      {suggestions.length > 0 && (
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            aria-pressed={!filter}
            onClick={() => setFilter(null)}
            className={pillClassName(!filter, 'border-primary bg-primary text-white dark:bg-primary-700')}
          >
            All ({suggestions.length})
          </button>
          {Object.entries(TYPE_CONFIG).map(([key, cfg]) => {
            const count = suggestions.filter(s => s.type === key).length;
            if (count === 0) return null;
            return (
              <button
                key={key}
                type="button"
                aria-pressed={filter === key}
                onClick={() => setFilter(filter === key ? null : key)}
                className={pillClassName(filter === key, `${cfg.bg} ${cfg.color} border-current`)}
              >
                {cfg.label} ({count})
              </button>
            );
          })}
        </div>
      )}

      {/* ── Suggestions List ──────────────── */}
      {filtered.length > 0 && (
        <MotionSection className="space-y-3">
          {filtered.map((s, index) => {
            const cfg = TYPE_CONFIG[s.type] || TYPE_CONFIG.vocabulary;
            const expanded = expandedId === s.id;
            return (
              <MotionItem key={s.id} delayIndex={Math.min(index, 5)}>
                <Card padding="md" className={expanded ? cfg.bg : undefined}>
                  <div className="mb-3 flex items-start justify-between gap-2">
                    <Badge variant="outline" className={`${cfg.color} shrink-0 text-3xs`}>{cfg.label}</Badge>
                    <div className="flex items-center gap-1">
                      <span className="text-3xs tabular-nums text-muted">{Math.round(s.confidence * 100)}%</span>
                      <button
                        type="button"
                        onClick={() => setExpandedId(expanded ? null : s.id)}
                        className="-m-2.5 inline-flex h-11 w-11 items-center justify-center rounded-control text-muted hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                        aria-expanded={expanded}
                        aria-label={expanded ? `Hide explanation for ${cfg.label} suggestion` : `Show explanation for ${cfg.label} suggestion`}
                      >
                        {expanded ? <ChevronUp className="h-3.5 w-3.5" aria-hidden="true" /> : <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />}
                      </button>
                    </div>
                  </div>

                  {/* original → suggested */}
                  <div className="mb-3 space-y-2">
                    <div className="flex items-start gap-2">
                      <span className="tile-label mt-1 w-12 shrink-0 text-muted">Before</span>
                      <p className="min-w-0 text-sm text-muted line-through">{s.originalText}</p>
                    </div>
                    <div className="flex items-start gap-2">
                      <span className="tile-label mt-1 w-12 shrink-0 text-primary">After</span>
                      <p className="min-w-0 text-sm font-medium text-navy">{s.suggestedText}</p>
                    </div>
                  </div>

                  {/* explanation (expanded) */}
                  {expanded && (
                    <div className="mb-3 rounded-lg bg-background-light p-3">
                      <p className="flex items-start gap-2 text-xs text-muted">
                        <BookOpen className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                        {s.explanation}
                      </p>
                    </div>
                  )}

                  {/* action buttons */}
                  <div className="flex gap-2">
                    <Button
                      size="sm"
                      variant="primary"
                      disabled={resolving === s.id}
                      onClick={() => resolve(s.id, 'accepted')}
                      className="flex-1"
                    >
                      <Check className="h-3.5 w-3.5" aria-hidden="true" />Accept
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={resolving === s.id}
                      onClick={() => resolve(s.id, 'dismissed')}
                      className="flex-1"
                    >
                      <X className="h-3.5 w-3.5" aria-hidden="true" />Dismiss
                    </Button>
                  </div>
                </Card>
              </MotionItem>
            );
          })}
        </MotionSection>
      )}

      {/* empty states */}
      {!loading && suggestions.length === 0 && stats && (
        <EmptyState
          icon={<Sparkles className="h-8 w-8" />}
          title="No active suggestions."
          description="Run a check to generate phrase improvements."
        />
      )}
    </>
  );
}
