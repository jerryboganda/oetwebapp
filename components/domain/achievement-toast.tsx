'use client';

import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { Trophy, X, Sparkles } from 'lucide-react';
import type { Achievement } from '@/lib/types/gamification';

interface AchievementToastProps {
  achievement: Achievement;
  xpAwarded?: number;
  onDismiss: () => void;
  autoDismissMs?: number;
}

export function AchievementToast({
  achievement,
  xpAwarded,
  onDismiss,
  autoDismissMs = 5000,
}: AchievementToastProps) {
  const [visible, setVisible] = useState(true);

  useEffect(() => {
    const timer = setTimeout(() => {
      setVisible(false);
      setTimeout(onDismiss, 300);
    }, autoDismissMs);
    return () => clearTimeout(timer);
  }, [autoDismissMs, onDismiss]);

  const handleDismiss = () => {
    setVisible(false);
    setTimeout(onDismiss, 300);
  };

  return (
    <AnimatePresence>
      {visible && (
        <motion.div
          initial={{ opacity: 0, y: -40, scale: 0.95 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          exit={{ opacity: 0, y: -20, scale: 0.95 }}
          transition={{ type: 'spring', damping: 20, stiffness: 300 }}
          className="fixed top-4 right-4 z-[100] max-w-sm"
        >
          <div className="relative overflow-hidden rounded-2xl border border-yellow-200/60 bg-gradient-to-br from-yellow-50 via-amber-50 to-orange-50 p-4 shadow-xl shadow-yellow-500/10 dark:border-yellow-800/40 dark:from-yellow-950/80 dark:via-amber-950/80 dark:to-orange-950/80">
            {/* Decorative sparkle */}
            <div className="absolute -right-2 -top-2 text-yellow-400/30">
              <Sparkles className="h-16 w-16" />
            </div>

            <div className="relative flex items-start gap-3">
              {/* Icon */}
              <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-yellow-400 to-amber-500 shadow-lg shadow-yellow-500/25">
                {achievement.iconUrl ? (
                  <span
                    aria-hidden="true"
                    className="h-7 w-7 bg-center bg-no-repeat bg-contain"
                    style={{ backgroundImage: `url(${achievement.iconUrl})` }}
                  />
                ) : (
                  <Trophy className="h-6 w-6 text-white" aria-hidden="true" />
                )}
              </div>

              {/* Content */}
              <div className="min-w-0 flex-1">
                <div className="mb-0.5 text-xs font-bold uppercase tracking-widest text-amber-600 dark:text-amber-400">
                  Achievement Unlocked
                </div>
                <h3 className="text-sm font-bold text-gray-900 dark:text-white">
                  {achievement.label}
                </h3>
                <p className="mt-0.5 text-xs text-gray-600 dark:text-gray-300 line-clamp-2">
                  {achievement.description}
                </p>
                {(xpAwarded ?? achievement.xpReward) > 0 && (
                  <div className="mt-1.5 inline-flex items-center gap-1 rounded-full bg-yellow-100 px-2 py-0.5 text-xs font-bold text-yellow-700 dark:bg-yellow-900/40 dark:text-yellow-300">
                    <Sparkles className="h-3 w-3" />+{xpAwarded ?? achievement.xpReward} XP
                  </div>
                )}
              </div>

              {/* Dismiss */}
              <button
                type="button"
                onClick={handleDismiss}
                className="shrink-0 rounded-lg p-1 text-gray-400 transition-colors hover:bg-yellow-100 hover:text-gray-600 dark:hover:bg-yellow-900/30"
                aria-label="Dismiss achievement notification"
              >
                <X className="h-4 w-4" />
              </button>
            </div>

            {/* Progress bar (auto-dismiss indicator) */}
            <motion.div
              initial={{ scaleX: 1 }}
              animate={{ scaleX: 0 }}
              transition={{ duration: autoDismissMs / 1000, ease: 'linear' }}
              className="absolute bottom-0 left-0 h-0.5 w-full origin-left bg-gradient-to-r from-yellow-400 to-amber-500"
            />
          </div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
