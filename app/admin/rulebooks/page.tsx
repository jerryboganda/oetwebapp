'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { BookOpen, Plus } from 'lucide-react';
import { adminListRulebooks, type AdminRulebookSummary } from '@/lib/api';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

const KINDS = ['', 'speaking', 'writing', 'reading', 'listening'];
const PROFESSIONS = ['', 'medicine', 'nursing', 'dentistry', 'pharmacy', 'physiotherapy', 'occupationaltherapy', 'optometry', 'podiatry', 'radiography', 'speech', 'veterinary'];

export default function AdminRulebooksListPage() {
  const [items, setItems] = useState<AdminRulebookSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [kind, setKind] = useState('');
  const [profession, setProfession] = useState('');

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const data = await adminListRulebooks({ kind: kind || undefined, profession: profession || undefined });
      setItems(data || []);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Failed to load rulebooks.' });
    } finally {
      setLoading(false);
    }
  }, [kind, profession]);

  useEffect(() => { queueMicrotask(() => void reload()); }, [reload]);

  const grouped = useMemo(() => {
    const map = new Map<string, AdminRulebookSummary[]>();
    for (const r of items) {
      const k = `${r.kind}/${r.profession}`;
      if (!map.has(k)) map.set(k, []);
      map.get(k)!.push(r);
    }
    return Array.from(map.entries()).sort(([a], [b]) => a.localeCompare(b));
  }, [items]);

  return (
    <div className="space-y-6 p-4 sm:p-6">
      <header className="flex flex-wrap items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <BookOpen className="h-7 w-7 text-blue-600" />
          <div>
            <h1 className="text-2xl font-bold">Rulebooks</h1>
            <p className="text-sm text-gray-600">Manage grading rules for Speaking, Writing, Reading, and Listening.</p>
          </div>
        </div>
      </header>

      <Card className="p-4">
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
          <Select
            label="Kind"
            value={kind}
            onChange={(e) => setKind(e.target.value)}
            options={KINDS.map((k) => ({ value: k, label: k || 'All' }))}
          />
          <Select
            label="Profession"
            value={profession}
            onChange={(e) => setProfession(e.target.value)}
            options={PROFESSIONS.map((p) => ({ value: p, label: p || 'All' }))}
          />
          <div className="flex items-end">
            <Button onClick={() => void reload()} variant="outline">Refresh</Button>
          </div>
        </div>
      </Card>

      {loading ? (
        <div className="space-y-3">
          {[1, 2, 3].map((i) => <Skeleton key={i} className="h-20" />)}
        </div>
      ) : grouped.length === 0 ? (
        <Card className="p-8 text-center text-gray-500">No rulebooks found.</Card>
      ) : (
        <div className="space-y-3">
          {grouped.map(([key, group]) => (
            <Card key={key} className="p-4">
              <div className="flex items-start justify-between flex-wrap gap-3 mb-3">
                <div>
                  <h2 className="font-semibold text-lg capitalize">{key.replace('/', ' · ')}</h2>
                  <p className="text-xs text-gray-500">{group.length} version(s)</p>
                </div>
              </div>
              <div className="space-y-2">
                {group.map((r) => (
                  <Link key={r.id} href={`/admin/rulebooks/${encodeURIComponent(r.id)}`}
                    className="flex items-center justify-between p-3 rounded border hover:bg-gray-50 transition">
                    <div className="flex items-center gap-3 flex-wrap">
                      <span className="font-mono text-sm">{r.version}</span>
                      <Badge variant={r.status === 'Published' ? 'success' : r.status === 'Draft' ? 'muted' : 'outline'}>
                        {r.status}
                      </Badge>
                      <span className="text-xs text-gray-500">
                        {r.sectionCount} sections · {r.ruleCount} rules
                      </span>
                    </div>
                    <span className="text-xs text-gray-400">
                      Updated {new Date(r.updatedAt).toLocaleDateString()}
                    </span>
                  </Link>
                ))}
              </div>
            </Card>
          ))}
        </div>
      )}

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </div>
  );
}
