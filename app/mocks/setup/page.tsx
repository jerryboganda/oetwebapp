'use client';

import { useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  FileText,
  Headphones,
  Mic,
  PenTool,
  Layers,
  Clock,
  Award,
  ShieldCheck,
  Stethoscope,
  Info,
  Check
} from 'lucide-react';
import { useRouter } from 'next/navigation';
import { AppShell } from '@/components/layout/app-shell';
import { Button } from '@/components/ui/button';
import { createMockSession } from '@/lib/api';
import { analytics } from '@/lib/analytics';

const PROFESSIONS = [
  'Medicine', 'Nursing', 'Pharmacy', 'Dentistry', 'Dietetics',
  'Optometry', 'Physiotherapy', 'Podiatry', 'Radiography',
  'Speech Pathology', 'Veterinary Science'
];

export default function MockSetup() {
  const router = useRouter();

  const [mockType, setMockType] = useState<'full' | 'sub'>('full');
  const [subType, setSubType] = useState<'reading' | 'listening' | 'writing' | 'speaking'>('reading');
  const [mode, setMode] = useState<'practice' | 'exam'>('exam');
  const [profession, setProfession] = useState('Medicine');
  const [strictTimer, setStrictTimer] = useState(true);
  const [includeReview, setIncludeReview] = useState(false);
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState<string | null>(null);

  const handleModeChange = (newMode: 'practice' | 'exam') => {
    setMode(newMode);
    if (newMode === 'exam') setStrictTimer(true);
  };

  const showProfession = mockType === 'full' || (mockType === 'sub' && (subType === 'writing' || subType === 'speaking'));

  const handleStart = async () => {
    setStarting(true);
    try {
      const result = await createMockSession({
        type: mockType,
        subType: mockType === 'sub' ? subType : undefined,
        mode,
        profession
      });
      analytics.track('mock_started', { mockType, mode });
      router.push(`/mocks/player/${result.sessionId}`);
    } catch {
      setStartError('Failed to start mock session. Please try again.');
      setStarting(false);
    }
  };

  return (
    <AppShell pageTitle="Configure Mock" subtitle="Set up your practice environment" backHref="/mocks">
      <div className="max-w-3xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-8 pb-24">

        {/* 1. Mock Type Selection */}
        <section className="bg-surface rounded-[32px] border border-gray-200 p-6 sm:p-8 shadow-sm">
          <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-6">1. Select Mock Type</h2>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mb-6">
            {[
              { id: 'full', icon: Layers, label: 'Full Mock', desc: 'All 4 sub-tests in sequence. ~3 hours.' },
              { id: 'sub', icon: FileText, label: 'Single Sub-test', desc: 'Focus on one specific skill area.' }
            ].map(({ id, icon: Icon, label, desc }) => (
              <button
                key={id}
                onClick={() => setMockType(id as 'full' | 'sub')}
                className={`p-5 rounded-2xl border-2 text-left transition-all ${
                  mockType === id ? 'border-primary bg-primary/5 ring-4 ring-primary/10' : 'border-gray-200 hover:border-gray-300 hover:bg-gray-50'
                }`}
              >
                <div className="flex items-center justify-between mb-3">
                  <div className={`w-10 h-10 rounded-full flex items-center justify-center ${mockType === id ? 'bg-primary text-white' : 'bg-gray-100 text-muted'}`}>
                    <Icon className="w-5 h-5" />
                  </div>
                  {mockType === id && <Check className="w-5 h-5 text-primary" />}
                </div>
                <h3 className={`text-lg font-bold ${mockType === id ? 'text-primary' : 'text-navy'}`}>{label}</h3>
                <p className="text-sm text-muted mt-1">{desc}</p>
              </button>
            ))}
          </div>
          <AnimatePresence>
            {mockType === 'sub' && (
              <motion.div initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: 'auto' }} exit={{ opacity: 0, height: 0 }} className="overflow-hidden">
                <div className="pt-2">
                  <h3 className="text-xs font-bold text-navy uppercase tracking-widest mb-3">Which Sub-test?</h3>
                  <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                    {[
                      { id: 'reading', label: 'Reading', icon: FileText },
                      { id: 'listening', label: 'Listening', icon: Headphones },
                      { id: 'writing', label: 'Writing', icon: PenTool },
                      { id: 'speaking', label: 'Speaking', icon: Mic },
                    ].map((st) => (
                      <button
                        key={st.id}
                        onClick={() => setSubType(st.id as typeof subType)}
                        className={`py-3 px-4 rounded-xl border flex flex-col items-center gap-2 transition-colors ${
                          subType === st.id ? 'bg-primary text-white border-primary' : 'bg-surface text-muted border-gray-200 hover:bg-gray-50'
                        }`}
                      >
                        <st.icon className="w-5 h-5" />
                        <span className="text-sm font-bold">{st.label}</span>
                      </button>
                    ))}
                  </div>
                </div>
              </motion.div>
            )}
          </AnimatePresence>
        </section>

        {/* 2. Profession (Conditional) */}
        <AnimatePresence>
          {showProfession && (
            <motion.section initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -20 }} className="bg-surface rounded-[32px] border border-gray-200 p-6 sm:p-8 shadow-sm">
              <div className="flex items-center gap-3 mb-6">
                <div className="w-8 h-8 rounded-full bg-indigo-100 flex items-center justify-center shrink-0">
                  <Stethoscope className="w-4 h-4 text-indigo-600" />
                </div>
                <h2 className="text-sm font-black text-muted uppercase tracking-widest">2. Profession</h2>
              </div>
              <p className="text-sm text-muted mb-4">Writing and Speaking tasks are profession-specific. Select your profession to get the correct materials.</p>
              <select
                value={profession}
                onChange={(e) => setProfession(e.target.value)}
                className="w-full sm:w-1/2 bg-gray-50 border border-gray-200 text-navy text-sm rounded-xl focus:ring-primary focus:border-primary block p-3 font-medium"
              >
                {PROFESSIONS.map(p => <option key={p} value={p}>{p}</option>)}
              </select>
            </motion.section>
          )}
        </AnimatePresence>

        {/* 3. Mode & Timer */}
        <section className="bg-surface rounded-[32px] border border-gray-200 p-6 sm:p-8 shadow-sm">
          <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-6">
            {showProfession ? '3.' : '2.'} Environment
          </h2>
          <div className="space-y-8">
            <div>
              <h3 className="text-xs font-bold text-navy uppercase tracking-widest mb-3">Testing Mode</h3>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                {[
                  { id: 'exam', icon: ShieldCheck, label: 'Exam Mode', desc: 'Strict timing, no pausing, simulates real test conditions.', color: 'rose' },
                  { id: 'practice', icon: Award, label: 'Practice Mode', desc: 'Flexible timing, ability to pause and review answers.', color: 'blue' }
                ].map(({ id, icon: Icon, label, desc, color }) => (
                  <button
                    key={id}
                    onClick={() => handleModeChange(id as 'practice' | 'exam')}
                    className={`p-4 rounded-xl border-2 text-left transition-all flex items-start gap-3 ${
                      mode === id ? `border-${color}-500 bg-${color}-50` : 'border-gray-200 hover:border-gray-300 hover:bg-gray-50'
                    }`}
                  >
                    <Icon className={`w-5 h-5 shrink-0 mt-0.5 ${mode === id ? `text-${color}-500` : 'text-muted'}`} />
                    <div>
                      <h4 className={`text-sm font-bold ${mode === id ? `text-${color}-700` : 'text-navy'}`}>{label}</h4>
                      <p className="text-xs text-muted mt-1">{desc}</p>
                    </div>
                  </button>
                ))}
              </div>
            </div>
            <div className="flex items-center justify-between pt-6 border-t border-gray-100">
              <div className="pr-4">
                <h3 className="text-sm font-bold text-navy flex items-center gap-2">
                  <Clock className="w-4 h-4 text-muted" /> Strict Timer
                </h3>
                <p className="text-xs text-muted mt-1">Enforce time limits for each section. Auto-submits when time is up.</p>
                {mode === 'exam' && (
                  <p className="text-[10px] font-black text-rose-500 uppercase tracking-widest mt-2 flex items-center gap-1">
                    <Info className="w-3 h-3" /> Required in Exam Mode
                  </p>
                )}
              </div>
              <button
                onClick={() => mode !== 'exam' && setStrictTimer(!strictTimer)}
                disabled={mode === 'exam'}
                className={`relative inline-flex h-7 w-12 shrink-0 cursor-pointer items-center rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none ${
                  strictTimer ? 'bg-primary' : 'bg-gray-200'
                } ${mode === 'exam' ? 'opacity-50 cursor-not-allowed' : ''}`}
                role="switch"
                aria-checked={strictTimer}
              >
                <span className="sr-only">Use strict timer</span>
                <span
                  aria-hidden="true"
                  className={`pointer-events-none inline-block h-6 w-6 transform rounded-full bg-white shadow ring-0 transition duration-200 ease-in-out ${
                    strictTimer ? 'translate-x-5' : 'translate-x-0'
                  }`}
                />
              </button>
            </div>
          </div>
        </section>

        {/* 4. Expert Review Add-on */}
        <section className="bg-surface rounded-[32px] border border-gray-200 p-6 sm:p-8 shadow-sm">
          <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-6">
            {showProfession ? '4.' : '3.'} Add-ons
          </h2>
          <div className="flex items-start sm:items-center justify-between gap-4">
            <div className="pr-4">
              <h3 className="text-sm font-bold text-navy">Include Expert Review</h3>
              <p className="text-xs text-muted mt-1">Have an OET expert grade your Writing and Speaking responses with detailed feedback.</p>
              <div className="mt-2 inline-flex items-center gap-1.5 px-2 py-1 rounded-md bg-amber-100 text-amber-800 text-[10px] font-black uppercase tracking-widest">
                Requires 1 Review Credit
              </div>
            </div>
            <button
              onClick={() => setIncludeReview(!includeReview)}
              className={`relative inline-flex h-7 w-12 shrink-0 cursor-pointer items-center rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none ${
                includeReview ? 'bg-amber-500' : 'bg-gray-200'
              }`}
              role="switch"
              aria-checked={includeReview}
            >
              <span className="sr-only">Include expert review</span>
              <span
                aria-hidden="true"
                className={`pointer-events-none inline-block h-6 w-6 transform rounded-full bg-white shadow ring-0 transition duration-200 ease-in-out ${
                  includeReview ? 'translate-x-5' : 'translate-x-0'
                }`}
              />
            </button>
          </div>
        </section>

        {/* Start Button */}
        {startError && (
          <div className="bg-red-50 border border-red-200 rounded-2xl p-4 text-sm text-red-700 text-center">{startError}</div>
        )}
        <div className="pt-4 pb-8">
          <Button
            onClick={handleStart}
            disabled={starting}
            size="lg"
            className="w-full bg-navy text-white hover:bg-navy/90 text-lg py-5 font-black gap-2"
          >
            {starting ? 'Starting...' : 'Start Mock Test'}
          </Button>
          <p className="text-center text-xs text-muted mt-4">
            Make sure you are in a quiet environment before starting.
          </p>
        </div>

      </div>
    </AppShell>
  );
}

