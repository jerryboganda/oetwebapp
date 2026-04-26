'use client';

import { LearnerPageHero } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';
import { ArrowLeftRight, Calculator, Globe, GraduationCap, Info } from 'lucide-react';
import { useEffect, useState } from 'react';

/* ── types ─────────────────────────────────────── */
interface ScoreEquivalence {
  oetGrade: string; oetScoreMin: number; oetScoreMax: number;
  ielts: number; pte: number; cefr: string;
}

interface Requirement {
  country: string; body: string; oetMinGrade: string; oetMinScore: number; ieltsMin: number;
}

interface EquivalenceData {
  equivalences: ScoreEquivalence[];
  commonRequirements: Requirement[];
}

/* ── api helper ───────────────────────────────── */
const apiRequest = apiClient.request;

/* ── grade colour helpers ──────────────────────── */
const GRADE_COLORS: Record<string, string> = {
  'A':  'bg-success/10 text-success',
  'B+': 'bg-success/10 text-success',
  'B':  'bg-info/10 text-info',
  'C+': 'bg-warning/10 text-warning',
  'C':  'bg-warning/10 text-warning',
  'D':  'bg-danger/10 text-danger',
  'E':  'bg-danger/10 text-danger',
};

export default function ScoreCalculatorPage() {
  const [data, setData] = useState<EquivalenceData | null>(null);
  const [loading, setLoading] = useState(true);
  const [oetScore, setOetScore] = useState<number>(350);
  const [countryFilter, setCountryFilter] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('score_calculator_viewed');
    apiRequest<EquivalenceData>('/v1/reference/score-equivalences')
      .then(setData)
      .catch(() => {})
      .finally(() => setLoading(false));
  }, []);

  /* find matching row for the score input */
  const matchedRow = data?.equivalences.find(e => oetScore >= e.oetScoreMin && oetScore <= e.oetScoreMax);

  /* filter requirements */
  const countries = data ? Array.from(new Set(data.commonRequirements.map(r => r.country))).sort() : [];
  const filteredReqs = data?.commonRequirements.filter(r => !countryFilter || r.country === countryFilter) || [];

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="max-w-3xl mx-auto space-y-4">
          <Skeleton className="h-8 w-64" /><Skeleton className="h-4 w-96" />
          <Skeleton className="h-32" /><Skeleton className="h-64" />
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Score Cross-Reference Calculator"
        description="Compare OET scores with IELTS, PTE, and CEFR equivalents · Check institution requirements"
      />

      <div className="max-w-3xl mx-auto space-y-8">

        {/* ── Score Input ────────────────────── */}
        <Card className="p-6">
          <div className="flex items-center gap-2 mb-4">
            <Calculator className="h-5 w-5 text-primary" />
            <h2 className="font-semibold">Enter Your OET Score</h2>
          </div>
          <div className="space-y-3">
            <input
              type="range"
              min={0} max={500} step={10}
              value={oetScore}
              onChange={e => setOetScore(Number(e.target.value))}
              className="w-full h-2 accent-primary"
            />
            <div className="flex items-center justify-between">
              <span className="text-xs text-muted-foreground">0</span>
              <span className="text-2xl font-bold text-primary">{oetScore}</span>
              <span className="text-xs text-muted-foreground">500</span>
            </div>
          </div>

          {matchedRow && (
            <div className="mt-6 grid grid-cols-2 sm:grid-cols-4 gap-3">
              <div className="text-center p-3 rounded-lg bg-muted/30">
                <p className="text-xs text-muted-foreground mb-1">OET Grade</p>
                <Badge className={`text-base px-3 py-1 ${GRADE_COLORS[matchedRow.oetGrade] || ''}`}>
                  {matchedRow.oetGrade}
                </Badge>
              </div>
              <div className="text-center p-3 rounded-lg bg-muted/30">
                <p className="text-xs text-muted-foreground mb-1">IELTS</p>
                <p className="text-xl font-bold">{matchedRow.ielts}</p>
              </div>
              <div className="text-center p-3 rounded-lg bg-muted/30">
                <p className="text-xs text-muted-foreground mb-1">PTE</p>
                <p className="text-xl font-bold">{matchedRow.pte}</p>
              </div>
              <div className="text-center p-3 rounded-lg bg-muted/30">
                <p className="text-xs text-muted-foreground mb-1">CEFR</p>
                <p className="text-xl font-bold">{matchedRow.cefr}</p>
              </div>
            </div>
          )}
        </Card>

        {/* ── Equivalence Table ──────────────── */}
        <Card className="overflow-hidden">
          <div className="p-4 border-b flex items-center gap-2">
            <ArrowLeftRight className="h-4 w-4 text-muted-foreground" />
            <h2 className="font-semibold text-sm">Full Equivalence Table</h2>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="bg-muted/30 text-left">
                  <th className="px-4 py-2 font-medium">OET Grade</th>
                  <th className="px-4 py-2 font-medium">OET Score</th>
                  <th className="px-4 py-2 font-medium">IELTS</th>
                  <th className="px-4 py-2 font-medium">PTE</th>
                  <th className="px-4 py-2 font-medium">CEFR</th>
                </tr>
              </thead>
              <tbody>
                {data?.equivalences.map((row, i) => {
                  const isMatch = matchedRow === row;
                  return (
                    <tr key={i} className={isMatch ? 'bg-primary/5 font-medium' : 'hover:bg-muted/20'}>
                      <td className="px-4 py-2.5">
                        <Badge variant="outline" className={`${GRADE_COLORS[row.oetGrade] || ''} border-0 text-xs`}>
                          {row.oetGrade}
                        </Badge>
                      </td>
                      <td className="px-4 py-2.5">{row.oetScoreMin}–{row.oetScoreMax}</td>
                      <td className="px-4 py-2.5">{row.ielts}</td>
                      <td className="px-4 py-2.5">{row.pte}</td>
                      <td className="px-4 py-2.5">{row.cefr}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </Card>

        {/* ── Institution Requirements ────────── */}
        <div>
          <div className="flex items-center justify-between mb-4">
            <div className="flex items-center gap-2">
              <Globe className="h-5 w-5 text-muted-foreground" />
              <h2 className="font-semibold">Institution Requirements</h2>
            </div>
            <div className="flex gap-2 flex-wrap">
              <button
                onClick={() => setCountryFilter(null)}
                className={`px-3 py-1 rounded-full text-xs border ${!countryFilter ? 'bg-primary text-primary-foreground border-primary' : 'bg-muted/30 border-border'}`}
              >
                All
              </button>
              {countries.map(c => (
                <button
                  key={c}
                  onClick={() => setCountryFilter(countryFilter === c ? null : c)}
                  className={`px-3 py-1 rounded-full text-xs border ${countryFilter === c ? 'bg-primary text-primary-foreground border-primary' : 'bg-muted/30 border-border'}`}
                >
                  {c}
                </button>
              ))}
            </div>
          </div>

          <MotionSection className="space-y-2">
            {filteredReqs.map((req, i) => {
              const meetsReq = oetScore >= req.oetMinScore;
              return (
                <MotionItem key={i}>
                  <Card className={`p-4 transition-colors ${meetsReq ? 'border-success/30' : ''}`}>
                    <div className="flex items-center justify-between">
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-2 mb-1">
                          <GraduationCap className="h-4 w-4 text-muted-foreground shrink-0" />
                          <span className="font-medium text-sm">{req.body}</span>
                          <Badge variant="outline" className="text-[10px]">{req.country}</Badge>
                        </div>
                        <p className="text-xs text-muted-foreground ml-6">
                          Min OET: <strong>{req.oetMinGrade}</strong> ({req.oetMinScore}) · Min IELTS: <strong>{req.ieltsMin}</strong>
                        </p>
                      </div>
                      <Badge variant={meetsReq ? 'default' : 'danger'} className="text-xs shrink-0">
                        {meetsReq ? 'Meets Requirement' : 'Below Minimum'}
                      </Badge>
                    </div>
                  </Card>
                </MotionItem>
              );
            })}
          </MotionSection>
        </div>

        {/* disclaimer */}
        <div className="flex items-start gap-2 text-xs text-muted-foreground bg-muted/30 rounded-lg p-3">
          <Info className="h-4 w-4 mt-0.5 shrink-0" />
          <p>Score equivalences are approximate and based on publicly available official guidance. Always verify requirements directly with the accepting institution or regulatory body.</p>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
