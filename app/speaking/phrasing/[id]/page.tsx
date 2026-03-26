'use client';

import { useState, useEffect, Suspense } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { motion, AnimatePresence } from 'motion/react';
import {
  ChevronLeft, ChevronRight, Mic, Play, RotateCcw,
  AlertCircle, Zap, MessageSquare, Loader2, Volume2,
} from 'lucide-react';
import { AppShell } from '@/components/layout/app-shell';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { BetterPhraseCard } from '@/components/domain/better-phrase-card';
import { fetchPhrasingData } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { PhrasingSegment } from '@/lib/mock-data';

function BetterPhrasingContent() {
  const params = useParams();
  const router = useRouter();
  const id = params?.id as string;

  // --- Data State ---
  const [title, setTitle] = useState('');
  const [segments, setSegments] = useState<PhrasingSegment[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  // --- UI State ---
  const [currentIndex, setCurrentIndex] = useState(0);
  const [isRecording, setIsRecording] = useState(false);
  const [hasRecorded, setHasRecorded] = useState(false);
  const [playbackActive, setPlaybackActive] = useState(false);

  useEffect(() => {
    fetchPhrasingData(id)
      .then((result) => {
        setTitle(result.title);
        setSegments(result.segments);
        analytics.track('content_view', { contentId: id, subtest: 'speaking', type: 'phrasing' });
      })
      .catch(() => setError(true))
      .finally(() => setLoading(false));
  }, [id]);

  const currentSegment = segments[currentIndex];
  const progress = segments.length > 0 ? ((currentIndex + 1) / segments.length) * 100 : 0;

  const handleNext = () => {
    if (currentIndex < segments.length - 1) {
      setCurrentIndex(prev => prev + 1);
      setHasRecorded(false);
    } else {
      router.push(`/speaking/results/${id}`);
    }
  };

  const handlePrev = () => {
    if (currentIndex > 0) {
      setCurrentIndex(prev => prev - 1);
      setHasRecorded(false);
    }
  };

  const toggleRecording = () => {
    if (isRecording) {
      setIsRecording(false);
      setHasRecorded(true);
    } else {
      setIsRecording(true);
    }
  };

  if (loading) {
    return (
      <AppShell pageTitle="Better Phrasing" distractionFree>
        <div className="max-w-3xl mx-auto p-6 space-y-6">
          <Skeleton className="h-48 rounded-xl" />
          <Skeleton className="h-64 rounded-xl" />
          <Skeleton className="h-32 rounded-xl" />
        </div>
      </AppShell>
    );
  }

  if (error || segments.length === 0) {
    return (
      <AppShell pageTitle="Better Phrasing">
        <InlineAlert variant="error">Could not load phrasing data. Please try again.</InlineAlert>
      </AppShell>
    );
  }

  return (
    <AppShell
      pageTitle="Better Phrasing"
      navActions={
        <div className="flex items-center gap-3">
          <span className="text-xs font-bold text-muted">
            Segment {currentIndex + 1} of {segments.length}
          </span>
          <div className="w-32 h-2 bg-gray-100 rounded-full overflow-hidden hidden md:block">
            <motion.div
              initial={{ width: 0 }}
              animate={{ width: `${progress}%` }}
              className="h-full bg-primary"
            />
          </div>
        </div>
      }
      distractionFree
    >
      <main className="flex-1 p-6 overflow-y-auto">
        <div className="max-w-3xl mx-auto space-y-6">
          <AnimatePresence mode="wait">
            <motion.div
              key={currentSegment.id}
              initial={{ opacity: 0, x: 20 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: -20 }}
              className="space-y-6"
            >
              {/* Original Phrase Card */}
              <Card className="p-8">
                <div className="flex items-center gap-3 mb-4">
                  <div className="w-8 h-8 rounded-lg bg-gray-50 flex items-center justify-center">
                    <MessageSquare className="w-4 h-4 text-muted" />
                  </div>
                  <h2 className="text-xs font-bold text-muted uppercase tracking-widest">Your Original Phrase</h2>
                </div>
                <p className="text-xl font-medium text-navy italic leading-relaxed">
                  &quot;{currentSegment.originalPhrase}&quot;
                </p>

                <div className="mt-8 pt-8 border-t border-gray-100">
                  <div className="flex items-start gap-4">
                    <div className="w-10 h-10 rounded-xl bg-amber-50 flex items-center justify-center shrink-0">
                      <AlertCircle className="w-5 h-5 text-amber-600" />
                    </div>
                    <div>
                      <h3 className="text-xs font-bold text-amber-600 uppercase tracking-widest mb-1">Issue Explanation</h3>
                      <p className="text-sm text-muted leading-relaxed">{currentSegment.issueExplanation}</p>
                    </div>
                  </div>
                </div>
              </Card>

              {/* Stronger Alternative Card */}
              <section className="bg-navy rounded-3xl p-8 text-white shadow-xl relative overflow-hidden">
                <div className="absolute top-0 right-0 p-8 opacity-10">
                  <Zap className="w-32 h-32" />
                </div>
                <div className="relative z-10">
                  <div className="flex items-center gap-3 mb-6">
                    <div className="w-10 h-10 rounded-xl bg-white/10 flex items-center justify-center">
                      <Zap className="w-5 h-5 text-primary" />
                    </div>
                    <h2 className="text-xs font-bold text-primary uppercase tracking-widest">Stronger Alternative</h2>
                  </div>
                  <p className="text-2xl font-black mb-8 leading-tight tracking-tight">
                    {currentSegment.strongerAlternative}
                  </p>
                  <div className="bg-white/5 rounded-2xl p-6 border border-white/10">
                    <div className="flex items-center gap-3 mb-3">
                      <Volume2 className="w-4 h-4 text-primary" />
                      <h3 className="text-xs font-bold text-white/60 uppercase tracking-widest">Drill Prompt</h3>
                    </div>
                    <p className="text-sm text-white/80 leading-relaxed">{currentSegment.drillPrompt}</p>
                  </div>
                </div>
              </section>

              {/* Repeat Drill Interaction */}
              <Card className="p-8 flex flex-col items-center text-center">
                <h3 className="text-sm font-bold text-navy uppercase tracking-widest mb-6">Practice Improved Phrasing</h3>
                <div className="flex items-center gap-8 mb-8">
                  <motion.button
                    whileHover={{ scale: 1.05 }}
                    whileTap={{ scale: 0.95 }}
                    onClick={toggleRecording}
                    className={`w-20 h-20 rounded-full flex items-center justify-center transition-all shadow-lg ${isRecording ? 'bg-red-500 animate-pulse' : 'bg-primary hover:bg-primary/90'}`}
                  >
                    <Mic className={`w-8 h-8 text-white ${isRecording ? 'animate-bounce' : ''}`} />
                  </motion.button>
                  {hasRecorded && (
                    <motion.button
                      initial={{ opacity: 0, scale: 0.5 }}
                      animate={{ opacity: 1, scale: 1 }}
                      onClick={() => setPlaybackActive(!playbackActive)}
                      className="w-14 h-14 rounded-full bg-gray-100 text-muted flex items-center justify-center hover:bg-gray-200 transition-all"
                    >
                      {playbackActive ? <RotateCcw className="w-6 h-6" /> : <Play className="w-6 h-6 fill-current ml-1" />}
                    </motion.button>
                  )}
                </div>
                <p className="text-xs font-bold text-muted uppercase tracking-widest">
                  {isRecording ? 'Recording your drill...' : hasRecorded ? 'Drill complete! Review or move to next.' : 'Tap to start repeat drill'}
                </p>
              </Card>
            </motion.div>
          </AnimatePresence>
        </div>
      </main>

      {/* Footer Controls */}
      <footer className="bg-white border-t border-gray-200 p-6 z-20 shrink-0">
        <div className="max-w-3xl mx-auto flex items-center justify-between">
          <Button variant="ghost" onClick={handlePrev} disabled={currentIndex === 0}>
            <ChevronLeft className="w-5 h-5" /> Previous
          </Button>

          <div className="flex items-center gap-2">
            {segments.map((_, i) => (
              <div
                key={i}
                className={`w-2 h-2 rounded-full transition-all ${i === currentIndex ? 'bg-primary w-6' : 'bg-gray-200'}`}
              />
            ))}
          </div>

          <Button variant="ghost" onClick={handleNext}>
            {currentIndex === segments.length - 1 ? 'Finish Review' : 'Next Segment'} <ChevronRight className="w-5 h-5" />
          </Button>
        </div>
      </footer>
    </AppShell>
  );
}

export default function BetterPhrasingView() {
  return (
    <Suspense fallback={
      <AppShell pageTitle="Better Phrasing" distractionFree>
        <div className="flex-1 flex items-center justify-center">
          <Loader2 className="w-8 h-8 text-primary animate-spin" />
        </div>
      </AppShell>
    }>
      <BetterPhrasingContent />
    </Suspense>
  );
}
