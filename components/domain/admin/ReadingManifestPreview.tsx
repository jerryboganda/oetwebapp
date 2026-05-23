'use client';

import { Badge } from '@/components/ui/badge';
import type { ReadingStructureManifestDto } from '@/lib/reading-authoring-api';
import { sanitizeBodyHtml } from '@/lib/wizard/sanitize-html';

/**
 * Phase 4 closure — read-only preview of a Reading manifest, used by the
 * admin AI-extraction approval flow. Walks each Part A / B / C section
 * and renders every text + question so the reviewer can spot incorrect
 * answer keys, malformed options, or off-topic stems before approving
 * a destructive `ReadingExtractionService.ApproveDraftAsync` call
 * (which calls `ImportManifestAsync` with `replaceExisting: true`).
 *
 * Renders nothing when `manifest` is null — caller should surface the
 * "no manifest" state on its own.
 */
export function ReadingManifestPreview({ manifest }: { manifest: ReadingStructureManifestDto }) {
  if (manifest.parts.length === 0) {
    return (
      <p className="text-sm text-muted">Manifest contains no parts.</p>
    );
  }

  return (
    <div className="space-y-6">
      {manifest.parts.map((part) => (
        <section key={part.partCode} className="rounded-2xl border border-border bg-background-light p-4">
          <header className="mb-3 flex flex-wrap items-center justify-between gap-2">
            <h4 className="text-sm font-bold uppercase tracking-[0.14em] text-navy">
              Part {part.partCode}
              <span className="ml-2 text-xs font-normal normal-case text-muted">
                {part.timeLimitMinutes ?? '—'} min · {part.questions.length} Qs · {part.texts.length} texts
              </span>
            </h4>
            {part.instructions ? (
              <p className="text-xs text-muted">{part.instructions}</p>
            ) : null}
          </header>

          {part.texts.length > 0 ? (
            <div className="mb-4 space-y-2">
              <p className="text-[10px] font-bold uppercase tracking-[0.14em] text-muted">Texts</p>
              {part.texts.map((text) => (
                <div key={`${part.partCode}-text-${text.displayOrder}`} className="rounded-xl bg-surface p-3 text-sm">
                  <div className="mb-1 flex items-center gap-2">
                    <Badge variant="info" className="text-[10px]">Text #{text.displayOrder}</Badge>
                    <span className="font-semibold text-navy">{text.title || '(untitled)'}</span>
                    <span className="text-xs text-muted">· {text.wordCount} words</span>
                  </div>
                  {text.source ? (
                    <p className="text-xs italic text-muted">Source: {text.source}</p>
                  ) : null}
                  <div
                    className="mt-2 max-h-32 overflow-y-auto text-xs text-foreground"
                    // Defense-in-depth: backend sanitises, but apply the same
                    // allow-list here too so admin preview matches learner view.
                    dangerouslySetInnerHTML={{ __html: sanitizeBodyHtml(text.bodyHtml || '') }}
                  />
                </div>
              ))}
            </div>
          ) : null}

          <div className="space-y-2">
            <p className="text-[10px] font-bold uppercase tracking-[0.14em] text-muted">Questions</p>
            {part.questions.length === 0 ? (
              <p className="text-xs text-muted">No questions extracted for this part.</p>
            ) : (
              part.questions.map((q) => {
                let options: unknown[] = [];
                try { options = JSON.parse(q.optionsJson || '[]'); }
                catch { options = []; }
                let correct: unknown = '';
                try { correct = JSON.parse(q.correctAnswerJson || '""'); }
                catch { correct = q.correctAnswerJson; }
                let synonyms: string[] = [];
                if (q.acceptedSynonymsJson) {
                  try {
                    const parsed = JSON.parse(q.acceptedSynonymsJson);
                    if (Array.isArray(parsed)) synonyms = parsed.filter((x): x is string => typeof x === 'string');
                  } catch { synonyms = []; }
                }

                return (
                  <div key={`${part.partCode}-q-${q.displayOrder}`} className="rounded-xl bg-surface p-3 text-sm">
                    <div className="mb-1 flex flex-wrap items-center gap-2">
                      <Badge variant="info" className="text-[10px]">Q{q.displayOrder}</Badge>
                      <Badge variant="outline" className="text-[10px]">{q.questionType}</Badge>
                      <span className="text-xs text-muted">{q.points} pt{q.points === 1 ? '' : 's'}</span>
                      {q.skillTag ? (
                        <Badge variant="outline" className="text-[10px]">{q.skillTag}</Badge>
                      ) : null}
                    </div>
                    <p className="text-sm text-foreground">{q.stem || '(empty stem)'}</p>
                    {Array.isArray(options) && options.length > 0 ? (
                      <ul className="mt-2 space-y-0.5 text-xs">
                        {options.map((opt, idx) => (
                          <li
                            key={`${part.partCode}-q${q.displayOrder}-opt${idx}`}
                            className="flex items-baseline gap-2"
                          >
                            <span className="font-mono text-muted">{String.fromCharCode(65 + idx)}.</span>
                            <span className="text-foreground">{String(opt)}</span>
                          </li>
                        ))}
                      </ul>
                    ) : null}
                    <p className="mt-2 text-xs">
                      <span className="font-semibold text-emerald-700">Answer:</span>{' '}
                      <span className="font-mono text-navy">{Array.isArray(correct) ? correct.join(', ') : String(correct)}</span>
                    </p>
                    {synonyms.length > 0 ? (
                      <p className="mt-1 text-xs text-muted">
                        Accepted variants: {synonyms.join(', ')}
                      </p>
                    ) : null}
                    {q.optionDistractorsJson ? (
                      <p className="mt-1 text-xs text-muted">
                        Distractor metadata: {q.optionDistractorsJson}
                      </p>
                    ) : null}
                  </div>
                );
              })
            )}
          </div>
        </section>
      ))}
    </div>
  );
}
