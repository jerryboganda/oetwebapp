'use client';

// TODO Phase 1: provider picker, model picker, temperature slider,
// approval-policy toggle, kill-switch shortcut (system_admin only).
export function SettingsQuickMenu(): React.JSX.Element {
  return (
    <button
      type="button"
      className="rounded p-1 text-slate-500 hover:bg-slate-100"
      aria-label="Assistant settings"
      disabled
    >
      ⚙
    </button>
  );
}
