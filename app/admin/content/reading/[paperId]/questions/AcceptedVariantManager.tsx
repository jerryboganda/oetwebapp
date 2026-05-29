'use client';

import { useState } from 'react';
import { Plus, X } from 'lucide-react';

import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import {
  addVariant,
  removeVariant,
  VARIANT_CATEGORY_OPTIONS,
  type AcceptedVariant,
  type VariantCategory,
} from './accepted-variants';

interface AcceptedVariantManagerProps {
  variants: AcceptedVariant[];
  onChange: (next: AcceptedVariant[]) => void;
}

const CATEGORY_LABELS = new Map<VariantCategory, string>(
  VARIANT_CATEGORY_OPTIONS.map((opt) => [opt.value, opt.label]),
);

export function AcceptedVariantManager({ variants, onChange }: AcceptedVariantManagerProps) {
  const [draft, setDraft] = useState('');
  const [category, setCategory] = useState<VariantCategory>('other');

  function handleAdd() {
    const next = addVariant(variants, draft, category);
    if (next !== variants) {
      onChange(next);
      setDraft('');
      setCategory('other');
    }
  }

  return (
    <div className="space-y-3">
      <p className="text-sm font-semibold tracking-tight text-navy">Accepted variants</p>
      <p className="text-xs leading-5 text-muted">
        Alternative answers the grader will accept. Category is an authoring hint only — grading
        compares the text value.
      </p>

      {variants.length > 0 ? (
        <ul className="flex flex-wrap gap-2" aria-label="Accepted variants">
          {variants.map((variant) => (
            <li key={variant.id}>
              <Badge variant="secondary" className="gap-1.5 pr-1">
                <span>{variant.value}</span>
                {variant.category !== 'other' && (
                  <span className="text-[10px] uppercase tracking-wide opacity-70">
                    {CATEGORY_LABELS.get(variant.category)}
                  </span>
                )}
                <button
                  type="button"
                  onClick={() => onChange(removeVariant(variants, variant.id))}
                  className="ml-0.5 rounded-full p-0.5 hover:bg-navy/10 focus:outline-none focus:ring-2 focus:ring-primary/40"
                  aria-label={`Remove ${variant.value}`}
                >
                  <X className="h-3 w-3" />
                </button>
              </Badge>
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-xs text-muted">No accepted variants yet.</p>
      )}

      <div className="flex flex-wrap items-end gap-2">
        <div className="flex-1 min-w-[10rem]">
          <Input
            label="Add variant"
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault();
                handleAdd();
              }
            }}
            placeholder="e.g. colour"
          />
        </div>
        <div className="w-40">
          <Select
            label="Category hint"
            value={category}
            onChange={(e) => setCategory(e.target.value as VariantCategory)}
            options={VARIANT_CATEGORY_OPTIONS.map((opt) => ({ value: opt.value, label: opt.label }))}
          />
        </div>
        <Button
          type="button"
          variant="secondary"
          size="md"
          onClick={handleAdd}
          disabled={!draft.trim()}
          startIcon={<Plus className="h-4 w-4" />}
        >
          Add
        </Button>
      </div>
    </div>
  );
}
