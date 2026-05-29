'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowRight, ClipboardCheck, RotateCw, Zap } from 'lucide-react';
import { AnimatePresence, motion } from 'motion/react';
import { apiClient } from '@/lib/api';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';

interface NextAction {
  kind: string;
  title: string;
  description: string;
  route: string;
  priority: number;
  dueAt: string | null;
  category: 'retention' | 'assessment' | 'practice';
}

interface LearnerNextActionsResponse {
  actions: NextAction[];
  generatedAt: string;
}

const categoryIcons = {
  retention: RotateCw,
  assessment: ClipboardCheck,
  practice: Zap,
} as const;

const categoryColors = {
  retention: 'bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400',
  assessment: 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400',
  practice: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400',
} as const;

export function NextActionCard() {
  const [data, setData] = useState<LearnerNextActionsResponse | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    apiClient
      .get<LearnerNextActionsResponse>('/v1/learner/next-actions')
      .then((res) => {
        if (!cancelled) setData(res);
      })
      .catch(() => {
        if (!cancelled) setData(null);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, []);

  if (loading) {
    return (
      <Card padding="md">
        <div className="flex items-start gap-4">
          <Skeleton variant="circle" className="h-10 w-10 shrink-0" />
          <div className="flex-1 space-y-2">
            <Skeleton className="h-5 w-2/3" />
            <Skeleton className="h-4 w-full" />
            <Skeleton className="mt-3 h-10 w-32 rounded-xl" />
          </div>
        </div>
      </Card>
    );
  }

  const topAction = data?.actions?.[0];

  if (!topAction) {
    return (
      <Card padding="md">
        <div className="flex flex-col items-center py-4 text-center">
          <Zap className="mb-2 h-8 w-8 text-muted" />
          <p className="text-sm text-muted">No actions right now. You're all caught up!</p>
        </div>
      </Card>
    );
  }

  const Icon = categoryIcons[topAction.category] ?? Zap;
  const iconColor = categoryColors[topAction.category] ?? categoryColors.practice;

  return (
    <AnimatePresence mode="wait">
      <motion.div
        initial={{ opacity: 0, y: 8 }}
        animate={{ opacity: 1, y: 0 }}
        exit={{ opacity: 0, y: -8 }}
        transition={{ duration: 0.2 }}
      >
        <Card padding="md" hoverable>
          <div className="flex items-start gap-4">
            <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${iconColor}`}>
              <Icon className="h-5 w-5" />
            </div>
            <div className="min-w-0 flex-1">
              <h3 className="text-base font-semibold text-navy">{topAction.title}</h3>
              <p className="mt-1 text-sm text-muted line-clamp-2">{topAction.description}</p>
              <div className="mt-3 flex items-center gap-3">
                <Button asChild variant="primary" size="sm">
                  <Link href={topAction.route}>
                    Start now
                    <ArrowRight className="ml-1 h-4 w-4" />
                  </Link>
                </Button>
                <Link
                  href="/next-actions"
                  className="text-sm font-medium text-primary hover:underline"
                >
                  See all
                </Link>
              </div>
            </div>
          </div>
        </Card>
      </motion.div>
    </AnimatePresence>
  );
}
