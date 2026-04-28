import { BookOpen, Scale, Star, AlertTriangle } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { PageViewBeacon } from '@/components/analytics/page-view-beacon';

const WRITING_RUBRIC = [
  { criterion: 'Overall Task Fulfilment', weight: 'High', bands: [
    { range: '6–7', desc: 'All relevant information addressed with excellent prioritisation and expansion.' },
    { range: '4–5', desc: 'Most information addressed; minor omissions or over-inclusion.' },
    { range: '2–3', desc: 'Significant omissions or irrelevant material included.' },
    { range: '0–1', desc: 'Fails to address the task adequately.' },
  ]},
  { criterion: 'Appropriateness of Language', weight: 'High', bands: [
    { range: '6–7', desc: 'Consistently appropriate register, tone, and formality.' },
    { range: '4–5', desc: 'Generally appropriate with occasional lapses.' },
    { range: '2–3', desc: 'Frequent register/tone errors affecting communication.' },
    { range: '0–1', desc: 'Inappropriate throughout.' },
  ]},
  { criterion: 'Comprehension of Stimulus', weight: 'Medium', bands: [
    { range: '6–7', desc: 'Accurate, complete understanding of all case note details.' },
    { range: '4–5', desc: 'Generally accurate with minor misinterpretations.' },
    { range: '2–3', desc: 'Several misreadings affecting accuracy.' },
    { range: '0–1', desc: 'Fundamental misunderstanding of case notes.' },
  ]},
  { criterion: 'Linguistic Features', weight: 'Medium', bands: [
    { range: '6–7', desc: 'Wide range of accurate grammar; effective cohesion devices.' },
    { range: '4–5', desc: 'Adequate grammar with some errors; basic cohesion present.' },
    { range: '2–3', desc: 'Limited grammar range; frequent errors impede communication.' },
    { range: '0–1', desc: 'Severely limited; mostly incomprehensible.' },
  ]},
  { criterion: 'Presentation of Purpose', weight: 'Medium', bands: [
    { range: '6–7', desc: 'Purpose immediately clear; specific action requested.' },
    { range: '4–5', desc: 'Purpose stated but could be more specific.' },
    { range: '2–3', desc: 'Purpose unclear or buried in the letter.' },
    { range: '0–1', desc: 'No discernible purpose.' },
  ]},
  { criterion: 'Conciseness', weight: 'Low', bands: [
    { range: '6–7', desc: 'Optimal length; no redundancy; every sentence contributes.' },
    { range: '4–5', desc: 'Slightly over/under length with minor repetition.' },
    { range: '2–3', desc: 'Significant repetition or irrelevant padding.' },
    { range: '0–1', desc: 'Extremely over/under length.' },
  ]},
];

const SPEAKING_RUBRIC = [
  // Linguistic criteria (0–6)
  { criterion: 'Intelligibility', weight: 'High', scale: '0–6', bands: [
    { range: '5–6', desc: 'Easily understood throughout; clear pronunciation.' },
    { range: '3–4', desc: 'Generally understood; occasional unclear words.' },
    { range: '1–2', desc: 'Frequent misunderstandings; requires listener effort.' },
  ]},
  { criterion: 'Fluency', weight: 'High', scale: '0–6', bands: [
    { range: '5–6', desc: 'Smooth, natural pace with appropriate pausing.' },
    { range: '3–4', desc: 'Some hesitations but maintains communication.' },
    { range: '1–2', desc: 'Frequent long pauses; broken delivery.' },
  ]},
  { criterion: 'Appropriateness of Language', weight: 'Medium', scale: '0–6', bands: [
    { range: '5–6', desc: 'Language perfectly suited to clinical context and patient.' },
    { range: '3–4', desc: 'Generally appropriate with occasional misjudgements.' },
    { range: '1–2', desc: 'Frequently inappropriate register or tone.' },
  ]},
  { criterion: 'Resources of Grammar & Expression', weight: 'Medium', scale: '0–6', bands: [
    { range: '5–6', desc: 'Wide range; rare errors; effective medical terminology.' },
    { range: '3–4', desc: 'Adequate range; some errors but meaning clear.' },
    { range: '1–2', desc: 'Very limited; frequent errors obscure meaning.' },
  ]},
  // Clinical Communication criteria (0–3 each — level descriptors)
  { criterion: 'Relationship Building', weight: 'High', scale: '0–3', bands: [
    { range: '3', desc: 'Adept: clear greeting/introduction, respectful attitude, non-judgemental, genuine empathy.' },
    { range: '2', desc: 'Competent: introductions present, empathy shown but may be generic.' },
    { range: '1', desc: 'Partially effective: mechanical greeting; empathy missing or misplaced.' },
    { range: '0', desc: 'Ineffective: no greeting/introduction or disrespectful tone.' },
  ]},
  { criterion: "Understanding & Incorporating Patient's Perspective", weight: 'High', scale: '0–3', bands: [
    { range: '3', desc: 'Adept: elicits ideas/concerns/expectations; relates explanations back to them.' },
    { range: '2', desc: 'Competent: acknowledges concerns; picks up some cues.' },
    { range: '1', desc: 'Partially effective: misses cues; concerns not explored.' },
    { range: '0', desc: 'Ineffective: ignores patient perspective.' },
  ]},
  { criterion: 'Providing Structure', weight: 'Medium', scale: '0–3', bands: [
    { range: '3', desc: 'Adept: logical sequencing, explicit signposting, organised explanations.' },
    { range: '2', desc: 'Competent: generally organised; some signposting.' },
    { range: '1', desc: 'Partially effective: jumps between topics without signposting.' },
    { range: '0', desc: 'Ineffective: disorganised, confusing flow.' },
  ]},
  { criterion: 'Information Gathering', weight: 'High', scale: '0–3', bands: [
    { range: '3', desc: 'Adept: open-then-closed questions; facilitates narrative; clarifies; summarises.' },
    { range: '2', desc: 'Competent: mostly appropriate questioning; one compound/leading question.' },
    { range: '1', desc: 'Partially effective: over-reliance on closed questions or interruptions.' },
    { range: '0', desc: 'Ineffective: leading/compound questions throughout.' },
  ]},
  { criterion: 'Information Giving', weight: 'High', scale: '0–3', bands: [
    { range: '3', desc: 'Adept: establishes prior knowledge, pauses, checks understanding, explores further needs.' },
    { range: '2', desc: 'Competent: explains clearly; some feedback sought.' },
    { range: '1', desc: 'Partially effective: monologue-style explanation; no checking.' },
    { range: '0', desc: 'Ineffective: no prior-knowledge check, no pausing, no checking.' },
  ]},
];

const CALIBRATION_TIPS = [
  'Compare AI scores with tutor scores to identify your calibration gaps.',
  'Scores within 1 band of the tutor are considered well-calibrated.',
  'When AI and tutor scores differ by 2+, read the tutor commentary carefully.',
  'Use the "I disagree" flag to surface scoring disputes for review.',
];

export default function RubricReferencePage() {
  return (
    <div className="min-h-screen bg-background">
      <PageViewBeacon event="expert_rubric_reference_viewed" />
      <div className="max-w-5xl mx-auto px-4 py-8 space-y-8">
        <div>
          <h1 className="text-3xl font-bold flex items-center gap-2"><BookOpen className="w-8 h-8" /> Expert Rubric Quick Reference</h1>
          <p className="text-muted-foreground mt-2">Band descriptors and scoring guidance for OET review criteria.</p>
        </div>

        <MotionSection className="space-y-6">
          <h2 className="text-xl font-semibold flex items-center gap-2"><Scale className="w-5 h-5" /> Writing Sub-test Rubric</h2>
          {WRITING_RUBRIC.map(r => (
            <MotionItem key={r.criterion}>
              <Card className="p-5">
                <div className="flex items-center justify-between mb-3">
                  <h3 className="font-semibold">{r.criterion}</h3>
                  <Badge variant={r.weight === 'High' ? 'default' : r.weight === 'Medium' ? 'muted' : 'outline'}>{r.weight} weight</Badge>
                </div>
                <div className="grid gap-2">
                  {r.bands.map(b => (
                    <div key={b.range} className="flex gap-3 items-start text-sm">
                      <span className="font-mono font-medium text-primary min-w-[3rem]">{b.range}</span>
                      <span className="text-muted-foreground">{b.desc}</span>
                    </div>
                  ))}
                </div>
              </Card>
            </MotionItem>
          ))}
        </MotionSection>

        <MotionSection className="space-y-6">
          <h2 className="text-xl font-semibold flex items-center gap-2"><Star className="w-5 h-5" /> Speaking Sub-test Rubric</h2>
          {SPEAKING_RUBRIC.map(r => (
            <MotionItem key={r.criterion}>
              <Card className="p-5">
                <div className="flex items-center justify-between mb-3">
                  <h3 className="font-semibold">{r.criterion}</h3>
                  <Badge variant={r.weight === 'High' ? 'default' : 'muted'}>{r.weight} weight</Badge>
                </div>
                <div className="grid gap-2">
                  {r.bands.map(b => (
                    <div key={b.range} className="flex gap-3 items-start text-sm">
                      <span className="font-mono font-medium text-primary min-w-[3rem]">{b.range}</span>
                      <span className="text-muted-foreground">{b.desc}</span>
                    </div>
                  ))}
                </div>
              </Card>
            </MotionItem>
          ))}
        </MotionSection>

        <MotionSection>
          <h2 className="text-xl font-semibold flex items-center gap-2 mb-4"><AlertTriangle className="w-5 h-5" /> Calibration Tips</h2>
          <Card className="p-5">
            <ul className="space-y-2">
              {CALIBRATION_TIPS.map((tip, i) => (
                <li key={i} className="flex gap-2 text-sm"><span className="text-primary font-bold">{i + 1}.</span>{tip}</li>
              ))}
            </ul>
          </Card>
        </MotionSection>
      </div>
    </div>
  );
}
