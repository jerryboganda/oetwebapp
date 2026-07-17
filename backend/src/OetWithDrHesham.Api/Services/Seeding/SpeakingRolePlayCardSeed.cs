using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Seeding;

// Phase 1 (G.10) of the OET Speaking module roadmap.
//
// Seeds 5-6 Nursing + 5-6 Medicine role-play cards via the new
// `RolePlayCard` + `InterlocutorScript` schema, each linked to a
// `ContentItem` with `SubtestCode = "speaking"`. All content here is
// ORIGINAL and written in the OET style; nothing is copied verbatim from
// the real Cambridge Boxhill / OET sample cards under
// `Project Real Content/Speaking_/`.
//
// Slug convention (also used as deterministic primary key suffix):
//   rpc-seed-{profession}-{nn}        for `RolePlayCard.Id`
//   is-seed-{profession}-{nn}         for `InterlocutorScript.Id`
//   ci-seed-speaking-{profession}-{nn} for the underlying `ContentItem.Id`
//
// The seeder is idempotent: it does a single existence probe (any
// `RolePlayCard` whose Id starts with `rpc-seed-`) and skips entirely on
// hit. This lets `Program.cs` call it on every startup without risk of
// duplicate inserts.
public static class SpeakingRolePlayCardSeed
{
    public const string SeedIdPrefix = "rpc-seed-";
    private const string SeederUserId = "system-speaking-seed";
    private const string CriteriaFocusJsonDefault = "[]";

    public static async Task SeedAsync(LearnerDbContext db, CancellationToken ct = default)
    {
        var alreadySeeded = await db.RolePlayCards
            .AsNoTracking()
            .AnyAsync(c => c.Id.StartsWith(SeedIdPrefix), ct);
        if (alreadySeeded)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var allCards = NursingCards().Concat(MedicineCards()).ToList();

        // Map each seeded card to a hidden card type by id. Only assign when the
        // target type row actually exists (FK-safe): if an admin has cleared the
        // seeded types, cards seed untyped rather than failing the whole seeder.
        var knownTypeIds = new HashSet<string>(
            await db.SpeakingCardTypes.AsNoTracking().Select(t => t.Id).ToListAsync(ct));

        foreach (var card in allCards)
        {
            var contentItemId = $"ci-seed-speaking-{card.ProfessionId}-{card.Slug}";
            var rolePlayCardId = $"rpc-seed-{card.ProfessionId}-{card.Slug}";
            var interlocutorScriptId = $"is-seed-{card.ProfessionId}-{card.Slug}";
            var seededTypeId = SpeakingCardTypeSeed.SeedId(CardTypeSlugFor(card));
            var cardTypeId = knownTypeIds.Contains(seededTypeId) ? seededTypeId : null;

            db.ContentItems.Add(new ContentItem
            {
                Id = contentItemId,
                ContentType = "speaking_roleplay",
                SubtestCode = "speaking",
                ProfessionId = card.ProfessionId,
                Title = card.ScenarioTitle,
                Difficulty = card.Difficulty,
                EstimatedDurationMinutes = 8,
                CriteriaFocusJson = JsonSupport.Serialize(card.CriteriaFocus),
                ScenarioType = "role_play",
                ModeSupportJson = JsonSupport.Serialize(new[] { "learning", "exam", "live_tutor" }),
                PublishedRevisionId = $"rev-seed-speaking-{card.ProfessionId}-{card.Slug}",
                Status = ContentStatus.Published,
                CaseNotes = card.Background,
                DetailJson = "{}",
                ModelAnswerJson = "{}",
                ExamFamilyCode = "oet",
                ExamTypeCode = "oet",
                DifficultyRating = card.Difficulty switch
                {
                    "extension" => 1700,
                    "exam" => 1900,
                    _ => 1500,
                },
                SourceType = "manual",
                SourceProvenance = "original",
                RightsStatus = "owned",
                QaStatus = "approved",
                FreshnessConfidence = "current",
                InstructionLanguage = "en",
                ContentLanguage = "en",
                CreatedBy = SeederUserId,
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now,
            });

            db.RolePlayCards.Add(new RolePlayCard
            {
                Id = rolePlayCardId,
                ContentItemId = contentItemId,
                ProfessionId = card.ProfessionId,
                CardTypeId = cardTypeId,
                ScenarioTitle = card.ScenarioTitle,
                Setting = card.Setting,
                CandidateRole = card.CandidateRole,
                InterlocutorRole = card.InterlocutorRole,
                PatientName = card.PatientName,
                PatientAge = card.PatientAge,
                Background = card.Background,
                Task1 = card.Tasks.ElementAtOrDefault(0),
                Task2 = card.Tasks.ElementAtOrDefault(1),
                Task3 = card.Tasks.ElementAtOrDefault(2),
                Task4 = card.Tasks.ElementAtOrDefault(3),
                Task5 = card.Tasks.ElementAtOrDefault(4),
                AllowedNotes = true,
                PrepTimeSeconds = 180,
                RolePlayTimeSeconds = 300,
                PatientEmotion = card.PatientEmotion,
                CommunicationGoal = card.CommunicationGoal,
                ClinicalTopic = card.ClinicalTopic,
                Difficulty = card.Difficulty,
                CriteriaFocusJson = JsonSupport.Serialize(card.CriteriaFocus),
                Disclaimer = "Practice estimate only. This is not an official OET score or result.",
                Status = ContentStatus.Published,
                IsLiveTutorEligible = true,
                CreatedByUserId = SeederUserId,
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now,
            });

            db.InterlocutorScripts.Add(new InterlocutorScript
            {
                Id = interlocutorScriptId,
                RolePlayCardId = rolePlayCardId,
                OpeningResponse = card.OpeningResponse,
                Prompt1 = card.Prompts.ElementAtOrDefault(0),
                Prompt2 = card.Prompts.ElementAtOrDefault(1),
                Prompt3 = card.Prompts.ElementAtOrDefault(2),
                HiddenInformation = card.HiddenInformation,
                ResistanceLevel = ResistanceLevels.Parse(card.ResistanceLevel),
                ClosingCue = card.ClosingCue,
                EmotionalState = card.EmotionalState,
                ProfessionRoleNotes = card.ProfessionRoleNotes,
                LayLanguageTriggersJson = JsonSupport.Serialize(card.LayLanguageTriggers),
                CreatedByUserId = SeederUserId,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    // Maps a seed card to one of the 6 hidden communication-function card types
    // (see SpeakingCardTypeSeed) using its communication goal, with a topic
    // override for breaking-bad-news. Keeps the seeded sample set spread across
    // every type so each type has live example content.
    private static string CardTypeSlugFor(SeedCardData card)
    {
        if (card.ClinicalTopic.Contains("breaking bad news", StringComparison.OrdinalIgnoreCase)
            || card.CommunicationGoal.Equals("BreakBadNews", StringComparison.OrdinalIgnoreCase))
        {
            return "bad-news";
        }

        return card.CommunicationGoal.Trim().ToLowerInvariant() switch
        {
            "inform" => "diagnosis",
            "reassure" => "reassurance",
            "negotiate" => "persuasion",
            "empower" => "health-education",
            "counsel" => "counselling",
            "advise" => "counselling",
            _ => "counselling",
        };
    }

    // ─── Nursing cards (6) ────────────────────────────────────────────────

    private static IEnumerable<SeedCardData> NursingCards() => new[]
    {
        // 1. Post-op discharge advice — core, post-op care
        new SeedCardData(
            Slug: "01",
            ProfessionId: "nursing",
            ScenarioTitle: "Discharge advice after laparoscopic appendectomy",
            Setting: "Surgical day-ward, late afternoon",
            CandidateRole: "Nurse",
            InterlocutorRole: "Patient",
            PatientName: "Mr Daniel Ortiz",
            PatientAge: "32",
            Background: "You are the staff nurse on a surgical day-ward. Your patient had a "
                + "laparoscopic appendectomy this morning, is awake, eating sips of water, and is "
                + "now medically cleared for discharge. The patient lives alone and works as a "
                + "long-haul truck driver. The discharge pack includes simple analgesia, a wound "
                + "review appointment in 7 days, and written instructions.",
            Tasks: new[]
            {
                "Find out how the patient is feeling now and check pain levels.",
                "Explain the discharge medication plan in plain language, including when and how often to take each medicine.",
                "Cover wound care: keeping dressings dry, what to look for, and when to seek urgent help.",
                "Address the patient's concerns about returning to driving and work.",
                "Confirm understanding and arrange follow-up.",
            },
            PatientEmotion: "anxious",
            CommunicationGoal: "Advise",
            ClinicalTopic: "post-operative recovery",
            Difficulty: "core",
            CriteriaFocus: new[] { "informationGiving", "patientPerspective", "appropriateness" },
            OpeningResponse: "I'm OK I think, but honestly I just want to get home — when can I go?",
            Prompts: new[]
            {
                "I have to be back behind the wheel by Monday, that's only three days — is that going to be a problem?",
                "The doctor mentioned painkillers, but I don't really like taking tablets. Can I just tough it out?",
                "What if the cuts start to look funny? I don't want to come all the way back here if it's nothing.",
            },
            HiddenInformation: "The patient has a long-haul shift booked for Monday (three days from now) "
                + "and is the sole earner for his elderly mother. He is reluctant to take opioid analgesia "
                + "because his brother had an addiction problem. Accept advice if reassured that the "
                + "prescribed simple analgesia is non-opioid and that one extra day off can be documented.",
            ResistanceLevel: "medium",
            ClosingCue: "Accepts the medication plan and agrees to delay driving once the nurse offers a "
                + "fit-note for one extra rest day.",
            EmotionalState: "Worried about money and time off work; mildly suspicious of strong painkillers",
            LayLanguageTriggers: new[] { "laparoscopic", "analgesia", "haematoma", "dehiscence" },
            ProfessionRoleNotes: null
        ),

        // 2. Medication adherence reminder — extension, polypharmacy
        new SeedCardData(
            Slug: "02",
            ProfessionId: "nursing",
            ScenarioTitle: "Medication review with an older adult on polypharmacy",
            Setting: "Community nursing home visit",
            CandidateRole: "Community Nurse",
            InterlocutorRole: "Patient",
            PatientName: "Mrs Eileen Park",
            PatientAge: "78",
            Background: "You are the community nurse visiting Mrs Park at home for a routine "
                + "medication review. She lives alone, takes eight regular medicines for "
                + "hypertension, heart failure, type 2 diabetes, and osteoarthritis. The "
                + "pharmacy's last refill record shows she is collecting only half the expected "
                + "amount of her diuretic. Her last BP reading at the GP surgery was elevated.",
            Tasks: new[]
            {
                "Open a conversation about how she manages her tablets each day.",
                "Sensitively explore whether she is taking all her medicines as prescribed.",
                "Explain in plain language why the 'water tablet' matters for her heart and breathing.",
                "Agree a practical plan that improves adherence without overwhelming her.",
                "Plan a brief follow-up check.",
            },
            PatientEmotion: "embarrassed",
            CommunicationGoal: "Negotiate",
            ClinicalTopic: "medication adherence",
            Difficulty: "extension",
            CriteriaFocus: new[] { "informationGathering", "relationshipBuilding", "patientPerspective" },
            OpeningResponse: "Oh, I take all my tablets, dear, don't you worry about that.",
            Prompts: new[]
            {
                "I just don't like the water one — it sends me running to the bathroom and at my age that's not very convenient.",
                "There are so many of them, I get muddled in the morning, but I always catch up later.",
                "If I miss one or two, it really won't matter, will it? I feel perfectly well.",
            },
            HiddenInformation: "She has been deliberately skipping the morning frusemide on days she "
                + "goes to her bridge club or her grandson's school pickup, because she fears wetting "
                + "herself in public. She has not told her GP. She would accept a plan that lets her "
                + "take the diuretic later in the morning or use a dosette box, provided the nurse "
                + "agrees not to alarm her family.",
            ResistanceLevel: "medium",
            ClosingCue: "Agrees to try a dosette box and to take the diuretic at a fixed time after "
                + "breakfast, once the nurse acknowledges her dignity concern and offers a private "
                + "follow-up call rather than involving the family unprompted.",
            EmotionalState: "Embarrassed about incontinence risk; protective of her independence",
            LayLanguageTriggers: new[] { "diuretic", "frusemide", "polypharmacy", "adherence" },
            ProfessionRoleNotes: null
        ),

        // 3. Wound care instructions — core, dressing care
        new SeedCardData(
            Slug: "03",
            ProfessionId: "nursing",
            ScenarioTitle: "Home wound-care teaching for a venous leg ulcer",
            Setting: "Community clinic dressing room",
            CandidateRole: "Practice Nurse",
            InterlocutorRole: "Patient",
            PatientName: "Mr Tomas Vargas",
            PatientAge: "64",
            Background: "You are the practice nurse. Mr Vargas attends for his fortnightly leg-ulcer "
                + "review. The ulcer is granulating well under compression bandaging. He wants to "
                + "learn how to manage simple dressing changes at home between clinic visits so he "
                + "can return to gardening. The wound is currently clean with no signs of infection.",
            Tasks: new[]
            {
                "Acknowledge his progress and ask about any concerns since the last visit.",
                "Demonstrate or explain the basic dressing-change technique in a way he can repeat at home.",
                "Outline signs of wound infection that should prompt a phone call to the clinic.",
                "Discuss compression hosiery care and skin hydration between dressings.",
                "Check understanding and arrange the next review.",
            },
            PatientEmotion: "hopeful",
            CommunicationGoal: "Empower",
            ClinicalTopic: "chronic wound care",
            Difficulty: "core",
            CriteriaFocus: new[] { "informationGiving", "structure", "intelligibility" },
            OpeningResponse: "It's looking better, isn't it? My wife says I'm finally allowed back in the garden if I'm careful.",
            Prompts: new[]
            {
                "Could you show me how to put a fresh dressing on at home? I'm not very confident with the tape.",
                "When should I worry — what does a 'bad' wound actually look like?",
                "Do I really have to wear these tight stockings forever? They're so hot in summer.",
            },
            HiddenInformation: "He has been removing the compression hosiery while watching TV in the "
                + "evenings because of itching. He plans to spend a whole weekend kneeling on damp soil "
                + "planting bulbs. He will accept guidance if given practical alternatives (cotton "
                + "liner, kneeling pad, scheduled rest breaks) rather than a blunt 'don't garden'.",
            ResistanceLevel: "low",
            ClosingCue: "Agrees to use a kneeling pad, keep compression on during gardening, and call "
                + "the clinic at the first sign of new redness or smell.",
            EmotionalState: "Encouraged by progress but secretly tempted to overdo the gardening",
            LayLanguageTriggers: new[] { "granulating", "venous", "compression hosiery", "exudate" },
            ProfessionRoleNotes: null
        ),

        // 4. Falls risk with elderly patient's relative — extension, falls
        new SeedCardData(
            Slug: "04",
            ProfessionId: "nursing",
            ScenarioTitle: "Falls-risk discussion with a worried daughter",
            Setting: "Older-adult inpatient ward, family room",
            CandidateRole: "Ward Nurse",
            InterlocutorRole: "Patient's daughter",
            PatientName: "Mrs Joan Whitford (patient, not in room)",
            PatientAge: "82",
            Background: "Mrs Whitford was admitted three days ago after a fall at home. She has "
                + "made a good recovery and is medically ready for discharge tomorrow. Her daughter "
                + "has asked to speak with you privately. The team has flagged Mrs Whitford as a "
                + "moderate falls risk; a community falls-prevention referral has been arranged.",
            Tasks: new[]
            {
                "Welcome the daughter and find out what is most concerning her.",
                "Explain the falls-risk assessment findings in plain language.",
                "Outline the discharge plan and the community supports being arranged.",
                "Address the daughter's request that her mother not be sent home.",
                "Agree practical next steps and offer a contact point.",
            },
            PatientEmotion: "anxious",
            CommunicationGoal: "Counsel",
            ClinicalTopic: "falls prevention",
            Difficulty: "extension",
            CriteriaFocus: new[] { "patientPerspective", "relationshipBuilding", "informationGiving" },
            OpeningResponse: "I'm really not happy about Mum going home tomorrow — what if she falls again?",
            Prompts: new[]
            {
                "She's so stubborn — she won't use the frame at home, I know it.",
                "Can't you keep her here just a few more days while we sort out a carer?",
                "What if she falls in the night? I can't be there every minute.",
            },
            HiddenInformation: "The daughter recently lost her father after a fall-related hip "
                + "fracture and is projecting that fear onto her mother. She works full-time and "
                + "feels guilty she cannot move in. She would accept the discharge if she can be "
                + "given a same-day phone number for the falls team, a follow-up call within 48 "
                + "hours, and clear written advice on home safety.",
            ResistanceLevel: "high",
            ClosingCue: "Agrees to the discharge once she has the falls-team direct number, a "
                + "scheduled 48-hour follow-up call, and an information leaflet for her mother.",
            EmotionalState: "Grieving recent bereavement, frightened of a repeat event, guilty about work commitments",
            LayLanguageTriggers: new[] { "syncope", "polypharmacy", "orthostatic hypotension", "Zimmer frame" },
            ProfessionRoleNotes: "Speak with the daughter, not the patient. Stay focused on her concerns rather than re-explaining the clinical history."
        ),

        // 5. Vaccination hesitancy — exam, immunisation
        new SeedCardData(
            Slug: "05",
            ProfessionId: "nursing",
            ScenarioTitle: "Childhood vaccination conversation with a hesitant parent",
            Setting: "GP practice immunisation clinic",
            CandidateRole: "Practice Nurse",
            InterlocutorRole: "Parent",
            PatientName: "Lily (8 months, daughter of the parent)",
            PatientAge: "8 months",
            Background: "You are the practice nurse running the routine 8-month immunisation "
                + "clinic. Lily is otherwise well. Her mother attended for the appointment but is "
                + "asking detailed questions about the vaccines, having read concerning posts in "
                + "an online parenting group. No vaccines have been refused before today; the "
                + "mother does want to do the right thing.",
            Tasks: new[]
            {
                "Welcome the parent and explore what specifically is worrying her.",
                "Listen without dismissing her concerns and acknowledge her motivation.",
                "Explain the benefits of today's vaccines and the most common short-term reactions in plain language.",
                "Address the specific worry she raises without overloading her with statistics.",
                "Agree a next step that respects her right to decide.",
            },
            PatientEmotion: "worried",
            CommunicationGoal: "Counsel",
            ClinicalTopic: "childhood immunisation",
            Difficulty: "exam",
            CriteriaFocus: new[] { "relationshipBuilding", "patientPerspective", "informationGiving" },
            OpeningResponse: "I want to be honest — I've read some things online and I'm not sure I'm comfortable with all of these today.",
            Prompts: new[]
            {
                "A mum in my group said her son was never the same after his jabs — how do I know that won't happen?",
                "If I just delay them for a few months, would that be safer?",
                "What actually goes into these things? I want to know what I'm putting in my baby.",
            },
            HiddenInformation: "She is leaning toward postponing rather than refusing outright. Her "
                + "main worry is a friend's child who was diagnosed with autism around the time of an "
                + "MMR vaccination — even though Lily's vaccine today is not MMR. She would accept "
                + "today's vaccines if her concerns are heard, the difference between today's vaccine "
                + "and MMR is explained, and she is offered a follow-up call to discuss MMR later.",
            ResistanceLevel: "high",
            ClosingCue: "Agrees to today's immunisations after the nurse acknowledges her fears, "
                + "explains which vaccine is given today (not MMR), and offers a follow-up appointment "
                + "for the MMR decision.",
            EmotionalState: "Conflicted between protective instinct and trust in the practice",
            LayLanguageTriggers: new[] { "immunogenicity", "MMR", "adjuvant", "anaphylaxis" },
            ProfessionRoleNotes: "Talk to the parent. Do not address Lily directly; she is a baby and not part of the dialogue."
        ),

        // 6. End-of-life comfort care — exam, palliative
        new SeedCardData(
            Slug: "06",
            ProfessionId: "nursing",
            ScenarioTitle: "Explaining comfort care to a relative at end of life",
            Setting: "Palliative-care side room, evening",
            CandidateRole: "Palliative Care Nurse",
            InterlocutorRole: "Patient's spouse",
            PatientName: "Mr Henry Lyons (patient, in bed and drowsy)",
            PatientAge: "71",
            Background: "Mr Lyons has metastatic pancreatic cancer and was transferred to your "
                + "palliative unit yesterday. The medical team has stopped active treatment and "
                + "moved him onto a comfort-care plan. His wife of 45 years has stepped outside "
                + "the room and asked you what 'comfort care' means and whether her husband is "
                + "now being 'left to die'.",
            Tasks: new[]
            {
                "Sit with the spouse and give her your full attention.",
                "Find out what she has understood from the medical team so far.",
                "Explain in plain language what comfort care does and does not involve.",
                "Address her concern that withdrawal of treatment is the same as withdrawal of care.",
                "Offer practical next steps and signpost ongoing support.",
            },
            PatientEmotion: "in pain",
            CommunicationGoal: "Reassure",
            ClinicalTopic: "palliative care",
            Difficulty: "exam",
            CriteriaFocus: new[] { "relationshipBuilding", "patientPerspective", "appropriateness" },
            OpeningResponse: "They said they're stopping treatment. Are you all just going to leave him to die?",
            Prompts: new[]
            {
                "If you don't give him the drip anymore, isn't he going to be hungry? Thirsty?",
                "He keeps moaning in his sleep — please tell me he isn't in pain.",
                "How will I know what to do when… when it's close?",
            },
            HiddenInformation: "She has been awake for two nights and has not eaten properly. The "
                + "couple's daughter is flying back from overseas tomorrow morning and the spouse is "
                + "terrified her husband will die before she arrives. She would accept the comfort-care "
                + "plan if she understands that pain and breathing will be actively managed, if the "
                + "team commits to keeping her informed, and if she is offered a quiet place to rest "
                + "nearby and a phone alert if his condition changes.",
            ResistanceLevel: "medium",
            ClosingCue: "Understands that comfort care is active care focused on dignity, accepts the "
                + "syringe driver plan, and agrees to take a short rest in the relatives' room with a "
                + "promise to be called immediately if anything changes.",
            EmotionalState: "Exhausted, frightened, anticipatory grief; needs to be heard before she can hear",
            LayLanguageTriggers: new[] { "syringe driver", "comfort care", "ANH (artificial nutrition and hydration)", "agonal breathing" },
            ProfessionRoleNotes: "Talk to the spouse. The patient is in the bed but not in the dialogue. Pace, silence and acknowledgement matter as much as content."
        ),
    };

    // ─── Medicine cards (6) ──────────────────────────────────────────────

    private static IEnumerable<SeedCardData> MedicineCards() => new[]
    {
        // 1. New Type 2 Diabetes diagnosis — core, diabetes diagnosis
        new SeedCardData(
            Slug: "01",
            ProfessionId: "medicine",
            ScenarioTitle: "Sharing a new diagnosis of Type 2 Diabetes",
            Setting: "GP clinic, scheduled review appointment",
            CandidateRole: "Doctor",
            InterlocutorRole: "Patient",
            PatientName: "Mr Aaron Bellamy",
            PatientAge: "52",
            Background: "Mr Bellamy attends to discuss recent blood-test results. His HbA1c is "
                + "consistent with a new diagnosis of Type 2 Diabetes. He has no current symptoms, "
                + "a BMI of 31, a stressful sales job, and a family history of diabetes (father, "
                + "grandmother). He is otherwise well and not on any regular medication.",
            Tasks: new[]
            {
                "Establish what the patient already knows and is expecting today.",
                "Share the diagnosis clearly and check his initial reaction.",
                "Outline the realistic short- and medium-term management plan in plain language.",
                "Address his immediate questions about lifestyle, medication and prognosis.",
                "Agree the next steps and arrange follow-up.",
            },
            PatientEmotion: "worried",
            CommunicationGoal: "Inform",
            ClinicalTopic: "type 2 diabetes diagnosis",
            Difficulty: "core",
            CriteriaFocus: new[] { "informationGiving", "structure", "patientPerspective" },
            OpeningResponse: "So, doctor, my wife sent me — she said the blood tests were back. Is it bad news?",
            Prompts: new[]
            {
                "Does this mean I have to start injecting myself with insulin now? My dad ended up on the needle.",
                "Will I have to give up everything I enjoy — beer, the lot?",
                "Honestly, can I just sort this out with the gym? I don't want tablets if I can avoid it.",
            },
            HiddenInformation: "He is genuinely frightened of needles and his image of diabetes is shaped "
                + "by watching his late father inject and develop foot complications. He is highly "
                + "motivated by his recently born first grandchild. He would accept a lifestyle-first "
                + "trial with a structured education programme if reassured that insulin is not the "
                + "starting point and that he will be reviewed regularly.",
            ResistanceLevel: "medium",
            ClosingCue: "Agrees to a 3-month lifestyle and metformin trial, a structured education "
                + "referral, and a 12-week follow-up, once reassured insulin is not imminent.",
            EmotionalState: "Frightened by family history, ashamed of weight, motivated by family",
            LayLanguageTriggers: new[] { "HbA1c", "metformin", "microvascular complications", "neuropathy" },
            ProfessionRoleNotes: null
        ),

        // 2. Febrile convulsion in toddler — extension, paediatrics
        new SeedCardData(
            Slug: "02",
            ProfessionId: "medicine",
            ScenarioTitle: "Counselling a parent after a first febrile convulsion",
            Setting: "Paediatric Emergency Department, after observation",
            CandidateRole: "Emergency Doctor",
            InterlocutorRole: "Parent",
            PatientName: "Sophie (3-year-old, asleep on parent's lap)",
            PatientAge: "3",
            Background: "Sophie was brought in this evening after a brief generalised seizure at "
                + "home during a viral illness. She has been observed for four hours, is now alert, "
                + "feeding and back to baseline. The diagnosis is a simple febrile convulsion. The "
                + "team is ready to discharge her with safety-net advice.",
            Tasks: new[]
            {
                "Explain in plain language what a febrile convulsion is and is not.",
                "Address the parent's fear that the child has epilepsy or brain damage.",
                "Give practical advice on managing future fevers and another seizure if it happens.",
                "Outline red-flag features that should prompt a 999 call.",
                "Agree follow-up and check understanding before discharge.",
            },
            PatientEmotion: "frightened",
            CommunicationGoal: "Reassure",
            ClinicalTopic: "paediatric febrile convulsion",
            Difficulty: "extension",
            CriteriaFocus: new[] { "patientPerspective", "informationGiving", "relationshipBuilding" },
            OpeningResponse: "I thought she was dying, doctor. I really did. Is she going to be epileptic now?",
            Prompts: new[]
            {
                "Should I be giving her paracetamol every time she so much as sneezes, to stop this happening again?",
                "What if it happens at nursery? Do I need to pull her out?",
                "My mother-in-law says we should never have let her get that hot — was this my fault?",
            },
            HiddenInformation: "The parent is sleep-deprived and carrying a heavy load of guilt fed by "
                + "well-meaning family. They have been searching online and read alarming content "
                + "about SUDEP. They will accept discharge if reassured that simple febrile convulsions "
                + "are common, usually benign, and not their fault, and if given a clear written "
                + "safety-net plan and a contact number for any worry overnight.",
            ResistanceLevel: "medium",
            ClosingCue: "Accepts discharge after the doctor names the parent's guilt explicitly, "
                + "explains that fever control does not prevent recurrence, and hands over a written "
                + "safety-net leaflet.",
            EmotionalState: "Shaken, guilty, sleep-deprived, primed by internet reading",
            LayLanguageTriggers: new[] { "febrile convulsion", "post-ictal", "epilepsy", "antipyretic" },
            ProfessionRoleNotes: "Address the parent. Do not direct any clinical instruction at the sleeping child."
        ),

        // 3. Chest pain investigation results — extension, cardiology workup
        new SeedCardData(
            Slug: "03",
            ProfessionId: "medicine",
            ScenarioTitle: "Discussing chest-pain investigation results",
            Setting: "Cardiology outpatient clinic, follow-up appointment",
            CandidateRole: "Cardiology Registrar",
            InterlocutorRole: "Patient",
            PatientName: "Ms Renata Kowalska",
            PatientAge: "47",
            Background: "Ms Kowalska presented six weeks ago with episodic exertional chest "
                + "tightness. She has completed an exercise tolerance test and a CT coronary "
                + "angiogram, both of which are reassuring with no significant coronary disease. "
                + "Symptoms are most consistent with musculoskeletal pain and gastro-oesophageal "
                + "reflux. She has been waiting anxiously for these results.",
            Tasks: new[]
            {
                "Greet her and check what she remembers of the tests so far.",
                "Share the results clearly and confirm there is no significant coronary disease.",
                "Explain the most likely cause of her symptoms in plain language.",
                "Acknowledge that the symptoms are real even though the tests are reassuring.",
                "Agree a practical plan and discharge criteria.",
            },
            PatientEmotion: "anxious",
            CommunicationGoal: "Reassure",
            ClinicalTopic: "non-cardiac chest pain workup",
            Difficulty: "extension",
            CriteriaFocus: new[] { "informationGiving", "patientPerspective", "appropriateness" },
            OpeningResponse: "Don't sugar-coat it, doctor — am I going to have a heart attack like my husband did?",
            Prompts: new[]
            {
                "But the pain is real — are you saying it's all in my head?",
                "How can you be so sure from a scan? My husband had a normal ECG two weeks before he died.",
                "What if it comes back? Do I just suffer at home, or do I call an ambulance every time?",
            },
            HiddenInformation: "Her husband died two years ago of a sudden cardiac event. She has been "
                + "avoiding exercise and sleeping poorly because of fear. She is carrying suppressed "
                + "grief. She would accept a non-cardiac plan if her grief and fear are acknowledged, "
                + "the limits of the tests are honestly named, and she is given a clear plan for what "
                + "to do if the pain changes character.",
            ResistanceLevel: "medium",
            ClosingCue: "Accepts the plan once the doctor acknowledges her husband's death, distinguishes "
                + "today's reassuring picture from sudden cardiac events, and gives a clear red-flag "
                + "list and a follow-up route.",
            EmotionalState: "Grief-laden, hyper-vigilant for cardiac symptoms, distrustful of reassurance",
            LayLanguageTriggers: new[] { "ischaemia", "CT coronary angiogram", "non-cardiac chest pain", "GORD" },
            ProfessionRoleNotes: null
        ),

        // 4. Antibiotic stewardship — exam, AMR
        new SeedCardData(
            Slug: "04",
            ProfessionId: "medicine",
            ScenarioTitle: "Declining an antibiotic request for a viral illness",
            Setting: "GP clinic, same-day appointment",
            CandidateRole: "GP",
            InterlocutorRole: "Patient",
            PatientName: "Mr Felix Hartwell",
            PatientAge: "39",
            Background: "Mr Hartwell attends with a three-day history of sore throat, runny "
                + "nose, low-grade fever and a productive cough. Examination is unremarkable: no "
                + "tonsillar exudate, clear chest, observations normal. The picture is consistent "
                + "with a self-limiting viral upper respiratory tract infection. He is asking for "
                + "'the strong antibiotics' he was given last winter.",
            Tasks: new[]
            {
                "Find out what he is most worried about and what he is hoping for today.",
                "Share your clinical findings and explain why this looks like a viral illness.",
                "Explain why antibiotics will not help in this situation in plain, respectful language.",
                "Offer a clear plan for symptom relief and safety-netting.",
                "Negotiate a path he can accept without leaving frustrated.",
            },
            PatientEmotion: "frustrated",
            CommunicationGoal: "Negotiate",
            ClinicalTopic: "antibiotic stewardship",
            Difficulty: "exam",
            CriteriaFocus: new[] { "appropriateness", "patientPerspective", "relationshipBuilding" },
            OpeningResponse: "Look, doctor, last year I was given antibiotics and I was back at work in two days — can we just do the same?",
            Prompts: new[]
            {
                "I've got a really important presentation on Friday. I can't afford to wait this out.",
                "If you're not going to give me anything, what was the point of me coming in?",
                "Fine — but if I'm worse in two days, I want a prescription waiting at reception, OK?",
            },
            HiddenInformation: "He runs his own small business and is the sole earner; missing work "
                + "feels existential to him. His belief that antibiotics 'cured' last winter's cold "
                + "is anchoring his expectation. He would accept a non-antibiotic plan if his work "
                + "pressure is acknowledged, the difference between viral and bacterial illness is "
                + "honestly explained, and he is offered a clear safety-net plan (including a "
                + "delayed-prescription option if the doctor judges it appropriate).",
            ResistanceLevel: "high",
            ClosingCue: "Accepts conservative management and a clear safety-net plan, optionally a "
                + "delayed prescription with explicit criteria, after the doctor names his work "
                + "pressure and refuses without dismissing him.",
            EmotionalState: "Frustrated, time-poor, anchored to a prior 'success' with antibiotics",
            LayLanguageTriggers: new[] { "viral URTI", "antimicrobial resistance", "delayed prescription", "tonsillar exudate" },
            ProfessionRoleNotes: null
        ),

        // 5. Lifestyle counselling for hypertension — core, BP management
        new SeedCardData(
            Slug: "05",
            ProfessionId: "medicine",
            ScenarioTitle: "Lifestyle counselling for newly identified hypertension",
            Setting: "GP clinic, routine follow-up",
            CandidateRole: "GP",
            InterlocutorRole: "Patient",
            PatientName: "Ms Priya Chandran",
            PatientAge: "44",
            Background: "Ms Chandran has had three elevated clinic blood-pressure readings and a "
                + "24-hour ambulatory monitor confirming Stage 1 hypertension. She has no other "
                + "cardiovascular risk factors today. She is hoping to address this with lifestyle "
                + "changes before starting medication. She works long hours in IT and reports high "
                + "stress and disrupted sleep.",
            Tasks: new[]
            {
                "Confirm the diagnosis in plain language and check her understanding.",
                "Find out what changes she feels are realistic for her in the next three months.",
                "Discuss the highest-yield lifestyle changes for her in particular.",
                "Address her preference to avoid starting medication today.",
                "Agree a measurable plan and a clear review point.",
            },
            PatientEmotion: "motivated",
            CommunicationGoal: "Empower",
            ClinicalTopic: "hypertension lifestyle management",
            Difficulty: "core",
            CriteriaFocus: new[] { "structure", "informationGiving", "patientPerspective" },
            OpeningResponse: "I've been reading about this — I really would prefer not to start any tablets if I don't have to.",
            Prompts: new[]
            {
                "What's actually the biggest thing I can change? I don't want to do everything badly.",
                "How long do I have to prove I can do this before you put me on medication?",
                "My job is the real problem — I can't really change that. What then?",
            },
            HiddenInformation: "Her father is on multiple antihypertensives and has had a TIA at age "
                + "62; she fears medication means she has joined his trajectory. She is genuinely "
                + "motivated and has been considering a structured exercise plan. She would accept a "
                + "3-month lifestyle trial with home BP monitoring and a clear threshold at which "
                + "medication would be reconsidered.",
            ResistanceLevel: "low",
            ClosingCue: "Agrees to a 3-month structured lifestyle plan with home BP monitoring and a "
                + "review point, with a clearly defined trigger for starting medication.",
            EmotionalState: "Motivated, slightly perfectionist, anxious about following in father's footsteps",
            LayLanguageTriggers: new[] { "ambulatory BP monitoring", "DASH diet", "antihypertensive", "TIA" },
            ProfessionRoleNotes: null
        ),

        // 6. Breaking bad news of cancer — exam, oncology
        new SeedCardData(
            Slug: "06",
            ProfessionId: "medicine",
            ScenarioTitle: "Sharing a new diagnosis of colorectal cancer",
            Setting: "Surgical outpatient clinic, dedicated quiet room",
            CandidateRole: "Surgical Registrar",
            InterlocutorRole: "Patient",
            PatientName: "Mrs Hannah Ward",
            PatientAge: "58",
            Background: "Mrs Ward underwent a colonoscopy two weeks ago after iron-deficiency "
                + "anaemia and a change in bowel habit. Biopsy results have confirmed adenocarcinoma "
                + "of the sigmoid colon. Staging investigations are pending. She has attended alone, "
                + "expecting 'just the biopsy result'. The multidisciplinary team will meet next "
                + "week.",
            Tasks: new[]
            {
                "Check what she is expecting and what support she has with her today.",
                "Share the diagnosis honestly, clearly, and without overload.",
                "Allow time for her reaction and respond to it.",
                "Outline the immediate next steps (staging, MDT, contact nurse) at a pace she can absorb.",
                "Close safely, ensuring she does not leave alone or unsupported.",
            },
            PatientEmotion: "shocked",
            CommunicationGoal: "Counsel",
            ClinicalTopic: "breaking bad news (oncology)",
            Difficulty: "exam",
            CriteriaFocus: new[] { "appropriateness", "relationshipBuilding", "patientPerspective" },
            OpeningResponse: "I thought I was just popping in for a quick result, doctor. You're being very quiet — is it bad?",
            Prompts: new[]
            {
                "Am I going to die? Please just tell me straight.",
                "How am I supposed to tell my children? They've already lost their dad.",
                "What happens now — do I need surgery? Chemo? When?",
            },
            HiddenInformation: "Her husband died of metastatic disease three years ago and her two "
                + "adult children are abroad. She is a recently retired teacher who 'researches "
                + "everything' and may default to clinical questions to avoid emotion. She would "
                + "accept the staging plan if the news is delivered honestly, her reaction is met "
                + "without rushing, the team's structure is named, and she is offered an explicit "
                + "support pathway including a CNS contact and someone to call her in 24 hours.",
            ResistanceLevel: "low",
            ClosingCue: "Understands the immediate plan, has the CNS contact card in her hand, agrees "
                + "to a 24-hour check-in call, and has identified a friend or relative to be told today.",
            EmotionalState: "Stunned, intermittently composed and tearful, prone to defaulting to clinical questions to avoid feelings",
            LayLanguageTriggers: new[] { "adenocarcinoma", "staging", "MDT", "neoadjuvant therapy" },
            ProfessionRoleNotes: "Pace and silence are part of the assessment. Do not move to the plan until the diagnosis has landed."
        ),
    };
}

// Internal data transfer object describing one seed role-play card and its
// linked interlocutor script. Lives in this file because nothing else in
// the codebase consumes it.
internal sealed record SeedCardData(
    string Slug,
    string ProfessionId,
    string ScenarioTitle,
    string Setting,
    string CandidateRole,
    string InterlocutorRole,
    string? PatientName,
    string? PatientAge,
    string Background,
    string[] Tasks,
    string PatientEmotion,
    string CommunicationGoal,
    string ClinicalTopic,
    string Difficulty,
    string[] CriteriaFocus,
    string OpeningResponse,
    string[] Prompts,
    string HiddenInformation,
    string ResistanceLevel,
    string ClosingCue,
    string EmotionalState,
    string[] LayLanguageTriggers,
    string? ProfessionRoleNotes
);
