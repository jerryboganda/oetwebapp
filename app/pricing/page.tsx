'use client';

import { useEffect, useState } from 'react';
import { motion, useReducedMotion } from 'motion/react';
import {
  ArrowRight,
  CheckCircle2,
  Headphones,
  Mic,
  FilePenLine,
  BookOpen,
  Star,
  ShieldCheck,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import Link from 'next/link';

interface Plan {
  planId: string;
  code: string;
  label: string;
  tier: string;
  description: string;
  price: { amount: number; currency: string; interval: string };
  reviewCredits: number;
  mockReportsIncluded: boolean;
  includedSubtests: string[];
  trialDays: number;
  isRenewable: boolean;
  changeDirection: string;
}

async function fetchPublicPlans(): Promise<{ items: Plan[] }> {
  const res = await fetch('/v1/public/plans', { cache: 'no-store' });
  if (!res.ok) throw new Error('Failed to load plans');
  return res.json();
}

export default function PricingPage() {
  const reducedMotion = useReducedMotion();
  const [plans, setPlans] = useState<Plan[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchPublicPlans()
      .then((data) => setPlans(data.items))
      .catch(() => setError('Unable to load pricing. Please refresh.'))
      .finally(() => setLoading(false));
  }, []);

  return (
    <div className="min-h-screen bg-background">
      {/* Hero */}
      <section className="relative overflow-hidden px-4 pb-16 pt-20 text-center">
        <motion.div
          initial={reducedMotion ? false : { opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.5 }}
        >
          <h1 className="mx-auto max-w-3xl text-4xl font-black tracking-tight text-navy sm:text-5xl">
            Prepare for OET, IELTS & PTE with confidence
          </h1>
          <p className="mx-auto mt-4 max-w-2xl text-lg text-muted">
            AI-powered practice, expert tutor reviews, and personalised study plans
            — all in one place.
          </p>
          <div className="mt-6 flex flex-wrap items-center justify-center gap-3 text-sm text-muted">
            <span className="inline-flex items-center gap-1.5">
              <CheckCircle2 className="h-4 w-4 text-emerald-600" /> AI evaluation
            </span>
            <span className="inline-flex items-center gap-1.5">
              <CheckCircle2 className="h-4 w-4 text-emerald-600" /> Expert reviews
            </span>
            <span className="inline-flex items-center gap-1.5">
              <CheckCircle2 className="h-4 w-4 text-emerald-600" /> Mock exams
            </span>
            <span className="inline-flex items-center gap-1.5">
              <CheckCircle2 className="h-4 w-4 text-emerald-600" /> Study plans
            </span>
          </div>
        </motion.div>
      </section>

      {/* Plans */}
      <section className="px-4 pb-24">
        <div className="mx-auto max-w-6xl">
          {loading ? (
            <div className="grid gap-6 md:grid-cols-3">
              {[0, 1, 2].map((i) => (
                <div
                  key={i}
                  className="h-96 animate-pulse rounded-2xl border border-border bg-surface"
                />
              ))}
            </div>
          ) : error ? (
            <div className="rounded-2xl border border-border bg-surface p-8 text-center text-muted">
              {error}
            </div>
          ) : (
            <div className="grid gap-6 md:grid-cols-3">
              {plans.map((plan, index) => (
                <motion.article
                  key={plan.planId}
                  initial={reducedMotion ? false : { opacity: 0, y: 16 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.35, delay: index * 0.08 }}
                  className="flex flex-col rounded-2xl border border-border bg-surface p-6 shadow-sm"
                >
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <p className="text-[11px] font-black uppercase tracking-widest text-muted">
                        {plan.tier}
                      </p>
                      <h3 className="mt-1 text-2xl font-black text-navy">
                        {plan.label}
                      </h3>
                    </div>
                    {plan.changeDirection === 'current' && (
                      <span className="rounded-full bg-primary/10 px-2.5 py-1 text-[10px] font-black uppercase tracking-widest text-primary">
                        Popular
                      </span>
                    )}
                  </div>

                  <p className="mt-3 text-sm leading-6 text-muted">
                    {plan.description}
                  </p>

                  <div className="mt-5 rounded-xl border border-border bg-background-light p-4">
                    <p className="text-3xl font-black text-navy">
                      {new Intl.NumberFormat('en-AU', {
                        style: 'currency',
                        currency: plan.price.currency || 'AUD',
                      }).format(plan.price.amount)}
                      <span className="ml-1 text-sm font-semibold text-muted">
                        / {plan.price.interval}
                      </span>
                    </p>
                    {plan.reviewCredits > 0 && (
                      <p className="mt-1 text-sm text-muted">
                        {plan.reviewCredits} expert review credits included
                      </p>
                    )}
                  </div>

                  <ul className="mt-6 space-y-2.5 text-sm text-navy">
                    {plan.includedSubtests && plan.includedSubtests.length > 0 && (
                      <li className="flex items-start gap-2">
                        <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-emerald-600" />
                        <span>
                          Tutor reviews for{' '}
                          <strong>{plan.includedSubtests.join(' & ')}</strong>
                        </span>
                      </li>
                    )}
                    {plan.mockReportsIncluded && (
                      <li className="flex items-start gap-2">
                        <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-emerald-600" />
                        <span>Full mock exam reports</span>
                      </li>
                    )}
                    {plan.trialDays > 0 && (
                      <li className="flex items-start gap-2">
                        <Star className="mt-0.5 h-4 w-4 flex-none text-amber-500" />
                        <span>{plan.trialDays}-day free trial</span>
                      </li>
                    )}
                    <li className="flex items-start gap-2">
                      <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-emerald-600" />
                      <span>AI-generated feedback</span>
                    </li>
                    <li className="flex items-start gap-2">
                      <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-emerald-600" />
                      <span>Personalised study plan</span>
                    </li>
                    <li className="flex items-start gap-2">
                      <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-emerald-600" />
                      <span>Progress tracking</span>
                    </li>
                  </ul>

                  <div className="mt-auto pt-6">
                    <Link href="/register">
                      <Button variant="primary" className="w-full">
                        Get started
                        <ArrowRight className="ml-1.5 h-4 w-4" />
                      </Button>
                    </Link>
                  </div>
                </motion.article>
              ))}
            </div>
          )}
        </div>
      </section>

      {/* Features grid */}
      <section className="border-t border-border bg-surface px-4 py-20">
        <div className="mx-auto max-w-6xl">
          <h2 className="text-center text-2xl font-black text-navy">
            Everything you need to pass
          </h2>
          <div className="mt-10 grid gap-6 sm:grid-cols-2 lg:grid-cols-4">
            {[
              {
                icon: FilePenLine,
                title: 'Writing practice',
                desc: 'Letter & essay tasks with AI scoring against official rubrics.',
              },
              {
                icon: Mic,
                title: 'Speaking mock sets',
                desc: 'Two-roleplay mock sessions with band estimates and fluency feedback.',
              },
              {
                icon: Headphones,
                title: 'Listening & Reading',
                desc: 'Part-A to Part-C practice with distractor-aware question sets.',
              },
              {
                icon: BookOpen,
                title: 'Study plans',
                desc: 'Auto-generated weekly plans based on your weakest criteria.',
              },
              {
                icon: ShieldCheck,
                title: 'Score guarantee',
                desc: 'Conditional score-back pledge for qualifying subscribers.',
              },
              {
                icon: Star,
                title: 'Expert reviews',
                desc: 'Human tutor feedback on writing and speaking submissions.',
              },
              {
                icon: CheckCircle2,
                title: 'Mock exams',
                desc: 'Timed full-subtest mocks with auto-scored reports.',
              },
              {
                icon: CheckCircle2,
                title: 'Credit top-ups',
                desc: 'Buy extra review credits whenever you need them.',
              },
            ].map((f, i) => (
              <motion.div
                key={f.title}
                initial={reducedMotion ? false : { opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.3, delay: i * 0.04 }}
                className="rounded-xl border border-border bg-background p-5"
              >
                <f.icon className="h-6 w-6 text-primary" />
                <h4 className="mt-3 text-sm font-black text-navy">{f.title}</h4>
                <p className="mt-1 text-sm text-muted">{f.desc}</p>
              </motion.div>
            ))}
          </div>
        </div>
      </section>

      {/* CTA */}
      <section className="px-4 py-20 text-center">
        <div className="mx-auto max-w-2xl">
          <h2 className="text-3xl font-black text-navy">
            Start your free trial today
          </h2>
          <p className="mt-3 text-muted">
            No credit card required. Cancel anytime.
          </p>
          <div className="mt-6 flex flex-wrap items-center justify-center gap-3">
            <Link href="/register">
              <Button variant="primary" size="lg">
                Create free account
                <ArrowRight className="ml-1.5 h-4 w-4" />
              </Button>
            </Link>
            <Link href="/sign-in">
              <Button variant="outline" size="lg">
                Sign in
              </Button>
            </Link>
          </div>
        </div>
      </section>
    </div>
  );
}
