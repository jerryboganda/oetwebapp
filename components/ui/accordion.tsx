'use client';

import { cn } from '@/lib/utils';
import { ChevronDown } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { motion, useReducedMotion } from 'motion/react';
import { motionTokens, prefersReducedMotion } from '@/lib/motion';
import { MotionCollapse } from './motion-primitives';

interface AccordionItem {
  id: string;
  title: ReactNode;
  content: ReactNode;
  defaultOpen?: boolean;
}

interface AccordionProps {
  items: AccordionItem[];
  allowMultiple?: boolean;
  className?: string;
}

export function Accordion({ items, allowMultiple = false, className }: AccordionProps) {
  const [openIds, setOpenIds] = useState<Set<string>>(
    new Set(items.filter((i) => i.defaultOpen).map((i) => i.id)),
  );
  const reducedMotion = prefersReducedMotion(useReducedMotion());

  const toggle = (id: string) => {
    setOpenIds((prev) => {
      const next = new Set(allowMultiple ? prev : []);
      if (prev.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  return (
    <div className={cn('divide-y divide-gray-200 border border-gray-200 rounded dark:divide-gray-700 dark:border-gray-700', className)}>
      {items.map((item) => {
        const isOpen = openIds.has(item.id);
        return (
          <div key={item.id}>
            <button
              type="button"
              id={`accordion-button-${item.id}`}
              onClick={() => toggle(item.id)}
              aria-expanded={isOpen}
              aria-controls={`accordion-panel-${item.id}`}
              className="flex items-center justify-between w-full px-5 py-4 text-left font-semibold text-navy hover:bg-gray-50 dark:hover:bg-gray-800 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-inset"
            >
              <span>{item.title}</span>
              <motion.span
                animate={{ rotate: isOpen ? 180 : 0 }}
                transition={
                  reducedMotion
                    ? { duration: motionTokens.duration.instant }
                    : { type: 'spring', stiffness: 500, damping: 30 }
                }
                className="inline-flex"
              >
                <ChevronDown className="w-4 h-4 text-muted" />
              </motion.span>
            </button>
            <MotionCollapse
              open={isOpen}
              id={`accordion-panel-${item.id}`}
              role="region"
              aria-labelledby={`accordion-button-${item.id}`}
            >
              <div className="px-5 pb-4 text-sm text-navy/80">
                {item.content}
              </div>
            </MotionCollapse>
          </div>
        );
      })}
    </div>
  );
}
