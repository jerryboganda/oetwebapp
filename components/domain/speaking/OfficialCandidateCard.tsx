'use client';

/**
 * Speaking module rebuild (2026-06-11 spec).
 *
 * The official OET-style CANDIDATE (doctor) card the student sees. Layout
 * mirrors the real OET role-play card:
 *   ┌───────────────────────────────────────────────┐
 *   │ CANDIDATE CARD NO. 2                  MEDICINE │  ← dark header bar
 *   ├───────────────────────────────────────────────┤
 *   │ SETTING    General Practice                    │
 *   │ DOCTOR     You have just examined …            │
 *   │ TASK       • Take a brief history …            │
 *   │            • Explain the likely diagnosis …    │
 *   └───────────────────────────────────────────────┘
 *
 * MISSION CRITICAL: this component renders ONLY candidate-facing fields. It is
 * never handed the roleplayer (patient) card or the hidden card type — those
 * live in `RoleplayerCard` which is imported only by tutor/admin routes.
 */
import { cn } from '@/lib/utils';

export interface OfficialCandidateCardData {
  professionId: string;
  setting: string;
  candidateRole: string;
  background: string;
  tasks: string[];
  patientName?: string | null;
  patientAge?: string | null;
  displayCardNumber?: number | null;
  disclaimer?: string | null;
}

export interface OfficialCandidateCardProps {
  card: OfficialCandidateCardData;
  /** Falls back to `card.displayCardNumber`, then to this when both unset. */
  cardNumber?: number;
  className?: string;
}

function titleCaseProfession(raw: string): string {
  if (!raw) return '';
  return raw.charAt(0).toUpperCase() + raw.slice(1);
}

export function OfficialCandidateCard({ card, cardNumber, className }: OfficialCandidateCardProps) {
  const number = card.displayCardNumber ?? cardNumber;
  const roleLabel = (card.candidateRole || 'Doctor').toUpperCase();
  const tasks = (card.tasks ?? []).filter((t) => t && t.trim().length > 0);

  return (
    <article
      className={cn(
        'overflow-hidden rounded-lg border border-border bg-white text-slate-900 shadow-sm',
        'dark:bg-slate-50',
        className,
      )}
      data-testid="official-candidate-card"
      aria-label="Candidate card"
    >
      {/* Header bar */}
      <header className="flex items-center justify-between gap-3 bg-slate-800 px-4 py-2 text-white">
        <h3 className="text-sm font-bold uppercase tracking-wide">
          Candidate Card{number != null ? ` No. ${number}` : ''}
        </h3>
        <span className="text-xs font-semibold uppercase tracking-widest text-slate-200">
          {titleCaseProfession(card.professionId)}
        </span>
      </header>

      <div className="divide-y divide-slate-200">
        <Row label="Setting">
          <p className="text-sm">{card.setting}</p>
        </Row>

        <Row label={roleLabel}>
          {(card.patientName || card.patientAge) && (
            <p className="mb-1 text-sm font-medium text-slate-700">
              {[card.patientName, card.patientAge].filter(Boolean).join(', ')}
            </p>
          )}
          <p className="whitespace-pre-line text-sm leading-relaxed">{card.background}</p>
        </Row>

        {tasks.length > 0 && (
          <Row label="Task">
            <ul className="list-disc space-y-1.5 pl-4 text-sm leading-relaxed">
              {tasks.map((task, i) => (
                <li key={i}>{task}</li>
              ))}
            </ul>
          </Row>
        )}
      </div>

      {card.disclaimer ? (
        <footer className="border-t border-slate-200 bg-slate-50 px-4 py-2 text-[11px] italic text-slate-500">
          {card.disclaimer}
        </footer>
      ) : null}
    </article>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="grid grid-cols-[88px_1fr] gap-3 px-4 py-3 sm:grid-cols-[110px_1fr]">
      <div className="text-xs font-bold uppercase tracking-wide text-slate-600">{label}</div>
      <div className="min-w-0">{children}</div>
    </div>
  );
}

export default OfficialCandidateCard;
