import FeedbackGuideContent, { type RubricCriterion } from './_content';

const WRITING_CRITERIA: RubricCriterion[] = [
  { code: 'overall_task_fulfilment', label: 'Overall Task Fulfilment', bands: '0–7', description: 'How well the letter addresses all information from the case notes, with appropriate prioritisation.', improve: 'Identify ALL relevant case note points. Organise by clinical importance, not chronological order.' },
  { code: 'appropriateness_of_language', label: 'Appropriateness of Language', bands: '0–7', description: 'Register, tone, and formality suited to the healthcare communication purpose.', improve: 'Use formal clinical register. Avoid colloquialisms. Match tone to the recipient (GP, specialist, patient).' },
  { code: 'comprehension_of_stimulus', label: 'Comprehension of Stimulus', bands: '0–7', description: 'Accurate understanding and representation of the case note information.', improve: 'Read case notes twice. Don\'t misinterpret abbreviations. Distinguish relevant from irrelevant details.' },
  { code: 'linguistic_features', label: 'Linguistic Features (Grammar & Cohesion)', bands: '0–7', description: 'Grammar accuracy, sentence structure variety, and text cohesion.', improve: 'Use complex sentences appropriately. Ensure pronoun references are clear. Use linking words for cohesion.' },
  { code: 'presentation_of_purpose', label: 'Presentation of Purpose', bands: '0–7', description: 'Clear statement of the letter\'s purpose and requested action.', improve: 'State purpose in the opening paragraph. Be specific about what you\'re requesting from the recipient.' },
  { code: 'conciseness', label: 'Conciseness', bands: '0–7', description: 'Appropriate length and absence of unnecessary repetition or irrelevant information.', improve: 'Remove filler phrases. Don\'t repeat information. Keep to approximately 180-200 words.' },
];

const SPEAKING_CRITERIA: RubricCriterion[] = [
  { code: 'intelligibility', label: 'Intelligibility', bands: '0–6', description: 'How clearly and easily understood your speech is.', improve: 'Focus on clear word endings and stress patterns. Slow down at key information points.' },
  { code: 'fluency', label: 'Fluency', bands: '0–6', description: 'Smooth delivery with natural pace and minimal hesitation.', improve: 'Reduce filler words (um, uh). Practice connected speech. Allow natural pauses at sentence boundaries.' },
  { code: 'appropriateness', label: 'Appropriateness of Language', bands: '0–6', description: 'Using language suited to the clinical context and patient relationship.', improve: 'Avoid jargon with patients. Use empathetic language. Adapt formality to the role-play scenario.' },
  { code: 'resources_of_grammar_expression', label: 'Resources of Grammar & Expression', bands: '0–6', description: 'Range and accuracy of grammatical structures and vocabulary.', improve: 'Use varied sentence types. Employ medical terminology accurately. Use conditional and modal verbs.' },
  { code: 'relationship_building', label: 'Relationship Building', bands: '0–6', description: 'Demonstrating empathy, active listening, and rapport.', improve: 'Acknowledge patient concerns explicitly. Use the patient\'s name. Ask open-ended questions.' },
];

export default function FeedbackGuidePage() {
  return <FeedbackGuideContent writingCriteria={WRITING_CRITERIA} speakingCriteria={SPEAKING_CRITERIA} />;
}
