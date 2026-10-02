'use client';

import { HelpCircle, CheckCircle2, TrendingUp, Target } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { PageViewBeacon } from '@/components/analytics/page-view-beacon';

const WRITING_CRITERIA = [
  { code: 'overall_task_fulfilment', label: 'Overall Task Fulfilment', bands: '0–7', description: 'How well the letter addresses all information from the case notes, with appropriate prioritisation.', improve: 'Identify ALL relevant case note points. Organise by clinical importance, not chronological order.' },
  { code: 'appropriateness_of_language', label: 'Appropriateness of Language', bands: '0–7', description: 'Register, tone, and formality suited to the healthcare communication purpose.', improve: 'Use formal clinical register. Avoid colloquialisms. Match tone to the recipient (GP, specialist, patient).' },
  { code: 'comprehension_of_stimulus', label: 'Comprehension of Stimulus', bands: '0–7', description: 'Accurate understanding and representation of the case note information.', improve: 'Read case notes twice. Don\'t misinterpret abbreviations. Distinguish relevant from irrelevant details.' },
  { code: 'linguistic_features', label: 'Linguistic Features (Grammar & Cohesion)', bands: '0–7', description: 'Grammar accuracy, sentence structure variety, and text cohesion.', improve: 'Use complex sentences appropriately. Ensure pronoun references are clear. Use linking words for cohesion.' },
  { code: 'presentation_of_purpose', label: 'Presentation of Purpose', bands: '0–7', description: 'Clear statement of the letter\'s purpose and requested action.', improve: 'State purpose in the opening paragraph. Be specific about what you\'re requesting from the recipient.' },
  { code: 'conciseness', label: 'Conciseness', bands: '0–7', description: 'Appropriate length and absence of unnecessary repetition or irrelevant information.', improve: 'Remove filler phrases. Don\'t repeat information. Keep to approximately 180-200 words.' },
];

const SPEAKING_CRITERIA = [
  { code: 'intelligibility', label: 'Intelligibility', bands: '0–6', description: 'How clearly and easily understood your speech is.', improve: 'Focus on clear word endings and stress patterns. Slow down at key information points.' },
  { code: 'fluency', label: 'Fluency', bands: '0–6', description: 'Smooth delivery with natural pace and minimal hesitation.', improve: 'Reduce filler words (um, uh). Practice connected speech. Allow natural pauses at sentence boundaries.' },
  { code: 'appropriateness', label: 'Appropriateness of Language', bands: '0–6', description: 'Using language suited to the clinical context and patient relationship.', improve: 'Avoid jargon with patients. Use empathetic language. Adapt formality to the role-play scenario.' },
  { code: 'resources_of_grammar_expression', label: 'Resources of Grammar & Expression', bands: '0–6', description: 'Range and accuracy of grammatical structures and vocabulary.', improve: 'Use varied sentence types. Employ medical terminology accurately. Use conditional and modal verbs.' },
  { code: 'relationship_building', label: 'Relationship Building', bands: '0–6', description: 'Demonstrating empathy, active listening, and rapport.', improve: 'Acknowledge patient concerns explicitly. Use the patient\'s name. Ask open-ended questions.' },
];

export default function FeedbackGuidePage() {
  const heroHighlights = [
    { icon: CheckCircle2, label: 'Writing criteria', value: `${WRITING_CRITERIA.length}` },
    { icon: Target, label: 'Speaking criteria', value: `${SPEAKING_CRITERIA.length}` },
    { icon: TrendingUp, label: 'Score bands', value: '0–7 / 0–6 / 0–3' },
  ];

  return (
    <>
      <PageViewBeacon event="feedback_guide_viewed" />
      <LearnerPageHero
        title="Feedback Interpretation Guide"
        description="Understand what each criterion score means and how to improve."
        icon={HelpCircle}
        highlights={heroHighlights}
      />

      <MotionSection>
        <LearnerSurfaceSectionHeader
          eyebrow="Writing criteria"
          title="How writing feedback is scored"
          description="Understand each Writing criterion and what it scores."
          className="mb-4"
        />
        <CriteriaList criteria={WRITING_CRITERIA} />
      </MotionSection>

      <MotionSection delayIndex={1}>
        <LearnerSurfaceSectionHeader
          eyebrow="Speaking criteria"
          title="How speaking feedback is scored"
          description="A guided walkthrough of your Speaking feedback."
          className="mb-4"
        />
        <CriteriaList criteria={SPEAKING_CRITERIA} />
      </MotionSection>

      <MotionSection delayIndex={2}>
        <LearnerSurfaceSectionHeader
          eyebrow="Score guide"
          title="How to read the bands"
          description="Quick interpretation tips so you know exactly what to do next."
          className="mb-4"
        />
        <Card padding="lg" className="space-y-3">
          <div className="flex items-start gap-2"><CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-success-strong" aria-hidden="true" /><p className="text-sm"><strong>Score 5-7 (Writing) / 5-6 (Speaking):</strong> Strong performance. Focus on consistency and refinement.</p></div>
          <div className="flex items-start gap-2"><Target className="mt-0.5 h-5 w-5 shrink-0 text-warning-strong" aria-hidden="true" /><p className="text-sm"><strong>Score 3-4:</strong> Adequate but needs improvement. Target specific criteria with focused practice.</p></div>
          <div className="flex items-start gap-2"><HelpCircle className="mt-0.5 h-5 w-5 shrink-0 text-danger-strong" aria-hidden="true" /><p className="text-sm"><strong>Score 0-2:</strong> Significant gaps. Start with foundation resources and work with a tutor reviewer.</p></div>
        </Card>
      </MotionSection>
    </>
  );
}

function CriteriaList({ criteria }: { criteria: typeof WRITING_CRITERIA }) {
  return (
    <div className="space-y-3">
      {criteria.map((c, index) => (
        <MotionItem key={c.code} delayIndex={Math.min(index, 5)}>
          <Card>
            <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
              <h3 className="font-semibold text-navy">{c.label}</h3>
              <Badge variant="outline" className="tabular-nums">Bands {c.bands}</Badge>
            </div>
            <p className="max-w-3xl text-sm text-muted">{c.description}</p>
            <div className="mt-3 rounded-xl bg-success/10 p-3">
              <p className="max-w-3xl text-sm"><TrendingUp className="me-1 inline h-4 w-4 text-success-strong" aria-hidden="true" /><strong>How to improve:</strong> {c.improve}</p>
            </div>
          </Card>
        </MotionItem>
      ))}
    </div>
  );
}
