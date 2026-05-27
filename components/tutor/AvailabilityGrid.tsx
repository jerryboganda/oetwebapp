'use client';

import { useMemo, useState } from 'react';
import { Plus, Trash2 } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import type { TutorAvailabilitySlot, TutorAvailabilityUpsertPayload, DayOfWeekString } from '@/lib/api';

const DAYS: DayOfWeekString[] = [
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
];

function dayName(day: number | DayOfWeekString): DayOfWeekString {
  if (typeof day === 'string') return day;
  return DAYS[day] ?? 'Sunday';
}

function dayIndex(day: number | DayOfWeekString): number {
  if (typeof day === 'number') return day;
  return DAYS.indexOf(day);
}

function normalizeTime(value: string): string {
  // backend sends HH:mm:ss; input value uses HH:mm
  if (!value) return '00:00';
  return value.length >= 5 ? value.slice(0, 5) : value;
}

function toApiTime(value: string): string {
  // HH:mm -> HH:mm:00
  if (value.length === 5) return `${value}:00`;
  return value;
}

interface DraftSlot {
  id: string;
  dayOfWeek: DayOfWeekString;
  startTime: string; // HH:mm
  endTime: string; // HH:mm
  isActive: boolean;
}

export interface AvailabilityGridProps {
  slots: TutorAvailabilitySlot[];
  timeZone: string;
  saving: boolean;
  onSave: (slots: TutorAvailabilityUpsertPayload[]) => Promise<void>;
}

export function AvailabilityGrid({ slots, timeZone, saving, onSave }: AvailabilityGridProps) {
  const initial = useMemo<DraftSlot[]>(
    () =>
      slots.map((slot, i) => ({
        id: slot.id ?? `slot-${i}`,
        dayOfWeek: dayName(slot.dayOfWeek),
        startTime: normalizeTime(slot.startTime),
        endTime: normalizeTime(slot.endTime),
        isActive: slot.isActive,
      })),
    [slots],
  );

  const [drafts, setDrafts] = useState<DraftSlot[]>(initial);

  function add() {
    setDrafts((prev) => [
      ...prev,
      {
        id: `new-${prev.length}-${Date.now()}`,
        dayOfWeek: 'Monday',
        startTime: '09:00',
        endTime: '17:00',
        isActive: true,
      },
    ]);
  }

  function update(idx: number, patch: Partial<DraftSlot>) {
    setDrafts((prev) => prev.map((s, i) => (i === idx ? { ...s, ...patch } : s)));
  }

  function remove(idx: number) {
    setDrafts((prev) => prev.filter((_, i) => i !== idx));
  }

  async function save() {
    const payload: TutorAvailabilityUpsertPayload[] = drafts.map((d) => ({
      dayOfWeek: dayIndex(d.dayOfWeek),
      startTime: toApiTime(d.startTime),
      endTime: toApiTime(d.endTime),
      isActive: d.isActive,
    }));
    await onSave(payload);
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-1 rounded-2xl border border-border bg-surface p-4 shadow-sm sm:flex-row sm:items-center sm:justify-between">
        <div>
          <p className="text-sm font-semibold text-navy">Weekly availability</p>
          <p className="text-xs text-muted">All times shown in your time zone: <span className="font-medium text-navy">{timeZone}</span></p>
        </div>
        <Button type="button" variant="outline" size="sm" onClick={add}>
          <Plus className="h-4 w-4" /> Add slot
        </Button>
      </div>

      {drafts.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-border bg-surface p-8 text-center text-sm text-muted">
          No availability set. Add slots to indicate when you can teach.
        </div>
      ) : (
        <div className="space-y-3">
          {drafts.map((slot, idx) => (
            <div key={slot.id} className="grid gap-3 rounded-2xl border border-border bg-surface p-4 shadow-sm sm:grid-cols-[1.5fr_1fr_1fr_1fr_auto] sm:items-end">
              <Select
                label="Day"
                value={slot.dayOfWeek}
                onChange={(e) => update(idx, { dayOfWeek: e.target.value as DayOfWeekString })}
                options={DAYS.map((d) => ({ value: d, label: d }))}
              />
              <Input
                type="time"
                label="Start"
                value={slot.startTime}
                onChange={(e) => update(idx, { startTime: e.target.value })}
              />
              <Input
                type="time"
                label="End"
                value={slot.endTime}
                onChange={(e) => update(idx, { endTime: e.target.value })}
              />
              <label className="flex items-center gap-2 self-end pb-3 text-sm text-navy">
                <input
                  type="checkbox"
                  checked={slot.isActive}
                  onChange={(e) => update(idx, { isActive: e.target.checked })}
                  className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
                />
                Active
              </label>
              <Button type="button" variant="ghost" size="sm" onClick={() => remove(idx)}>
                <Trash2 className="h-3.5 w-3.5" />
              </Button>
            </div>
          ))}
        </div>
      )}

      <div className="flex justify-end">
        <Button type="button" variant="primary" loading={saving} onClick={() => void save()}>
          Save availability
        </Button>
      </div>
    </div>
  );
}
