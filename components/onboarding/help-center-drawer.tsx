'use client';

import Link from 'next/link';
import { BookOpen, CheckCircle2, MessageSquareText, PlayCircle } from 'lucide-react';
import { Drawer } from '@/components/ui/modal';
import { useAuth } from '@/contexts/auth-context';
import { buildSupportMailto } from '@/lib/auth/support';
import { toursForRole } from '@/lib/onboarding/tour-registry';
import { useTourSafe } from './tour-provider';
import type { TourId, TourRole } from '@/lib/onboarding/tour-types';
import type { UserRole } from '@/lib/types/auth';

function toTourRole(role: UserRole | null | undefined): TourRole | null {
  if (role === 'learner' || role === 'expert' || role === 'admin') return role;
  return null;
}

interface HelpCenterDrawerProps {
  open: boolean;
  onClose: () => void;
  workspaceRole?: UserRole;
}

/**
 * Role-aware Help / replay center. Lists every guided tour the user can replay
 * (with a completed marker), plus quick links to existing guides and a short
 * mock-vs-practice explainer for learners. Reuses the focus-trapped Drawer.
 */
export function HelpCenterDrawer({ open, onClose, workspaceRole }: HelpCenterDrawerProps) {
  const { user } = useAuth();
  const tourCtx = useTourSafe();
  const startTour = tourCtx?.startTour ?? (() => Promise.resolve());
  const isCompleted = tourCtx?.isCompleted ?? (() => false);
  const tourRole = toTourRole(workspaceRole ?? user?.role);
  const tours = tourRole ? toursForRole(tourRole) : [];
  const isLearner = tourRole === 'learner';

  const replay = (id: TourId) => {
    onClose();
    // Let the drawer close (and restore the DOM) before the tour spotlights anchors.
    window.setTimeout(() => void startTour(id, { replay: true }), 250);
  };

  return (
    <Drawer open={open} onClose={onClose} title="Help & guided tours">
      <div className="space-y-7">
        <section aria-labelledby="help-tours-heading" className="space-y-3">
          <h3 id="help-tours-heading" className="text-xs font-semibold uppercase tracking-[0.18em] text-muted">
            Guided tours
          </h3>
          {tours.length === 0 ? (
            <p className="text-sm text-muted">No guided tours are available for this workspace yet.</p>
          ) : (
            <ul className="space-y-2">
              {tours.map((tour) => {
                const done = isCompleted(tour.completionKey);
                return (
                  <li key={tour.id}>
                    <button
                      type="button"
                      onClick={() => replay(tour.id)}
                      className="group flex w-full items-start gap-3 rounded-2xl border border-border bg-surface p-3 text-left transition-colors hover:border-primary/40 hover:bg-primary/5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                    >
                      <span className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                        {done ? <CheckCircle2 className="h-4 w-4" aria-hidden="true" /> : <PlayCircle className="h-4 w-4" aria-hidden="true" />}
                      </span>
                      <span className="min-w-0">
                        <span className="flex items-center gap-2 text-sm font-semibold text-navy">
                          {tour.title}
                          {done ? <span className="text-[11px] font-medium text-success">Completed</span> : null}
                        </span>
                        <span className="mt-0.5 block text-xs text-muted">{tour.description}</span>
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </section>

        {isLearner ? (
          <section aria-labelledby="help-modes-heading" className="space-y-2">
            <h3 id="help-modes-heading" className="text-xs font-semibold uppercase tracking-[0.18em] text-muted">
              Strict mock vs practice mode
            </h3>
            <p className="text-sm text-muted">
              <span className="font-semibold text-navy">Strict mock</span> mirrors test-day rules: Listening audio plays
              once, Reading Part A locks after 15 minutes, and Writing/Speaking run to exam timing with no hints.
            </p>
            <p className="text-sm text-muted">
              <span className="font-semibold text-navy">Practice</span> is for learning — replay audio, view transcripts
              and explanations, and take your time. Use it to build skills, then switch to mock to rehearse the real thing.
            </p>
          </section>
        ) : null}

        <section aria-labelledby="help-guides-heading" className="space-y-2">
          <h3 id="help-guides-heading" className="text-xs font-semibold uppercase tracking-[0.18em] text-muted">
            Guides & support
          </h3>
          <div className="grid gap-2">
            {isLearner ? (
              <Link
                href="/feedback-guide"
                onClick={onClose}
                className="flex items-center gap-3 rounded-2xl border border-border bg-surface p-3 text-sm font-semibold text-navy transition-colors hover:border-primary/40 hover:bg-primary/5"
              >
                <BookOpen className="h-4 w-4 text-primary" aria-hidden="true" />
                Feedback &amp; criteria guide
              </Link>
            ) : null}
            <a
              href={buildSupportMailto(user?.email ?? undefined)}
              onClick={onClose}
              className="flex items-center gap-3 rounded-2xl border border-border bg-surface p-3 text-sm font-semibold text-navy transition-colors hover:border-primary/40 hover:bg-primary/5"
            >
              <MessageSquareText className="h-4 w-4 text-primary" aria-hidden="true" />
              Contact support
            </a>
          </div>
        </section>
      </div>
    </Drawer>
  );
}
