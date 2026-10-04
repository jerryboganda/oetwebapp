import type { SpeakingIntelligibilityEvidence } from '@/lib/api/speaking-assessments';
import {
  INTELLIGIBILITY_AUDIO_LABEL,
  INTELLIGIBILITY_AUDIO_LIMITED_NOTE,
  INTELLIGIBILITY_TRANSCRIPT_ONLY_LABEL,
} from '@/lib/speaking/score-label';

/**
 * What the Intelligibility score was judged from (owner spec 4 Oct 2026). Judged from the recording, it lists what the
 * listener heard; with no usable audio it says plainly that the number is estimated from the transcript only.
 * No payload (an older API) shows nothing rather than guessing.
 */
export function IntelligibilityEvidenceNote({ evidence }: { evidence?: SpeakingIntelligibilityEvidence | null }) {
  if (!evidence) return null;

  if (evidence.source !== 'audio') {
    return (
      <p className="mt-2 text-xs font-semibold text-warning-strong" data-testid="intelligibility-evidence-label">
        {INTELLIGIBILITY_TRANSCRIPT_ONLY_LABEL}
        {evidence.reasonText ? ` — ${evidence.reasonText}` : ''}.
      </p>
    );
  }

  return (
    <div className="mt-2 space-y-1.5" data-testid="intelligibility-evidence">
      <p className="text-xs font-semibold text-primary-dark" data-testid="intelligibility-evidence-label">
        {INTELLIGIBILITY_AUDIO_LABEL}
      </p>
      {evidence.confidence === 'low' ? (
        <p className="text-xs text-muted" data-testid="intelligibility-evidence-limited">
          {INTELLIGIBILITY_AUDIO_LIMITED_NOTE}
        </p>
      ) : null}
      {evidence.observations.length > 0 ? (
        <div>
          <p className="eyebrow text-muted">What we heard</p>
          <ul className="mt-1 list-disc space-y-1 pl-5 text-xs leading-relaxed text-navy" data-testid="intelligibility-observations">
            {evidence.observations.map((observation, i) => (
              <li key={i}>
                {observation.approxSecond > 0 ? <span className="font-semibold">About {observation.approxSecond} s: </span> : null}
                {observation.issue}
                {observation.example ? <span className="italic"> (&ldquo;{observation.example}&rdquo;)</span> : null}
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </div>
  );
}

export default IntelligibilityEvidenceNote;
