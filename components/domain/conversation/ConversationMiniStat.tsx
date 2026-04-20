'use client';

import type { LucideIcon } from 'lucide-react';

interface Props {
  icon: LucideIcon;
  label: string;
  value: string;
}

export function ConversationMiniStat({ icon: Icon, label, value }: Props) {
  return (
    <div className="flex items-center gap-2 rounded-2xl border border-border bg-surface px-3 py-2 text-xs shadow-sm">
      <Icon className="h-3.5 w-3.5 text-primary" />
      <span className="font-semibold uppercase tracking-wide text-muted">{label}</span>
      <span className="text-navy">{value}</span>
    </div>
  );
}
