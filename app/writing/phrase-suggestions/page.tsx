'use client';

import { useEffect, useState, useCallback } from 'react';
import { Sparkles, Check, X, ChevronDown, ChevronUp, BookOpen, RefreshCw, BarChart3 } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { analytics } from '@/lib/analytics';

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
async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

/* ── category config ─────────────────────────── */
const TYPE_CONFIG: Record<string, { label: string; color: string; bg: string }> = {
  grammar:      { label: 'Grammar',      color: 'text-danger',  bg: 'bg-danger/10 border-danger/30' },
  vocabulary:   { label: 'Better Phrase', color: 'text-info',    bg: 'bg-info/10 border-info/30' },
  tone:         { label: 'Tone',         color: 'text-primary', bg: 'bg-primary/10 border-primary/30' },
  conciseness:  { label: 'Conciseness',  color: 'text-warning', bg: 'bg-warning/10 border-warning/30' },
  structure:    { label: 'Structure',    color: 'text-success', bg: 'bg-success/10 border-success/30' },
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

  /* load stats for current attempt */
  const loadStats = useCallback(async (aid: string) => {
    try {
      const s = await apiRequest<CoachStats>(`/v1/writing/attempts/${aid}/coach-stats`);
      setStats(s);
    } catch { /* */ }
  }, []);

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
  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="AI Phrase Coach"
        description="Get intelligent suggestions to upgrade your vocabulary, fix grammar, and improve writing tone"
      />

      <div className="max-w-3xl mx-auto space-y-6">

        {/* ── Input Section ─────────────────── */}
        <Card className="p-5">
          <label htmlFor="attempt-id" className="text-sm font-medium block mb-2">Attempt ID</label>
          <div className="flex gap-2 mb-4">
            <input
              id="attempt-id"
              type="text"
              value={attemptId}
              onChange={e => setAttemptId(e.target.value)}
              placeholder="Enter your writing attempt ID"
              className="flex-1 bg-muted/30 border rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary/50"
            />
            <Button onClick={runCheck} disabled={loading || !attemptId.trim()}>
              {loading ? <RefreshCw className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4 mr-1.5" />}
              {loading ? 'Checking…' : 'Check'}
            </Button>
          </div>

          <label htmlFor="input-text" className="text-sm font-medium block mb-2">
            Paste text <span className="text-muted-foreground font-normal">(optional — overrides stored attempt)</span>
          </label>
          <textarea
            id="input-text"
            value={inputText}
            onChange={e => setInputText(e.target.value)}
            rows={4}
            placeholder="Optionally paste writing text to check for phrase suggestions…"
            className="w-full border bg-muted/30 rounded-lg p-3 text-sm resize-none focus:outline-none focus:ring-2 focus:ring-primary/50"
          />
        </Card>

        {/* ── Stats Row ─────────────────────── */}
        {stats && (
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            {[
              { label: 'Suggestions', value: stats.totalGenerated, icon: Sparkles },
              { label: 'Accepted', value: stats.accepted, icon: Check },
              { label: 'Dismissed', value: stats.dismissed, icon: X },
              { label: 'Acceptance', value: `${Math.round(stats.acceptanceRate)}%`, icon: BarChart3 },
            ].map(s => (
              <Card key={s.label} className="p-3 text-center">
                <s.icon className="h-4 w-4 mx-auto mb-1 text-muted-foreground" />
                <p className="text-lg font-bold">{s.value}</p>
                <p className="text-xs text-muted-foreground">{s.label}</p>
              </Card>
            ))}
          </div>
        )}

        {/* ── Filter Pills ──────────────────── */}
        {suggestions.length > 0 && (
          <div className="flex gap-2 flex-wrap">
            <button
              onClick={() => setFilter(null)}
              className={`px-3 py-1.5 rounded-full text-xs font-medium border transition-colors
                ${!filter ? 'bg-primary text-primary-foreground border-primary' : 'bg-muted/30 border-border hover:border-primary/50'}`}
            >
              All ({suggestions.length})
            </button>
            {Object.entries(TYPE_CONFIG).map(([key, cfg]) => {
              const count = suggestions.filter(s => s.type === key).length;
              if (count === 0) return null;
              return (
                <button
                  key={key}
                  onClick={() => setFilter(filter === key ? null : key)}
                  className={`px-3 py-1.5 rounded-full text-xs font-medium border transition-colors
                    ${filter === key ? `${cfg.bg} ${cfg.color} border-current` : 'bg-muted/30 border-border hover:border-primary/50'}`}
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
            {filtered.map(s => {
              const cfg = TYPE_CONFIG[s.type] || TYPE_CONFIG.vocabulary;
              const expanded = expandedId === s.id;
              return (
                <MotionItem key={s.id}>
                  <Card className={`overflow-hidden border ${expanded ? cfg.bg : ''}`}>
                    <div className="p-4">
                      <div className="flex items-start justify-between gap-2 mb-3">
                        <Badge variant="outline" className={`${cfg.color} text-[10px] shrink-0`}>{cfg.label}</Badge>
                        <div className="flex items-center gap-1">
                          <span className="text-[10px] text-muted-foreground">{Math.round(s.confidence * 100)}%</span>
                          <button onClick={() => setExpandedId(expanded ? null : s.id)} className="p-2.5 -m-1">
                            {expanded ? <ChevronUp className="h-3.5 w-3.5" /> : <ChevronDown className="h-3.5 w-3.5" />}
                          </button>
                        </div>
                      </div>

                      {/* original → suggested */}
                      <div className="space-y-2 mb-3">
                        <div className="flex items-start gap-2">
                          <span className="text-[10px] text-muted-foreground mt-1 shrink-0 w-12">Before</span>
                          <p className="text-sm line-through text-muted-foreground">{s.originalText}</p>
                        </div>
                        <div className="flex items-start gap-2">
                          <span className="text-[10px] text-primary mt-1 shrink-0 w-12">After</span>
                          <p className="text-sm font-medium">{s.suggestedText}</p>
                        </div>
                      </div>

                      {/* explanation (expanded) */}
                      {expanded && (
                        <div className="bg-background/60 rounded-lg p-3 mb-3">
                          <p className="text-xs text-muted-foreground flex items-start gap-2">
                            <BookOpen className="h-3.5 w-3.5 mt-0.5 shrink-0" />
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
                          className="flex-1 h-8 text-xs"
                        >
                          <Check className="h-3.5 w-3.5 mr-1" />Accept
                        </Button>
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={resolving === s.id}
                          onClick={() => resolve(s.id, 'dismissed')}
                          className="flex-1 h-8 text-xs"
                        >
                          <X className="h-3.5 w-3.5 mr-1" />Dismiss
                        </Button>
                      </div>
                    </div>
                  </Card>
                </MotionItem>
              );
            })}
          </MotionSection>
        )}

        {/* empty states */}
        {!loading && suggestions.length === 0 && stats && (
          <div className="text-center py-12 text-muted-foreground">
            <Sparkles className="h-10 w-10 mx-auto mb-3 opacity-30" />
            <p className="text-sm">No active suggestions. Run a check to generate phrase improvements.</p>
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
