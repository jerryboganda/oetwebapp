import { Gift, PlayCircle, Repeat, Ticket } from 'lucide-react';

// Learner-facing explainer for how Reading / Listening credits are spent.
// Billing rule (backend: paper is the unit — one credit per sample): opening any
// part (A/B/C) OR the full paper of a sample uses ONE credit; the other parts, the
// full paper, and repeat attempts of that same sample are then free. Shown on the
// Reading and Listening hubs so learners understand the rule before they choose.

type CreditModule = 'reading' | 'listening';

const THEME: Record<
  CreditModule,
  {
    unit: string;
    container: string;
    medallion: string;
    eyebrow: string;
    step: string;
    stepIcon: string;
  }
> = {
  reading: {
    unit: 'Reading',
    container:
      'border-info/20 bg-gradient-to-br from-blue-50 to-surface dark:from-blue-950/40 dark:to-surface',
    medallion: 'bg-info/10 text-info',
    eyebrow: 'text-info',
    step: 'border-info/20 bg-white/70 dark:bg-blue-950/30',
    stepIcon: 'text-info',
  },
  listening: {
    unit: 'Listening',
    container:
      'border-primary/20 bg-gradient-to-br from-violet-50 to-surface dark:from-violet-950/40 dark:to-surface',
    medallion: 'bg-lavender text-primary-dark',
    eyebrow: 'text-primary',
    step: 'border-primary/20 bg-white/70 dark:bg-violet-950/30',
    stepIcon: 'text-primary',
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
      className={`rounded-2xl border p-5 shadow-sm sm:p-6 ${theme.container} ${className}`}
    >
      <div className="flex items-start gap-4">
        <span
          className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-xl ${theme.medallion}`}
          aria-hidden
        >
          <Ticket className="h-5 w-5" />
        </span>
        <div className="min-w-0">
          <p className={`text-2xs font-bold uppercase tracking-widest ${theme.eyebrow}`}>
            How your credits work
          </p>
          <h3 className="mt-0.5 text-base font-bold text-navy">
            One {unit} credit unlocks the whole sample
          </h3>
          <p className="mt-1 text-sm text-muted">
            You&apos;re only charged once per sample. The first time you open any part or the full
            paper, it uses a single {unit} credit — everything else in that sample is then free.
          </p>
        </div>
      </div>

      <ol className="mt-4 grid gap-3 sm:grid-cols-3">
        {steps.map((step) => {
          const Icon = step.icon;
          return (
            <li
              key={step.title}
              className={`flex flex-col gap-1.5 rounded-xl border p-3 ${theme.step}`}
            >
              <Icon className={`h-4 w-4 ${theme.stepIcon}`} aria-hidden />
              <p className="text-xs font-bold text-navy">{step.title}</p>
              <p className="text-xs leading-snug text-muted">{step.detail}</p>
            </li>
          );
        })}
      </ol>
    </section>
  );
}
