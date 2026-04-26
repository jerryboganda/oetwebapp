'use client';

import { useEffect, useState } from 'react';
import { Download } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { exportToCsv, formatDateForExport } from '@/lib/csv-export';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

interface ContentItem { contentId: string; title: string; subtestCode: string; difficulty: string; totalAttempts: number; completionRate: number; averageScore: number | null; avgTimeSeconds: number | null; effectivenessScore: number | null }
interface EffectivenessData { subtestFilter: string | null; items: ContentItem[]; generatedAt: string }

const apiRequest = apiClient.request;

export default function ContentEffectivenessPage() {
  const [data, setData] = useState<EffectivenessData | null>(null);
  const [loading, setLoading] = useState(true);
  const [subtest, setSubtest] = useState('');

  const load = (s: string) => {
    setLoading(true); setSubtest(s);
    const q = s ? `?subtestCode=${s}&top=50` : '?top=50';
    apiRequest<EffectivenessData>(`/v1/admin/analytics/content-effectiveness${q}`).then(setData).catch(() => {}).finally(() => setLoading(false));
  };

  // eslint-disable-next-line react-hooks/set-state-in-effect -- data-fetch on mount: setState from API response is the correct pattern
  useEffect(() => { analytics.track('admin_content_effectiveness_viewed'); load(''); }, []);

  return (
    <div className="min-h-screen bg-background-light">
      <div className="max-w-5xl mx-auto px-4 py-8">
        <div className="flex items-center justify-between mb-1">
          <h1 className="text-2xl font-bold">Content Effectiveness</h1>
          {data && data.items.length > 0 && (
            <Button variant="outline" size="sm" className="gap-2" onClick={() => {
              const rows = data.items.map(item => ({
                title: item.title,
                subtest: item.subtestCode,
                difficulty: item.difficulty,
                totalAttempts: item.totalAttempts,
                completionRate: item.completionRate,
                averageScore: item.averageScore,
                avgTimeSeconds: item.avgTimeSeconds,
                effectivenessScore: item.effectivenessScore,
              }));
              exportToCsv(rows, `content-effectiveness-${formatDateForExport(new Date())}.csv`);
            }}>
              <Download className="w-4 h-4" />
              Export CSV
            </Button>
          )}
        </div>
        <p className="text-muted mb-6">Which content produces the most improvement and engagement?</p>

        <div className="flex gap-2 mb-6 flex-wrap">
          {['', 'writing', 'speaking', 'reading', 'listening'].map(s => (
            <button key={s} onClick={() => load(s)} className={`px-4 py-2 rounded-lg text-sm font-medium capitalize ${subtest === s ? 'bg-primary text-primary-foreground' : 'bg-muted'}`}>{s || 'All'}</button>
          ))}
        </div>

        {loading ? <div className="space-y-3">{Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-xl" />)}</div> : data ? (
          <MotionSection className="space-y-3">
            {data.items.map((item, idx) => (
              <MotionItem key={item.contentId}>
                <Card className="p-4">
                  <div className="flex items-start gap-3">
                    <div className="w-8 h-8 rounded-full bg-muted flex items-center justify-center text-sm font-bold flex-shrink-0">{idx + 1}</div>
                    <div className="flex-1 min-w-0">
                      <h3 className="text-sm font-semibold truncate">{item.title}</h3>
                      <div className="flex gap-2 mt-1 flex-wrap"><Badge variant="outline" className="capitalize text-[10px]">{item.subtestCode}</Badge><Badge variant="outline" className="text-[10px]">{item.difficulty}</Badge></div>
                    </div>
                    <div className="grid grid-cols-4 gap-4 text-center flex-shrink-0">
                      <div><p className="text-sm font-bold">{item.totalAttempts}</p><p className="text-[10px] text-muted">Attempts</p></div>
                      <div><p className="text-sm font-bold">{item.completionRate}%</p><p className="text-[10px] text-muted">Complete</p></div>
                      <div><p className="text-sm font-bold">{item.averageScore ?? '--'}</p><p className="text-[10px] text-muted">Avg Score</p></div>
                      <div><p className="text-sm font-bold">{item.effectivenessScore ?? '--'}</p><p className="text-[10px] text-muted">Score</p></div>
                    </div>
                  </div>
                </Card>
              </MotionItem>
            ))}
          </MotionSection>
        ) : <Card className="p-8 text-center text-muted"><p>No data available.</p></Card>}
      </div>
    </div>
  );
}
