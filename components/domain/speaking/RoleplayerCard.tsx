'use client';

/**
 * Speaking module rebuild (2026-06-11 spec).
 *
 * The official OET-style ROLEPLAYER (patient) card. Mirrors the candidate card
 * layout but renders the patient's background + tasks.
 *
 * ┌───────────────────────────────────────────────┐
 * │ ROLEPLAYER CARD NO. 2                 MEDICINE │
 * ├───────────────────────────────────────────────┤
 * │ SETTING    General Practice                    │
 * │ PATIENT    You are an 18-year-old …            │
 * │ TASK       • Explain your current symptoms …   │
 * └───────────────────────────────────────────────┘
 *
 * ⚠️ MISSION CRITICAL — TUTOR / ADMIN / AI ONLY ⚠️
 * The student must NEVER see this card. Do NOT import this component from any
 * route under `app/speaking/**` that a learner can reach. It belongs only to
 * `app/expert/**`, `app/admin/**`, and previews behind admin auth. The data it
 * renders comes from the hidden `InterlocutorScript` and is never serialized to
 * learner endpoints (pinned by SpeakingExamLeakageTests).
 */
import { cn } from '@/lib/utils';

export interface RoleplayerCardData {
  professionId: string;
  setting: string;
  interlocutorRole: string;
  patientName?: string | null;
  patientAge?: string | null;
  patientBackground: string;
  patientTasks: string[];
  displayCardNumber?: number | null;
}

export interface RoleplayerCardProps {
  card: RoleplayerCardData;
  cardNumber?: number;
  className?: string;
}

function titleCaseProfession(raw: string): string {
  if (!raw) return '';
  return raw.charAt(0).toUpperCase() + raw.slice(1);
}

export function RoleplayerCard({ card, cardNumber, className }: RoleplayerCardProps) {
  const number = card.displayCardNumber ?? cardNumber;
  const roleLabel = (card.interlocutorRole || 'Patient').toUpperCase();
  const tasks = (card.patientTasks ?? []).filter((t) => t && t.trim().length > 0);

  return (
    <article
      className={cn(
        'overflow-hidden rounded-lg border border-amber-300 bg-amber-50 text-slate-900 shadow-sm',
        className,
      )}
      data-testid="roleplayer-card"
      aria-label="Roleplayer card (tutor only)"
    >
      <header className="flex items-center justify-between gap-3 bg-amber-700 px-4 py-2 text-white">
        <h3 className="text-sm font-bold uppercase tracking-wide">
          Roleplayer Card{number != null ? ` No. ${number}` : ''}
        </h3>
        <span className="text-xs font-semibold uppercase tracking-widest text-amber-100">
          {titleCaseProfession(card.professionId)}
        </span>
      </header>

      <div className="divide-y divide-amber-200">
        <Row label="Setting">
          <p className="text-sm">{card.setting}</p>
        </Row>

        <Row label={roleLabel}>
          {(card.patientName || card.patientAge) && (
            <p className="mb-1 text-sm font-medium text-slate-700">
              {[card.patientName, card.patientAge].filter(Boolean).join(', ')}
            </p>
          )}
          <p className="whitespace-pre-line text-sm leading-relaxed">{card.patientBackground}</p>
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

      <footer className="border-t border-amber-200 bg-amber-100/60 px-4 py-1.5 text-[11px] font-medium uppercase tracking-wide text-amber-800">
        Tutor / examiner only — not shown to the candidate
      </footer>
    </article>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="grid grid-cols-[88px_1fr] gap-3 px-4 py-3 sm:grid-cols-[110px_1fr]">
      <div className="text-xs font-bold uppercase tracking-wide text-amber-800">{label}</div>
      <div className="min-w-0">{children}</div>
    </div>
  );
}

export default RoleplayerCard;
