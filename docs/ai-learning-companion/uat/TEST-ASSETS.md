# SAMI UAT — Standardised Multimodal Test Assets (SAMI PDF §21)

These are the synthetic reproducibility assets for the 80-scenario UAT. All values are
fictional. Testers screenshot / photograph / record them exactly as each scenario
instructs — the tester action is listed on every asset.

---

## TEST ASSET A — Synthetic OET Score Report

Open `test-asset-a-score-report.html` in a browser at 100% zoom and screenshot the card.
Upload the SCREENSHOT (not typed values) wherever a scenario says "score report".

| Field | Value |
|---|---|
| Candidate | TEST CANDIDATE |
| Profession | Medicine |
| Test date | 30 August 2026 |
| Listening | 320 |
| Reading | 295 |
| Writing | 350 |
| Speaking | 370 |

Tester action: screenshot the score card above and upload the screenshot.

For **Pack 3 Test 19** (two-result trend), change the scores to L 330 / R 315 / W 350 / S 370
and screenshot a second variant (`test-asset-a-score-report-v2.html`).

---

## TEST ASSET B — Writing Case Notes

- OET-style synthetic task — Medicine
- Patient: Mr Daniel Price, 58 years old.
- General practice appointment: 7 September 2026.
- Type 2 diabetes diagnosed 8 years ago.
- HbA1c remains high despite metformin and lifestyle advice.
- Home glucose readings frequently 11–15 mmol/L.
- Reports increasing thirst and nocturia over the last 6 weeks.
- No chest pain or acute shortness of breath.
- Blood pressure 132/78 mmHg.
- Enjoys gardening and watches football every weekend.
- Weight increased by 4 kg in 6 months.
- Medication: metformin 1 g twice daily; adherence reported as good.
- Previous dietitian review 10 months ago.
- Plan: refer to endocrinologist for optimisation of diabetes management and
  consideration of additional therapy.

### Writing Task

Using the information in the case notes, write a referral letter to Dr Emily Ross,
Consultant Endocrinologist, Riverside Specialist Centre, requesting further assessment
and optimisation of Mr Price's diabetes management.

### Draft A (weak)

Dear Dr Ross, I am writing about Mr Daniel Price who is 58 years old. He has diabetes,
likes gardening and watches football. He has gained 4 kg and was seen by a dietitian ten
months ago. His blood pressure is normal. He has high sugar and I would like you to see
him. He also has thirst and nocturia. Please manage him.

### Draft B (strong)

Dear Dr Ross, I am writing to refer Mr Daniel Price, a 58-year-old man with persistently
uncontrolled type 2 diabetes, for specialist review and optimisation of his treatment.
Despite metformin 1 g twice daily and reported good adherence, his HbA1c remains elevated,
with home glucose readings commonly 11–15 mmol/L. Over the past six weeks, he has also
developed increasing thirst and nocturia. I would appreciate your assessment and
consideration of additional glucose-lowering therapy.

`test-asset-b-case-notes.pdf` holds the same notes for the whole-PDF scenarios.

---

## TEST ASSET C — Reading Question

**Text**

A hospital introduced a medication-reconciliation checklist after audits showed that
discrepancies were most common when patients moved between wards. The new process requires
the receiving clinician to compare the current medication chart with the latest verified
list, clarify any unexplained differences, and document intentional changes. The policy is
intended to reduce preventable medication errors during transitions of care rather than to
replace clinical judgement.

**Question** — What is the main purpose of the new process?

- A. To ensure all patients receive the same medicines after transfer.
- B. To identify and resolve unintended medication discrepancies during transfers.
- C. To reduce the amount of documentation required from clinicians.
- D. To allow medication changes without further clinical review.

**Correct answer: B.**

Tester action: clean screenshot for the normal image test; deliberately blur/darken a copy
for the low-quality image test.

---

## TEST ASSET D — Voice, Handwriting and Privacy Scripts

### Voice Script

Record this as a voice note (phone recorder, normal room):

> "The patient was discharged yesterday after laparoscopic surgery. Although she feels
> better, she remains worried about the possibility of infection and is unsure when she
> should return to work."

### Handwriting Script

Handwrite this on paper and upload a photo:

> "Pt dont take his medication regulary because it make him dizzy."

### Sensitive Data Trap

Handwrite or screenshot this and upload it for the privacy scenario:

> Patient name: Sarah Mahmoud | Date of birth: 03/11/1982 | Phone: 050 555 0137 |
> MRN: 784562 | Note: Persistent cough; follow-up arranged next week.

All values in the sensitive-data trap are fictional UAT data but intentionally resemble
patient-identifying information.
