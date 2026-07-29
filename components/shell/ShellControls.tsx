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
 * Expanded: smoothly slides open into a vertical stacked list card displaying icon + title + description.
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
            aria-label="Open quick access menu"
            title="Quick access controls"
            className="group flex h-10 items-center gap-1 rounded-l-full border-y border-l border-primary/25 bg-lavender/90 py-1.5 pl-2.5 pr-1.5 text-primary shadow-lg shadow-primary/15 backdrop-blur-md transition-all hover:border-primary/40 hover:bg-lavender hover:shadow-primary/25 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary active:scale-95 motion-reduce:active:scale-100 dark:border-white/20 dark:bg-slate-900/90 dark:text-violet-200 dark:hover:bg-slate-900"
          >
            <ChevronLeft className="h-4 w-4 transition-transform group-hover:-translate-x-0.5" aria-hidden="true" />
            <div className="flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 dark:bg-white/10">
              <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
            </div>
          </button>
        ) : (
          /* Expanded Stacked Vertical Card Menu */
          <div
            aria-expanded={true}
            className="mr-2 w-64 rounded-2xl border border-primary/25 bg-lavender/95 p-2.5 shadow-2xl shadow-primary/20 backdrop-blur-xl animate-in slide-in-from-right-4 duration-200 dark:border-white/20 dark:bg-slate-900/95 dark:shadow-black/60"
          >
            {/* Menu Header */}
            <div className="mb-2 flex items-center justify-between border-b border-primary/15 pb-2 pl-1 pr-0.5 dark:border-white/15">
              <span className="text-xs font-semibold uppercase tracking-wider text-primary/80 dark:text-violet-200/80">
                Quick Access
              </span>
              <button
                type="button"
                onClick={() => setIsExpanded(false)}
                aria-label="Collapse quick access menu"
                title="Close menu"
                className="flex h-6 w-6 items-center justify-center rounded-full text-primary/70 transition-colors hover:bg-primary/15 hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary dark:text-violet-300/70 dark:hover:bg-white/15 dark:hover:text-violet-200"
              >
                <ChevronRight className="h-4 w-4" aria-hidden="true" />
              </button>
            </div>

            {/* Stacked Menu Options */}
            <div className="flex flex-col gap-1">
              <MenuItem
                icon={<RefreshCw className="h-4 w-4" />}
                title="Reload App"
                description="Fetch latest content & settings"
                onClick={() => {
                  setIsExpanded(false);
                  void hardReload();
                }}
              />

              <MenuItem
                icon={<ArrowDownToLine className="h-4 w-4" />}
                title="Check for Updates"
                description="Download latest app version"
                onClick={() => {
                  setIsExpanded(false);
                  setUpdateOpen(true);
                }}
              />
            </div>
          </div>
        )}
      </div>

      <UpdateDialog open={updateOpen} onClose={() => setUpdateOpen(false)} />
    </>
  );
}

function MenuItem({
  icon,
  title,
  description,
  onClick,
}: {
  icon: ReactNode;
  title: string;
  description: string;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="group flex w-full items-start gap-3 rounded-xl p-2.5 text-left transition-all hover:bg-primary/12 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary active:scale-[0.98] motion-reduce:active:scale-100 dark:hover:bg-white/12"
    >
      <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary transition-colors group-hover:bg-primary group-hover:text-white dark:bg-white/10 dark:text-violet-200 dark:group-hover:bg-violet-500 dark:group-hover:text-white">
        {icon}
      </div>
      <div className="flex flex-col min-w-0">
        <span className="text-sm font-medium leading-tight text-primary dark:text-violet-100">
          {title}
        </span>
        <span className="mt-0.5 text-xs text-muted-foreground line-clamp-1">
          {description}
        </span>
      </div>
    </button>
  );
}
