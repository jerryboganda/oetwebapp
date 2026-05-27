'use client';

import { Quote } from 'lucide-react';

/**
 * Lightweight three-card testimonial strip for the pricing page. Static
 * content for now — when a CMS or testimonials backend ships, swap the
 * default `items` prop for the live data.
 */

export interface Testimonial {
  quote: string;
  name: string;
  role: string;
  band?: string;
}

export interface TestimonialsProps {
  items?: Testimonial[];
}

const DEFAULT_ITEMS: Testimonial[] = [
  {
    quote:
      'The writing letter assessments alone are worth the price. The feedback hit every weak point I had.',
    name: 'Dr. Amira',
    role: 'Medicine - Cairo',
    band: 'B (Writing)',
  },
  {
    quote:
      'Speaking sessions felt like the real test. The interlocutor was patient and the scoring was honest.',
    name: 'Nurse Lara',
    role: 'Nursing - Manila',
    band: 'B (Speaking)',
  },
  {
    quote:
      'I cleared OET on my second attempt after Dr. Ahmed\'s crash course. The pacing was perfect.',
    name: 'Dr. Hassan',
    role: 'Medicine - Riyadh',
    band: 'B (Overall)',
  },
];

export function Testimonials({ items = DEFAULT_ITEMS }: TestimonialsProps) {
  return (
    <section className="border-t border-border px-4 py-14">
      <div className="mx-auto max-w-5xl">
        <h2 className="text-2xl font-bold">What candidates say</h2>
        <div className="mt-6 grid gap-5 md:grid-cols-3">
          {items.map((item) => (
            <figure
              key={item.name}
              className="rounded-2xl border border-border bg-background p-5 shadow-sm"
            >
              <Quote className="h-5 w-5 text-[#D4A44F]" aria-hidden="true" />
              <blockquote className="mt-3 text-sm text-navy">{item.quote}</blockquote>
              <figcaption className="mt-4 text-xs text-muted">
                <span className="font-semibold text-navy">{item.name}</span> - {item.role}
                {item.band ? <span className="ml-1.5 text-[#996F1F]">- {item.band}</span> : null}
              </figcaption>
            </figure>
          ))}
        </div>
      </div>
    </section>
  );
}
