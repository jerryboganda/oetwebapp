'use client';

import { useState, useRef, useEffect, Suspense } from 'react';
import { useParams } from 'next/navigation';
import { motion, AnimatePresence } from 'motion/react';
import {
  Play, Pause, RotateCcw, Volume2, AlertCircle, MessageCircle,
  Clock, Zap, Target, Info, Search, Filter, Loader2,
} from 'lucide-react';
import { AppShell } from '@/components/layout/app-shell';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchTranscript } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { TranscriptLine, MarkerType, TranscriptMarker } from '@/lib/mock-data';

const markerStyles: Record<MarkerType, { color: string; bg: string; icon: typeof AlertCircle }> = {
  pronunciation: { color: 'text-amber-600', bg: 'bg-amber-50', icon: MessageCircle },
  fluency: { color: 'text-blue-600', bg: 'bg-blue-50', icon: Clock },
  empathy: { color: 'text-rose-600', bg: 'bg-rose-50', icon: Zap },
  vocabulary: { color: 'text-purple-600', bg: 'bg-purple-50', icon: Target },
  grammar: { color: 'text-orange-600', bg: 'bg-orange-50', icon: AlertCircle },
  structure: { color: 'text-indigo-600', bg: 'bg-indigo-50', icon: Info },
};

function TranscriptReviewContent() {
  const params = useParams();
  const id = params?.id as string;

  // --- Data State ---
  const [data, setData] = useState<{ title: string; date: string; duration: number; transcript: TranscriptLine[] } | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  // --- State ---
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [selectedMarker, setSelectedMarker] = useState<TranscriptMarker | null>(null);
  const [filter, setFilter] = useState<MarkerType | 'all'>('all');

  // --- Refs ---
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const transcriptContainerRef = useRef<HTMLDivElement | null>(null);

  // --- Fetch transcript data ---
  useEffect(() => {
    fetchTranscript(id)
      .then((result) => {
        setData(result);
        analytics.track('content_view', { contentId: id, subtest: 'speaking', type: 'transcript' });
      })
      .catch(() => setError(true))
      .finally(() => setLoading(false));
  }, [id]);

  // --- Effects ---
  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;

    const handleTimeUpdate = () => {
      setCurrentTime(audio.currentTime);
    };

    const handleEnded = () => {
      setIsPlaying(false);
    };

    audio.addEventListener('timeupdate', handleTimeUpdate);
    audio.addEventListener('ended', handleEnded);

    return () => {
      audio.removeEventListener('timeupdate', handleTimeUpdate);
      audio.removeEventListener('ended', handleEnded);
    };
  }, []);

  // --- Handlers ---
  const togglePlay = () => {
    if (audioRef.current) {
      if (isPlaying) {
        audioRef.current.pause();
      } else {
        audioRef.current.play();
      }
      setIsPlaying(!isPlaying);
    }
  };

  const seekTo = (time: number) => {
    if (audioRef.current) {
      audioRef.current.currentTime = time;
      setCurrentTime(time);
      if (!isPlaying) {
        audioRef.current.play();
        setIsPlaying(true);
      }
    }
  };

  const formatTime = (seconds: number) => {
    const mins = Math.floor(seconds / 60);
    const secs = Math.floor(seconds % 60);
    return `${mins}:${secs.toString().padStart(2, '0')}`;
  };

  const filteredTranscript = data?.transcript.filter(line => {
    if (filter === 'all') return true;
    return line.markers?.some(m => m.type === filter);
  }) ?? [];

  if (loading) {
    return (
      <AppShell pageTitle="Transcript Review" distractionFree>
        <div className="flex-1 flex gap-6 p-6">
          <Skeleton className="flex-1 h-96 rounded-xl" />
          <Skeleton className="w-96 h-96 rounded-xl" />
        </div>
      </AppShell>
    );
  }

  if (error || !data) {
    return (
      <AppShell pageTitle="Transcript Review">
        <InlineAlert variant="error">Could not load the transcript. Please try again.</InlineAlert>
      </AppShell>
    );
  }

  return (
    <AppShell pageTitle={data.title} distractionFree>

      <div className="flex-1 flex overflow-hidden">
        {/* Left: Transcript Pane */}
        <main className="flex-1 flex flex-col bg-white border-r border-gray-200 overflow-hidden">
          {/* Transcript Toolbar */}
          <div className="px-6 py-3 border-b border-gray-100 flex items-center justify-between bg-gray-50/50">
            <div className="flex items-center gap-4">
              <div className="relative">
                <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
                <input 
                  type="text" 
                  placeholder="Search transcript..." 
                  className="pl-9 pr-4 py-1.5 bg-white border border-gray-200 rounded-full text-xs focus:outline-none focus:ring-2 focus:ring-primary/20 w-48"
                />
              </div>
              <div className="flex items-center gap-2">
                <Filter className="w-3.5 h-3.5 text-gray-400" />
                <select 
                  value={filter}
                  onChange={(e) => setFilter(e.target.value as MarkerType | 'all')}
                  className="text-xs font-bold text-muted bg-transparent focus:outline-none"
                >
                  <option value="all">All Markers</option>
                  <option value="pronunciation">Pronunciation</option>
                  <option value="fluency">Fluency</option>
                  <option value="grammar">Grammar</option>
                  <option value="vocabulary">Vocabulary</option>
                  <option value="empathy">Empathy</option>
                  <option value="structure">Structure</option>
                </select>
              </div>
            </div>
            <div className="text-[10px] font-black text-gray-400 uppercase tracking-widest">
              {filteredTranscript.length} Lines Found
            </div>
          </div>

          {/* Transcript Content */}
          <div 
            ref={transcriptContainerRef}
            className="flex-1 overflow-y-auto p-8 space-y-8 scroll-smooth"
          >
            {data.transcript.map((line) => {
              const isActive = currentTime >= line.startTime && currentTime < line.endTime;
              const isFilteredOut = filter !== 'all' && !line.markers?.some(m => m.type === filter);

              return (
                <div 
                  key={line.id} 
                  className={`group relative transition-all duration-300 ${isFilteredOut ? 'opacity-20 grayscale' : 'opacity-100'}`}
                >
                  <div className="flex gap-6">
                    <div className="w-16 shrink-0 flex flex-col items-end pt-1">
                      <span className={`text-[10px] font-black uppercase tracking-widest ${line.speaker === 'Nurse' ? 'text-primary' : 'text-gray-400'}`}>
                        {line.speaker}
                      </span>
                      <button 
                        onClick={() => seekTo(line.startTime)}
                        className="text-[10px] font-bold text-gray-300 hover:text-primary transition-colors mt-1 tabular-nums"
                      >
                        {formatTime(line.startTime)}
                      </button>
                    </div>

                    <div className={`flex-1 p-4 rounded-2xl transition-all ${isActive ? 'bg-primary/5 border-l-4 border-primary' : 'hover:bg-gray-50 border-l-4 border-transparent'}`}>
                      <p className={`text-sm leading-relaxed ${line.speaker === 'Nurse' ? 'text-gray-900 font-medium' : 'text-gray-600 italic'}`}>
                        {line.text}
                      </p>

                      {/* Inline Markers */}
                      {line.markers && line.markers.map((marker) => {
                        const style = markerStyles[marker.type];
                        return (
                          <motion.button
                            key={marker.id}
                            whileHover={{ scale: 1.02 }}
                            onClick={() => setSelectedMarker(marker)}
                            className={`mt-3 flex items-center gap-2 px-3 py-1.5 rounded-lg border border-transparent hover:border-current transition-all ${style.bg} ${style.color}`}
                          >
                            <style.icon className="w-3.5 h-3.5" />
                            <span className="text-[10px] font-black uppercase tracking-wider">{marker.type}</span>
                          </motion.button>
                        );
                      })}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        </main>

        {/* Right: Insights & Waveform Pane */}
        <aside className="w-96 bg-gray-50 border-l border-gray-200 flex flex-col shrink-0">
          {/* Waveform / Audio Control */}
          <div className="p-6 bg-white border-b border-gray-200">
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-xs font-black uppercase tracking-widest text-gray-400">Audio Playback</h3>
              <div className="flex items-center gap-2 text-[10px] font-black text-gray-900 tabular-nums">
                <span>{formatTime(currentTime)}</span>
                <span className="text-gray-300">/</span>
                <span>{formatTime(data.duration)}</span>
              </div>
            </div>

            {/* Mock Waveform */}
            <div className="h-24 flex items-end gap-0.5 mb-6 bg-gray-50 rounded-xl p-4 overflow-hidden relative">
              {[...Array(60)].map((_, i) => {
                // Use a deterministic height based on index for purity
                const height = ((i * 13) % 80) + 10;
                const progress = (i / 60) * data.duration;
                const isPlayed = currentTime > progress;
                return (
                  <div 
                    key={i} 
                    className={`flex-1 rounded-full transition-all duration-300 ${isPlayed ? 'bg-primary' : 'bg-gray-200'}`}
                    style={{ height: `${height}%` }}
                  />
                );
              })}
              {/* Markers on Waveform */}
              {data.transcript.flatMap(l => l.markers || []).map(m => (
                <div 
                  key={m.id}
                  className="absolute bottom-0 w-1 h-full bg-red-500/20 border-x border-red-500/40"
                  style={{ left: `${(m.startTime / data.duration) * 100}%` }}
                />
              ))}
            </div>

            <div className="flex items-center justify-center gap-6">
              <button className="p-2 hover:bg-gray-100 rounded-full text-gray-400 transition-colors"><RotateCcw className="w-5 h-5" /></button>
              <button 
                onClick={togglePlay}
                className="w-14 h-14 rounded-full bg-primary text-white flex items-center justify-center shadow-lg hover:bg-primary/90 transition-all active:scale-95"
              >
                {isPlaying ? <Pause className="w-6 h-6 fill-current" /> : <Play className="w-6 h-6 fill-current ml-1" />}
              </button>
              <button className="p-2 hover:bg-gray-100 rounded-full text-gray-400 transition-colors"><Volume2 className="w-5 h-5" /></button>
            </div>
          </div>

          {/* Marker Detail / Legend */}
          <div className="flex-1 overflow-y-auto p-6">
            <AnimatePresence mode="wait">
              {selectedMarker ? (
                <motion.div 
                  key={selectedMarker.id}
                  initial={{ opacity: 0, x: 20 }}
                  animate={{ opacity: 1, x: 0 }}
                  exit={{ opacity: 0, x: -20 }}
                  className="space-y-6"
                >
                  <div className="flex items-center justify-between">
                    <div className={`px-3 py-1 rounded-full text-[10px] font-black uppercase tracking-widest flex items-center gap-2 ${markerStyles[selectedMarker.type].bg} ${markerStyles[selectedMarker.type].color}`}>
                      {selectedMarker.type}
                    </div>
                    <button onClick={() => setSelectedMarker(null)} className="text-[10px] font-bold text-gray-400 hover:text-gray-900">Close</button>
                  </div>

                  <div>
                    <h4 className="text-xs font-black text-gray-400 uppercase tracking-widest mb-2">Flagged Moment</h4>
                    <p className="text-sm text-gray-900 font-medium italic border-l-2 border-gray-200 pl-4 leading-relaxed">
                      &quot;{selectedMarker.text}&quot;
                    </p>
                  </div>

                  <div className="bg-white rounded-2xl border border-gray-200 p-5 shadow-sm">
                    <div className="flex items-center gap-2 mb-3">
                      <Zap className="w-4 h-4 text-primary" />
                      <h4 className="text-xs font-black text-gray-900 uppercase tracking-widest">AI Suggestion</h4>
                    </div>
                    <p className="text-sm text-gray-600 leading-relaxed">
                      {selectedMarker.suggestion}
                    </p>
                  </div>

                  <Button
                    fullWidth
                    onClick={() => seekTo(selectedMarker.startTime)}
                    className="bg-navy text-white"
                  >
                    <Play className="w-3 h-3 fill-current" /> Replay Moment
                  </Button>
                </motion.div>
              ) : (
                <motion.div 
                  key="legend"
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  className="space-y-8"
                >
                  <div>
                    <h3 className="text-xs font-black uppercase tracking-widest text-gray-400 mb-6">Marker Legend</h3>
                    <div className="grid grid-cols-1 gap-3">
                      {(Object.keys(markerStyles) as MarkerType[]).map((type) => {
                        const style = markerStyles[type];
                        const count = data.transcript.flatMap(l => l.markers || []).filter(m => m.type === type).length;
                        return (
                          <button 
                            key={type}
                            onClick={() => setFilter(type)}
                            className={`flex items-center justify-between p-3 rounded-xl border transition-all ${filter === type ? 'bg-white border-primary shadow-sm' : 'bg-white/50 border-gray-100 hover:bg-white hover:border-gray-200'}`}
                          >
                            <div className="flex items-center gap-3">
                              <div className={`w-8 h-8 rounded-lg flex items-center justify-center ${style.bg}`}>
                                <style.icon className={`w-4 h-4 ${style.color}`} />
                              </div>
                              <span className="text-[10px] font-black uppercase tracking-wider text-gray-700">{type}</span>
                            </div>
                            <span className="text-[10px] font-bold text-gray-400 bg-gray-100 px-2 py-0.5 rounded-full">{count}</span>
                          </button>
                        );
                      })}
                    </div>
                  </div>

                  <div className="p-5 bg-blue-50 border border-blue-100 rounded-2xl">
                    <div className="flex items-center gap-2 mb-2">
                      <Info className="w-4 h-4 text-blue-600" />
                      <h4 className="text-xs font-black text-blue-900 uppercase tracking-widest">Review Tip</h4>
                    </div>
                    <p className="text-xs text-blue-800 leading-relaxed">
                      Click on any marker in the transcript to see specific feedback and suggestions for improvement.
                    </p>
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        </aside>
      </div>

      {/* Hidden Audio Element */}
      <audio ref={audioRef} src="" />
    </AppShell>
  );
}

export default function TranscriptReview() {
  return (
    <Suspense fallback={
      <AppShell pageTitle="Transcript Review" distractionFree>
        <div className="flex-1 flex items-center justify-center">
          <Loader2 className="w-8 h-8 text-primary animate-spin" />
        </div>
      </AppShell>
    }>
      <TranscriptReviewContent />
    </Suspense>
  );
}
