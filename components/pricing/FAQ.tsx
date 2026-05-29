'use client';

import { useState } from 'react';
import { Minus, Plus } from 'lucide-react';

/**
 * Accessible FAQ accordion for the pricing page. Each item is its own
 * `<details>` so screen readers and keyboard users get standard
 * disclosure semantics for free.
 */

export interface FaqItem {
  question: string;
  answer: string;
}

export interface FAQProps {
  items?: FaqItem[];
}

const DEFAULT_ITEMS: FaqItem[] = [
  {
    question: 'How long do I have access to a recorded course?',
    answer:
      'Most full recorded courses include 180 days of access. Bundles extend to 365 days. The exact window is shown on each plan card.',
  },
  {
    question: 'Can I add writing or speaking assessments later?',
    answer:
      'Yes - add-ons attach to any plan that carries the matching eligibility flag (W or S). Open the add-ons reference below the matrix for the price list.',
  },
  {
    question: 'Do you support refunds?',
    answer:
      'Refund eligibility depends on usage and the time elapsed since purchase. Check the refund policy linked in the footer or contact support.',
  },
  {
    question: 'Which currencies do you bill in?',
    answer:
      'All purchases are billed in GBP. The display currency picker on this page swaps the *shown* price; the underlying charge stays in GBP.',
  },
];

export function FAQ({ items = DEFAULT_ITEMS }: FAQProps) {
  return (
    <section className="border-t border-border bg-surface px-4 py-14">
      <div className="mx-auto max-w-3xl">
        <h2 className="text-2xl font-bold">Frequently asked questions</h2>
        <div className="mt-6 space-y-3">
          {items.map((item, index) => (
            <FAQItem key={index} item={item} />
          ))}
        </div>
      </div>
    </section>
  );
}

function FAQItem({ item }: { item: FaqItem }) {
  const [open, setOpen] = useState(false);
  return (
    <details
      className="group rounded-2xl border border-border bg-surface"
      open={open}
      onToggle={(event) => setOpen((event.target as HTMLDetailsElement).open)}
    >
      <summary className="flex cursor-pointer list-none items-center justify-between gap-3 p-4 text-left font-medium">
        <span>{item.question}</span>
        <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-background-light text-muted">
          {open ? (
            <Minus className="h-4 w-4" aria-hidden="true" />
          ) : (
            <Plus className="h-4 w-4" aria-hidden="true" />
          )}
        </span>
      </summary>
      <p className="px-4 pb-4 text-sm text-muted">{item.answer}</p>
    </details>
  );
}
