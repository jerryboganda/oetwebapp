using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Seeds CMS-authored conversation templates. Covers the two canonical
/// OET Speaking task types (<c>oet-roleplay</c> and <c>oet-handover</c>)
/// across the most common healthcare professions. Idempotent: rows are only
/// inserted if <c>ConversationTemplates</c> is empty.
/// </summary>
public static class ConversationSeedData
{
    public static async Task EnsureAsync(LearnerDbContext db, CancellationToken ct = default)
    {
        if (await db.ConversationTemplates.AnyAsync(ct)) return;
        var now = DateTimeOffset.UtcNow;
        var seed = BuildSeed(now);
        db.ConversationTemplates.AddRange(seed);
        await db.SaveChangesAsync(ct);
    }

    private static IEnumerable<ConversationTemplate> BuildSeed(DateTimeOffset now)
    {
        int idx = 0;

        ConversationTemplate T(
            string taskType, string profession, string difficulty, string title,
            string patientContext, string roleDescription, string scenario, string expectedOutcomes,
            string[] objectives, string[] redFlags, string[] keyVocab,
            Dictionary<string, object?> voice, int durationSeconds = 300)
            => new()
            {
                Id = $"CVT-SEED-{++idx:D3}",
                Title = title,
                TaskTypeCode = taskType,
                ProfessionId = profession,
                Scenario = scenario,
                RoleDescription = roleDescription,
                PatientContext = patientContext,
                ExpectedOutcomes = expectedOutcomes,
                ObjectivesJson = JsonSupport.Serialize(objectives),
                ExpectedRedFlagsJson = JsonSupport.Serialize(redFlags),
                KeyVocabularyJson = JsonSupport.Serialize(keyVocab),
                PatientVoiceJson = JsonSupport.Serialize(voice),
                Difficulty = difficulty,
                EstimatedDurationSeconds = durationSeconds,
                Status = "published",
                PublishedAtUtc = now,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedByUserId = "seed",
                UpdatedByUserId = "seed",
            };

        // ── Medicine — Role plays ──────────────────────────────────────────
        yield return T(
            "oet-roleplay", "medicine", "medium",
            "Post-operative Hip Replacement — Discharge Counselling",
            "GP clinic, 68-year-old Mr James Wheeler 5 days after elective right total hip replacement.",
            "You are the GP. The patient is attending for discharge review and is anxious about resuming activities.",
            "Mr Wheeler has had an uncomplicated right THR. He is mobilising with a frame and taking prescribed analgesia. He is worried about driving, stairs and showering.",
            "Reassure, provide clear post-op advice, arrange follow-up and safety-net.",
            new[]
            {
                "Greet the patient warmly and confirm identity.",
                "Elicit ideas, concerns and expectations.",
                "Explain safe mobilisation, driving, and bathing limits in plain English.",
                "Address analgesia plan and warn about red-flag symptoms.",
                "Offer written information and close with a safety-net.",
            },
            new[] { "sudden calf pain or swelling (DVT)", "fever or wound discharge", "hip dislocation symptoms" },
            new[] { "analgesia", "mobilisation", "weight-bearing", "thromboprophylaxis", "wound healing" },
            new Dictionary<string, object?> { ["gender"] = "male", ["age"] = 68, ["accent"] = "en-GB", ["tone"] = "worried" });

        yield return T(
            "oet-roleplay", "medicine", "medium",
            "Type 2 Diabetes — Lifestyle Counselling",
            "Primary-care consultation, 52-year-old Mrs Amara Okafor newly diagnosed with T2DM.",
            "You are the GP. Counsel the patient on lifestyle changes and starting metformin.",
            "HbA1c 8.4%. BMI 31. Strong family history. Patient is skeptical about medication and prefers to try lifestyle first.",
            "Negotiate a shared plan that includes lifestyle + pharmacotherapy.",
            new[]
            {
                "Elicit patient's views about diabetes and medication.",
                "Explain diabetes and the role of metformin in plain English.",
                "Agree practical lifestyle goals (diet, activity, weight).",
                "Address side-effect concerns and adherence.",
                "Arrange follow-up and provide safety-netting.",
            },
            new[] { "symptoms of hyperglycaemia", "ketoacidosis warning signs", "hypoglycaemia awareness" },
            new[] { "HbA1c", "metformin", "glycaemic control", "carbohydrate counting" },
            new Dictionary<string, object?> { ["gender"] = "female", ["age"] = 52, ["accent"] = "en-GB", ["tone"] = "skeptical" });

        yield return T(
            "oet-roleplay", "medicine", "hard",
            "Breaking Bad News — Abnormal Mammogram",
            "Breast clinic, 46-year-old Ms Sarah Chen recalled after screening mammogram.",
            "You are the breast surgeon. You need to share that the mammogram has shown a suspicious lesion requiring biopsy.",
            "The patient is alone, very anxious. Family history of breast cancer. The lesion requires core biopsy — not yet a diagnosis of cancer.",
            "Deliver the news with empathy using SPIKES, agree next steps, and provide support.",
            new[]
            {
                "Set up the conversation (private, unhurried).",
                "Elicit what the patient already understands.",
                "Give information in chunks with warning shot.",
                "Acknowledge and respond to emotion before facts.",
                "Agree a clear next step (biopsy, follow-up) and safety-net.",
            },
            new[] { "lump that changes size", "nipple discharge/inversion", "skin changes" },
            new[] { "mammogram", "core biopsy", "triple assessment", "lesion" },
            new Dictionary<string, object?> { ["gender"] = "female", ["age"] = 46, ["accent"] = "en-GB", ["tone"] = "worried" });

        yield return T(
            "oet-roleplay", "medicine", "easy",
            "Medication Counselling — Starting Statin",
            "GP clinic, 59-year-old Mr David Thompson with raised cardiovascular risk.",
            "You are the GP. Counsel the patient on starting atorvastatin 20 mg once daily.",
            "QRisk 15%. No contraindications. Patient read online articles about statin side-effects and is hesitant.",
            "Address myths, explain benefits/side-effects, agree a trial period.",
            new[]
            {
                "Elicit concerns about statins.",
                "Explain cardiovascular risk and how statins reduce it.",
                "Discuss common and serious side-effects plainly.",
                "Agree a review date and safety-net for muscle symptoms.",
            },
            new[] { "unexplained muscle pain", "dark urine (rhabdomyolysis)", "jaundice" },
            new[] { "cardiovascular risk", "LDL cholesterol", "myopathy", "hepatic function" },
            new Dictionary<string, object?> { ["gender"] = "male", ["age"] = 59, ["accent"] = "en-GB", ["tone"] = "neutral" });

        yield return T(
            "oet-roleplay", "medicine", "medium",
            "Informed Consent — Colonoscopy",
            "Gastro clinic, 62-year-old Mrs Patricia Okuma referred for rectal bleeding.",
            "You are the gastroenterologist. Consent the patient for an outpatient colonoscopy.",
            "Clinical picture suggests colonoscopy indicated. Patient is anxious about sedation and the procedure.",
            "Obtain informed consent covering indication, risks, benefits, alternatives.",
            new[]
            {
                "Confirm understanding of referral reason.",
                "Explain procedure in simple language.",
                "Discuss risks (perforation, bleeding, sedation) and benefits.",
                "Cover alternatives and what happens if she declines.",
                "Ensure she understands and can ask questions.",
            },
            new[] { "post-procedure abdominal pain", "rectal bleeding >24h", "fever" },
            new[] { "bowel preparation", "sedation", "perforation risk", "polypectomy" },
            new Dictionary<string, object?> { ["gender"] = "female", ["age"] = 62, ["accent"] = "en-GB", ["tone"] = "worried" });

        // ── Medicine — Handovers ───────────────────────────────────────────
        yield return T(
            "oet-handover", "medicine", "medium",
            "ED Night → Day Handover: Chest Pain Awaiting Troponin",
            "Emergency Department — 07:30 handover.",
            "You are the outgoing night SHO handing over a patient to the day team.",
            "Mrs Sarah Chen, 45, came in at 04:00 with central chest pain. ECG non-specific. First troponin pending. On 2L oxygen, obs stable. Family history of IHD.",
            "Deliver a structured ISBAR handover with a clear ask.",
            new[]
            {
                "Identify yourself and the patient clearly.",
                "State the situation in one sentence.",
                "Summarise relevant background.",
                "Give your clinical assessment.",
                "Make a specific recommendation to the receiving team.",
            },
            new[] { "rising troponin", "recurrent chest pain", "haemodynamic instability" },
            new[] { "ISBAR", "troponin", "ST-elevation", "reperfusion" },
            new Dictionary<string, object?> { ["gender"] = "female", ["age"] = 35, ["accent"] = "en-GB", ["tone"] = "neutral" });

        yield return T(
            "oet-handover", "medicine", "hard",
            "Ward Handover: Post-op Sepsis Risk",
            "Surgical ward — shift handover.",
            "You are the outgoing registrar. Handover to incoming on-call.",
            "Mr Khaled Ahmed, 71, day 2 post laparoscopic cholecystectomy. NEWS 6 at 18:00 (temp 38.4, HR 108). Started IV co-amoxiclav. Bloods sent.",
            "Deliver a safe, prioritised handover with explicit escalation triggers.",
            new[]
            {
                "Lead with ISBAR framework.",
                "Highlight active problem (suspected sepsis).",
                "State obs trend and investigations pending.",
                "Give clear escalation triggers (repeat NEWS threshold).",
                "Invite questions.",
            },
            new[] { "rising NEWS", "lactate >2", "signs of septic shock" },
            new[] { "sepsis-6", "blood cultures", "lactate", "escalation" },
            new Dictionary<string, object?> { ["gender"] = "male", ["age"] = 40, ["accent"] = "en-GB", ["tone"] = "neutral" });

        // ── Nursing — Role plays ───────────────────────────────────────────
        yield return T(
            "oet-roleplay", "nursing", "medium",
            "Falls Prevention — Elderly Inpatient",
            "Geriatric ward, 82-year-old Mrs Eileen O'Hara recovering from UTI.",
            "You are the staff nurse. Discuss falls prevention before discharge planning.",
            "Patient has had two falls in the past year. Lives alone. Takes zopiclone at night.",
            "Agree a practical falls-prevention plan with the patient and family.",
            new[]
            {
                "Elicit her fears and what she can already manage.",
                "Explain what we know about her falls risk in simple language.",
                "Review medications with the patient.",
                "Suggest home adaptations and community support.",
                "Safety-net for red flags.",
            },
            new[] { "loss of consciousness with falls", "new confusion", "injurious falls" },
            new[] { "polypharmacy", "orthostatic hypotension", "mobility aid" },
            new Dictionary<string, object?> { ["gender"] = "female", ["age"] = 82, ["accent"] = "en-GB", ["tone"] = "calm" });

        yield return T(
            "oet-roleplay", "nursing", "easy",
            "Vaccination Counselling — Anxious Parent",
            "Paediatric clinic, 8-week-old baby with first-time mother.",
            "You are the nurse. Address the mother's concerns before scheduled vaccinations.",
            "The mother has read online that vaccines cause autism and wants to delay.",
            "Provide evidence-based reassurance and agree a way forward.",
            new[]
            {
                "Elicit the mother's specific concerns.",
                "Acknowledge her worry before giving information.",
                "Explain benefits of vaccination simply.",
                "Address common myths without being dismissive.",
                "Agree a plan that keeps the baby protected.",
            },
            new[] { "signs of systemic illness post vaccine", "persistent high fever" },
            new[] { "herd immunity", "immunisation schedule", "antigen" },
            new Dictionary<string, object?> { ["gender"] = "female", ["age"] = 29, ["accent"] = "en-GB", ["tone"] = "worried" });

        // ── Nursing — Handovers ────────────────────────────────────────────
        yield return T(
            "oet-handover", "nursing", "medium",
            "Nursing Handover — Acute Confusion Post-op",
            "Surgical ward — nursing shift change.",
            "You are the outgoing nurse handing over Mr Peter Lawson (Bay 4, Bed 2).",
            "Day 1 post hip fracture repair. New confusion since 02:00. Pulled out IV line. Family contacted.",
            "Deliver an ISBAR nursing handover highlighting safety priorities.",
            new[]
            {
                "Identify yourself and patient, location.",
                "State current situation.",
                "Summarise background (admission, op, meds).",
                "Give your nursing assessment.",
                "Recommend enhanced observation and delirium screen.",
            },
            new[] { "desaturation", "aggression", "missed analgesia" },
            new[] { "delirium screen", "1:1 enhanced care", "ISBAR" },
            new Dictionary<string, object?> { ["gender"] = "female", ["age"] = 35, ["accent"] = "en-GB", ["tone"] = "neutral" });

        // ── Pharmacy — Role plays ──────────────────────────────────────────
        yield return T(
            "oet-roleplay", "pharmacy", "medium",
            "Warfarin Counselling — New Start",
            "Community pharmacy consultation room.",
            "You are the pharmacist counselling 67-year-old Mr Harold Kim newly prescribed warfarin for AF.",
            "Patient takes several OTC supplements and enjoys a daily beer.",
            "Counsel safely on warfarin use, interactions, monitoring and red flags.",
            new[]
            {
                "Confirm indication and patient understanding.",
                "Explain INR monitoring and the yellow book.",
                "Discuss key interactions (alcohol, supplements, diet).",
                "Cover bleeding precautions and red flags.",
                "Offer written information and follow-up.",
            },
            new[] { "unexplained bruising", "blood in urine/stool", "severe headache" },
            new[] { "INR", "target range", "vitamin K", "bridging therapy" },
            new Dictionary<string, object?> { ["gender"] = "male", ["age"] = 67, ["accent"] = "en-GB", ["tone"] = "neutral" });

        yield return T(
            "oet-roleplay", "pharmacy", "easy",
            "Asthma Inhaler Technique — Review",
            "Community pharmacy, 24-year-old Ms Fatima Yusuf with poorly controlled asthma.",
            "You are the pharmacist. Review her inhaler technique and adherence.",
            "She uses the salbutamol inhaler ≥3x/week. Skips her preventer because 'it's not doing anything'.",
            "Identify adherence gaps, correct technique, reinforce preventer.",
            new[]
            {
                "Elicit current symptom pattern and belief about meds.",
                "Demonstrate correct technique.",
                "Explain the role of the preventer.",
                "Agree a short-term review plan.",
                "Safety-net for exacerbation.",
            },
            new[] { "nocturnal symptoms", "peak flow drop", "reliever overuse" },
            new[] { "spacer", "ICS", "MART", "asthma action plan" },
            new Dictionary<string, object?> { ["gender"] = "female", ["age"] = 24, ["accent"] = "en-GB", ["tone"] = "skeptical" });

        // ── Physiotherapy — Role plays ─────────────────────────────────────
        yield return T(
            "oet-roleplay", "physiotherapy", "medium",
            "Post-ACL Reconstruction Rehab Plan",
            "Outpatient physio clinic, 32-year-old Ms Emily Watson, 6 weeks post right ACL reconstruction.",
            "You are the physiotherapist. Assess progress and agree the next rehab stage.",
            "Range of motion improving. Quads weakness. Patient keen to return to football.",
            "Set realistic rehab goals, tailor exercises, manage expectations.",
            new[]
            {
                "Assess current range, strength and pain.",
                "Explain stages of ACL rehab in plain English.",
                "Negotiate return-to-sport timeline.",
                "Prescribe exercise progression.",
                "Safety-net for swelling/instability.",
            },
            new[] { "significant new swelling", "giving way", "fever" },
            new[] { "proprioception", "closed-chain exercise", "plyometrics" },
            new Dictionary<string, object?> { ["gender"] = "female", ["age"] = 32, ["accent"] = "en-GB", ["tone"] = "neutral" });

        // ── Dentistry — Role plays ─────────────────────────────────────────
        yield return T(
            "oet-roleplay", "dentistry", "medium",
            "Root Canal — Informed Consent",
            "Dental practice consultation, 45-year-old Mr John Hayes with irreversible pulpitis.",
            "You are the dentist. Obtain informed consent for root canal treatment.",
            "Patient asks for extraction instead because 'it's cheaper'. He works night shifts.",
            "Present options, risks and benefits; respect autonomy.",
            new[]
            {
                "Explain diagnosis in plain English.",
                "Present options (RCT, extraction, no treatment) with pros/cons.",
                "Discuss risks and recovery time.",
                "Address cost and work schedule concerns.",
                "Obtain documented consent and agree next step.",
            },
            new[] { "increasing pain", "facial swelling", "systemic symptoms (fever)" },
            new[] { "pulpitis", "irrigation", "obturation", "extraction" },
            new Dictionary<string, object?> { ["gender"] = "male", ["age"] = 45, ["accent"] = "en-GB", ["tone"] = "neutral" });
    }
}
