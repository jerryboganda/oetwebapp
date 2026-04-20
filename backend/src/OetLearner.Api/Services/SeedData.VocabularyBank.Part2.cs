using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public static partial class SeedData
{
    /// <summary>
    /// Part 2 of the OET vocabulary bank.  Covers procedures, diagnostics,
    /// clinical communication, and additional anatomy/pharmacology.
    ///
    /// Uses the ID counter captured in Part 1 via the delegate parameter so
    /// term IDs remain contiguous (vt-026 onward).
    /// </summary>
    private static IEnumerable<VocabularyTerm> BuildOetVocabularyBank_Part2(Func<string> nextId)
    {
        VocabularyTerm V(
            string term, string definition, string example, string category,
            string difficulty = "medium",
            string? professionId = null,
            string? ipa = null,
            string? contextNotes = null,
            IEnumerable<string>? synonyms = null,
            IEnumerable<string>? collocations = null,
            IEnumerable<string>? related = null)
            => new()
            {
                Id = nextId(),
                Term = term,
                Definition = definition,
                ExampleSentence = example,
                ContextNotes = contextNotes,
                ExamTypeCode = "oet",
                ProfessionId = professionId,
                Category = category,
                Difficulty = difficulty,
                IpaPronunciation = ipa,
                SynonymsJson = System.Text.Json.JsonSerializer.Serialize((synonyms ?? Array.Empty<string>()).ToArray()),
                CollocationsJson = System.Text.Json.JsonSerializer.Serialize((collocations ?? Array.Empty<string>()).ToArray()),
                RelatedTermsJson = System.Text.Json.JsonSerializer.Serialize((related ?? Array.Empty<string>()).ToArray()),
                SourceProvenance = VocabBankProvenance,
                Status = "active",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

        // ── PROCEDURES (70 terms) ──────────────────────────────────────────
        yield return V("appendicectomy", "Surgical removal of the appendix.", "An urgent laparoscopic appendicectomy was performed for acute appendicitis.", "procedures", "medium", synonyms: new[] { "appendectomy" });
        yield return V("cholecystectomy", "Surgical removal of the gallbladder.", "Laparoscopic cholecystectomy was arranged for symptomatic gallstones.", "procedures", "medium", ipa: "/ˌkɒlɪsɪsˈtɛktəmi/");
        yield return V("hysterectomy", "Surgical removal of the uterus.", "A total abdominal hysterectomy was performed for heavy menstrual bleeding.", "procedures", "medium");
        yield return V("mastectomy", "Surgical removal of a breast.", "A right mastectomy was performed with sentinel node biopsy.", "procedures", "medium");
        yield return V("lumpectomy", "Surgical removal of a lump, especially in breast tissue.", "A wide local excision lumpectomy was followed by adjuvant radiotherapy.", "procedures", "medium");
        yield return V("tonsillectomy", "Surgical removal of the tonsils.", "Bilateral tonsillectomy was indicated for recurrent tonsillitis.", "procedures", "medium");
        yield return V("adenoidectomy", "Surgical removal of the adenoids.", "Adenoidectomy was combined with grommet insertion.", "procedures", "hard");
        yield return V("thyroidectomy", "Surgical removal of part or all of the thyroid gland.", "A near-total thyroidectomy was performed for a suspicious nodule.", "procedures", "medium");
        yield return V("prostatectomy", "Surgical removal of part or all of the prostate gland.", "Robotic-assisted radical prostatectomy was performed for localised disease.", "procedures", "medium");
        yield return V("nephrectomy", "Surgical removal of a kidney.", "A right partial nephrectomy preserved renal function where possible.", "procedures", "medium");
        yield return V("gastrectomy", "Surgical removal of part or all of the stomach.", "A subtotal gastrectomy was performed for distal gastric cancer.", "procedures", "medium");
        yield return V("colectomy", "Surgical removal of part or all of the colon.", "A right hemicolectomy was performed for caecal adenocarcinoma.", "procedures", "medium");
        yield return V("hemicolectomy", "Surgical removal of half of the colon.", "A laparoscopic right hemicolectomy was completed without complication.", "procedures", "hard");
        yield return V("craniotomy", "Surgical opening of the skull.", "A craniotomy was required to evacuate the acute subdural haematoma.", "procedures", "hard");
        yield return V("laparotomy", "A surgical incision into the abdominal cavity.", "An emergency laparotomy was performed for generalised peritonitis.", "procedures", "medium");
        yield return V("laparoscopy", "A minimally invasive surgical technique using a camera through small incisions.", "A diagnostic laparoscopy identified endometriotic deposits.", "procedures", "medium");
        yield return V("endoscopy", "Visualisation of internal organs using a flexible camera.", "Upper GI endoscopy revealed a duodenal ulcer.", "procedures", "easy");
        yield return V("colonoscopy", "Endoscopic examination of the colon.", "Screening colonoscopy identified two tubular adenomas, which were removed.", "procedures", "easy");
        yield return V("gastroscopy", "Endoscopic examination of the stomach.", "Gastroscopy showed mild gastritis without bleeding.", "procedures", "medium");
        yield return V("bronchoscopy", "Endoscopic examination of the bronchi.", "Flexible bronchoscopy with biopsy was planned for the suspicious lesion.", "procedures", "medium");
        yield return V("cystoscopy", "Endoscopic examination of the bladder.", "Cystoscopy excluded bladder malignancy.", "procedures", "medium");
        yield return V("arthroscopy", "Endoscopic examination of a joint.", "Knee arthroscopy allowed for meniscal repair.", "procedures", "medium");
        yield return V("biopsy", "Removal of a small sample of tissue for diagnostic examination.", "An ultrasound-guided liver biopsy confirmed the diagnosis.", "procedures", "easy");
        yield return V("aspiration", "The removal of fluid or tissue by suction.", "Therapeutic aspiration of the pleural effusion provided symptomatic relief.", "procedures", "medium", contextNotes: "'Aspiration' also denotes inhalation of foreign material — context disambiguates.");
        yield return V("paracentesis", "Needle drainage of ascitic fluid from the peritoneal cavity.", "Therapeutic paracentesis drained 4 L of ascitic fluid.", "procedures", "hard");
        yield return V("thoracocentesis", "Needle drainage of pleural fluid from the pleural cavity.", "Ultrasound-guided thoracocentesis confirmed an exudative effusion.", "procedures", "hard");
        yield return V("lumbar puncture", "A procedure to collect cerebrospinal fluid from the lower back.", "A lumbar puncture excluded bacterial meningitis.", "procedures", "medium");
        yield return V("central venous catheterisation", "Insertion of a catheter into a large central vein.", "Central venous catheterisation was performed under ultrasound guidance.", "procedures", "hard");
        yield return V("cannulation", "Insertion of a cannula into a vein for access.", "Peripheral cannulation was established in the left antecubital fossa.", "procedures", "easy");
        yield return V("intubation", "Insertion of a tube into the trachea for airway control.", "Orotracheal intubation was performed for airway protection.", "procedures", "medium");
        yield return V("extubation", "Removal of the endotracheal tube after recovery.", "The patient was extubated uneventfully the following morning.", "procedures", "medium");
        yield return V("tracheostomy", "A surgical opening in the trachea for an airway.", "A percutaneous tracheostomy was performed after prolonged ventilation.", "procedures", "medium");
        yield return V("catheterisation", "Insertion of a catheter, commonly urinary.", "Urinary catheterisation was required for acute retention.", "procedures", "easy");
        yield return V("defibrillation", "Delivery of an electric shock to restore normal heart rhythm.", "Biphasic defibrillation at 200 J restored sinus rhythm.", "procedures", "medium");
        yield return V("cardioversion", "A procedure to restore normal heart rhythm.", "Elective cardioversion was scheduled after three weeks of anticoagulation.", "procedures", "medium");
        yield return V("cardiac catheterisation", "A procedure to examine coronary arteries via a catheter.", "Diagnostic cardiac catheterisation identified a 70% LAD stenosis.", "procedures", "hard");
        yield return V("percutaneous coronary intervention", "A non-surgical procedure to open blocked coronary arteries.", "Primary percutaneous coronary intervention was performed within 60 minutes.", "procedures", "hard", synonyms: new[] { "PCI", "angioplasty" });
        yield return V("coronary artery bypass grafting", "Open-heart surgery to bypass blocked coronary arteries.", "Triple-vessel disease was managed with coronary artery bypass grafting.", "procedures", "hard", synonyms: new[] { "CABG" });
        yield return V("dialysis", "A procedure to remove waste and excess fluid when the kidneys fail.", "Haemodialysis three times weekly supported the patient with end-stage renal disease.", "procedures", "medium");
        yield return V("haemodialysis", "A form of dialysis using an external machine to filter blood.", "The haemodialysis session was uneventful with good fluid removal.", "procedures", "hard");
        yield return V("peritoneal dialysis", "A form of dialysis using the peritoneum as a semipermeable membrane.", "Continuous ambulatory peritoneal dialysis was preferred given his travel.", "procedures", "hard");
        yield return V("chemotherapy", "Medical treatment of cancer using drugs.", "The patient tolerated the first cycle of chemotherapy without significant toxicity.", "procedures", "easy");
        yield return V("radiotherapy", "The treatment of disease using ionising radiation.", "Adjuvant radiotherapy was planned over five weeks.", "procedures", "easy");
        yield return V("immunotherapy", "The use of substances that stimulate or suppress the immune system.", "Checkpoint-inhibitor immunotherapy was offered for metastatic melanoma.", "procedures", "medium");
        yield return V("transfusion", "The administration of blood or blood products.", "A two-unit packed red cell transfusion was given for symptomatic anaemia.", "procedures", "easy");
        yield return V("vaccination", "Administration of a vaccine to stimulate immune protection.", "The childhood vaccination schedule was up to date.", "procedures", "easy");
        yield return V("immunisation", "The process of becoming protected against disease through vaccination.", "Influenza immunisation was offered to all at-risk adults.", "procedures", "easy");
        yield return V("physiotherapy", "Treatment of physical problems using movement, exercise and manipulation.", "Intensive physiotherapy improved post-stroke mobility.", "procedures", "easy");
        yield return V("occupational therapy", "Therapy to enable people to participate in daily activities.", "Occupational therapy assessed the home for safe return to independence.", "procedures", "easy");
        yield return V("speech therapy", "Treatment of communication and swallowing disorders.", "Speech therapy focused on swallowing rehabilitation after the stroke.", "procedures", "easy");
        yield return V("dressing change", "The process of changing a wound dressing under aseptic technique.", "A dressing change every 48 hours was arranged.", "procedures", "easy");
        yield return V("wound irrigation", "Flushing of a wound with fluid to remove debris or contaminants.", "Wound irrigation with sterile saline preceded closure.", "procedures", "medium");
        yield return V("suturing", "Closing a wound with stitches.", "Suturing was performed in layers under local anaesthesia.", "procedures", "easy");
        yield return V("debridement", "Removal of dead, damaged, or infected tissue from a wound.", "Sharp debridement was performed at the bedside.", "procedures", "medium", ipa: "/dɪˈbriːdmɒ̃/");
        yield return V("amputation", "Surgical removal of a limb or extremity.", "Below-knee amputation was undertaken for non-salvageable ischaemia.", "procedures", "medium");
        yield return V("fracture reduction", "Realignment of a broken bone into normal position.", "Closed fracture reduction was performed under sedation.", "procedures", "medium");
        yield return V("fixation", "The stabilisation of a fracture, internally or externally.", "Open reduction and internal fixation was required for the complex tibial plateau fracture.", "procedures", "medium");
        yield return V("joint replacement", "Surgical replacement of a diseased joint with a prosthesis.", "Total hip joint replacement relieved his debilitating pain.", "procedures", "easy");
        yield return V("caesarean section", "Surgical delivery of a baby through the abdomen and uterus.", "An emergency caesarean section was performed for foetal distress.", "procedures", "medium");
        yield return V("induction of labour", "Artificial initiation of labour before it starts spontaneously.", "Induction of labour was planned at 41 weeks of gestation.", "procedures", "medium");
        yield return V("episiotomy", "A surgical incision of the perineum to enlarge the vaginal opening during childbirth.", "A mediolateral episiotomy facilitated an assisted delivery.", "procedures", "hard");
        yield return V("endotracheal intubation", "Placement of a tube into the trachea via the mouth or nose.", "Rapid-sequence endotracheal intubation was performed for airway protection.", "procedures", "hard");
        yield return V("nasogastric tube insertion", "Placement of a tube through the nose into the stomach.", "Nasogastric tube insertion was undertaken for gastric decompression.", "procedures", "medium");
        yield return V("enteral feeding", "Delivery of nutrition through the gastrointestinal tract via a tube.", "Enteral feeding via a PEG was commenced.", "procedures", "medium");
        yield return V("parenteral nutrition", "Intravenous administration of nutrients when gut function is unavailable.", "Total parenteral nutrition was started through a central line.", "procedures", "medium");
        yield return V("blood culture", "A laboratory test to detect bacteria or fungi in the blood.", "Two sets of blood cultures were taken before antibiotic administration.", "procedures", "easy");
        yield return V("urinalysis", "Analysis of a urine sample.", "Urinalysis revealed leucocytes and nitrites.", "procedures", "easy");
        yield return V("ECG", "Electrocardiogram; a recording of the heart's electrical activity.", "A 12-lead ECG showed new T-wave inversion in the inferior leads.", "procedures", "easy", synonyms: new[] { "electrocardiogram" });
        yield return V("echocardiogram", "Ultrasound imaging of the heart.", "A transthoracic echocardiogram demonstrated preserved systolic function.", "procedures", "medium");
        yield return V("electroencephalogram", "A recording of the electrical activity of the brain.", "An electroencephalogram was obtained during a provocation study.", "procedures", "hard", synonyms: new[] { "EEG" });

        // ── DIAGNOSTICS / INVESTIGATIONS (50 terms) ────────────────────────
        yield return V("X-ray", "An imaging technique using ionising radiation to produce images.", "A chest X-ray was obtained on admission.", "diagnostics", "easy");
        yield return V("ultrasound", "An imaging technique using sound waves.", "An abdominal ultrasound ruled out cholelithiasis.", "diagnostics", "easy");
        yield return V("CT scan", "Computed tomography; cross-sectional imaging using X-rays.", "A non-contrast CT scan of the head excluded haemorrhage.", "diagnostics", "easy");
        yield return V("MRI scan", "Magnetic resonance imaging; detailed imaging using magnetic fields.", "An urgent MRI scan of the spine was arranged to exclude cord compression.", "diagnostics", "easy");
        yield return V("PET scan", "Positron emission tomography; functional imaging using radiotracers.", "A PET scan staged the disease as locoregional.", "diagnostics", "medium");
        yield return V("full blood count", "A blood test measuring red cells, white cells, and platelets.", "A full blood count revealed normocytic anaemia.", "diagnostics", "easy", synonyms: new[] { "FBC" });
        yield return V("urea and electrolytes", "A blood test assessing renal function and electrolyte balance.", "Urea and electrolytes were within the normal range.", "diagnostics", "medium", synonyms: new[] { "U&E" });
        yield return V("liver function tests", "Blood tests assessing hepatic function.", "Liver function tests revealed transaminitis.", "diagnostics", "medium", synonyms: new[] { "LFTs" });
        yield return V("coagulation screen", "A blood test assessing clotting function.", "A coagulation screen demonstrated a prolonged INR.", "diagnostics", "medium");
        yield return V("C-reactive protein", "An acute-phase protein indicating inflammation.", "A raised C-reactive protein supported the diagnosis of infection.", "diagnostics", "medium", synonyms: new[] { "CRP" });
        yield return V("erythrocyte sedimentation rate", "A non-specific marker of inflammation.", "An elevated erythrocyte sedimentation rate prompted further evaluation.", "diagnostics", "hard", synonyms: new[] { "ESR" });
        yield return V("glycated haemoglobin", "A blood test reflecting average blood glucose over three months.", "Glycated haemoglobin was elevated at 64 mmol/mol.", "diagnostics", "medium", synonyms: new[] { "HbA1c" });
        yield return V("thyroid function tests", "Blood tests assessing thyroid activity.", "Thyroid function tests confirmed primary hypothyroidism.", "diagnostics", "medium", synonyms: new[] { "TFTs" });
        yield return V("arterial blood gas", "A sample from an artery to assess oxygen, carbon dioxide, and pH.", "An arterial blood gas demonstrated type 2 respiratory failure.", "diagnostics", "medium", synonyms: new[] { "ABG" });
        yield return V("pulse oximetry", "Non-invasive measurement of peripheral oxygen saturation.", "Pulse oximetry on admission was 89% on room air.", "diagnostics", "easy");
        yield return V("peak expiratory flow", "The maximum speed of expiration.", "Peak expiratory flow had dropped to 45% of predicted.", "diagnostics", "medium", synonyms: new[] { "PEFR" });
        yield return V("spirometry", "A test of lung function measuring airflow.", "Spirometry demonstrated an obstructive pattern.", "diagnostics", "medium");
        yield return V("holter monitor", "A portable device recording continuous ECG over 24 hours or longer.", "A 48-hour Holter monitor captured paroxysmal atrial fibrillation.", "diagnostics", "medium");
        yield return V("exercise tolerance test", "An ECG recorded during physical exertion.", "The exercise tolerance test was terminated for exertional chest pain.", "diagnostics", "medium");
        yield return V("urine culture", "A microbiological test to identify urinary pathogens.", "A urine culture grew E. coli sensitive to nitrofurantoin.", "diagnostics", "medium");
        yield return V("stool culture", "A microbiological test of stool to identify pathogens.", "A stool culture excluded Clostridioides difficile.", "diagnostics", "medium");
        yield return V("sputum culture", "A microbiological test of sputum to identify respiratory pathogens.", "A sputum culture grew Streptococcus pneumoniae.", "diagnostics", "medium");
        yield return V("Gram stain", "A laboratory technique distinguishing bacteria by cell wall properties.", "The Gram stain showed Gram-positive cocci in clusters.", "diagnostics", "medium");
        yield return V("sensitivity testing", "Laboratory testing to identify effective antimicrobial agents.", "Sensitivity testing guided the switch to oral therapy.", "diagnostics", "medium");
        yield return V("mantoux test", "A tuberculin skin test for latent tuberculosis.", "A Mantoux test was performed during the contact-tracing investigation.", "diagnostics", "hard");
        yield return V("interferon-gamma release assay", "A blood test for tuberculosis infection.", "An interferon-gamma release assay confirmed latent tuberculosis infection.", "diagnostics", "hard");
        yield return V("mammography", "Breast X-ray imaging used for screening and diagnosis.", "Routine screening mammography detected an area of concern.", "diagnostics", "medium");
        yield return V("dual-energy X-ray absorptiometry", "A scan measuring bone mineral density.", "A dual-energy X-ray absorptiometry scan confirmed osteoporosis.", "diagnostics", "hard", synonyms: new[] { "DEXA" });
        yield return V("bone marrow biopsy", "A test of bone marrow tissue to diagnose haematological disease.", "Bone marrow biopsy confirmed acute myeloid leukaemia.", "diagnostics", "hard");
        yield return V("fine needle aspiration", "Aspiration of cells from a mass using a fine needle.", "Fine needle aspiration of the thyroid nodule was benign.", "diagnostics", "medium");
        yield return V("core biopsy", "A biopsy using a hollow needle to extract a tissue core.", "A core biopsy of the breast lesion guided further management.", "diagnostics", "medium");
        yield return V("polymerase chain reaction", "A laboratory technique amplifying nucleic acid sequences.", "A polymerase chain reaction assay detected SARS-CoV-2.", "diagnostics", "hard", synonyms: new[] { "PCR" });
        yield return V("serology", "Laboratory testing of antibodies and antigens in serum.", "Serology was consistent with past infection.", "diagnostics", "hard");
        yield return V("autoantibody screen", "Blood tests for antibodies against self-tissues.", "An autoantibody screen supported the diagnosis of lupus.", "diagnostics", "hard");
        yield return V("troponin", "A cardiac enzyme released during myocardial injury.", "A rising troponin confirmed non-ST-elevation myocardial infarction.", "diagnostics", "medium");
        yield return V("B-type natriuretic peptide", "A biomarker released by the stressed heart.", "An elevated B-type natriuretic peptide supported the diagnosis of heart failure.", "diagnostics", "hard", synonyms: new[] { "BNP", "NT-proBNP" });
        yield return V("D-dimer", "A breakdown product of fibrin suggesting active clot lysis.", "A raised D-dimer prompted CTPA to exclude pulmonary embolism.", "diagnostics", "medium");
        yield return V("ferritin", "A protein storing iron; reflects iron stores.", "A low ferritin confirmed iron-deficiency anaemia.", "diagnostics", "medium");
        yield return V("vitamin B12 level", "A measurement of circulating vitamin B12.", "A low vitamin B12 level was treated with intramuscular replacement.", "diagnostics", "medium");
        yield return V("folate level", "A measurement of circulating folic acid.", "A low folate level was supplemented orally.", "diagnostics", "medium");
        yield return V("lipid profile", "A panel of blood tests measuring cholesterol and triglycerides.", "A lipid profile revealed elevated LDL cholesterol.", "diagnostics", "medium");
        yield return V("prostate-specific antigen", "A blood marker used in prostate cancer screening.", "A raised prostate-specific antigen prompted further urological assessment.", "diagnostics", "medium", synonyms: new[] { "PSA" });
        yield return V("HIV test", "A blood test for human immunodeficiency virus.", "A negative HIV test was documented.", "diagnostics", "medium");
        yield return V("hepatitis B surface antigen", "A blood test for active hepatitis B infection.", "The hepatitis B surface antigen was negative.", "diagnostics", "hard", synonyms: new[] { "HBsAg" });
        yield return V("pap smear", "A screening test for cervical abnormalities.", "A pap smear returned as normal.", "diagnostics", "medium", synonyms: new[] { "cervical smear" });
        yield return V("colposcopy", "Examination of the cervix with a magnifying instrument.", "Colposcopy was arranged following an abnormal smear.", "diagnostics", "medium");
        yield return V("urodynamics", "Tests assessing bladder and urethral function.", "Urodynamic studies confirmed detrusor overactivity.", "diagnostics", "hard");
        yield return V("nerve conduction study", "A test of electrical conduction through peripheral nerves.", "Nerve conduction studies confirmed carpal tunnel syndrome.", "diagnostics", "hard");
        yield return V("EMG", "Electromyography; measures electrical activity of skeletal muscle.", "An EMG was requested to assess for myopathy.", "diagnostics", "hard", synonyms: new[] { "electromyography" });

        // ── CLINICAL COMMUNICATION (80 terms) ──────────────────────────────
        yield return V("contraindicated", "Not recommended due to potential harm.", "NSAIDs are contraindicated in patients with active peptic ulcer disease.", "clinical_communication", "medium", synonyms: new[] { "unsafe", "inadvisable" });
        yield return V("indication", "A valid reason to use a particular treatment.", "The indication for anticoagulation is secondary prevention.", "clinical_communication", "medium");
        yield return V("dosage", "The prescribed amount of a medicine.", "The dosage was reduced in renal impairment.", "clinical_communication", "easy");
        yield return V("dose adjustment", "Modification of a medicine's dose based on clinical factors.", "A dose adjustment was required due to hepatic impairment.", "clinical_communication", "medium");
        yield return V("titrate", "To gradually adjust the dose of a medicine.", "The opioid dose was titrated to pain response.", "clinical_communication", "medium", related: new[] { "up-titrate", "down-titrate" });
        yield return V("adherence", "The extent to which a patient follows an agreed treatment plan.", "Poor adherence to antihypertensives was addressed through simplification.", "clinical_communication", "medium", related: new[] { "compliance" });
        yield return V("compliance", "The extent to which a patient follows medical advice.", "Compliance with follow-up appointments has been excellent.", "clinical_communication", "medium");
        yield return V("referral", "Directing a patient to another clinician for assessment or treatment.", "A same-day referral to gastroenterology was arranged.", "clinical_communication", "easy");
        yield return V("handover", "The transfer of clinical responsibility from one team to another.", "A structured ISBAR handover was given to the night team.", "clinical_communication", "medium");
        yield return V("discharge summary", "A written summary provided when a patient leaves hospital.", "The discharge summary detailed the hospital course and follow-up plan.", "clinical_communication", "easy");
        yield return V("follow-up", "A planned subsequent consultation or assessment.", "A six-week clinic follow-up was scheduled.", "clinical_communication", "easy");
        yield return V("consent", "Permission granted for medical treatment after understanding the implications.", "Written informed consent was obtained before the procedure.", "clinical_communication", "easy", related: new[] { "capacity", "autonomy" });
        yield return V("informed consent", "Consent given after full disclosure of risks, benefits, and alternatives.", "Informed consent was documented in the clinical notes.", "clinical_communication", "medium");
        yield return V("capacity", "The ability of a patient to make informed decisions.", "The patient was assessed as having capacity to refuse treatment.", "clinical_communication", "medium");
        yield return V("advance care plan", "A document recording wishes regarding future care.", "An advance care plan was reviewed with the family.", "clinical_communication", "medium");
        yield return V("do not resuscitate", "A documented instruction not to perform CPR if the heart stops.", "A do not resuscitate order was discussed and agreed with the family.", "clinical_communication", "medium", synonyms: new[] { "DNACPR", "DNR" });
        yield return V("palliative care", "Care focused on symptom relief rather than cure.", "Palliative care input improved symptom control.", "clinical_communication", "medium");
        yield return V("hospice care", "End-of-life care focused on comfort and dignity.", "The family elected hospice care for the final weeks.", "clinical_communication", "medium");
        yield return V("curative intent", "Treatment aimed at eradicating disease.", "The surgical approach was with curative intent.", "clinical_communication", "hard");
        yield return V("symptomatic relief", "Treatment aimed at reducing symptoms rather than curing the disease.", "Management focused on symptomatic relief of breathlessness.", "clinical_communication", "medium");
        yield return V("prognosis", "A forecast of the likely course of a disease.", "The prognosis was guarded given the advanced stage.", "clinical_communication", "easy", collocations: new[] { "poor prognosis", "favourable prognosis" });
        yield return V("differential diagnosis", "A list of possible conditions explaining the presentation.", "The differential diagnosis included pneumonia and pulmonary embolism.", "clinical_communication", "medium");
        yield return V("provisional diagnosis", "A working diagnosis pending further investigation.", "The provisional diagnosis was viral gastroenteritis.", "clinical_communication", "medium");
        yield return V("exacerbation", "A worsening of symptoms in an existing condition.", "The exacerbation of COPD was triggered by a respiratory infection.", "clinical_communication", "medium", collocations: new[] { "acute exacerbation" });
        yield return V("remission", "A period during which symptoms of disease are reduced or absent.", "Complete remission was documented after six cycles of chemotherapy.", "clinical_communication", "medium");
        yield return V("relapse", "A return of disease after a period of improvement.", "A relapse prompted re-initiation of induction therapy.", "clinical_communication", "medium");
        yield return V("complication", "An unfavourable evolution of a disease or treatment.", "The main post-operative complication was superficial wound infection.", "clinical_communication", "easy");
        yield return V("adverse event", "An unfavourable outcome occurring during treatment.", "The adverse event was reported to the pharmacovigilance team.", "clinical_communication", "medium");
        yield return V("side effect", "An unintended effect of a medication.", "Dry mouth was a troublesome side effect of the antidepressant.", "clinical_communication", "easy");
        yield return V("drug interaction", "An effect produced when two drugs are taken together.", "A clinically significant drug interaction prompted substitution.", "clinical_communication", "medium");
        yield return V("allergic reaction", "An immune response to a substance the body perceives as harmful.", "An allergic reaction to penicillin was documented in the notes.", "clinical_communication", "easy");
        yield return V("anaphylaxis", "A severe, potentially life-threatening allergic reaction.", "Anaphylaxis following nut ingestion required intramuscular adrenaline.", "clinical_communication", "medium");
        yield return V("prophylaxis", "Treatment given to prevent disease.", "Antibiotic prophylaxis was given before the dental procedure.", "clinical_communication", "hard");
        yield return V("screening", "Testing apparently well individuals to detect disease early.", "Breast cancer screening identified an early-stage malignancy.", "clinical_communication", "easy");
        yield return V("triage", "Prioritising patients based on urgency of need.", "She was triaged as category 2 in the emergency department.", "clinical_communication", "easy");
        yield return V("resuscitation", "The process of restoring life after cardiac or respiratory arrest.", "Advanced life support resuscitation continued for 25 minutes.", "clinical_communication", "medium");
        yield return V("observation", "Close monitoring without active treatment.", "The patient was admitted for observation overnight.", "clinical_communication", "easy");
        yield return V("monitoring", "The repeated assessment of a patient's condition or treatment.", "Continuous ECG monitoring was instituted.", "clinical_communication", "easy");
        yield return V("assessment", "A systematic evaluation of a patient's condition.", "A thorough cardiovascular assessment was performed.", "clinical_communication", "easy");
        yield return V("evaluation", "A careful examination and judgement of a clinical situation.", "Specialist evaluation was requested.", "clinical_communication", "easy");
        yield return V("history of presenting complaint", "The narrative of the current symptoms.", "The history of presenting complaint was recorded in chronological order.", "clinical_communication", "medium");
        yield return V("past medical history", "A record of previous illnesses and treatments.", "Her past medical history included hypertension and asthma.", "clinical_communication", "easy");
        yield return V("family history", "A record of health conditions affecting immediate relatives.", "A strong family history of bowel cancer was noted.", "clinical_communication", "easy");
        yield return V("social history", "Information about lifestyle, occupation, and social circumstances.", "The social history revealed occasional alcohol use.", "clinical_communication", "easy");
        yield return V("drug history", "A complete list of current and recent medications.", "The drug history included three antihypertensives.", "clinical_communication", "medium");
        yield return V("allergy history", "A record of previous allergic reactions and their severity.", "An allergy history of amoxicillin-induced rash was documented.", "clinical_communication", "medium");
        yield return V("systems review", "A structured enquiry about each body system.", "A full systems review was unremarkable.", "clinical_communication", "medium", synonyms: new[] { "review of systems" });
        yield return V("chief complaint", "The main reason a patient seeks medical attention.", "The chief complaint was two weeks of progressive dyspnoea.", "clinical_communication", "easy");
        yield return V("examination findings", "The results of a physical examination.", "Examination findings included bibasal crepitations and peripheral oedema.", "clinical_communication", "medium");
        yield return V("working diagnosis", "The most likely diagnosis guiding initial management.", "The working diagnosis was community-acquired pneumonia.", "clinical_communication", "medium");
        yield return V("management plan", "An outline of proposed investigations and treatments.", "The management plan included intravenous antibiotics and oxygen therapy.", "clinical_communication", "easy");
        yield return V("care plan", "A structured plan describing the support a patient will receive.", "The care plan was updated with input from the multidisciplinary team.", "clinical_communication", "easy");
        yield return V("multidisciplinary team", "A group of healthcare professionals from different specialties.", "The case was discussed at the multidisciplinary team meeting.", "clinical_communication", "medium", synonyms: new[] { "MDT" });
        yield return V("red flag", "A symptom or sign indicating serious underlying pathology.", "Red-flag features of back pain prompted urgent imaging.", "clinical_communication", "medium");
        yield return V("safety netting", "Advice on what to do if symptoms change or worsen.", "Appropriate safety netting advice was given before discharge.", "clinical_communication", "medium");
        yield return V("reassure", "To offer comfort and confidence to a patient.", "She was reassured that the findings were benign.", "clinical_communication", "easy");
        yield return V("counsel", "To provide professional guidance to a patient.", "He was counselled regarding the side effects of the medication.", "clinical_communication", "medium");
        yield return V("educate", "To provide information enabling a patient to understand their condition.", "The patient was educated about insulin self-administration.", "clinical_communication", "easy");
        yield return V("clarify", "To make something clear or easier to understand.", "I clarified the dosing schedule before discharge.", "clinical_communication", "easy");
        yield return V("empathise", "To show understanding of a patient's feelings and experience.", "She empathised with the patient's concerns about the diagnosis.", "clinical_communication", "medium");
        yield return V("acknowledge", "To recognise a patient's feelings or experience.", "He acknowledged her anxiety about the procedure.", "clinical_communication", "easy");
        yield return V("explain", "To make something understandable through description.", "She explained the risks and benefits of surgery.", "clinical_communication", "easy");
        yield return V("demonstrate", "To show how something should be done.", "The nurse demonstrated the inhaler technique.", "clinical_communication", "easy");
        yield return V("check understanding", "To confirm that a patient has understood the information given.", "I used the teach-back method to check understanding.", "clinical_communication", "medium");
        yield return V("rapport", "A harmonious, understanding relationship with a patient.", "Good rapport was established during the initial consultation.", "clinical_communication", "medium");
        yield return V("open question", "A question that invites a detailed response.", "An open question was used to explore her concerns.", "clinical_communication", "medium");
        yield return V("closed question", "A question answered with yes or no.", "A closed question confirmed the absence of chest pain.", "clinical_communication", "medium");
        yield return V("summarise", "To give a brief statement of the main points.", "I summarised the management plan at the end of the consultation.", "clinical_communication", "easy");
        yield return V("signpost", "To indicate a change in topic or direction in a consultation.", "I signposted the transition from history-taking to examination.", "clinical_communication", "medium");
        yield return V("ideas, concerns, expectations", "A framework for exploring a patient's perspective.", "The ideas, concerns, and expectations framework revealed unspoken worries.", "clinical_communication", "medium", synonyms: new[] { "ICE" });
        yield return V("SBAR", "A communication framework: Situation, Background, Assessment, Recommendation.", "An SBAR communication was used to escalate to the senior doctor.", "clinical_communication", "medium");
        yield return V("ISBAR", "A communication framework adding Introduction to SBAR.", "The ISBAR handover was concise and complete.", "clinical_communication", "medium");
        yield return V("escalate", "To raise a clinical concern to a more senior team member.", "The nurse escalated the deteriorating observations to the medical registrar.", "clinical_communication", "medium");
        yield return V("deteriorate", "To become worse in condition.", "The patient began to deteriorate overnight, prompting review.", "clinical_communication", "medium");
        yield return V("stabilise", "To restore or maintain a patient's condition in a safe state.", "The patient was stabilised before transfer to the ward.", "clinical_communication", "medium");
        yield return V("palliate", "To relieve symptoms without curing the underlying disease.", "The treatment aimed to palliate breathlessness at the end of life.", "clinical_communication", "hard");
        yield return V("debrief", "A structured conversation after a clinical event.", "A team debrief followed the cardiac arrest.", "clinical_communication", "medium");
        yield return V("breaking bad news", "Delivering distressing clinical information to a patient or family.", "Breaking bad news followed the SPIKES protocol.", "clinical_communication", "medium");
        yield return V("shared decision-making", "A collaborative process between patient and clinician.", "Shared decision-making guided the choice between medical and surgical therapy.", "clinical_communication", "medium");
        yield return V("second opinion", "A professional view from another clinician on a diagnosis or plan.", "A second opinion was sought before committing to surgery.", "clinical_communication", "easy");
        yield return V("specialist referral", "Directing a patient to a specialist for assessment.", "A specialist referral to cardiology was made.", "clinical_communication", "easy");
        yield return V("discharge planning", "Preparation for a patient's transition from hospital to home.", "Discharge planning included community nursing input.", "clinical_communication", "medium");
        yield return V("home visit", "A healthcare appointment conducted in the patient's home.", "A home visit was arranged to review medication adherence.", "clinical_communication", "easy");
    }
}
