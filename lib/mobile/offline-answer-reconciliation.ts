export type OfflineAnswerPayload = {
  questionId: string;
  value: string;
  /** Last server-confirmed value before this offline edit. */
  baseValue: string | null;
};

export type OfflineAnswerReconciliation = 'already-synced' | 'safe-to-replay' | 'conflict';

/**
 * Reconcile without trusting a client timestamp. A queued answer is replayed
 * only when the server still has the value that was confirmed before the
 * offline edit. A different server value is treated as authoritative and is
 * never overwritten by the queue.
 */
export function reconcileOfflineAnswer(
  serverValue: string | null | undefined,
  queued: OfflineAnswerPayload,
): OfflineAnswerReconciliation {
  const current = serverValue ?? null;
  if (current === queued.value) return 'already-synced';
  if (current === queued.baseValue) return 'safe-to-replay';
  return 'conflict';
}
