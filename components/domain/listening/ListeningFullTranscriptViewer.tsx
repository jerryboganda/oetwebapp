'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { BookOpen, Clock, Play, Volume2 } from 'lucide-react';
import { SelectionToVocab } from '@/components/domain/vocabulary';
import type { ListeningReviewDto } from '@/lib/listening-api';
import { cn } from '@/lib/utils';

type TranscriptSegment = ListeningReviewDto['transcriptSegments'][number];

type HighlightEvidence = {
  questionNumber: number;
  partCode: string;
  startMs: number | null;
  endMs: number | null;
  excerpt: string | null;
} | null;

interface Props {
  transcriptSegments: TranscriptSegment[];
  extracts?: ListeningReviewDto['paper']['extracts'];
  highlightedEvidence?: HighlightEvidence;
  onPlayEvidence: (startMs: number | null | undefined, endMs: number | null | undefined, partCode?: string | null) => void;
  attemptId: string;
}

type MainPart = 'A' | 'B' | 'C';

function parentPart(partCode: string | null | undefined): MainPart {
  const code = (partCode ?? '').trim().toUpperCase();
  if (code.startsWith('A')) return 'A';
  if (code.startsWith('C')) return 'C';
  return 'B';
}

function subLabel(partCode: string | null | undefined): string | null {
  const code = (partCode ?? '').trim().toUpperCase();
  if (code === 'A1' || code === 'A2' || code === 'C1' || code === 'C2') return code;
  if (code.startsWith('A')) return code.includes('2') ? 'A2' : 'A1';
  if (code.startsWith('C')) return code.includes('2') ? 'C2' : 'C1';
  if (code.startsWith('B')) return 'B';
  return null;
}

function formatMs(value: number | null | undefined) {
  if (value == null) return null;
  const s = Math.floor(value / 1000);
  const m = Math.floor(s / 60);
  const r = s % 60;
  return `${m}:${r.toString().padStart(2, '0')}`;
}

function isHighlighted(seg: TranscriptSegment, hl: HighlightEvidence): boolean {
  if (!hl || hl.startMs == null) return false;
  // Prefer time overlap when we have a window
  if (hl.startMs != null) {
    const hlStart = hl.startMs;
    const hlEnd = hl.endMs ?? hl.startMs + 8000;
    // Overlap: seg.start < hlEnd && seg.end > hlStart
    if (seg.startMs < hlEnd && seg.endMs > hlStart) return true;
  }
  // Fallback: text containment when excerpt exists but no times
  if (hl.excerpt && seg.text) {
    const excerpt = hl.excerpt.trim().toLowerCase();
    const text = seg.text.trim().toLowerCase();
    if (excerpt.length > 8 && (text.includes(excerpt.slice(0, 40)) || excerpt.includes(text.slice(0, 30)))) return true;
  }
  return false;
}

export function ListeningFullTranscriptViewer({ transcriptSegments, highlightedEvidence, onPlayEvidence, attemptId }: Props) {
  const availableParents = useMemo(() => {
    const set = new Set<MainPart>();
    for (const seg of transcriptSegments) set.add(parentPart(seg.partCode));
    return set;
  }, [transcriptSegments]);

  // Default to first available or A
  const firstAvailable: MainPart = availableParents.has('A') ? 'A' : availableParents.has('B') ? 'B' : availableParents.has('C') ? 'C' : 'A';
  const [activeMain, setActiveMain] = useState<MainPart>(firstAvailable);

  useEffect(() => {
    // When highlight changes to a different part, switch tab
    if (highlightedEvidence?.partCode) {
      const p = parentPart(highlightedEvidence.partCode);
      if (availableParents.has(p)) setActiveMain(p);
    }
  }, [highlightedEvidence, availableParents]);

  // Keep activeMain in sync if segments load after mount
  useEffect(() => {
    if (!availableParents.has(activeMain) && availableParents.size > 0) {
      setActiveMain(firstAvailable);
    }
  }, [availableParents, activeMain, firstAvailable]);

  const grouped = useMemo(() => {
    // Structure: A -> { A1: seg[], A2: seg[] }, B -> { B: seg[] }, C -> { C1,C2 }
    const map: Record<string, TranscriptSegment[]> = { A1: [], A2: [], B: [], C1: [], C2: [] };
    for (const seg of transcriptSegments) {
      const sub = subLabel(seg.partCode) ?? parentPart(seg.partCode);
      if (sub === 'A1' || sub === 'A2' || sub === 'B' || sub === 'C1' || sub === 'C2') map[sub].push(seg);
      else map[parentPart(seg.partCode)].push(seg);
    }
    // sort each bucket by startMs
    for (const k of Object.keys(map)) map[k].sort((a, b) => a.startMs - b.startMs);
    return map;
  }, [transcriptSegments]);

  const hasAnySegments = transcriptSegments.length > 0;
  const highlightRef = useRef<HTMLDivElement | null>(null);
  const sectionRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (highlightedEvidence && highlightRef.current) {
      // scroll to first highlighted segment within active tab
      const el = highlightRef.current;
      // Ensure the transcript viewer is in viewport first, then the segment
      sectionRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
      setTimeout(() => el.scrollIntoView({ behavior: 'smooth', block: 'center' }), 350);
    }
  }, [highlightedEvidence, activeMain]);

  if (!hasAnySegments) {
    return (
      <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
        <div className="flex items-center gap-2 text-sm font-black uppercase tracking-widest text-muted">
          <BookOpen className="h-4 w-4" /> Full transcript
        </div>
        <p className="mt-3 rounded-xl border border-warning/20 bg-warning/10 px-4 py-3 text-sm text-warning">
          No time-coded transcript segment map is authored for this part yet. If an Audio Script PDF is attached it is shown above. Per-question evidence still works where authored excerpts exist.
        </p>
      </section>
    );
  }

  const tabDefs: Array<{ key: MainPart; label: string; count: number; disabled: boolean }> = [
    { key: 'A', label: 'Part A', count: grouped.A1.length + grouped.A2.length, disabled: !availableParents.has('A') },
    { key: 'B', label: 'Part B', count: grouped.B.length, disabled: !availableParents.has('B') },
    { key: 'C', label: 'Part C', count: grouped.C1.length + grouped.C2.length, disabled: !availableParents.has('C') },
  ];

  const renderSegments = (segments: TranscriptSegment[], emptyLabel: string) => {
    if (segments.length === 0) {
      return <p className="rounded-xl border border-border bg-background-light px-4 py-3 text-sm text-muted">{emptyLabel}</p>;
    }
    let firstHighlightRendered = false;
    return (
      <div className="space-y-2">
        {segments.map((seg, idx) => {
          const hl = highlightedEvidence ? isHighlighted(seg, highlightedEvidence) : false;
          const shouldAttachRef = hl && !firstHighlightRendered;
          if (shouldAttachRef) firstHighlightRendered = true;
          return (
            <div
              key={`${seg.startMs}-${seg.endMs}-${idx}`}
              ref={shouldAttachRef ? (highlightRef as unknown as React.RefObject<HTMLDivElement>) : undefined}
              className={cn(
                'group relative rounded-xl border p-3 text-left transition',
                hl ? 'border-info/40 bg-info/10 ring-1 ring-info/30' : 'border-border bg-background-light hover:border-border-hover hover:bg-surface',
              )}
            >
              <div className="flex flex-wrap items-center gap-2 text-[11px] font-black uppercase tracking-widest text-muted">
                <span className="inline-flex items-center gap-1">
                  <Clock className="h-3 w-3" />
                  {formatMs(seg.startMs)}–{formatMs(seg.endMs)}
                </span>
                {seg.partCode ? <span>{seg.partCode}</span> : null}
                {seg.speakerId ? <span className="rounded bg-surface px-1.5 py-0.5 text-[10px] leading-none">{seg.speakerId}</span> : null}
                <button
                  type="button"
                  onClick={() => onPlayEvidence(seg.startMs, seg.endMs, seg.partCode)}
                  className="ml-auto inline-flex items-center gap-1 rounded-full bg-info/10 px-2 py-1 text-[11px] font-bold text-info hover:bg-info/20"
                  aria-label={`Play audio for ${formatMs(seg.startMs)} to ${formatMs(seg.endMs)}`}
                >
                  <Play className="h-3 w-3" /> Play
                </button>
              </div>
              <p className="mt-2 text-sm leading-6 text-navy">{seg.text}</p>
              {hl ? <span className="absolute right-2 top-2 rounded-full bg-info px-2 py-0.5 text-[10px] font-black uppercase tracking-widest text-white">Highlighted · Q{highlightedEvidence?.questionNumber}</span> : null}
            </div>
          );
        })}
      </div>
    );
  };

  return (
    <section ref={sectionRef} id="full-transcript-viewer" className="rounded-2xl border border-border bg-surface p-6 shadow-sm" aria-label="Full transcript by part">
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          <p className="text-xs font-black uppercase tracking-widest text-muted">Full transcript</p>
          <h3 className="mt-1 text-lg font-black text-navy">Complete script for the submitted part</h3>
          <p className="mt-1 max-w-2xl text-sm leading-6 text-muted">
            Select any word or phrase to look it up. Click <span className="font-semibold">Play</span> on a segment to replay that span. The relevant section for the question you are reviewing is highlighted.
          </p>
        </div>
        <Volume2 className="hidden h-5 w-5 shrink-0 text-muted sm:block" aria-hidden />
      </div>

      <div role="tablist" aria-label="Transcript parts" className="mb-4 flex gap-2 overflow-x-auto pb-1">
        {tabDefs.map((tab) => (
          <button
            key={tab.key}
            type="button"
            role="tab"
            aria-selected={activeMain === tab.key}
            aria-controls={`transcript-panel-${tab.key}`}
            disabled={tab.disabled}
            onClick={() => setActiveMain(tab.key)}
            className={cn(
              'shrink-0 rounded-xl px-4 py-2.5 text-sm font-black transition',
              tab.disabled ? 'cursor-not-allowed bg-background-light text-muted/50' : activeMain === tab.key ? 'bg-primary text-white shadow-sm' : 'bg-background-light text-navy hover:bg-surface',
            )}
          >
            {tab.label}
            <span className={cn('ml-2 rounded-full px-1.5 py-0.5 text-xs', activeMain === tab.key ? 'bg-white/20 text-white' : 'bg-surface text-muted')}>
              {tab.count}
            </span>
          </button>
        ))}
      </div>

      <div className="rounded-2xl border border-border bg-background-card p-4">
        {highlightedEvidence ? (
          <p className="mb-3 rounded-xl border border-info/20 bg-info/10 px-3 py-2 text-xs font-semibold text-info">
            Relevant transcript section highlighted below — Q{highlightedEvidence.questionNumber} (Part {parentPart(highlightedEvidence.partCode)})
          </p>
        ) : null}
        {/* VOCAB: entire panel is selectable */}
        <SelectionToVocab source="listening" sourceRefPrefix={`listening:transcript:${attemptId}:${activeMain}`}>
          <div id={`transcript-panel-${activeMain}`} role="tabpanel" className="space-y-6">
            {activeMain === 'A' ? (
              <>
                <div>
                  <h4 className="mb-2 flex items-center gap-2 text-xs font-black uppercase tracking-widest text-muted">
                    <span className="h-1.5 w-1.5 rounded-full bg-primary" /> A1 — Extract 1
                  </h4>
                  {renderSegments(grouped.A1, 'No A1 transcript segments authored for this paper.')}
                </div>
                <div>
                  <h4 className="mb-2 flex items-center gap-2 text-xs font-black uppercase tracking-widest text-muted">
                    <span className="h-1.5 w-1.5 rounded-full bg-primary" /> A2 — Extract 2
                  </h4>
                  {renderSegments(grouped.A2, 'No A2 transcript segments authored for this paper.')}
                </div>
              </>
            ) : null}
            {activeMain === 'B' ? (
              <div>
                <h4 className="mb-2 flex items-center gap-2 text-xs font-black uppercase tracking-widest text-muted">
                  <span className="h-1.5 w-1.5 rounded-full bg-primary" /> Part B — Workplace extracts (B1–B6 share one audio)
                </h4>
                {renderSegments(grouped.B, 'No Part B transcript segments authored for this paper.')}
              </div>
            ) : null}
            {activeMain === 'C' ? (
              <>
                <div>
                  <h4 className="mb-2 flex items-center gap-2 text-xs font-black uppercase tracking-widest text-muted">
                    <span className="h-1.5 w-1.5 rounded-full bg-primary" /> C1 — Extract 1
                  </h4>
                  {renderSegments(grouped.C1, 'No C1 transcript segments authored for this paper.')}
                </div>
                <div>
                  <h4 className="mb-2 flex items-center gap-2 text-xs font-black uppercase tracking-widest text-muted">
                    <span className="h-1.5 w-1.5 rounded-full bg-primary" /> C2 — Extract 2
                  </h4>
                  {renderSegments(grouped.C2, 'No C2 transcript segments authored for this paper.')}
                </div>
              </>
            ) : null}
          </div>
        </SelectionToVocab>
        <p className="mt-4 text-xs leading-5 text-muted">Tip: drag to select a word in any segment — the vocabulary lookup appears automatically. Audio remains replayable after submission for every submitted part.</p>
      </div>
    </section>
  );
}
