# OET Writing Model Answer Rules — PERMANENT OWNER DIRECTIVES

**Authority:** product owner (Dr Ahmed Hesham), directives of 13 Sep 2026 and the Rev8 addendum work.
**Applies to:** every person or AI agent who generates, repairs, validates, imports, or assesses OET Writing Model Answers in this repository — regardless of which tool or agent is used.
**Enforcement:** every language rule below is enforced by a named deterministic detector in the deployed validator (`WritingRuleEngine` / `WritingRuleEngine.Rev8`) and pinned by regression tests (`backend/tests/OetLearner.Api.Tests/Writing/WritingRev8RegressionFixtureTests.cs`). If a rule here and the validator ever disagree, BOTH are wrong — fix the validator and update this doc together.

---

## 1. THE $0 HARD RULE (no exceptions)

Never call a paid AI API for Writing work: no generation, no semantic validation, no grading. The coding agent writes, repairs, and semantically reviews every letter **itself**, using:
- deterministic validation: `POST /v1/admin/writing/tasks/{id}/model-answer/validate` with `{"letterText": ..., "includeSemantic": false}` — free, ~2 s;
- import with `{"letterText": ..., "includeSemantic": false}` (default is `true` — always pass `false` explicitly);
- the agent's own documented semantic review against the rule map (handoff: `.tools-state/rev8-writing/AGENT-HANDOFF-WRITING-REV10.md`).
Only the owner can lift this rule, explicitly, in writing.

## 2. THE WORKFLOW PRINCIPLE (owner, verbatim intent)

> CASE NOTES + WRITING TASK + CURRENT RULES → produce the best clinically accurate OET Model Answer → validator checks that answer correctly.
> NOT: validator has a limitation → rewrite good clinical English strangely → make the regex stop detecting it → report PASS.

- **If correct professional wording exposes a weakness in the validator, FIX THE VALIDATOR.** Never restructure correct clinical English so a regex stops seeing it (that is a bypass, not a fix). Historical example: the medication parser was taught dose ranges ("5-10 mg") and combination strengths ("Targin, 20/10 twice daily") instead of splitting the sentence to zero parseable items.
- **Targeted repair only:** existing best draft → identify exact defect → targeted repair → revalidate. Full regeneration only if the content itself is fundamentally wrong.
- **Every new defect type the owner flags becomes a permanent regression test** (positive: the corrected letter lints clean; injection: the deliberately re-introduced defect fires its specific rule) before any further letters are produced. See `WritingRev8RegressionFixtureTests.cs` for the pattern.

## 3. CONTENT SELECTION PRINCIPLE

The 180–200-word body cap means selection is a tested skill: **do NOT copy every case-note fact into the letter.** Priority order when space forces a choice:
1. NEVER drop: allergies (with reaction type), current medicines with dose + frequency, infection findings + antibiotics given, pain/functional story, smoking/alcohol **with their daily frequencies**, the requested services, follow-up appointments.
2. Drop first (lowest clinical value for the recipient): prosthesis/implant brands, operative approach, drains, pets, entitlement cards (DVA etc.), transient mild symptoms, medicines already **ceased**, day-numbering administrative details.
3. Never buy word count by deleting a clinical fact; buy it from connective tissue.
4. Whatever the writer claims was retained MUST be in the final letter — explanation and letter are always consistent.

## 4. LANGUAGE RULES (each enforced — wrong → right, all real corrections)

| Rule | Wrong (from real cases) | Right |
|---|---|---|
| Premium clinical register, even when the case notes are colloquial | "feeling tired, stressed and sluggish" | "with fatigue, stress and lethargy" |
| No vague durations | "has been overweight for a long time" | "has been overweight long term" / "remains overweight" |
| No emotional/judgmental observations | "He appeared anxious with shortness of breath." | "He had shortness of breath." (state objective findings only; keep source severity words like "significant" — never ADD unearned severity) |
| Passive voice for medications | "amitriptyline ceased due to difficulty urinating" | "amitriptyline was discontinued due to difficulty urinating" |
| English frequencies — no Latin | "Lipitor, 20 mg nocte" | "Lipitor, 20 mg at night" |
| Medication list punctuation | "Zyloric 300 mg" / comma-only lists | comma after drug: "Zyloric, 300 mg daily"; 2 drugs: "Drug, dose and Drug, dose"; 3+: "Drug, dose; Drug, dose; and Drug, dose" |
| Legitimate dose formats are first-class | (parser used to miss these) | ranges "5-10 mg four-hourly", combination strengths "Targin, 20/10 twice daily", unitless dose + frequency |
| Value/unit spacing | "37.8°C" | "37.8 °C" (space required; °C, mmHg, bpm, /min are the vital-sign units) |
| Never strip clinical precision for word count | "smoking 20 cigarettes" | "smoking 20 cigarettes daily" |
| Precise referents | "a tophus and possible removal" | "a tophus, and possible tophus removal" |
| No duplicated request | closure repeating the intro's request wording verbatim | reword one side (keep the closure wording that grounds) |
| Sentence length | >30 tokens | split (hyphenated words count as one; no minimum) |
| Anonymous recipient | "Dear Sir/Madam" + "Yours sincerely" | "Dear Sir/Madam" + "Yours **faithfully**" |

Grounding note: premium wording still grounds — "fatigue, stress and lethargy" maps to case notes saying "tired, stressed and sluggish" (proven in production). Grounding is sentence-level traceability, not verbatim copying.

## 5. DETECTOR ↔ RULE MAP (as deployed 13 Sep 2026, commit `6e065a9db`)

| Owner rule | Detector |
|---|---|
| Note-form medication voice | `medication_passive_grammar` |
| Smoking/drinks daily frequency | `lifestyle_frequency_precision` |
| Colloquial/vague/judgmental wording (incl. "for a long time", "appeared anxious") | `register_colloquial` — **no case-notes exemption by design** |
| Latin abbreviations (incl. nocte, mane) | `latin_abbreviations_translated` |
| Compressed units (°C etc.) | `value_unit_spacing` |
| Missing vital-sign units (bpm, /min, mmHg convention) | `numerical_values_have_units` (keywords incl. heart rate, respiratory rate) |
| Dose/list syntax (ranges, ratios, semicolons) | `medication_list_punctuation` + `MedicationItemRe` |
| Minor/adult naming (age attribution) | `WritingPatientAgeExtractor` (relatives' ages never classify the patient) |

## 6. SCOPE GATE

Medicine (5 types: DG, NM, RR, TR, UR — Medicine has no OT; Nursing adds OT) → owner review → Nursing → owner review → remaining professions → Track B → 224 catalogue. Each gate requires explicit owner approval. The three Medicine letters (McDonald/Weir/Taylor) are permanent regression fixtures; treat them as the reference standard.

---

*Handoff trail: `.tools-state/rev8-writing/AGENT-HANDOFF-WRITING-REV8.md` → `REV9` → `REV10` (local machine). This document is the permanent, in-repo home of the rules.*
