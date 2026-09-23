import { cn } from '@/lib/utils';

/**
 * 22 Sep 2026 handoff (item 4): exam-style B&W card, matching the real OET
 * role-play card format. Field order is fixed: Profession, Setting,
 * Background, Tasks. Emotion/Goal/Topic and the standalone Patient/Task(brief)
 * rows are gone — patient name/age read as part of Background.
 *
 * 23 Sep 2026: this is the ONE card for every Speaking surface (roleplay,
 * prep, active, live tutor and the full exam, which replaced its separate
 * OfficialCandidateCard with this). Candidate-facing fields only — never the
 * roleplayer (patient) card.
 */
export interface SpeakingRoleCardProps {
  role: string;
  setting: string;
  patient: string;
  background?: string;
  tasks?: string[];
  prepTimeSeconds?: number;
  roleplayTimeSeconds?: number;
  cardNumber?: number;
  disclaimer?: string;
  /** Rights notice printed on the source card. Shown verbatim beneath the card. */
  sourceAttribution?: string;
  className?: string;
}

function formatSeconds(seconds?: number) {
  if (!seconds) return null;
  const minutes = Math.floor(seconds / 60);
  const remainder = seconds % 60;
  return remainder === 0 ? `${minutes} min` : `${minutes}m ${remainder}s`;
}

/** Learner-safe card fields shared by the session and exam DTOs. */
export interface LearnerRoleCardSource {
  professionId: string;
  candidateRole?: string | null;
  setting: string;
  patientName?: string | null;
  patientAge?: string | null;
  background: string;
  tasks: string[];
  disclaimer?: string | null;
  displayCardNumber?: number | null;
}

/** Maps a server card projection onto the one exam-style card. */
export function roleCardPropsFrom(card: LearnerRoleCardSource): SpeakingRoleCardProps {
  const profession = card.professionId ? card.professionId.charAt(0).toUpperCase() + card.professionId.slice(1) : '';
  return {
    role: card.candidateRole?.trim() || profession,
    setting: card.setting,
    patient: [card.patientName, card.patientAge].filter(Boolean).join(', '),
    background: card.background,
    tasks: card.tasks ?? [],
    cardNumber: card.displayCardNumber ?? undefined,
    disclaimer: card.disclaimer ?? undefined,
  };
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="grid grid-cols-[100px_1fr] gap-3 px-4 py-3 sm:grid-cols-[130px_1fr]">
      <div className="text-xs font-bold uppercase tracking-wide text-slate-600">{label}</div>
      <div className="min-w-0">{children}</div>
    </div>
  );
}

export function SpeakingRoleCard({
  role,
  setting,
  patient,
  background,
  tasks = [],
  prepTimeSeconds,
  roleplayTimeSeconds,
  cardNumber,
  disclaimer,
  sourceAttribution,
  className,
}: SpeakingRoleCardProps) {
  const prepLabel = formatSeconds(prepTimeSeconds);
  const roleplayLabel = formatSeconds(roleplayTimeSeconds);
  const cleanTasks = tasks.filter((t) => t && t.trim().length > 0);

  return (
    <div className={cn('space-y-3', className)} role="region" aria-label="Role card details" data-testid="speaking-role-card">
      {(prepLabel || roleplayLabel) && (
        <div className="flex flex-wrap gap-2">
          {prepLabel && <span className="rounded-full border border-border bg-surface px-3 py-1 text-[11px] font-bold tracking-wide text-muted">PREP: {prepLabel}</span>}
          {roleplayLabel && <span className="rounded-full border border-border bg-surface px-3 py-1 text-[11px] font-bold tracking-wide text-muted">ROLE-PLAY: {roleplayLabel}</span>}
        </div>
      )}

      <article className="overflow-hidden rounded-lg border border-border bg-white text-slate-900 shadow-sm dark:bg-slate-50">
        <header className="flex items-center justify-between gap-3 bg-slate-800 px-4 py-2 text-white">
          <h3 className="text-sm font-bold uppercase tracking-wide">
            Role-Play Card{cardNumber != null ? ` No. ${cardNumber}` : ''}
          </h3>
        </header>

        <div className="divide-y divide-slate-200">
          <Row label="Profession">
            <p className="text-sm font-medium">{role}</p>
          </Row>

          <Row label="Setting">
            <p className="text-sm">{setting}</p>
          </Row>

          <Row label="Background">
            {patient && (
              <p className="mb-1 text-sm font-medium text-slate-700">{patient}</p>
            )}
            {background && (
              <p className="whitespace-pre-line text-sm leading-relaxed">&quot;{background}&quot;</p>
            )}
          </Row>

          {cleanTasks.length > 0 && (
            <Row label="Tasks">
              <ul className="list-disc space-y-1.5 pl-4 text-sm leading-relaxed">
                {cleanTasks.map((item, index) => (
                  <li key={`${item}-${index}`}>{item}</li>
                ))}
              </ul>
            </Row>
          )}
        </div>

        {disclaimer && (
          <footer className="border-t border-slate-200 bg-slate-50 px-4 py-2 text-[11px] italic text-slate-500">
            {disclaimer}
          </footer>
        )}
      </article>

      {sourceAttribution && (
        <p className="px-1 text-[11px] leading-relaxed text-muted/80 text-center">
          {sourceAttribution}
        </p>
      )}
    </div>
  );
}
