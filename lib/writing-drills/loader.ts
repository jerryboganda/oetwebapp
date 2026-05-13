/**
 * ============================================================================
 * Writing Drills — Loader
 * ============================================================================
 *
 * Static-imports the seeded drill JSON files, validates them against the
 * zod schema, and exposes typed lookup helpers. Pattern matches
 * `lib/rulebook/loader.ts`.
 *
 * Adding a new drill:
 *   1. Drop a JSON file under `rulebooks/writing/drills/<profession>/<type>/`.
 *   2. Add a static import + `register(...)` call below.
 *   3. Validation runs at module load — invalid drills throw immediately.
 * ============================================================================
 */

import medicineRelevance001 from '../../rulebooks/writing/drills/medicine/relevance/relevance-001.json';
import medicineOpening001 from '../../rulebooks/writing/drills/medicine/opening/opening-001.json';
import medicineOrdering001 from '../../rulebooks/writing/drills/medicine/ordering/ordering-001.json';
import medicineExpansion001 from '../../rulebooks/writing/drills/medicine/expansion/expansion-001.json';
import medicineTone001 from '../../rulebooks/writing/drills/medicine/tone/tone-001.json';
import medicineAbbreviation001 from '../../rulebooks/writing/drills/medicine/abbreviation/abbreviation-001.json';
import nursingAbbreviation001 from '../../rulebooks/writing/drills/nursing/abbreviation/abbreviation-001.json';
import pharmacyAbbreviation001 from '../../rulebooks/writing/drills/pharmacy/abbreviation/abbreviation-001.json';
import physiotherapyAbbreviation001 from '../../rulebooks/writing/drills/physiotherapy/abbreviation/abbreviation-001.json';
import dentistryAbbreviation001 from '../../rulebooks/writing/drills/dentistry/abbreviation/abbreviation-001.json';
import occupationalTherapyAbbreviation001 from '../../rulebooks/writing/drills/occupational-therapy/abbreviation/abbreviation-001.json';
import radiographyAbbreviation001 from '../../rulebooks/writing/drills/radiography/abbreviation/abbreviation-001.json';
import podiatryAbbreviation001 from '../../rulebooks/writing/drills/podiatry/abbreviation/abbreviation-001.json';
import dieteticsAbbreviation001 from '../../rulebooks/writing/drills/dietetics/abbreviation/abbreviation-001.json';
import optometryAbbreviation001 from '../../rulebooks/writing/drills/optometry/abbreviation/abbreviation-001.json';
import speechPathologyAbbreviation001 from '../../rulebooks/writing/drills/speech-pathology/abbreviation/abbreviation-001.json';
import veterinaryAbbreviation001 from '../../rulebooks/writing/drills/veterinary/abbreviation/abbreviation-001.json';

import {
  DrillSchema,
  toDrillSummary,
  type Drill,
  type DrillSummary,
  type DrillType,
  type LetterType,
  type Profession,
} from './types';

type ProfessionDrillSeed = {
  profession: Exclude<Profession, 'medicine'>;
  displayName: string;
  letterType: LetterType;
  patient: string;
  writerRole: string;
  recipientRole: string;
  purpose: string;
  currentNeed: string;
  historyPoint: string;
  request: string;
  specialistTerm: string;
};

const NON_MEDICINE_PROFESSION_DRILL_SEEDS: ProfessionDrillSeed[] = [
  { profession: 'nursing', displayName: 'Nursing', letterType: 'transfer', patient: 'Mrs Helen Carter, 76', writerRole: 'Ward nurse', recipientRole: 'Community nurse', purpose: 'Transfer for wound care and falls-risk monitoring after discharge', currentNeed: 'dressing changes for a healing lower-leg wound', historyPoint: 'two falls at home in the past month', request: 'review the wound, check mobility risks, and update the GP if healing stalls', specialistTerm: 'pressure-area care' },
  { profession: 'pharmacy', displayName: 'Pharmacy', letterType: 'advice', patient: 'Mr Omar Lane, 62', writerRole: 'Clinical pharmacist', recipientRole: 'General practitioner', purpose: 'Medication review after suspected warfarin interaction', currentNeed: 'a high INR after starting clarithromycin', historyPoint: 'long-term anticoagulation for atrial fibrillation', request: 'review the anticoagulation plan and arrange repeat INR testing', specialistTerm: 'medicine reconciliation' },
  { profession: 'physiotherapy', displayName: 'Physiotherapy', letterType: 'update', patient: 'Ms Priya Shah, 49', writerRole: 'Physiotherapist', recipientRole: 'Orthopaedic consultant', purpose: 'Progress update after knee rehabilitation', currentNeed: 'persistent swelling after six rehabilitation sessions', historyPoint: 'right knee arthroscopy eight weeks ago', request: 'advise whether further imaging or consultant review is required', specialistTerm: 'range-of-motion assessment' },
  { profession: 'dentistry', displayName: 'Dentistry', letterType: 'urgent_referral', patient: 'Mr Lucas Brown, 34', writerRole: 'Dentist', recipientRole: 'Oral and maxillofacial surgeon', purpose: 'Urgent referral for spreading wisdom-tooth infection', currentNeed: 'facial swelling and trismus despite oral antibiotics', historyPoint: 'recurrent pericoronitis around the lower right third molar', request: 'assess urgently for drainage or extraction', specialistTerm: 'pericoronal infection' },
  { profession: 'occupational_therapy', displayName: 'Occupational Therapy', letterType: 'discharge', patient: 'Mrs Agnes Miller, 81', writerRole: 'Occupational therapist', recipientRole: 'General practitioner', purpose: 'Discharge update with home-safety recommendations', currentNeed: 'difficulty showering safely after a wrist fracture', historyPoint: 'lives alone in a two-storey home', request: 'support equipment funding and monitor safety at home', specialistTerm: 'activities of daily living' },
  { profession: 'radiography', displayName: 'Radiography', letterType: 'update', patient: 'Ms Nora Evans, 52', writerRole: 'Reporting radiographer', recipientRole: 'Referring physician', purpose: 'Imaging update after abnormal chest CT findings', currentNeed: 'a newly identified right upper-lobe opacity', historyPoint: 'persistent cough for six weeks', request: 'correlate clinically and arrange respiratory follow-up', specialistTerm: 'contrast-enhanced CT' },
  { profession: 'podiatry', displayName: 'Podiatry', letterType: 'referral', patient: 'Mr Alan Reed, 67', writerRole: 'Podiatrist', recipientRole: 'Diabetes nurse specialist', purpose: 'Referral for diabetic foot ulcer monitoring', currentNeed: 'a plantar ulcer with delayed healing', historyPoint: 'type 2 diabetes with peripheral neuropathy', request: 'coordinate glycaemic review and high-risk foot follow-up', specialistTerm: 'off-loading footwear' },
  { profession: 'dietetics', displayName: 'Dietetics', letterType: 'advice', patient: 'Mrs Farah Khan, 45', writerRole: 'Dietitian', recipientRole: 'General practitioner', purpose: 'Nutrition advice after unintentional weight loss', currentNeed: 'six kilograms of unintentional weight loss in two months', historyPoint: 'reduced appetite during chemotherapy', request: 'monitor weight and consider oral nutrition supplements', specialistTerm: 'high-protein meal plan' },
  { profession: 'optometry', displayName: 'Optometry', letterType: 'urgent_referral', patient: 'Mr George Ellis, 71', writerRole: 'Optometrist', recipientRole: 'Ophthalmologist', purpose: 'Urgent referral for suspected retinal tear', currentNeed: 'new flashes, floaters, and a peripheral field shadow', historyPoint: 'high myopia and previous cataract surgery', request: 'assess the retina urgently and advise on treatment', specialistTerm: 'dilated fundus examination' },
  { profession: 'speech_pathology', displayName: 'Speech Pathology', letterType: 'referral', patient: 'Mrs Maria Cruz, 58', writerRole: 'Speech pathologist', recipientRole: 'Neurologist', purpose: 'Referral for dysphagia review after stroke', currentNeed: 'coughing with thin fluids during mealtimes', historyPoint: 'left hemispheric stroke four weeks ago', request: 'review neurological recovery and swallowing safety', specialistTerm: 'modified-texture diet' },
  { profession: 'veterinary', displayName: 'Veterinary', letterType: 'urgent_referral', patient: 'Bella, a 6-year-old Labrador', writerRole: 'Veterinary surgeon', recipientRole: 'Emergency veterinarian', purpose: 'Urgent referral for suspected gastric dilatation-volvulus', currentNeed: 'acute abdominal distension with non-productive retching', historyPoint: 'large-breed dog with sudden restlessness after feeding', request: 'assess immediately for stabilisation and surgery', specialistTerm: 'gastric dilatation-volvulus' },
];

function generatedProfessionDrills(): unknown[] {
  return NON_MEDICINE_PROFESSION_DRILL_SEEDS.flatMap((seed) => {
    const prefix = seed.profession.replace(/_/g, '-');
    return [
      {
        id: `writing-drill-${prefix}-relevance-001`,
        type: 'relevance',
        profession: seed.profession,
        letterType: seed.letterType,
        title: `Relevance — ${seed.displayName} Case-Note Selection`,
        brief: `Select the notes that support this ${seed.displayName.toLowerCase()} letter purpose; leave distracting biographical detail out.`,
        difficulty: 'core',
        estimatedMinutes: 5,
        rulebookRefs: ['R12.1', 'R12.2'],
        scenario: { patient: seed.patient, writerRole: seed.writerRole, recipientRole: seed.recipientRole, purpose: seed.purpose },
        notes: [
          { id: `${prefix}-rel-1`, category: 'purpose', text: seed.currentNeed, expected: 'relevant', rationale: 'This is the immediate issue that explains why the letter is being written.' },
          { id: `${prefix}-rel-2`, category: 'background', text: seed.historyPoint, expected: 'relevant', rationale: 'This background changes the reader’s assessment or follow-up plan.' },
          { id: `${prefix}-rel-3`, category: 'request', text: seed.request, expected: 'relevant', rationale: 'A clear request turns the letter from a summary into an actionable handover.' },
          { id: `${prefix}-rel-4`, category: 'clinical term', text: seed.specialistTerm, expected: 'relevant', rationale: 'The specialist term is useful because the recipient is a healthcare professional.' },
          { id: `${prefix}-rel-5`, category: 'safety', text: 'No known allergies or adverse reactions were documented at the last review.', expected: 'optional', rationale: 'Safety information can be included if it affects treatment or medication decisions.' },
          { id: `${prefix}-rel-6`, category: 'irrelevant history', text: 'Childhood tonsillectomy with no ongoing complications.', expected: 'irrelevant', rationale: 'Remote unrelated history distracts from the current purpose.' },
          { id: `${prefix}-rel-7`, category: 'social', text: 'Transport to appointments is unreliable unless family support is arranged.', expected: 'optional', rationale: 'Social context is optional unless it affects attendance or follow-up.' },
          { id: `${prefix}-rel-8`, category: 'administrative', text: 'The patient prefers morning appointments where possible.', expected: 'irrelevant', rationale: 'Scheduling preference is not necessary in the clinical body of this letter.' },
        ],
      },
      {
        id: `writing-drill-${prefix}-opening-001`,
        type: 'opening',
        profession: seed.profession,
        letterType: seed.letterType,
        title: `Opening — ${seed.displayName} Purpose-First Sentence`,
        brief: `Choose the opening that states the patient, purpose, and requested action for this ${seed.displayName.toLowerCase()} task.`,
        difficulty: 'core',
        estimatedMinutes: 3,
        rulebookRefs: ['R07.1', 'R07.2'],
        scenario: { patient: seed.patient, writerRole: seed.writerRole, recipientRole: seed.recipientRole, purpose: seed.purpose },
        choices: [
          { id: `${prefix}-open-1`, text: `I am writing regarding ${seed.patient} to request your support with ${seed.purpose.charAt(0).toLowerCase()}${seed.purpose.slice(1)}.`, quality: 'best', rationale: 'The sentence identifies the purpose and patient immediately without copying note form.', flags: [] },
          { id: `${prefix}-open-2`, text: `${seed.patient} has been reviewed by our service and has several issues that may need your attention.`, quality: 'acceptable', rationale: 'The patient is named, but the purpose and requested action are delayed.', flags: ['unclear_purpose'] },
          { id: `${prefix}-open-3`, text: `Hi, can you please look at this case when you have time?`, quality: 'weak', rationale: 'The tone is informal and the patient, problem, and purpose are missing.', flags: ['informal_tone', 'unclear_purpose', 'wrong_reader'] },
        ],
      },
      {
        id: `writing-drill-${prefix}-ordering-001`,
        type: 'ordering',
        profession: seed.profession,
        letterType: seed.letterType,
        title: `Ordering — ${seed.displayName} Logical Handover`,
        brief: `Arrange the paragraph units so the reader sees purpose, current issue, relevant background, then the requested action.`,
        difficulty: 'core',
        estimatedMinutes: 4,
        rulebookRefs: ['R08.1', 'R08.2'],
        items: [
          { id: `${prefix}-order-opening`, text: `I am writing regarding ${seed.patient} to request your support with ${seed.purpose.charAt(0).toLowerCase()}${seed.purpose.slice(1)}.`, role: 'opening' },
          { id: `${prefix}-order-current`, text: `The main current concern is ${seed.currentNeed}.`, role: 'current' },
          { id: `${prefix}-order-history`, text: `Relevant background includes ${seed.historyPoint}.`, role: 'history' },
          { id: `${prefix}-order-request`, text: `I would be grateful if you could ${seed.request}.`, role: 'request' },
        ],
        expectedOrder: [`${prefix}-order-opening`, `${prefix}-order-current`, `${prefix}-order-history`, `${prefix}-order-request`],
      },
      {
        id: `writing-drill-${prefix}-expansion-001`,
        type: 'expansion',
        profession: seed.profession,
        letterType: seed.letterType,
        title: `Expansion — ${seed.displayName} Note Form to Sentence`,
        brief: `Rewrite compact notes as full clinical sentences suitable for the recipient.`,
        difficulty: 'core',
        estimatedMinutes: 5,
        rulebookRefs: ['R09.1', 'R09.2'],
        targets: [
          { id: `${prefix}-exp-1`, noteForm: `${seed.patient}; current issue: ${seed.currentNeed}`, mustInclude: [seed.patient.split(',')[0], seed.currentNeed.split(' ')[0]], mustNotInclude: [';'], exemplar: `${seed.patient} currently requires attention because of ${seed.currentNeed}.`, rationale: 'A full sentence names the patient and explains the issue clearly.' },
          { id: `${prefix}-exp-2`, noteForm: `Please ${seed.request}`, mustInclude: ['please', seed.request.split(' ')[0]], mustNotInclude: ['->'], exemplar: `I would be grateful if you could ${seed.request}.`, rationale: 'The request should be polite, grammatical, and specific.' },
        ],
      },
      {
        id: `writing-drill-${prefix}-tone-001`,
        type: 'tone',
        profession: seed.profession,
        letterType: seed.letterType,
        title: `Tone — ${seed.displayName} Professional Register`,
        brief: `Convert informal clinical messages into concise professional phrasing.`,
        difficulty: 'core',
        estimatedMinutes: 4,
        rulebookRefs: ['R10.1', 'R10.2'],
        items: [
          { id: `${prefix}-tone-1`, informal: `Can you have a quick look at ${seed.patient}?`, acceptableFormal: ['I would be grateful if you could assess', 'I would appreciate your assessment'], forbidden: ['quick look', 'can you'], exemplar: `I would be grateful if you could assess ${seed.patient}.`, rationale: 'The revised version is polite and specific without sounding casual.' },
          { id: `${prefix}-tone-2`, informal: `They have been a bit difficult to manage, so I am sending them on.`, acceptableFormal: ['requires further review', 'would benefit from further review'], forbidden: ['difficult', 'sending them on'], exemplar: `${seed.patient} requires further review because ${seed.currentNeed}.`, rationale: 'Professional tone describes the clinical need rather than judging the patient.' },
        ],
      },
    ];
  });
}

const RAW_REGISTRY: unknown[] = [
  medicineRelevance001,
  medicineOpening001,
  medicineOrdering001,
  medicineExpansion001,
  medicineTone001,
  medicineAbbreviation001,
  nursingAbbreviation001,
  pharmacyAbbreviation001,
  physiotherapyAbbreviation001,
  dentistryAbbreviation001,
  occupationalTherapyAbbreviation001,
  radiographyAbbreviation001,
  podiatryAbbreviation001,
  dieteticsAbbreviation001,
  optometryAbbreviation001,
  speechPathologyAbbreviation001,
  veterinaryAbbreviation001,
  ...generatedProfessionDrills(),
];

let registry: Map<string, Drill> | null = null;

function buildRegistry(): Map<string, Drill> {
  const map = new Map<string, Drill>();
  for (const raw of RAW_REGISTRY) {
    const parsed = DrillSchema.safeParse(raw);
    if (!parsed.success) {
      const issues = parsed.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`).join('; ');
      const id = (raw as { id?: string })?.id ?? '<unknown>';
      throw new Error(`[writing-drills] Invalid drill "${id}": ${issues}`);
    }
    if (map.has(parsed.data.id)) {
      throw new Error(`[writing-drills] Duplicate drill id: ${parsed.data.id}`);
    }
    map.set(parsed.data.id, parsed.data);
  }
  return map;
}

function getRegistry(): Map<string, Drill> {
  if (!registry) registry = buildRegistry();
  return registry;
}

export class DrillNotFoundError extends Error {
  constructor(id: string) {
    super(`Writing drill not found: ${id}`);
    this.name = 'DrillNotFoundError';
  }
}

export function listDrills(filter?: {
  type?: DrillType;
  profession?: Profession;
}): DrillSummary[] {
  const all = Array.from(getRegistry().values());
  return all
    .filter((d) => (filter?.type ? d.type === filter.type : true))
    .filter((d) => (filter?.profession ? d.profession === filter.profession : true))
    .map(toDrillSummary);
}

export function getDrill(id: string): Drill {
  const drill = getRegistry().get(id);
  if (!drill) throw new DrillNotFoundError(id);
  return drill;
}

export function getDrillsByType(type: DrillType, profession?: Profession): Drill[] {
  return Array.from(getRegistry().values())
    .filter((d) => d.type === type)
    .filter((d) => (profession ? d.profession === profession : true));
}
