'use client';

import { useEffect, useRef, useState, type ReactNode } from 'react';
import { ArrowDownToLine, ChevronLeft, ChevronRight, RefreshCw } from 'lucide-react';
import { getAppRuntimeKind } from '@/lib/runtime-signals';
import { hardReload } from '@/lib/shell/hard-reload';
import { useAppVersionGate } from '@/app/providers/AppVersionGateProvider';
import { UpdateDialog } from './UpdateDialog';

/**
 * Quick access toolbar pinned to the RIGHT side of the screen in the lower 1/3rd.
 * Shown only inside the desktop/mobile shells (never on the website). Styled in the
 * platform's lavender/violet brand theme.
 *
 * Collapsed by default: displays a floating right-edge handle trigger button.
 * Expanded: smoothly slides open to reveal "Reload" (hard-reload: drop caches,
 * re-fetch fresh settings from server), "Check for updates" (animated UpdateDialog),
 * and a collapse toggle handle.
 */
export function ShellControls() {
  const [mounted, setMounted] = useState(false);
  const [isExpanded, setIsExpanded] = useState(false);
  const [updateOpen, setUpdateOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  const { blocked } = useAppVersionGate();

  useEffect(() => setMounted(true), []);

  // Collapse on click outside or Escape key press
  useEffect(() => {
    if (!isExpanded) return;

    function handleClickOutside(event: MouseEvent | TouchEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setIsExpanded(false);
      }
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setIsExpanded(false);
      }
    }

    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('touchstart', handleClickOutside);
    document.addEventListener('keydown', handleKeyDown);

    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('touchstart', handleClickOutside);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [isExpanded]);

  if (!mounted || getAppRuntimeKind() === 'web' || blocked) return null;

  return (
    <>
      <div
        ref={containerRef}
        className="fixed right-0 top-[65%] z-[60] -translate-y-1/2 transition-all duration-300 ease-out"
        style={{ paddingRight: 'env(safe-area-inset-right)' }}
      >
        {!isExpanded ? (
          /* Collapsed Floating Trigger Handle on Right Edge */
          <button
            type="button"
            onClick={() => setIsExpanded(true)}
            aria-expanded={false}
            aria-label="Open quick access toolbar"
            title="Quick access controls"
            className="group flex h-10 items-center gap-1 rounded-l-full border-y border-l border-primary/25 bg-lavender/90 py-1.5 pl-2.5 pr-1.5 text-primary shadow-lg shadow-primary/15 backdrop-blur-md transition-all hover:border-primary/40 hover:bg-lavender hover:shadow-primary/25 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary active:scale-95 motion-reduce:active:scale-100 dark:border-white/20 dark:bg-slate-900/90 dark:text-violet-200 dark:hover:bg-slate-900"
          >
            <ChevronLeft className="h-4 w-4 transition-transform group-hover:-translate-x-0.5" aria-hidden="true" />
            <div className="flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 dark:bg-white/10">
              <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
            </div>
          </button>
        ) : (
          /* Expanded Quick Access Toolbar Panel */
          <div
            aria-expanded={true}
            className="flex items-center gap-1.5 rounded-l-full border-y border-l border-primary/30 bg-lavender/95 py-1.5 pl-2.5 pr-1.5 shadow-xl shadow-primary/20 backdrop-blur-md animate-in slide-in-from-right-3 duration-200 dark:border-white/20 dark:bg-slate-900/95 dark:shadow-black/50"
          >
            <IconButton
              label="Reload (fetch latest from server)"
              onClick={() => void hardReload()}
            >
              <RefreshCw className="h-4 w-4" aria-hidden="true" />
            </IconButton>

            <IconButton
              label="Check for updates"
              onClick={() => setUpdateOpen(true)}
            >
              <ArrowDownToLine className="h-4 w-4" aria-hidden="true" />
            </IconButton>

            <div className="mx-0.5 h-4 w-px bg-primary/20 dark:bg-white/20" aria-hidden="true" />

            <IconButton
              label="Collapse quick access toolbar"
              onClick={() => setIsExpanded(false)}
            >
              <ChevronRight className="h-4 w-4" aria-hidden="true" />
            </IconButton>
          </div>
        )}
      </div>

      <UpdateDialog open={updateOpen} onClose={() => setUpdateOpen(false)} />
    </>
  );
}

function IconButton({ label, onClick, children }: { label: string; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={label}
      title={label}
      className="flex h-8 w-8 items-center justify-center rounded-full text-primary transition-colors hover:bg-primary/15 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary active:scale-95 motion-reduce:active:scale-100 dark:text-violet-200 dark:hover:bg-white/15"
    >
      {children}
    </button>
  );
}
