'use client';

import { useMemo, useState } from 'react';
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
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { MotionCollapse, MotionSection, MotionPresence } from '@/components/ui/motion-primitives';
import { createMockSession } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type ReviewSelection = 'none' | 'writing' | 'speaking' | 'writing_and_speaking' | 'current_subtest';
type MockType = 'full' | 'sub';
type MockSubType = 'reading' | 'listening' | 'writing' | 'speaking';

const PROFESSIONS = [
  'Medicine', 'Nursing', 'Pharmacy', 'Dentistry', 'Dietetics',
  'Optometry', 'Physiotherapy', 'Podiatry', 'Radiography',
  'Speech Pathology', 'Veterinary Science'
];

function buildReviewOptions(mockType: MockType, subType: MockSubType) {
  if (mockType === 'full') {
    return [
      { id: 'none' as const, label: 'No Review', description: 'Run the mock without productive-skill review add-ons.' },
      { id: 'writing' as const, label: 'Writing Only', description: 'Attach expert review to the Writing section only.' },
      { id: 'speaking' as const, label: 'Speaking Only', description: 'Attach expert review to the Speaking section only.' },
      { id: 'writing_and_speaking' as const, label: 'Writing + Speaking', description: 'Add review to both productive skills in the full mock.' },
    ];
  }

  if (subType === 'writing' || subType === 'speaking') {
    return [
      { id: 'none' as const, label: 'No Review', description: 'Keep this sub-test mock AI-evaluated only.' },
      { id: 'current_subtest' as const, label: 'Review Current Sub-test', description: 'Use one review credit on this productive-skill mock.' },
    ];
  }

  return [
    { id: 'none' as const, label: 'No Review', description: 'Expert review is not offered for Reading or Listening mocks.' },
  ];
}

function normalizeReviewSelection(mockType: MockType, subType: MockSubType, selection: ReviewSelection): ReviewSelection {
  return buildReviewOptions(mockType, subType).some((option) => option.id === selection) ? selection : 'none';
}

export default function MockSetup() {
  const router = useRouter();

  const [mockType, setMockType] = useState<MockType>('full');
  const [subType, setSubType] = useState<MockSubType>('reading');
  const [mode, setMode] = useState<'practice' | 'exam'>('exam');
  const [profession, setProfession] = useState('Medicine');
  const [strictTimer, setStrictTimer] = useState(true);
  const [reviewSelection, setReviewSelection] = useState<ReviewSelection>('none');
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState<string | null>(null);
  const reviewOptions = useMemo(() => buildReviewOptions(mockType, subType), [mockType, subType]);
  const selectedReviewSelection = normalizeReviewSelection(mockType, subType, reviewSelection);

  const handleModeChange = (newMode: 'practice' | 'exam') => {
    setMode(newMode);
    if (newMode === 'exam') setStrictTimer(true);
  };

  const showProfession = mockType === 'full' || (mockType === 'sub' && (subType === 'writing' || subType === 'speaking'));

  const handleMockTypeChange = (nextType: MockType) => {
    setMockType(nextType);
    setReviewSelection((current) => normalizeReviewSelection(nextType, subType, current));
  };

  const handleSubTypeChange = (nextSubType: MockSubType) => {
    setSubType(nextSubType);
    setReviewSelection((current) => normalizeReviewSelection(mockType, nextSubType, current));
  };

  const handleStart = async () => {
    setStarting(true);
    setStartError(null);
    try {
      const result = await createMockSession({
        type: mockType,
        subType: mockType === 'sub' ? subType : undefined,
        mode,
        profession,
        strictTimer,
        reviewSelection: selectedReviewSelection,
      });
      analytics.track('mock_started', { mockType, mode, reviewSelection: selectedReviewSelection });
      router.push(`/mocks/player/${result.sessionId}`);
    } catch {
      setStartError('Failed to start mock session. Please try again.');
      setStarting(false);
    }
  };

  return (
    <LearnerDashboardShell pageTitle="Configure Mock" subtitle="Set up your practice environment" backHref="/mocks">
      <div className="space-y-8 pb-24">

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
                onClick={() => handleMockTypeChange(id as MockType)}
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
          <MotionCollapse open={mockType === 'sub'}>
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
                        onClick={() => handleSubTypeChange(st.id as MockSubType)}
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
          </MotionCollapse>
        </section>

        {/* 2. Profession (Conditional) */}
        <MotionPresence>
          {showProfession && (
            <MotionSection className="bg-surface rounded-[32px] border border-gray-200 p-6 sm:p-8 shadow-sm">
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
            </MotionSection>
          )}
        </MotionPresence>

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
                className={`relative inline-flex h-8 w-13 shrink-0 cursor-pointer items-center rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none ${
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
          <div className="space-y-4">
            <div className="pr-4">
              <h3 className="text-sm font-bold text-navy">Productive-skill review selection</h3>
              <p className="text-xs text-muted mt-1">Review add-ons apply only to Writing and Speaking because those are the OET productive skills that benefit from human review.</p>
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              {reviewOptions.map((option) => (
                <button
                  key={option.id}
                  onClick={() => setReviewSelection(option.id)}
                  className={`rounded-2xl border p-4 text-left transition-colors ${
                    selectedReviewSelection === option.id
                      ? 'border-amber-400 bg-amber-50'
                      : 'border-gray-200 bg-white hover:border-gray-300'
                  }`}
                >
                  <p className="text-sm font-bold text-navy">{option.label}</p>
                  <p className="mt-2 text-xs leading-5 text-muted">{option.description}</p>
                </button>
              ))}
            </div>
            {selectedReviewSelection !== 'none' ? (
              <div className="inline-flex items-center gap-1.5 rounded-md bg-amber-100 px-2 py-1 text-[10px] font-black uppercase tracking-widest text-amber-800">
                Uses review credits on productive skills only
              </div>
            ) : null}
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
    </LearnerDashboardShell>
  );
}
