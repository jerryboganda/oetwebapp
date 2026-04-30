/**
 * scripts/generate-writing-rulebooks.cjs
 *
 * Phase D — generate writing rulebook.v1.json for the 11 non-medicine
 * professions. Each profession-specific rulebook clones the medicine rules
 * verbatim (OET writing rules are largely profession-agnostic) and overrides:
 *   - `profession` field
 *   - `authoritySource`
 *   - a small `professionSpecific` payload describing recipient defaults,
 *     letter-type emphasis, and any profession-only smoking/drinking carve-outs.
 *
 * The medicine rulebook remains the canonical source. To resync after editing
 * medicine/rulebook.v1.json, re-run this script:
 *   node scripts/generate-writing-rulebooks.cjs
 */

const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.join(__dirname, '..', 'rulebooks', 'writing');
const BASE_PATH = path.join(ROOT, 'medicine', 'rulebook.v1.json');

const base = JSON.parse(fs.readFileSync(BASE_PATH, 'utf8'));

/**
 * Per-profession metadata. Slugs match `ExamProfession` in lib/rulebook/types.ts.
 * `recipients` lists the most common letter recipients in OET tasks for that
 * profession, useful for the AI grounded prompt and for drill seeding.
 */
const PROFESSIONS = [
  {
    slug: 'nursing',
    label: 'Nursing',
    recipients: ['Charge Nurse', 'Nurse Manager', 'Nurse Unit Manager', 'GP', 'Specialist Nurse'],
    primaryLetterTypes: ['referral', 'transfer', 'discharge'],
    notes: 'Letters are typically nurse-to-nurse handovers, transfers, or referrals to GP. Handover detail and care plan continuity are weighted heavily.',
  },
  {
    slug: 'dentistry',
    label: 'Dentistry',
    recipients: ['Oral Surgeon', 'Periodontist', 'Orthodontist', 'GP', 'Endodontist'],
    primaryLetterTypes: ['referral', 'urgent_referral'],
    notes: 'Most common: referral to a specialist for treatment that exceeds general-practice scope. Include relevant medical history when sedation or surgery is planned.',
  },
  {
    slug: 'pharmacy',
    label: 'Pharmacy',
    recipients: ['GP', 'Prescriber', 'Specialist'],
    primaryLetterTypes: ['referral', 'advice'],
    notes: 'Pharmacist letters are usually advice or medication-review referrals to the prescriber. Always cite drug, dose, frequency, indication, and the suspected issue.',
  },
  {
    slug: 'physiotherapy',
    label: 'Physiotherapy',
    recipients: ['GP', 'Orthopaedic Surgeon', 'Sports Physician', 'Rheumatologist'],
    primaryLetterTypes: ['referral', 'discharge'],
    notes: 'Functional status, ROM, pain, and adherence to home exercise programme drive content selection.',
  },
  {
    slug: 'veterinary',
    label: 'Veterinary',
    recipients: ['Specialist Veterinarian', 'Veterinary Surgeon', 'Veterinary Dermatologist', 'Owner'],
    primaryLetterTypes: ['referral', 'advice'],
    notes: 'Patient = animal; owner provides history. Replace human-specific demographics with species, breed, age, weight, and vaccination status. Smoking/drinking rules do not apply.',
    overrides: {
      // Veterinary letters never need smoking/drinking on the patient.
      smokingDrinkingRequired: false,
    },
  },
  {
    slug: 'optometry',
    label: 'Optometry',
    recipients: ['Ophthalmologist', 'GP', 'Low Vision Clinic', 'Paediatric Ophthalmologist'],
    primaryLetterTypes: ['referral', 'urgent_referral'],
    notes: 'Visual acuity, intraocular pressure, refraction, and history of systemic disease (diabetes, hypertension) drive content.',
  },
  {
    slug: 'radiography',
    label: 'Radiography',
    recipients: ['Referring Doctor', 'Radiologist', 'Specialist'],
    primaryLetterTypes: ['advice', 'referral'],
    notes: 'Most common: report or follow-up advice to the referring doctor. Imaging modality, region, findings, and recommended follow-up are mandatory.',
  },
  {
    slug: 'occupational-therapy',
    label: 'Occupational Therapy',
    recipients: ['GP', 'Specialist OT', 'Care Facility Manager', 'Social Worker'],
    primaryLetterTypes: ['referral', 'discharge', 'advice'],
    notes: 'Functional capacity, activities of daily living (ADLs), home environment and equipment needs drive content.',
    overrides: {
      // Per R03.4, smoking/drinking is excluded when writing TO an OT, not FROM.
      // OT-authored letters still include smoking/drinking when relevant to the recipient.
      smokingDrinkingRequired: true,
    },
  },
  {
    slug: 'speech-pathology',
    label: 'Speech Pathology',
    recipients: ['GP', 'ENT Specialist', 'Neurologist', 'Audiologist', 'School'],
    primaryLetterTypes: ['referral', 'discharge'],
    notes: 'Communication and swallowing assessment results, communication aids, and family/caregiver involvement are central.',
  },
  {
    slug: 'podiatry',
    label: 'Podiatry',
    recipients: ['GP', 'Vascular Surgeon', 'Endocrinologist', 'Orthopaedic Surgeon'],
    primaryLetterTypes: ['referral', 'urgent_referral'],
    notes: 'Diabetic foot status, vascular assessment, and ulcer staging drive content selection.',
  },
  {
    slug: 'dietetics',
    label: 'Dietetics',
    recipients: ['GP', 'Endocrinologist', 'Gastroenterologist', 'Bariatric Surgeon'],
    primaryLetterTypes: ['referral', 'advice'],
    notes: 'Anthropometry, dietary recall, biochemistry (HbA1c, lipids), and adherence to dietary plan drive content.',
  },
];

const PROFESSION_SLUGS = PROFESSIONS.map((p) => p.slug);

let written = 0;
let skipped = 0;

for (const meta of PROFESSIONS) {
  const dir = path.join(ROOT, meta.slug);
  const out = path.join(dir, 'rulebook.v1.json');
  fs.mkdirSync(dir, { recursive: true });

  // Deep clone the base via JSON round-trip so each profession's rulebook
  // is fully independent and can be edited in place without affecting medicine.
  const cloned = JSON.parse(JSON.stringify(base));
  cloned.profession = meta.slug;
  cloned.authoritySource = `${base.authoritySource} — adapted for ${meta.label} (Phase D v1)`;
  cloned.professionSpecific = {
    label: meta.label,
    typicalRecipients: meta.recipients,
    primaryLetterTypes: meta.primaryLetterTypes,
    notes: meta.notes,
    ...(meta.overrides ?? {}),
  };

  fs.writeFileSync(out, JSON.stringify(cloned, null, 2) + '\n', 'utf8');
  written += 1;
  // eslint-disable-next-line no-console
  console.log(`wrote ${path.relative(path.join(__dirname, '..'), out)}`);
}

// eslint-disable-next-line no-console
console.log(`\nGenerated ${written} rulebook(s); skipped ${skipped}.`);
console.log(`Profession slugs: ${PROFESSION_SLUGS.join(', ')}`);
