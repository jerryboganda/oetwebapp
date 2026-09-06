/**
 * Candidate-facing Speaking reference content — single source of truth.
 *
 * Two native resources shown in the Speaking selection block:
 *  - Speaking Assessment Criteria (same for all professions, 9 criteria)
 *  - Speaking Intro Questions (12 global questions with personalisable sample answers)
 *
 * Deliberately separate from the internal Speaking rulebook
 * (`rulebooks/speaking/*`, `/speaking/rulebook`) and from AI grading prompts.
 * No source-PDF branding, cover, footer, or contact details belong here.
 */

export interface SpeakingCriterion {
  no: number;
  id: string;
  name: string;
  weight: string;
  points: number;
  summary: string;
  whatItMeans: string[];
  doWell: string[];
  avoid: string[];
}

export const SPEAKING_CRITERIA: SpeakingCriterion[] = [
  {
    no: 1,
    id: 'intelligibility',
    name: 'Intelligibility',
    weight: '6 points',
    points: 6,
    summary: 'Be easy to understand — clear pronunciation, word stress, rhythm, and intonation.',
    whatItMeans: [
      'Having an accent is expected. Your pronunciation, word stress, and rhythm should still be clear and easy to follow.',
      'Words that sound similar must sound distinct (for example, “share” vs “chair”; “today” must not sound like “tidy”).',
      'Match your tone to the function: let your voice rise at the end of questions and fall at the end of statements.',
      'Keep a steady pace — not so fast or so slow that the listener has to strain to stay focused.',
    ],
    doWell: [
      'Correct, natural word choice',
      'Clear pronunciation and even rhythm',
      'Appropriate intonation for questions vs statements',
    ],
    avoid: [
      'Poor or limited word choice',
      'Unclear pronunciation',
      'Speaking too quickly, too slowly, or with erratic fast–slow pacing',
      'Speaking in a monotone',
    ],
  },
  {
    no: 2,
    id: 'fluency',
    name: 'Fluency',
    weight: '6 points',
    points: 6,
    summary: 'Keep a natural flow — occasional hesitation is fine, constant stopping is not.',
    whatItMeans: [
      'Occasional hesitation is normal. Overall your speech should flow at a normal pace without excessive pauses that strain the listener.',
      'A rare filler sound (“um”, “err”) or a quick self-correction is acceptable.',
      'Frequent self-correction or hesitation suggests a limited range of vocabulary and grammar.',
    ],
    doWell: [
      'Rare self-correction and filler sounds',
      'Good range of expressions',
      'Normal, steady flow of conversation',
    ],
    avoid: [
      'Repeated self-correction mid-sentence',
      'Searching audibly for words',
      'Overusing filler sounds such as “um”, “err”, “ahh”',
    ],
  },
  {
    no: 3,
    id: 'appropriateness',
    name: 'Appropriateness of language',
    weight: '6 points',
    points: 6,
    summary: 'Stay professional yet approachable, using language your patient or client understands.',
    whatItMeans: [
      'Be approachable but always professional — never overly familiar or informal.',
      'Use vocabulary a lay person understands. Avoid overusing technical jargon, but do not talk down to the person.',
      'If you are unsure they understood a term, ask — then explain it in simpler words.',
    ],
    doWell: [
      'Professional but warm tone',
      'Plain language for technical ideas, with brief explanations where needed',
      'Checking understanding before moving on',
    ],
    avoid: [
      'Being overly familiar or informal',
      'Using technical jargon without explanation',
      'Assuming understanding without checking',
    ],
  },
  {
    no: 4,
    id: 'grammar-expression',
    name: 'Grammar and expression',
    weight: '6 points',
    points: 6,
    summary: 'Use a wide, accurate range of grammar and vocabulary — and adapt it to the moment.',
    whatItMeans: [
      'Show a wide range of grammar and vocabulary, used correctly and accurately.',
      'Be comfortable with natural, idiomatic speech — and adjust your wording when the other person responds unexpectedly.',
      'Avoid relying on memorised phrases that may not fit the situation. Occasional small errors are fine if the conversation still flows.',
    ],
    doWell: [
      'Wide range of vocabulary and grammar',
      'Confident, natural phrasing',
      'Flexibly rewording when the response changes',
      'Few errors, so neither side has to strain',
    ],
    avoid: [
      'Fixed memorised phrases that do not fit the situation',
      'Limited vocabulary with no way to rephrase',
      'Frequent grammar errors that make the conversation a strain',
    ],
  },
  {
    no: 5,
    id: 'relationship-building',
    name: 'Relationship building',
    weight: '3 points',
    points: 3,
    summary: 'Open warmly and stay respectful, empathetic, and attentive throughout.',
    whatItMeans: [
      'Start with an appropriate greeting — professional but approachable.',
      'Be respectful, empathetic, and non-judgemental while the other person speaks.',
      'Stay attentive: do not cut the person off, look distracted, or judge their concerns.',
    ],
    doWell: [
      'Greeting the person by name and putting them at ease',
      'Listening attentively without interrupting',
      'Responding with empathy, not judgement',
    ],
    avoid: [
      'Opening with a blunt “So what is your problem today?”',
      'Cutting the person off or appearing distracted',
      'Judgemental remarks about their situation',
    ],
  },
  {
    no: 6,
    id: 'patient-perspective',
    name: 'Incorporating patient perspective',
    weight: '3 points',
    points: 3,
    summary: 'Show you heard their worries — even when they differ from your clinical view.',
    whatItMeans: [
      'The other person’s concerns may differ from your professional priorities. Show you have heard their worries and put them in perspective.',
      'Gently confirm what is correct and correct what is inaccurate — professionally and without judgement.',
      'Pick up on cues about fears or beliefs rather than dismissing them.',
    ],
    doWell: [
      'Listening for worries and naming them back',
      'Picking up on emotional cues and exploring them kindly',
      'Correcting misinformation respectfully',
    ],
    avoid: [
      'Dismissing concerns as unimportant next to the clinical issue',
      'Being rude about inaccurate information',
      'Ignoring hints about fears at home or about treatment',
    ],
  },
  {
    no: 7,
    id: 'providing-structure',
    name: 'Providing structure',
    weight: '3 points',
    points: 3,
    summary: 'Guide the conversation through the task points without rushing or controlling it.',
    whatItMeans: [
      'Use the role-play points to guide the interview while letting the other person speak freely.',
      'Use signposting and linking language to move forward naturally (“Speaking of tablets, how is your blood pressure?”).',
      'Cover as many points as you can, in any sensible order — you do not need a rigid order, and you do not need every single point.',
    ],
    doWell: [
      'Opening the interview clearly (“Hello, my name is…”)',
      'Guiding with prompts (“Tell me about…”)',
      'Using the other person’s own words as a bridge to the next topic',
      'Allowing time to answer without interrupting',
    ],
    avoid: [
      'Waiting for the other person to lead the conversation',
      'Letting one topic run so long that other points are missed',
      'Rushing through every point with no room to answer',
      'Sticking rigidly to card order with no flexibility',
    ],
  },
  {
    no: 8,
    id: 'information-gathering',
    name: 'Information gathering',
    weight: '3 points',
    points: 3,
    summary: 'Ask open questions first, follow cues, and clarify — don’t interrogate.',
    whatItMeans: [
      'Start with open questions so the person can answer freely, then narrow to closed questions.',
      'Follow the person’s cues and signposts into the next topic (“You mentioned long hours at work — does that leave time to exercise?”).',
      'Ask for clarification when unsure, and acknowledge concerns even when they are not the main clinical focus.',
    ],
    doWell: [
      'Open question first, then focused follow-up (“Roughly how much do you drink in the evening?” → “Do you think this affects your sleep?”)',
      'Using cues to open the next topic',
      'Clarifying vague answers (“When you say insomnia, do you mean falling asleep or waking often?”)',
      'Acknowledging worries, including ones off the task card',
    ],
    avoid: [
      'Only yes/no questions',
      'Compound questions (“How much do you drink and how many hours do you sleep?”)',
      'Rushing past answers you did not understand',
      'Ignoring concerns that are not on the role-play card',
    ],
  },
  {
    no: 9,
    id: 'information-giving',
    name: 'Information giving',
    weight: '3 points',
    points: 3,
    summary: 'Build on what they already know, explain clearly, and check understanding.',
    whatItMeans: [
      'First find out what the person already knows, then build on it and correct anything inaccurate.',
      'Give information in stages: explain one point, check understanding, then move on — and factor in their opinions and attitudes.',
      'Summarise the important points, confirm they have what they need, and offer where to find out more where relevant.',
    ],
    doWell: [
      'Asking what they know first (“Have you had this procedure before?”)',
      'Explaining briefly after they answer, in plain language',
      'Pausing to check (“Does that make sense so far?”)',
      'Offering a leaflet or trusted source for more detail',
    ],
    avoid: [
      'Assuming they know nothing and over-explaining the obvious',
      'Asking everything first, then dumping all the information at once',
      'Never pausing to check understanding',
      'Ignoring their views when giving advice',
    ],
  },
];

export interface SpeakingIntroQuestion {
  no: number;
  question: string;
  sampleAnswer: string;
  note?: string;
}

export const SPEAKING_INTRO_QUESTIONS: SpeakingIntroQuestion[] = [
  {
    no: 1,
    question: 'What is your name?',
    sampleAnswer: 'My name is [full name], but you can call me [preferred name].',
  },
  {
    no: 2,
    question: 'What is your profession?',
    sampleAnswer:
      'I am a [profession]. I have been working in this field for [number of years], mainly in [area / setting].',
  },
  {
    no: 3,
    question: 'Why are you taking the OET?',
    sampleAnswer:
      'I am taking the OET because I need to demonstrate my English proficiency for [professional registration / work / study] in [country or organisation]. Passing the test will help me move forward with my career plans and communicate safely and confidently in an English-speaking healthcare environment.',
  },
  {
    no: 4,
    question: 'How long have you been working in your profession?',
    sampleAnswer:
      'I have been working as a [profession] for [number of years]. Most of my experience has been in [main workplace / specialty / area], where I have developed experience in [brief example of your work].',
  },
  {
    no: 5,
    question: 'What are your usual working hours?',
    sampleAnswer:
      'I usually work around [number] hours a day, [number] days a week. My schedule is generally [daytime / evening / shift-based], although it can vary depending on the workload, appointments, and any urgent cases.',
  },
  {
    no: 6,
    question: 'Why did you choose your profession as a career?',
    sampleAnswer:
      "I chose [profession] because I wanted a career that combines professional knowledge, communication, and the opportunity to make a meaningful difference to people's health and quality of life. I also particularly enjoy [specific aspect of your profession].",
  },
  {
    no: 7,
    question: 'What is your specialty / main area of practice, and why did you choose it?',
    sampleAnswer:
      'My main area of practice is [specialty / area]. I chose it because [personal or professional reason]. I particularly enjoy [specific feature of the work], and I feel that it suits my interests and strengths.',
    note: 'If your profession does not use formal specialties, answer using your main area of practice, workplace, or clinical focus.',
  },
  {
    no: 8,
    question: 'What advice would you give to fresh graduates in your profession?',
    sampleAnswer:
      'I would advise new graduates to keep learning, ask for feedback, communicate clearly, and never be afraid to seek help when they need it. It is also important to stay up to date, work well with the wider team, and treat every patient or client with respect and empathy.',
  },
  {
    no: 9,
    question: 'What do you think makes someone successful in your profession?',
    sampleAnswer:
      'I think success comes from a combination of strong professional knowledge, good communication, empathy, teamwork, reliability, and continuous learning. A successful professional should also know their limits, ask for support when necessary, and always put safety and quality of care first.',
  },
  {
    no: 10,
    question: 'What was the last professional training or course you completed?',
    sampleAnswer:
      'The most recent training I completed was [course / workshop / programme]. It focused on [topic]. I found it very useful because it improved my knowledge and confidence in [area], and I have been able to apply what I learned in my daily work.',
  },
  {
    no: 11,
    question: 'What is the most recent advance in your profession that you have heard about?',
    sampleAnswer:
      'One recent development I have been interested in is [new technology / treatment / guideline / professional development]. It may improve [patient outcome / safety / efficiency / quality of care]. I find it interesting because it shows how quickly the profession is evolving, so I try to keep up to date through guidelines, courses, and professional literature.',
  },
  {
    no: 12,
    question: 'What does a typical working day look like for you?',
    sampleAnswer:
      'A typical working day usually starts with [handover / reviewing appointments / preparing the clinic]. I then spend most of the day [main clinical or professional duties]. I also spend time documenting my work, communicating with colleagues, and supporting or educating patients and clients. Every day is slightly different, which is one of the things I enjoy about my profession.',
    note: 'If you are not currently working, adapt this answer to your most recent role, internship, placement, or usual clinical training day.',
  },
];

export const SPEAKING_ASSESSMENT_CRITERIA_HREF = '/speaking/assessment-criteria';
export const SPEAKING_INTRO_QUESTIONS_HREF = '/speaking/intro-questions';
