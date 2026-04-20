using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Seeded OET vocabulary bank — 500+ terms spread across clinical communication,
/// symptoms, anatomy, procedures, pharmacology, conditions, and diagnostics.
///
/// Source provenance: curated from standard OET candidate reference lists
/// (Oxford Dictionary of Medicine, BNF, BP, NICE guidelines terminology) and
/// edited for OET letter register by the platform editorial team.
///
/// All entries are published as Status="active" on first boot. Future seed
/// operations are idempotent thanks to the `!AnyAsync` guard in SeedData.Apply.
/// </summary>
public static partial class SeedData
{
    private const string VocabBankProvenance =
        "Editorial curation — OET Medical Vocabulary Bank v1 (Dr. Ahmed Hesham platform, 2026-04-20).";

    /// <summary>
    /// Produces the full bank of ~500 OET medical vocabulary terms.
    /// Called from <see cref="SeedVocabularyTerms"/> after the original 25
    /// canonical demo terms have been inserted, so vt-001…vt-025 remain stable.
    /// </summary>
    private static IEnumerable<VocabularyTerm> BuildOetVocabularyBank()
    {
        var idCounter = 26;                                 // vt-001..vt-025 reserved for demo terms
        string NextId() => $"vt-{idCounter++:000}";

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
                Id = NextId(),
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

        // ── SYMPTOMS (80 terms) ──────────────────────────────────────────
        yield return V("dysphagia", "Difficulty in swallowing.", "She was referred for evaluation of progressive dysphagia over six months.", "symptoms", "medium", ipa: "/dɪsˈfeɪdʒə/", synonyms: new[] { "swallowing difficulty" }, collocations: new[] { "progressive dysphagia", "oropharyngeal dysphagia" });
        yield return V("dysphasia", "Impairment of the ability to produce or understand speech.", "He developed expressive dysphasia following the stroke.", "symptoms", "hard", ipa: "/dɪsˈfeɪʒə/", synonyms: new[] { "speech disturbance" }, collocations: new[] { "expressive dysphasia", "receptive dysphasia" });
        yield return V("dysarthria", "Difficult or unclear articulation of speech that is otherwise linguistically normal.", "On examination, she had mild dysarthria and right-sided facial weakness.", "symptoms", "hard", ipa: "/dɪsˈɑːθriə/", collocations: new[] { "mild dysarthria", "flaccid dysarthria" });
        yield return V("dysuria", "Painful or difficult urination.", "She reported dysuria, frequency, and lower abdominal discomfort.", "symptoms", "medium", ipa: "/dɪsˈjʊəriə/", synonyms: new[] { "painful urination" }, collocations: new[] { "dysuria and frequency" });
        yield return V("polyuria", "The passage of abnormally large volumes of urine.", "Polyuria and polydipsia prompted investigation for diabetes mellitus.", "symptoms", "medium", ipa: "/ˌpɒliˈjʊəriə/", collocations: new[] { "polyuria and polydipsia" });
        yield return V("oliguria", "Reduced urine output, typically less than 400 mL in 24 hours in adults.", "Oliguria and rising creatinine suggested acute kidney injury.", "symptoms", "hard", ipa: "/ˌɒlɪˈɡjʊəriə/", related: new[] { "anuria", "acute kidney injury" });
        yield return V("anuria", "The absence of urine production, typically less than 50 mL in 24 hours.", "He developed anuria following the contrast study and required urgent dialysis.", "symptoms", "hard", ipa: "/əˈnjʊəriə/");
        yield return V("haematuria", "The presence of blood in the urine.", "Painless macroscopic haematuria warranted urgent urological referral.", "symptoms", "medium", ipa: "/ˌhiːməˈtjʊəriə/", contextNotes: "Spelled 'hematuria' in US English.", collocations: new[] { "macroscopic haematuria", "microscopic haematuria" });
        yield return V("proteinuria", "An abnormal quantity of protein in the urine.", "Persistent proteinuria on routine screening prompted renal follow-up.", "symptoms", "medium", ipa: "/ˌprəʊtiːˈnjʊəriə/");
        yield return V("nocturia", "The need to wake during the night to urinate.", "He reported nocturia up to three times per night.", "symptoms", "medium", ipa: "/nɒkˈtjʊəriə/", related: new[] { "benign prostatic hyperplasia" });
        yield return V("enuresis", "Involuntary urination, particularly at night in children.", "Primary nocturnal enuresis was addressed with behavioural strategies.", "symptoms", "medium", ipa: "/ˌɛnjʊˈriːsɪs/", collocations: new[] { "nocturnal enuresis" });
        yield return V("incontinence", "Loss of control over urination or defecation.", "She presented with stress urinary incontinence that worsened postpartum.", "symptoms", "easy", ipa: "/ɪnˈkɒntɪnəns/", collocations: new[] { "urinary incontinence", "faecal incontinence" });
        yield return V("constipation", "Infrequent or difficult evacuation of faeces.", "Chronic constipation was managed with dietary advice and osmotic laxatives.", "symptoms", "easy", ipa: "/ˌkɒnstɪˈpeɪʃən/", synonyms: new[] { "difficult defecation" });
        yield return V("diarrhoea", "Abnormally frequent loose or watery stools.", "Acute diarrhoea with fever prompted stool sample testing.", "symptoms", "easy", ipa: "/ˌdaɪəˈrɪə/", contextNotes: "Spelled 'diarrhea' in US English.", collocations: new[] { "bloody diarrhoea", "watery diarrhoea" });
        yield return V("nausea", "A feeling of sickness with an inclination to vomit.", "She reported persistent nausea following chemotherapy.", "symptoms", "easy", ipa: "/ˈnɔːziə/", related: new[] { "emesis", "antiemetic" });
        yield return V("emesis", "The act of vomiting.", "Coffee-ground emesis suggested upper gastrointestinal bleeding.", "symptoms", "medium", ipa: "/ˈɛmɪsɪs/", synonyms: new[] { "vomiting" }, collocations: new[] { "bilious emesis", "coffee-ground emesis" });
        yield return V("haematemesis", "Vomiting of blood.", "Fresh haematemesis prompted urgent endoscopy.", "symptoms", "hard", ipa: "/ˌhiːməˈtɛmɪsɪs/", related: new[] { "melaena" });
        yield return V("melaena", "Black, tarry stools indicating upper gastrointestinal bleeding.", "Melaena and haemoglobin of 78 g/L warranted admission.", "symptoms", "hard", ipa: "/mɪˈliːnə/");
        yield return V("haematochezia", "The passage of fresh blood through the anus.", "Haematochezia in an elderly patient prompted urgent colonoscopy.", "symptoms", "hard", ipa: "/ˌhiːmətəˈkiːziə/");
        yield return V("haemoptysis", "The coughing up of blood from the respiratory tract.", "Intermittent haemoptysis over three weeks warranted a chest CT.", "symptoms", "hard", ipa: "/hɪˈmɒptɪsɪs/", contextNotes: "Red-flag symptom; always investigate.", collocations: new[] { "frank haemoptysis", "massive haemoptysis" });
        yield return V("pallor", "An unhealthy pale appearance of the skin.", "Pallor and tachycardia were noted on examination.", "symptoms", "medium", ipa: "/ˈpælə/", related: new[] { "anaemia" });
        yield return V("cyanosis", "A bluish discolouration of the skin due to low blood oxygen.", "Central cyanosis prompted urgent pulse oximetry.", "symptoms", "medium", ipa: "/ˌsaɪəˈnəʊsɪs/", collocations: new[] { "central cyanosis", "peripheral cyanosis" });
        yield return V("jaundice", "Yellow discolouration of the skin and sclerae due to hyperbilirubinaemia.", "Painless jaundice and weight loss raised concern for pancreatic malignancy.", "symptoms", "easy", ipa: "/ˈdʒɔːndɪs/", synonyms: new[] { "icterus" });
        yield return V("pruritus", "An unpleasant sensation leading to the desire to scratch.", "Generalised pruritus without a visible rash prompted liver function testing.", "symptoms", "medium", ipa: "/pruːˈraɪtəs/", synonyms: new[] { "itching" });
        yield return V("erythema", "Redness of the skin caused by dilated capillaries.", "Localised erythema surrounded the cannula insertion site.", "symptoms", "medium", ipa: "/ˌɛrɪˈθiːmə/", collocations: new[] { "erythema migrans", "erythema nodosum" });
        yield return V("oedema", "Accumulation of fluid in body tissues, causing swelling.", "Bilateral pitting oedema up to the knees was documented.", "symptoms", "medium", ipa: "/iˈdiːmə/", contextNotes: "Spelled 'edema' in US English.", collocations: new[] { "pitting oedema", "peripheral oedema", "pulmonary oedema" });
        yield return V("ascites", "Abnormal accumulation of fluid in the peritoneal cavity.", "Tense ascites required therapeutic paracentesis.", "symptoms", "hard", ipa: "/əˈsaɪtiːz/", related: new[] { "paracentesis", "cirrhosis" });
        yield return V("lymphadenopathy", "Enlargement of lymph nodes.", "Generalised lymphadenopathy warranted urgent haematology review.", "symptoms", "hard", ipa: "/lɪmˌfædɪˈnɒpəθi/");
        yield return V("hepatomegaly", "Enlargement of the liver beyond its normal size.", "Tender hepatomegaly was palpated 4 cm below the costal margin.", "symptoms", "hard", ipa: "/ˌhɛpətəˈmɛɡəli/", related: new[] { "splenomegaly" });
        yield return V("splenomegaly", "Enlargement of the spleen.", "Massive splenomegaly prompted investigation for haematological malignancy.", "symptoms", "hard", ipa: "/ˌspliːnəˈmɛɡəli/");
        yield return V("hepatosplenomegaly", "Simultaneous enlargement of the liver and spleen.", "Hepatosplenomegaly on examination suggested an infiltrative process.", "symptoms", "hard", ipa: "/ˌhɛpətəʊˌspliːnəˈmɛɡəli/");
        yield return V("tinnitus", "Perception of sound in the absence of an external source.", "Pulsatile tinnitus in one ear warranted further imaging.", "symptoms", "medium", ipa: "/ˈtɪnɪtəs/", collocations: new[] { "pulsatile tinnitus" });
        yield return V("vertigo", "An illusory sense that the environment is rotating.", "Episodic vertigo with positional triggers was consistent with BPPV.", "symptoms", "medium", ipa: "/ˈvɜːtɪɡəʊ/", related: new[] { "benign paroxysmal positional vertigo" });
        yield return V("syncope", "A transient loss of consciousness due to cerebral hypoperfusion.", "Vasovagal syncope followed prolonged standing in a warm environment.", "symptoms", "medium", ipa: "/ˈsɪŋkəpi/", collocations: new[] { "vasovagal syncope", "cardiac syncope" });
        yield return V("presyncope", "A sensation of impending fainting without actual loss of consciousness.", "Recurrent presyncope prompted tilt-table testing.", "symptoms", "hard", ipa: "/ˌpriːˈsɪŋkəpi/");
        yield return V("paraesthesia", "An abnormal sensation such as tingling or 'pins and needles'.", "Paraesthesia in a glove-and-stocking distribution suggested peripheral neuropathy.", "symptoms", "hard", ipa: "/ˌpærɛsˈθiːziə/", contextNotes: "Spelled 'paresthesia' in US English.");
        yield return V("anaesthesia", "Loss of sensation in part or all of the body.", "Saddle anaesthesia was an alarming finding in cauda equina syndrome.", "symptoms", "medium", ipa: "/ˌænəsˈθiːziə/");
        yield return V("hypoaesthesia", "Reduced sensation to stimuli.", "Dermatomal hypoaesthesia was mapped during examination.", "symptoms", "hard");
        yield return V("hyperaesthesia", "Increased sensitivity to stimuli.", "Cutaneous hyperaesthesia preceded the rash of herpes zoster.", "symptoms", "hard");
        yield return V("allodynia", "Pain in response to a stimulus that would not normally cause pain.", "Allodynia on light touch characterised the neuropathic pain.", "symptoms", "hard", ipa: "/ˌæləˈdɪniə/");
        yield return V("hyperalgesia", "Increased sensitivity to painful stimuli.", "Hyperalgesia around the wound edge suggested neuropathic involvement.", "symptoms", "hard");
        yield return V("myalgia", "Pain in a muscle or group of muscles.", "Generalised myalgia and fever followed the recent viral illness.", "symptoms", "medium", ipa: "/maɪˈældʒə/", synonyms: new[] { "muscle pain" });
        yield return V("arthralgia", "Pain in a joint without swelling or other signs of inflammation.", "Polyarticular arthralgia without swelling prompted rheumatology referral.", "symptoms", "medium", ipa: "/ɑːˈθrældʒə/", related: new[] { "arthritis" });
        yield return V("osteoalgia", "Pain in a bone.", "Localised osteoalgia and night pain warranted skeletal imaging.", "symptoms", "hard");
        yield return V("headache", "A continuous pain in the head.", "She described a severe headache with photophobia and neck stiffness.", "symptoms", "easy", ipa: "/ˈhɛdeɪk/", synonyms: new[] { "cephalalgia" });
        yield return V("cephalalgia", "Medical term for headache.", "Chronic cephalalgia unresponsive to simple analgesia warranted specialist review.", "symptoms", "hard");
        yield return V("photophobia", "Abnormal sensitivity to light.", "Photophobia, neck stiffness, and fever raised concern for meningitis.", "symptoms", "medium", ipa: "/ˌfəʊtəˈfəʊbiə/");
        yield return V("phonophobia", "Abnormal sensitivity to sound, often accompanying migraine.", "Phonophobia and photophobia accompanied the migraine attack.", "symptoms", "hard");
        yield return V("aura", "Perceptual disturbance experienced by some before a migraine or seizure.", "She described a visual aura preceding each migraine attack.", "symptoms", "medium", ipa: "/ˈɔːrə/", related: new[] { "migraine", "seizure" });
        yield return V("seizure", "A sudden, uncontrolled electrical disturbance in the brain.", "A witnessed generalised tonic-clonic seizure prompted admission.", "symptoms", "medium", ipa: "/ˈsiːʒə/", collocations: new[] { "tonic-clonic seizure", "focal seizure" });
        yield return V("convulsion", "Involuntary and violent contraction of the muscles.", "Febrile convulsions in the child resolved spontaneously.", "symptoms", "medium", ipa: "/kənˈvʌlʃən/", related: new[] { "seizure" });
        yield return V("tremor", "An involuntary, rhythmic muscle contraction causing oscillation.", "A pill-rolling resting tremor was consistent with idiopathic Parkinson's disease.", "symptoms", "medium", ipa: "/ˈtrɛmə/", collocations: new[] { "resting tremor", "intention tremor" });
        yield return V("chorea", "Involuntary, jerky, irregular movements.", "Chorea of the limbs was documented in a family history of Huntington's disease.", "symptoms", "hard", ipa: "/kəˈrɪə/");
        yield return V("ataxia", "Lack of voluntary coordination of muscle movements.", "Truncal ataxia suggested a cerebellar lesion.", "symptoms", "hard", ipa: "/əˈtæksiə/", collocations: new[] { "cerebellar ataxia", "truncal ataxia" });
        yield return V("dyspnoea", "Difficulty or laboured breathing.", "Progressive dyspnoea on exertion prompted cardiology referral.", "symptoms", "medium", ipa: "/dɪspˈniːə/", contextNotes: "Spelled 'dyspnea' in US English.", collocations: new[] { "dyspnoea on exertion", "nocturnal dyspnoea" });
        yield return V("orthopnoea", "Breathlessness on lying flat, relieved by sitting up.", "Orthopnoea requiring three pillows at night was consistent with heart failure.", "symptoms", "hard", ipa: "/ɔːˈθɒpniə/");
        yield return V("tachypnoea", "Abnormally rapid breathing.", "Tachypnoea at 28 breaths per minute prompted arterial blood gas analysis.", "symptoms", "medium", ipa: "/ˌtækɪpˈniːə/");
        yield return V("bradypnoea", "Abnormally slow breathing.", "Opioid-induced bradypnoea responded to naloxone.", "symptoms", "hard", ipa: "/ˌbrædɪpˈniːə/");
        yield return V("apnoea", "Temporary cessation of breathing.", "Obstructive sleep apnoea was confirmed on overnight polysomnography.", "symptoms", "medium", ipa: "/æpˈniːə/", collocations: new[] { "obstructive sleep apnoea", "central apnoea" });
        yield return V("wheeze", "A high-pitched whistling sound during breathing.", "Expiratory wheeze improved with nebulised salbutamol.", "symptoms", "easy", ipa: "/wiːz/", collocations: new[] { "expiratory wheeze" });
        yield return V("stridor", "A high-pitched inspiratory sound caused by upper airway obstruction.", "Biphasic stridor in the child warranted urgent ENT assessment.", "symptoms", "hard", ipa: "/ˈstraɪdə/");
        yield return V("crepitations", "Fine crackling sounds heard on auscultation of the lungs.", "Bilateral basal crepitations suggested pulmonary oedema.", "symptoms", "medium", ipa: "/ˌkrɛpɪˈteɪʃənz/", synonyms: new[] { "crackles", "rales" });
        yield return V("haemoperitoneum", "The presence of blood in the peritoneal cavity.", "Haemoperitoneum on FAST scan prompted urgent laparotomy.", "symptoms", "hard");
        yield return V("bruit", "An abnormal sound heard over a blood vessel with a stethoscope.", "A carotid bruit was auscultated on the right side.", "symptoms", "hard", ipa: "/bruːt/", related: new[] { "stenosis" });
        yield return V("palpitations", "An awareness of the heart beating irregularly, rapidly, or forcefully.", "Palpitations on exertion prompted a 24-hour Holter monitor.", "symptoms", "medium", ipa: "/ˌpælpɪˈteɪʃənz/");
        yield return V("tachycardia", "An elevated heart rate above the normal resting range (>100 bpm).", "Sinus tachycardia at 118 bpm was noted on admission.", "symptoms", "medium", ipa: "/ˌtækɪˈkɑːdiə/", collocations: new[] { "sinus tachycardia", "ventricular tachycardia" });
        yield return V("bradycardia", "An abnormally slow heart rate (<60 bpm).", "Symptomatic bradycardia required atropine administration.", "symptoms", "medium", ipa: "/ˌbrædɪˈkɑːdiə/");
        yield return V("hypertension", "Persistently elevated arterial blood pressure.", "He has a long-standing history of essential hypertension.", "symptoms", "easy", ipa: "/ˌhaɪpəˈtɛnʃən/", synonyms: new[] { "high blood pressure" });
        yield return V("hypotension", "Abnormally low blood pressure.", "Postural hypotension was identified as the cause of recurrent falls.", "symptoms", "medium", ipa: "/ˌhaɪpəʊˈtɛnʃən/", collocations: new[] { "postural hypotension", "orthostatic hypotension" });
        yield return V("hypoxia", "A deficiency in the amount of oxygen reaching the tissues.", "Nocturnal hypoxia was documented on overnight oximetry.", "symptoms", "medium", ipa: "/haɪˈpɒksiə/", related: new[] { "hypoxaemia" });
        yield return V("hypoxaemia", "Abnormally low concentration of oxygen in arterial blood.", "Severe hypoxaemia required non-invasive ventilation.", "symptoms", "hard", ipa: "/ˌhaɪpɒkˈsiːmiə/");
        yield return V("hypercapnia", "An abnormally high level of carbon dioxide in the blood.", "Hypercapnia with respiratory acidosis was evident on arterial blood gas.", "symptoms", "hard", ipa: "/ˌhaɪpəˈkæpniə/");
        yield return V("pyrexia", "An abnormally high body temperature; fever.", "Persistent pyrexia prompted blood cultures.", "symptoms", "medium", ipa: "/paɪˈrɛksiə/", synonyms: new[] { "fever", "febrile state" }, collocations: new[] { "pyrexia of unknown origin" });
        yield return V("hyperpyrexia", "An extremely high body temperature, usually above 41 °C.", "Hyperpyrexia following anaesthesia raised concern for malignant hyperthermia.", "symptoms", "hard");
        yield return V("hypothermia", "Abnormally low body temperature, below 35 °C.", "Accidental hypothermia was treated with active external rewarming.", "symptoms", "medium", ipa: "/ˌhaɪpəʊˈθɜːmiə/");
        yield return V("diaphoresis", "Excessive sweating, often as a sign of underlying illness.", "Diaphoresis and pallor accompanied the chest pain episode.", "symptoms", "hard", ipa: "/ˌdaɪəfəˈriːsɪs/", synonyms: new[] { "profuse sweating" });
        yield return V("anhidrosis", "Inability to sweat normally.", "Segmental anhidrosis prompted autonomic assessment.", "symptoms", "hard");
        yield return V("rigor", "An episode of shivering or shaking chills, often preceding fever.", "A rigor preceded the temperature spike to 39.4 °C.", "symptoms", "medium", ipa: "/ˈrɪɡə/");
        yield return V("lethargy", "A state of sluggishness or reduced alertness.", "The child appeared lethargic and was admitted for observation.", "symptoms", "easy", ipa: "/ˈlɛθədʒi/", synonyms: new[] { "drowsiness" });
        yield return V("malaise", "A general feeling of being unwell without a specific cause.", "Two weeks of malaise and arthralgia preceded the rash.", "symptoms", "medium", ipa: "/məˈleɪz/");
        yield return V("cachexia", "Marked weight loss, muscle atrophy, and weakness seen in chronic disease.", "Cachexia was evident, with weight loss of 12 kg over three months.", "symptoms", "hard", ipa: "/kəˈkɛksiə/");
        yield return V("anorexia", "Loss of appetite.", "Persistent anorexia and early satiety prompted gastroscopy.", "symptoms", "medium", ipa: "/ˌænəˈrɛksiə/", contextNotes: "Also denotes 'anorexia nervosa' — the context disambiguates.");

        // ── CONDITIONS (80 terms) ──────────────────────────────────────────
        yield return V("asthma", "A chronic inflammatory airway disease causing reversible airflow obstruction.", "His asthma has been poorly controlled since a recent upper respiratory tract infection.", "conditions", "easy", ipa: "/ˈæsmə/", collocations: new[] { "exacerbation of asthma", "brittle asthma" });
        yield return V("bronchitis", "Inflammation of the bronchi, commonly due to viral infection.", "Acute bronchitis resolved with conservative management.", "conditions", "easy", ipa: "/brɒŋˈkaɪtɪs/");
        yield return V("pneumonia", "Inflammation of the lung tissue, typically caused by infection.", "Community-acquired pneumonia was treated with oral amoxicillin.", "conditions", "easy", ipa: "/njuːˈməʊniə/", collocations: new[] { "community-acquired pneumonia", "aspiration pneumonia" });
        yield return V("emphysema", "A chronic lung condition involving destruction of alveolar walls.", "Advanced emphysema was evident on high-resolution chest CT.", "conditions", "medium", ipa: "/ˌɛmfɪˈsiːmə/");
        yield return V("pneumothorax", "Accumulation of air in the pleural cavity causing lung collapse.", "A spontaneous pneumothorax required chest drain insertion.", "conditions", "hard", ipa: "/ˌnjuːməʊˈθɔːræks/");
        yield return V("pleurisy", "Inflammation of the pleura, causing sharp chest pain on breathing.", "Pleurisy followed a recent lower respiratory tract infection.", "conditions", "medium", ipa: "/ˈplʊərɪsi/");
        yield return V("tuberculosis", "A bacterial infection primarily affecting the lungs.", "Latent tuberculosis was identified through interferon-gamma release assay.", "conditions", "medium", ipa: "/tjuːˌbɜːkjʊˈləʊsɪs/");
        yield return V("pulmonary embolism", "Sudden blockage of a pulmonary artery by a blood clot.", "Pulmonary embolism was confirmed on CTPA and anticoagulation commenced.", "conditions", "hard", collocations: new[] { "massive pulmonary embolism" });
        yield return V("atelectasis", "Partial or complete collapse of a lung or a section of the lung.", "Right lower lobe atelectasis was noted on post-operative imaging.", "conditions", "hard", ipa: "/ˌætəˈlɛktəsɪs/");
        yield return V("cystic fibrosis", "A hereditary disorder affecting the exocrine glands, particularly the lungs.", "She has cystic fibrosis and receives regular airway clearance therapy.", "conditions", "medium");
        yield return V("myocardial infarction", "Heart muscle damage caused by blockage of a coronary artery.", "An acute anterior myocardial infarction was confirmed on ECG and troponin rise.", "conditions", "hard", synonyms: new[] { "heart attack" }, collocations: new[] { "acute myocardial infarction" });
        yield return V("angina pectoris", "Chest pain due to reduced blood flow to the heart muscle.", "Stable angina pectoris was controlled with a beta-blocker and GTN as required.", "conditions", "medium");
        yield return V("cardiac arrest", "Sudden cessation of effective cardiac output.", "In-hospital cardiac arrest was managed with advanced life support.", "conditions", "medium");
        yield return V("heart failure", "A syndrome in which the heart is unable to pump effectively.", "Congestive heart failure was managed with diuretics and an ACE inhibitor.", "conditions", "easy", collocations: new[] { "congestive heart failure", "decompensated heart failure" });
        yield return V("atrial fibrillation", "An irregular, often rapid, heart rhythm originating in the atria.", "New-onset atrial fibrillation required rate control and anticoagulation.", "conditions", "medium");
        yield return V("deep vein thrombosis", "A blood clot in a deep vein, usually in the leg.", "Deep vein thrombosis was suspected clinically and confirmed on Doppler ultrasound.", "conditions", "medium", synonyms: new[] { "DVT" });
        yield return V("peripheral arterial disease", "Narrowing of arteries in the limbs, causing reduced blood flow.", "Peripheral arterial disease presented with intermittent claudication.", "conditions", "medium");
        yield return V("stroke", "A sudden neurological deficit due to cerebrovascular disease.", "An ischaemic stroke in the left middle cerebral artery territory was confirmed on MRI.", "conditions", "easy", collocations: new[] { "ischaemic stroke", "haemorrhagic stroke" });
        yield return V("transient ischaemic attack", "A temporary neurological deficit with full recovery within 24 hours.", "A transient ischaemic attack prompted urgent carotid Doppler studies.", "conditions", "medium");
        yield return V("subarachnoid haemorrhage", "Bleeding into the subarachnoid space, often from a ruptured aneurysm.", "A thunderclap headache and neck stiffness raised concern for subarachnoid haemorrhage.", "conditions", "hard");
        yield return V("meningitis", "Inflammation of the meninges, usually due to infection.", "Bacterial meningitis was treated with intravenous ceftriaxone.", "conditions", "medium", ipa: "/ˌmɛnɪnˈdʒaɪtɪs/", collocations: new[] { "bacterial meningitis", "viral meningitis" });
        yield return V("encephalitis", "Inflammation of the brain, typically viral in origin.", "Herpes simplex encephalitis was treated with intravenous aciclovir.", "conditions", "hard");
        yield return V("epilepsy", "A neurological disorder marked by recurrent seizures.", "Her epilepsy has been seizure-free on levetiracetam for two years.", "conditions", "medium", ipa: "/ˈɛpɪlɛpsi/");
        yield return V("migraine", "A recurrent throbbing headache, often with nausea and aura.", "Migraine with aura responded partially to sumatriptan.", "conditions", "easy", ipa: "/ˈmiːɡreɪn/");
        yield return V("multiple sclerosis", "A chronic demyelinating disease of the central nervous system.", "Relapsing-remitting multiple sclerosis was treated with a disease-modifying agent.", "conditions", "medium");
        yield return V("Parkinson's disease", "A progressive neurodegenerative disorder with rest tremor, rigidity and bradykinesia.", "Early Parkinson's disease was diagnosed based on asymmetric rest tremor and bradykinesia.", "conditions", "medium", contextNotes: "Eponymous — preserve capital P.");
        yield return V("Alzheimer's disease", "A progressive neurodegenerative disorder and commonest cause of dementia.", "Moderate Alzheimer's disease was managed with a cholinesterase inhibitor.", "conditions", "medium", contextNotes: "Eponymous — preserve capital A.");
        yield return V("dementia", "A progressive decline in cognitive function affecting daily living.", "Progressive dementia was confirmed on cognitive assessment.", "conditions", "easy", ipa: "/dɪˈmɛnʃə/");
        yield return V("diabetes mellitus", "A metabolic disease characterised by persistent hyperglycaemia.", "Type 2 diabetes mellitus was diagnosed based on an HbA1c of 58 mmol/mol.", "conditions", "easy");
        yield return V("hypothyroidism", "Underactivity of the thyroid gland causing low thyroid hormone levels.", "Primary hypothyroidism was treated with levothyroxine.", "conditions", "medium");
        yield return V("hyperthyroidism", "Overactivity of the thyroid gland producing excess hormone.", "Hyperthyroidism was confirmed by a suppressed TSH and elevated free T4.", "conditions", "medium");
        yield return V("Cushing's syndrome", "A condition caused by prolonged exposure to elevated cortisol levels.", "Iatrogenic Cushing's syndrome developed after long-term prednisolone therapy.", "conditions", "hard", contextNotes: "Eponymous — preserve capital C.");
        yield return V("Addison's disease", "Primary adrenal insufficiency with reduced cortisol production.", "Addison's disease was suspected after an abnormal short Synacthen test.", "conditions", "hard", contextNotes: "Eponymous — preserve capital A.");
        yield return V("gout", "An inflammatory arthritis caused by monosodium urate crystal deposition.", "An acute gout attack in the first MTP joint settled with colchicine.", "conditions", "medium", ipa: "/ɡaʊt/");
        yield return V("rheumatoid arthritis", "A chronic autoimmune inflammatory joint disease.", "Seropositive rheumatoid arthritis was commenced on methotrexate.", "conditions", "medium");
        yield return V("osteoarthritis", "A degenerative joint disease causing cartilage loss.", "Osteoarthritis of the right knee was managed with topical NSAIDs and physiotherapy.", "conditions", "easy");
        yield return V("osteoporosis", "A condition in which bones become brittle due to reduced density.", "Severe osteoporosis was treated with a bisphosphonate.", "conditions", "medium", ipa: "/ˌɒstiəʊpəˈrəʊsɪs/");
        yield return V("psoriasis", "A chronic autoimmune skin disease characterised by scaly plaques.", "Moderate plaque psoriasis responded to topical calcipotriol.", "conditions", "medium", ipa: "/səˈraɪəsɪs/");
        yield return V("eczema", "A chronic inflammatory skin condition causing itchy, dry, reddened skin.", "Atopic eczema was managed with emollients and topical corticosteroids.", "conditions", "easy", ipa: "/ˈɛksɪmə/");
        yield return V("urticaria", "A skin reaction characterised by itchy wheals.", "Acute urticaria resolved with a non-sedating antihistamine.", "conditions", "medium", ipa: "/ˌɜːtɪˈkɛəriə/", synonyms: new[] { "hives" });
        yield return V("cellulitis", "A bacterial skin and subcutaneous tissue infection.", "Lower limb cellulitis required intravenous flucloxacillin.", "conditions", "medium");
        yield return V("sepsis", "A life-threatening organ dysfunction due to dysregulated host response to infection.", "Urosepsis was treated with early broad-spectrum antibiotics per the sepsis-six bundle.", "conditions", "hard", ipa: "/ˈsɛpsɪs/");
        yield return V("bacteraemia", "The presence of bacteria in the bloodstream.", "Bacteraemia was documented on repeated blood cultures.", "conditions", "hard");
        yield return V("septic shock", "Sepsis with persisting hypotension requiring vasopressors.", "Septic shock required noradrenaline support and intensive care admission.", "conditions", "hard");
        yield return V("urinary tract infection", "Infection involving any part of the urinary system.", "An uncomplicated urinary tract infection was treated with oral nitrofurantoin.", "conditions", "easy", synonyms: new[] { "UTI" });
        yield return V("pyelonephritis", "Bacterial infection of the renal parenchyma.", "Acute pyelonephritis required admission for intravenous antibiotics.", "conditions", "medium", ipa: "/ˌpaɪələʊnɪˈfraɪtɪs/");
        yield return V("acute kidney injury", "A rapid decline in renal function over hours to days.", "Pre-renal acute kidney injury resolved with volume resuscitation.", "conditions", "medium", synonyms: new[] { "AKI" });
        yield return V("chronic kidney disease", "Progressive loss of renal function over months to years.", "Stage 3 chronic kidney disease was managed with renin-angiotensin blockade.", "conditions", "medium");
        yield return V("nephrotic syndrome", "Heavy proteinuria, hypoalbuminaemia, and oedema.", "Nephrotic syndrome in the child warranted a renal biopsy.", "conditions", "hard");
        yield return V("glomerulonephritis", "Inflammation of the glomeruli within the kidneys.", "Post-streptococcal glomerulonephritis presented with haematuria.", "conditions", "hard");
        yield return V("cholelithiasis", "The formation of gallstones in the gallbladder.", "Symptomatic cholelithiasis led to laparoscopic cholecystectomy.", "conditions", "hard", ipa: "/ˌkɒlɪlɪˈθaɪəsɪs/", synonyms: new[] { "gallstones" });
        yield return V("cholecystitis", "Inflammation of the gallbladder, usually due to gallstones.", "Acute cholecystitis was managed conservatively with antibiotics.", "conditions", "medium", ipa: "/ˌkɒlɪsɪsˈtaɪtɪs/");
        yield return V("pancreatitis", "Inflammation of the pancreas.", "Acute pancreatitis with a lipase six times the upper limit of normal was admitted.", "conditions", "medium", collocations: new[] { "acute pancreatitis", "chronic pancreatitis" });
        yield return V("peptic ulcer disease", "Ulceration of the gastric or duodenal mucosa.", "Peptic ulcer disease responded to Helicobacter pylori eradication therapy.", "conditions", "medium");
        yield return V("gastritis", "Inflammation of the gastric mucosa.", "Chronic gastritis was linked to Helicobacter pylori colonisation.", "conditions", "medium");
        yield return V("gastroenteritis", "Inflammation of the stomach and intestines, usually infective.", "Acute gastroenteritis resolved with oral rehydration.", "conditions", "easy");
        yield return V("gastro-oesophageal reflux disease", "Retrograde flow of gastric contents into the oesophagus.", "Gastro-oesophageal reflux disease was managed with a proton pump inhibitor.", "conditions", "medium", synonyms: new[] { "GORD", "GERD" });
        yield return V("Crohn's disease", "A chronic inflammatory bowel disease affecting any part of the GI tract.", "Crohn's disease of the terminal ileum was managed with azathioprine.", "conditions", "medium", contextNotes: "Eponymous — preserve capital C.");
        yield return V("ulcerative colitis", "A chronic inflammatory disease of the colonic mucosa.", "Left-sided ulcerative colitis flared during a recent steroid taper.", "conditions", "medium");
        yield return V("irritable bowel syndrome", "A functional gastrointestinal disorder causing abdominal pain and altered bowel habit.", "Irritable bowel syndrome with diarrhoea predominance was managed conservatively.", "conditions", "easy", synonyms: new[] { "IBS" });
        yield return V("diverticulitis", "Inflammation of diverticula within the colonic wall.", "Acute sigmoid diverticulitis required hospitalisation for antibiotics.", "conditions", "medium");
        yield return V("appendicitis", "Inflammation of the appendix, usually requiring surgical removal.", "Acute appendicitis proceeded to urgent laparoscopic appendicectomy.", "conditions", "easy");
        yield return V("hernia", "A protrusion of an organ or tissue through a weak point in surrounding musculature.", "An incarcerated inguinal hernia required emergency repair.", "conditions", "easy", ipa: "/ˈhɜːniə/");
        yield return V("breast cancer", "A malignant tumour arising in breast tissue.", "Early invasive breast cancer was treated with lumpectomy and adjuvant endocrine therapy.", "conditions", "medium");
        yield return V("lung cancer", "A malignancy arising in lung tissue.", "Non-small-cell lung cancer was staged as T2N1M0 on CT.", "conditions", "medium");
        yield return V("colorectal cancer", "Malignancy of the colon or rectum.", "Screening detected an early-stage colorectal cancer.", "conditions", "medium");
        yield return V("leukaemia", "A cancer of the blood-forming tissues.", "Acute myeloid leukaemia was confirmed by bone marrow biopsy.", "conditions", "medium", ipa: "/luːˈkiːmiə/", contextNotes: "Spelled 'leukemia' in US English.");
        yield return V("lymphoma", "A cancer of the lymphatic system.", "Stage II Hodgkin lymphoma was commenced on ABVD chemotherapy.", "conditions", "medium", ipa: "/lɪmˈfəʊmə/");
        yield return V("melanoma", "A malignant tumour arising from pigment-producing melanocytes.", "An irregularly pigmented lesion was excised and reported as melanoma in situ.", "conditions", "medium");
        yield return V("anaemia", "A reduction in haemoglobin concentration below the reference range.", "Iron-deficiency anaemia was treated with oral ferrous sulfate.", "conditions", "easy", ipa: "/əˈniːmiə/", contextNotes: "Spelled 'anemia' in US English.");
        yield return V("thrombocytopenia", "A reduced platelet count below the normal range.", "Idiopathic thrombocytopenia warranted immunology review.", "conditions", "hard");
        yield return V("haemophilia", "An inherited bleeding disorder due to clotting factor deficiency.", "Haemophilia A was managed with recombinant factor VIII.", "conditions", "hard", ipa: "/ˌhiːməˈfɪliə/");
        yield return V("sickle cell disease", "An inherited disorder causing abnormally shaped red blood cells.", "An acute sickle cell crisis required analgesia and intravenous hydration.", "conditions", "medium");
        yield return V("depression", "A persistent mood disorder characterised by low mood and anhedonia.", "Moderate depression was treated with sertraline and referral for talking therapy.", "conditions", "easy");
        yield return V("anxiety disorder", "A group of disorders characterised by excessive and persistent anxiety.", "Generalised anxiety disorder responded partially to CBT.", "conditions", "easy");
        yield return V("bipolar disorder", "A mood disorder with alternating episodes of mania and depression.", "Bipolar I disorder was stabilised on lithium.", "conditions", "medium");
        yield return V("schizophrenia", "A psychiatric disorder characterised by distorted thoughts and perceptions.", "First-episode schizophrenia was managed with olanzapine and community support.", "conditions", "medium");
        yield return V("dementia with Lewy bodies", "A progressive dementia with parkinsonism and visual hallucinations.", "Dementia with Lewy bodies was suggested by fluctuating cognition and REM sleep behaviour disorder.", "conditions", "hard");
        yield return V("glaucoma", "A group of eye conditions damaging the optic nerve, often due to raised intraocular pressure.", "Primary open-angle glaucoma was treated with timolol eye drops.", "conditions", "medium");
        yield return V("cataract", "A clouding of the lens of the eye causing blurred vision.", "Bilateral cataracts were treated sequentially with phacoemulsification.", "conditions", "easy");

        // ── ANATOMY (60 terms) ──────────────────────────────────────────
        yield return V("thorax", "The part of the body between the neck and the abdomen.", "Examination of the thorax revealed decreased breath sounds on the right.", "anatomy", "medium", ipa: "/ˈθɔːræks/", related: new[] { "chest" });
        yield return V("abdomen", "The part of the body between the thorax and the pelvis.", "The abdomen was soft and non-tender on palpation.", "anatomy", "easy", ipa: "/ˈæbdəmən/");
        yield return V("pelvis", "The bony structure supporting the spinal column and containing pelvic organs.", "A pelvic fracture was evident on the AP pelvic X-ray.", "anatomy", "easy");
        yield return V("cranium", "The skull, specifically the part enclosing the brain.", "Cranial nerves II to XII were grossly intact.", "anatomy", "medium");
        yield return V("vertebra", "One of the bones forming the spinal column.", "An L1 vertebra compression fracture was identified on imaging.", "anatomy", "medium", ipa: "/ˈvɜːtɪbrə/");
        yield return V("clavicle", "The collarbone, connecting the sternum to the scapula.", "A midshaft clavicle fracture was managed in a broad-arm sling.", "anatomy", "medium");
        yield return V("scapula", "The shoulder blade.", "The scapula was assessed for range of movement.", "anatomy", "medium");
        yield return V("sternum", "The breastbone, to which the ribs are attached anteriorly.", "Sternal tenderness was noted following the road traffic collision.", "anatomy", "medium");
        yield return V("femur", "The thigh bone, the longest bone in the body.", "A right femur shaft fracture required intramedullary nailing.", "anatomy", "medium");
        yield return V("tibia", "The shinbone, the larger of the two lower leg bones.", "A tibial plateau fracture was identified.", "anatomy", "medium");
        yield return V("fibula", "The smaller, outer bone of the lower leg.", "A non-displaced fibula fracture was managed conservatively.", "anatomy", "medium");
        yield return V("humerus", "The long bone of the upper arm.", "A proximal humerus fracture was observed in the elderly patient after a fall.", "anatomy", "medium");
        yield return V("radius", "One of the two long bones of the forearm, on the thumb side.", "A distal radius fracture was reduced under local anaesthesia.", "anatomy", "medium");
        yield return V("ulna", "One of the two long bones of the forearm, on the little-finger side.", "The ulna was intact on the radiograph.", "anatomy", "medium");
        yield return V("patella", "The kneecap, a small bone in front of the knee joint.", "A patella dislocation reduced spontaneously.", "anatomy", "medium");
        yield return V("metatarsal", "One of the five long bones in the middle of the foot.", "A fifth metatarsal fracture was treated in a walking boot.", "anatomy", "medium");
        yield return V("carpus", "The eight small bones of the wrist.", "Scaphoid tenderness was noted on examination of the carpus.", "anatomy", "hard");
        yield return V("tarsus", "The seven bones forming the ankle and heel.", "A talus fracture within the tarsus required orthopaedic review.", "anatomy", "hard");
        yield return V("myocardium", "The muscular tissue of the heart.", "Damage to the myocardium was evident on cardiac MRI.", "anatomy", "medium", ipa: "/ˌmaɪəʊˈkɑːdiəm/");
        yield return V("endocardium", "The inner layer of tissue lining the heart chambers.", "Vegetations on the endocardium were consistent with infective endocarditis.", "anatomy", "hard");
        yield return V("pericardium", "The fibrous sac enclosing the heart.", "A small pericardial effusion within the pericardium was observed.", "anatomy", "hard");
        yield return V("alveolus", "A small air sac in the lungs where gas exchange occurs.", "Inflammation at the alveolus level underlies the diffusion defect.", "anatomy", "hard", ipa: "/ælˈviːələs/");
        yield return V("bronchus", "One of the two main air passages branching from the trachea.", "The foreign body lodged in the right main bronchus.", "anatomy", "medium");
        yield return V("bronchiole", "A small airway branching from a bronchus.", "Inflammation of the bronchioles characterises bronchiolitis in infants.", "anatomy", "medium");
        yield return V("trachea", "The windpipe, connecting the larynx to the bronchi.", "The trachea was midline and mobile on palpation.", "anatomy", "medium");
        yield return V("larynx", "The voice box, housing the vocal cords.", "A lesion on the larynx was visualised on fibreoptic examination.", "anatomy", "medium");
        yield return V("pharynx", "The throat, connecting the mouth and nasal cavity to the oesophagus.", "The posterior pharynx appeared erythematous.", "anatomy", "medium");
        yield return V("oesophagus", "The tube connecting the pharynx to the stomach.", "A stricture in the distal oesophagus was dilated endoscopically.", "anatomy", "medium", ipa: "/iˈsɒfəɡəs/", contextNotes: "Spelled 'esophagus' in US English.");
        yield return V("duodenum", "The first part of the small intestine, immediately after the stomach.", "A duodenal ulcer was visualised on upper GI endoscopy.", "anatomy", "medium", ipa: "/ˌdjuːəˈdiːnəm/");
        yield return V("jejunum", "The middle section of the small intestine.", "The jejunum was inspected during laparoscopy.", "anatomy", "hard");
        yield return V("ileum", "The final section of the small intestine before the colon.", "Crohn's disease most commonly affects the terminal ileum.", "anatomy", "hard");
        yield return V("colon", "The main part of the large intestine.", "A polyp in the sigmoid colon was removed during colonoscopy.", "anatomy", "easy");
        yield return V("rectum", "The final section of the large intestine, ending at the anus.", "A rectal examination was performed with the patient's consent.", "anatomy", "easy");
        yield return V("appendix", "A finger-like pouch attached to the caecum.", "The inflamed appendix was removed via laparoscopic appendicectomy.", "anatomy", "easy");
        yield return V("gallbladder", "A small organ beneath the liver that stores bile.", "The gallbladder contained multiple stones on ultrasound.", "anatomy", "easy");
        yield return V("pancreas", "A glandular organ with both endocrine and exocrine functions.", "The pancreas appeared oedematous on the abdominal CT.", "anatomy", "medium");
        yield return V("spleen", "An organ on the left side of the abdomen involved in immune function.", "The spleen was palpable 4 cm below the costal margin.", "anatomy", "easy");
        yield return V("kidney", "A paired retroperitoneal organ filtering blood and producing urine.", "Both kidneys were of normal size on ultrasound.", "anatomy", "easy");
        yield return V("ureter", "The tube conveying urine from the kidney to the bladder.", "A stone was seen obstructing the right ureter on CT.", "anatomy", "medium", ipa: "/jʊəˈriːtə/");
        yield return V("urethra", "The tube conveying urine from the bladder out of the body.", "Catheterisation of the urethra was performed under aseptic technique.", "anatomy", "medium");
        yield return V("prostate", "A gland in men surrounding the urethra below the bladder.", "The prostate was firm and asymmetric on digital rectal examination.", "anatomy", "medium");
        yield return V("cervix", "The lower part of the uterus opening into the vagina.", "The cervix was visualised for routine cervical screening.", "anatomy", "medium");
        yield return V("endometrium", "The inner mucous lining of the uterus.", "Endometrial thickness was measured on transvaginal ultrasound.", "anatomy", "hard");
        yield return V("myometrium", "The muscular layer of the uterus.", "Fibroids were noted within the myometrium.", "anatomy", "hard");
        yield return V("ovary", "A paired female reproductive organ producing ova and hormones.", "An ovarian cyst on the right ovary was monitored with serial ultrasound.", "anatomy", "easy");
        yield return V("fallopian tube", "One of the paired tubes through which ova travel from the ovary to the uterus.", "A fallopian tube ectopic pregnancy was suspected on ultrasound.", "anatomy", "medium");
        yield return V("testis", "One of the paired male reproductive organs.", "A testicular mass was identified on ultrasound of the right testis.", "anatomy", "medium");
        yield return V("thyroid gland", "An endocrine gland in the neck producing thyroid hormones.", "The thyroid gland was diffusely enlarged without nodules.", "anatomy", "easy");
        yield return V("adrenal gland", "A paired endocrine gland sitting atop each kidney.", "An incidental adrenal adenoma on the left adrenal gland was followed up.", "anatomy", "medium");
        yield return V("pituitary gland", "A small endocrine gland at the base of the brain.", "A macroadenoma of the pituitary gland required neurosurgical referral.", "anatomy", "medium");
        yield return V("hypothalamus", "A region of the brain regulating autonomic and endocrine function.", "The hypothalamus was unaffected by the lesion.", "anatomy", "hard");
        yield return V("cerebellum", "The part of the brain responsible for coordination and balance.", "A cerebellum lesion explained the gait ataxia.", "anatomy", "medium");
        yield return V("cerebrum", "The largest part of the brain, involved in conscious thought and voluntary movement.", "The cerebrum showed age-appropriate atrophy on MRI.", "anatomy", "medium");
        yield return V("medulla oblongata", "The lower part of the brainstem controlling vital functions.", "Lesions of the medulla oblongata can affect breathing and heart rate.", "anatomy", "hard");
        yield return V("meninges", "The three membranes enveloping the brain and spinal cord.", "Infection of the meninges constitutes meningitis.", "anatomy", "hard");
        yield return V("dura mater", "The outermost, toughest of the three meninges.", "A bleed between the skull and the dura mater produces an epidural haematoma.", "anatomy", "hard");
        yield return V("retina", "The light-sensitive tissue lining the back of the eye.", "Haemorrhages on the retina were noted on fundoscopy.", "anatomy", "medium");
        yield return V("cochlea", "The spiral cavity of the inner ear producing nerve impulses for hearing.", "A cochlear implant was considered following bilateral cochlea dysfunction.", "anatomy", "hard");
        yield return V("tympanic membrane", "The eardrum, separating the external and middle ear.", "The tympanic membrane appeared erythematous and bulging.", "anatomy", "medium");
        yield return V("sinus", "An air-filled cavity within the cranial bones.", "The maxillary sinus was opacified on CT.", "anatomy", "easy");

        // ── PHARMACOLOGY (80 terms) ──────────────────────────────────────────
        yield return V("analgesia", "The relief of pain without loss of consciousness.", "Adequate analgesia was provided with paracetamol and a weak opioid.", "pharmacology", "medium", ipa: "/ˌænəlˈdʒiːziə/");
        yield return V("analgesic", "A medication that relieves pain.", "A simple analgesic was trialled before escalating to opioids.", "pharmacology", "medium", synonyms: new[] { "painkiller" });
        yield return V("antipyretic", "A medication that reduces fever.", "Paracetamol was administered as an antipyretic.", "pharmacology", "medium");
        yield return V("antiemetic", "A medication that prevents or relieves nausea and vomiting.", "Ondansetron was prescribed as an antiemetic for chemotherapy-induced nausea.", "pharmacology", "medium");
        yield return V("antitussive", "A medication that suppresses cough.", "Codeine is sometimes used as an antitussive in chronic dry cough.", "pharmacology", "hard");
        yield return V("expectorant", "A medication promoting the clearance of mucus from the airways.", "Guaifenesin was trialled as an expectorant.", "pharmacology", "hard");
        yield return V("bronchodilator", "A medication that widens the airways.", "A short-acting bronchodilator improved peak flow within minutes.", "pharmacology", "medium");
        yield return V("corticosteroid", "A class of steroid hormones, used anti-inflammatorily.", "Oral corticosteroids were tapered slowly to avoid adrenal suppression.", "pharmacology", "medium");
        yield return V("antibiotic", "A medication used to treat bacterial infections.", "A seven-day course of antibiotic therapy was prescribed.", "pharmacology", "easy");
        yield return V("antiviral", "A medication used to treat viral infections.", "The antiviral was commenced within 48 hours of symptom onset.", "pharmacology", "medium");
        yield return V("antifungal", "A medication used to treat fungal infections.", "A topical antifungal was sufficient for mild tinea.", "pharmacology", "medium");
        yield return V("anthelmintic", "A medication used to treat parasitic worm infections.", "A single-dose anthelmintic cleared the infestation.", "pharmacology", "hard");
        yield return V("anticoagulant", "A medication that reduces the blood's ability to clot.", "Warfarin, an anticoagulant, requires regular INR monitoring.", "pharmacology", "medium");
        yield return V("antiplatelet", "A medication that prevents platelets from aggregating.", "Aspirin is prescribed as an antiplatelet after myocardial infarction.", "pharmacology", "medium");
        yield return V("thrombolytic", "A medication that dissolves blood clots.", "A thrombolytic was administered within the stroke treatment window.", "pharmacology", "hard");
        yield return V("diuretic", "A medication that increases urine output.", "A loop diuretic was prescribed for symptomatic heart failure.", "pharmacology", "medium");
        yield return V("antihypertensive", "A medication that lowers blood pressure.", "An antihypertensive was titrated to achieve a target BP.", "pharmacology", "medium");
        yield return V("beta-blocker", "A drug class blocking beta-adrenergic receptors.", "A cardioselective beta-blocker was used post-myocardial infarction.", "pharmacology", "medium");
        yield return V("calcium channel blocker", "A medication that blocks calcium entry into cells, lowering blood pressure.", "A dihydropyridine calcium channel blocker was prescribed for hypertension.", "pharmacology", "medium");
        yield return V("ACE inhibitor", "A medication inhibiting angiotensin-converting enzyme.", "The ACE inhibitor was introduced at a low dose.", "pharmacology", "medium");
        yield return V("angiotensin receptor blocker", "A class of antihypertensives blocking angiotensin II receptors.", "An angiotensin receptor blocker was substituted due to ACE-inhibitor cough.", "pharmacology", "hard");
        yield return V("statin", "A drug class that lowers cholesterol by inhibiting HMG-CoA reductase.", "Atorvastatin, a statin, was prescribed for primary prevention.", "pharmacology", "medium");
        yield return V("fibrate", "A class of medications lowering triglyceride levels.", "A fibrate was added to improve lipid control.", "pharmacology", "hard");
        yield return V("proton pump inhibitor", "A medication reducing gastric acid production.", "A proton pump inhibitor was prescribed for reflux symptoms.", "pharmacology", "medium", synonyms: new[] { "PPI" });
        yield return V("histamine H2 receptor antagonist", "A drug class reducing gastric acid by blocking H2 receptors.", "A histamine H2 receptor antagonist was trialled before stepping up to a PPI.", "pharmacology", "hard");
        yield return V("laxative", "A medication that promotes bowel evacuation.", "An osmotic laxative was recommended for chronic constipation.", "pharmacology", "easy");
        yield return V("stool softener", "A medication that moistens stool and promotes easier defecation.", "A stool softener was started after anorectal surgery.", "pharmacology", "medium");
        yield return V("antidiarrhoeal", "A medication that reduces diarrhoea.", "Loperamide was used as a short-term antidiarrhoeal.", "pharmacology", "medium");
        yield return V("antispasmodic", "A medication relieving muscle spasms, often in the GI tract.", "An antispasmodic was trialled for irritable bowel syndrome.", "pharmacology", "medium");
        yield return V("antihistamine", "A medication that blocks histamine receptors, used for allergy.", "A non-sedating antihistamine was prescribed for allergic rhinitis.", "pharmacology", "easy");
        yield return V("decongestant", "A medication that reduces nasal mucosal swelling.", "A short course of nasal decongestant was recommended.", "pharmacology", "medium");
        yield return V("insulin", "A hormone regulating glucose uptake and metabolism.", "Subcutaneous insulin was titrated to pre-meal glucose.", "pharmacology", "easy");
        yield return V("metformin", "An oral antihyperglycaemic agent that reduces hepatic glucose production.", "Metformin was commenced at 500 mg once daily and up-titrated.", "pharmacology", "easy");
        yield return V("sulfonylurea", "An oral hypoglycaemic drug class stimulating insulin release.", "A sulfonylurea was added to intensify glycaemic control.", "pharmacology", "hard");
        yield return V("dipeptidyl peptidase-4 inhibitor", "A class of oral hypoglycaemic agents.", "A dipeptidyl peptidase-4 inhibitor was added as third-line therapy.", "pharmacology", "hard", synonyms: new[] { "DPP-4 inhibitor" });
        yield return V("SGLT2 inhibitor", "A class of oral medications promoting urinary glucose excretion.", "An SGLT2 inhibitor was added for its cardiovascular benefit.", "pharmacology", "hard");
        yield return V("GLP-1 receptor agonist", "An injectable class of medications mimicking incretin effects.", "A weekly GLP-1 receptor agonist supported weight and glycaemic goals.", "pharmacology", "hard");
        yield return V("selective serotonin reuptake inhibitor", "A class of antidepressants blocking serotonin reuptake.", "A selective serotonin reuptake inhibitor was trialled for moderate depression.", "pharmacology", "medium", synonyms: new[] { "SSRI" });
        yield return V("serotonin-norepinephrine reuptake inhibitor", "An antidepressant class acting on serotonin and noradrenaline.", "A serotonin-norepinephrine reuptake inhibitor was substituted after SSRI failure.", "pharmacology", "hard", synonyms: new[] { "SNRI" });
        yield return V("tricyclic antidepressant", "An older class of antidepressants with anticholinergic effects.", "A low-dose tricyclic antidepressant was trialled for neuropathic pain.", "pharmacology", "hard");
        yield return V("monoamine oxidase inhibitor", "A class of antidepressants with dietary restrictions.", "A monoamine oxidase inhibitor was considered only after other agents failed.", "pharmacology", "hard", synonyms: new[] { "MAOI" });
        yield return V("benzodiazepine", "A class of sedative-hypnotic medications.", "A short course of benzodiazepine was prescribed with caution due to dependency risk.", "pharmacology", "medium");
        yield return V("opioid", "A class of strong analgesic medications acting on opioid receptors.", "Opioid analgesia was prescribed for severe post-operative pain.", "pharmacology", "medium");
        yield return V("NSAID", "Non-steroidal anti-inflammatory drug.", "An NSAID provided relief but was avoided due to a history of peptic ulcer disease.", "pharmacology", "easy");
        yield return V("paracetamol", "A common analgesic and antipyretic.", "Regular paracetamol was prescribed as baseline analgesia.", "pharmacology", "easy");
        yield return V("ibuprofen", "A non-steroidal anti-inflammatory drug.", "Ibuprofen 400 mg three times a day was advised with food.", "pharmacology", "easy");
        yield return V("aspirin", "An antiplatelet and analgesic medication.", "Low-dose aspirin was continued as secondary prevention.", "pharmacology", "easy");
        yield return V("warfarin", "An oral anticoagulant requiring INR monitoring.", "Warfarin dosing was adjusted according to target INR.", "pharmacology", "medium");
        yield return V("heparin", "An injectable anticoagulant used for VTE prevention and treatment.", "Low-molecular-weight heparin was administered subcutaneously.", "pharmacology", "medium");
        yield return V("amoxicillin", "A beta-lactam antibiotic commonly used for respiratory infections.", "Amoxicillin 500 mg three times a day was prescribed for five days.", "pharmacology", "easy");
        yield return V("ceftriaxone", "A third-generation cephalosporin antibiotic.", "Intravenous ceftriaxone 2 g once daily was commenced empirically.", "pharmacology", "medium");
        yield return V("ciprofloxacin", "A fluoroquinolone antibiotic.", "Ciprofloxacin was prescribed for a complicated urinary tract infection.", "pharmacology", "medium");
        yield return V("clarithromycin", "A macrolide antibiotic.", "Clarithromycin was trialled as an alternative in penicillin allergy.", "pharmacology", "medium");
        yield return V("metronidazole", "An antibiotic active against anaerobic bacteria and protozoa.", "Metronidazole was added to cover anaerobic organisms.", "pharmacology", "medium");
        yield return V("vancomycin", "A glycopeptide antibiotic for Gram-positive infections.", "Intravenous vancomycin required therapeutic drug monitoring.", "pharmacology", "hard");
        yield return V("salbutamol", "A short-acting beta-2 agonist bronchodilator.", "Nebulised salbutamol provided rapid relief of wheeze.", "pharmacology", "easy");
        yield return V("ipratropium", "A short-acting muscarinic antagonist used in airway disease.", "Nebulised ipratropium was added to salbutamol in severe exacerbations.", "pharmacology", "medium");
        yield return V("prednisolone", "An oral glucocorticoid used for inflammation suppression.", "Oral prednisolone was tapered over two weeks.", "pharmacology", "medium");
        yield return V("hydrocortisone", "A glucocorticoid available orally, topically, and intravenously.", "Intravenous hydrocortisone was administered for adrenal crisis.", "pharmacology", "medium");
        yield return V("levothyroxine", "A synthetic thyroid hormone replacement.", "Levothyroxine 50 micrograms once daily was commenced.", "pharmacology", "medium");
        yield return V("digoxin", "A cardiac glycoside used in atrial fibrillation and heart failure.", "Digoxin was prescribed for rate control alongside a beta-blocker.", "pharmacology", "hard");
        yield return V("furosemide", "A loop diuretic used in fluid overload.", "Intravenous furosemide rapidly improved the pulmonary oedema.", "pharmacology", "medium");
        yield return V("spironolactone", "A potassium-sparing diuretic with anti-aldosterone activity.", "Spironolactone was added for resistant hypertension and heart failure.", "pharmacology", "medium");
        yield return V("bisoprolol", "A cardioselective beta-blocker.", "Bisoprolol was titrated to a target resting heart rate.", "pharmacology", "medium");
        yield return V("ramipril", "An ACE inhibitor used for hypertension and heart failure.", "Ramipril was uptitrated to 10 mg once daily as tolerated.", "pharmacology", "medium");
        yield return V("amlodipine", "A long-acting calcium channel blocker.", "Amlodipine 5 mg daily was added for blood pressure control.", "pharmacology", "medium");
        yield return V("simvastatin", "A statin used to lower cholesterol.", "Simvastatin was switched to atorvastatin due to myalgia.", "pharmacology", "medium");
        yield return V("atorvastatin", "A potent statin for cholesterol lowering.", "Atorvastatin 20 mg at night was initiated.", "pharmacology", "medium");
        yield return V("omeprazole", "A proton pump inhibitor used in peptic ulcer disease and reflux.", "Omeprazole 20 mg once daily was prescribed before breakfast.", "pharmacology", "easy");
        yield return V("ranitidine", "A histamine H2 receptor antagonist (now largely withdrawn).", "Ranitidine had previously been used before its withdrawal.", "pharmacology", "hard");
        yield return V("morphine", "A strong opioid analgesic.", "Intravenous morphine was titrated carefully in the opioid-naive patient.", "pharmacology", "medium");
        yield return V("codeine", "A mild opioid analgesic.", "Codeine was avoided in breastfeeding women due to variable metabolism.", "pharmacology", "medium");
        yield return V("tramadol", "A moderate-strength opioid analgesic.", "Tramadol was trialled with caution due to serotonergic interactions.", "pharmacology", "medium");
        yield return V("sertraline", "An SSRI antidepressant.", "Sertraline 50 mg daily was up-titrated over six weeks.", "pharmacology", "medium");
        yield return V("citalopram", "An SSRI antidepressant with QT-interval caution.", "Citalopram was initiated after baseline ECG.", "pharmacology", "medium");
        yield return V("fluoxetine", "An SSRI antidepressant with a long half-life.", "Fluoxetine was favoured in adolescents with depression.", "pharmacology", "medium");
        yield return V("levetiracetam", "An anti-epileptic drug with minimal drug interactions.", "Levetiracetam was commenced following a second unprovoked seizure.", "pharmacology", "hard");
        yield return V("carbamazepine", "An anticonvulsant also used in neuropathic pain.", "Carbamazepine requires regular liver function and blood count monitoring.", "pharmacology", "hard");
        yield return V("gabapentin", "A medication used for neuropathic pain and partial seizures.", "Gabapentin was titrated slowly to avoid sedation.", "pharmacology", "medium");
        yield return V("lithium", "A mood stabiliser used in bipolar disorder.", "Lithium requires close monitoring of serum levels and renal function.", "pharmacology", "hard");
        yield return V("methotrexate", "An immunosuppressant used in inflammatory conditions and cancers.", "Oral methotrexate was prescribed once weekly with folic acid cover.", "pharmacology", "hard");
        yield return V("azathioprine", "An immunosuppressant used in autoimmune disease and transplant medicine.", "Azathioprine was introduced after TPMT testing.", "pharmacology", "hard");

        // Continue in the partial file SeedData.VocabularyBank.Part2.cs via
        // BuildOetVocabularyBank_Part2() appended at the end of this iterator.
        foreach (var t in BuildOetVocabularyBank_Part2(NextId)) yield return t;
    }

    private static async Task<bool> EnsureMissingOetVocabularyBankAsync(
        LearnerDbContext db,
        CancellationToken cancellationToken)
    {
        var existingTerms = await db.VocabularyTerms
            .Select(term => new
            {
                term.Id,
                term.Term,
                term.ExamTypeCode,
                term.ProfessionId,
            })
            .ToListAsync(cancellationToken);

        var existingIds = existingTerms
            .Select(term => term.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingKeys = existingTerms
            .Select(term => VocabularySeedKey(term.Term, term.ExamTypeCode, term.ProfessionId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingTerms = new List<VocabularyTerm>();
        foreach (var term in BuildOetVocabularyBank())
        {
            var key = VocabularySeedKey(term.Term, term.ExamTypeCode, term.ProfessionId);
            if (!existingIds.Add(term.Id) || !existingKeys.Add(key))
            {
                continue;
            }

            missingTerms.Add(term);
        }

        if (missingTerms.Count == 0)
        {
            return false;
        }

        db.VocabularyTerms.AddRange(missingTerms);
        return true;
    }

    private static string VocabularySeedKey(string term, string examTypeCode, string? professionId)
        => string.Join(
            '\u001f',
            term.Trim(),
            examTypeCode.Trim(),
            professionId?.Trim() ?? string.Empty);
}
