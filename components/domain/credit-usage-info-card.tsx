import { Gift, PlayCircle, Repeat, Ticket } from 'lucide-react';
import { cardClassName } from '@/components/ui/card';
import { cn } from '@/lib/utils';

// Learner-facing explainer for how Reading / Listening credits are spent.
// Billing rule (backend: paper is the unit — one credit per sample): opening any
// part (A/B/C) OR the full paper of a sample uses ONE credit; the other parts, the
// full paper, and repeat attempts of that same sample are then free. Shown on the
// Reading and Listening hubs so learners understand the rule before they choose.

type CreditModule = 'reading' | 'listening';

// Sub-test identity colours (DESIGN.md §2), not status.
const THEME: Record<CreditModule, { unit: string; tile: string; accent: string }> = {
  reading: {
    unit: 'Reading',
    tile: 'bg-skill-reading/10 text-skill-reading',
    accent: 'text-skill-reading',
  },
  listening: {
    unit: 'Listening',
    tile: 'bg-skill-listening/10 text-skill-listening',
    accent: 'text-skill-listening',
  },
};

export function CreditUsageInfoCard({
  module,
  className = '',
}: {
  module: CreditModule;
  className?: string;
}) {
  const theme = THEME[module];
  const unit = theme.unit;

  const steps = [
    {
      icon: PlayCircle,
      title: 'Open any part or the full paper',
      detail: `Uses just 1 ${unit} credit for that whole sample.`,
    },
    {
      icon: Gift,
      title: 'The other parts are free',
      detail: `Parts A, B and C — plus the full paper — of the same sample cost nothing more.`,
    },
    {
      icon: Repeat,
      title: 'Repeat attempts are free',
      detail: `Come back to that same sample as often as you like — no extra credit.`,
    },
  ];

  return (
    <section
      data-testid={`${module}-credit-usage-info`}
      aria-label={`How ${unit} credits are used`}
      className={cn(cardClassName({ padding: 'lg' }), className)}
    >
      <div className="flex items-start gap-4">
        <span
          className={cn('flex h-11 w-11 shrink-0 items-center justify-center rounded-xl', theme.tile)}
          aria-hidden
        >
          <Ticket className="h-5 w-5" />
        </span>
        <div className="min-w-0">
          <p className={cn('eyebrow', theme.accent)}>How your credits work</p>
          <h3 className="mt-0.5 text-base font-bold text-navy">
            One {unit} credit unlocks the whole sample
          </h3>
          <p className="mt-1 text-sm text-muted">
            You&apos;re only charged once per sample. The first time you open any part or the full
            paper, it uses a single {unit} credit — everything else in that sample is then free.
          </p>
        </div>
      </div>

      {/* Plain steps on the card's own surface: one surface level, no cards in cards. */}
      <ol className="mt-4 grid grid-cols-1 gap-3 border-t border-border pt-4 sm:grid-cols-3 sm:gap-4">
        {steps.map((step) => {
          const Icon = step.icon;
          return (
            <li key={step.title} className="flex min-w-0 items-start gap-2.5">
              <Icon className={cn('mt-0.5 h-4 w-4 shrink-0', theme.accent)} aria-hidden />
              <div className="min-w-0">
                <p className="text-xs font-bold text-navy">{step.title}</p>
                <p className="mt-0.5 text-xs leading-snug text-muted">{step.detail}</p>
              </div>
            </li>
          );
        })}
      </ol>
    </section>
  );
}
