using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// The parser recovers Part B/C stems and options from a paper's own
/// question-paper text. Its contract is precision over recall: a candidate must
/// never be shown a stem that belongs to a different item, so every fixture
/// here that the extractor renders ambiguously has to be REPORTED, not guessed.
///
/// The fixtures are the shapes real PDF text extraction produces for an OET
/// Listening question paper — options collapsed onto one line, a SAMPLE
/// watermark landing mid-sentence, running page headers between items, and two
/// printed items interleaved by a watermark column.
/// </summary>
public class ListeningPartBCSourceParserTests
{
    // Part B as a clean extractor renders it: context sentence, question line,
    // then the three options run together on one line.
    private const string CleanPartBText = """
        Part B

        For questions 25-30, choose the answer (A, B or C) which fits best according to what you hear.

        27. You hear the beginning of a training session for nurses about to start work on a paediatric ward. What is the focus of today's session?
        A comparing equipment used with patients of different ages B gaining an awareness of how some equipment is used C learning how best to organise some equipment

        [CANDIDATE NO.] LISTENING QUESTION PAPER 06/12

        28. You hear an occupational therapist briefing a trainee about a home visit that he's going to observe her making. What is the priority for today's visit?
        A helping the patient to regain independence in everyday tasks B meeting a family member who has concerns about the patient C ensuring that a mechanical device is appropriate for the patient
        """;

    // Part C with each option on its own line, and a SAMPLE watermark dropped
    // between two options — the exact interference seen in the seeded papers.
    private const string CleanPartCText = """
        Extract 1: Questions 31-36 You hear Dr Pietro Everall giving a presentation on the subject of cholesterol.
        SAMPLE You now have 90 seconds to read questions 31-36.
        31. Dr Everall thinks misunderstandings about the role of cholesterol largely arise due
        A an imprecise use of the term in the media. B inadequate explanations by health professionals. C a lack of focus on its positive influences in research studies.
        35. What does Dr Everall say about the drug called Inclisiran?
        A Its use could lead to considerable cost savings.
        SAMPLE B Patients are likely to tolerate it better than existing options.
        C Further research is needed to establish its full range of possible uses.
        """;

    // Verbatim from the production Atlas Sample Test 1 question paper: two
    // printed items interleaved by the watermark column. Q29's context line
    // sits above a bare "30." anchor, and BOTH option sets follow it.
    private const string InterleavedText = """
        29. You hear a trainee doctor telling his supervisor about a problem he had carrying out a procedure.

        30.

        SAMPLE The trainee feels the cause of the problem was
        A treatment administered previously. B the patient's negative reaction. C inappropriate equipment.
        You hear a doctor talking to a teenage boy who has a painful wrist. The doctor wants to establish whether A a fracture may be misaligned. B the swelling may be due to a sprain.

        C there may be more than one bone affected.
        """;

    [Fact]
    public void Recovers_part_b_stem_and_options_verbatim()
    {
        var result = ListeningPartBCSourceParser.Parse(CleanPartBText, [27]);

        var item = Assert.Single(result.Items);
        Assert.Equal(27, item.Number);
        Assert.Equal(
            "You hear the beginning of a training session for nurses about to start work on a paediatric ward. What is the focus of today's session?",
            item.Stem);
        Assert.Equal("comparing equipment used with patients of different ages", item.OptionA);
        Assert.Equal("gaining an awareness of how some equipment is used", item.OptionB);
        Assert.Equal("learning how best to organise some equipment", item.OptionC);
    }

    [Fact]
    public void Stops_each_item_at_the_next_printed_number()
    {
        var result = ListeningPartBCSourceParser.Parse(CleanPartBText, [27, 28]);

        Assert.Equal(2, result.Items.Count);
        var q27 = result.Items.Single(item => item.Number == 27);
        var q28 = result.Items.Single(item => item.Number == 28);

        // Q27's last option must not absorb the running page header or any of
        // Q28's printed text.
        Assert.Equal("learning how best to organise some equipment", q27.OptionC);
        Assert.DoesNotContain("CANDIDATE", q27.OptionC, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("occupational therapist", q27.OptionC, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("You hear an occupational therapist", q28.Stem, StringComparison.Ordinal);
    }

    [Fact]
    public void Strips_the_sample_watermark_and_timing_instructions_from_recovered_text()
    {
        var result = ListeningPartBCSourceParser.Parse(CleanPartCText, [31, 35]);

        Assert.Equal(2, result.Items.Count);
        foreach (var item in result.Items)
        {
            Assert.DoesNotContain("SAMPLE", item.Stem, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("You now have", item.Stem, StringComparison.OrdinalIgnoreCase);
            foreach (var option in item.Options)
            {
                Assert.DoesNotContain("SAMPLE", option, StringComparison.OrdinalIgnoreCase);
            }
        }

        var q31 = result.Items.Single(item => item.Number == 31);
        Assert.Equal(
            "Dr Everall thinks misunderstandings about the role of cholesterol largely arise due",
            q31.Stem);
        Assert.Equal("an imprecise use of the term in the media.", q31.OptionA);

        var q35 = result.Items.Single(item => item.Number == 35);
        Assert.Equal("What does Dr Everall say about the drug called Inclisiran?", q35.Stem);
        Assert.Equal("Patients are likely to tolerate it better than existing options.", q35.OptionB);
    }

    [Fact]
    public void Decodes_the_sample_watermark_interleave_to_the_right_items()
    {
        // The watermark column makes the extractor emit Q29's context line, a
        // bare "30.", then Q29's question + options followed by Q30's.
        // Attribution is fixed by order: the FIRST option set belongs to the
        // LOWER number, because its stem runs into the block.
        var result = ListeningPartBCSourceParser.Parse(InterleavedText, [29, 30]);

        Assert.Empty(result.Skipped);
        Assert.Equal(2, result.Items.Count);

        var q29 = result.Items.Single(item => item.Number == 29);
        Assert.Equal(
            "You hear a trainee doctor telling his supervisor about a problem he had carrying out a procedure. The trainee feels the cause of the problem was",
            q29.Stem);
        Assert.Equal("treatment administered previously.", q29.OptionA);
        Assert.Equal("the patient's negative reaction.", q29.OptionB);
        Assert.Equal("inappropriate equipment.", q29.OptionC);

        var q30 = result.Items.Single(item => item.Number == 30);
        Assert.Equal(
            "You hear a doctor talking to a teenage boy who has a painful wrist. The doctor wants to establish whether",
            q30.Stem);
        Assert.Equal("a fracture may be misaligned.", q30.OptionA);
        Assert.Equal("the swelling may be due to a sprain.", q30.OptionB);
        Assert.Equal("there may be more than one bone affected.", q30.OptionC);
    }

    [Fact]
    public void Reads_the_nova_bullet_layout_with_no_full_stop_and_glued_option_letters()
    {
        // Verbatim from a production Nova question paper: the number carries no
        // full stop and each option letter is glued to its text behind a bullet.
        const string text = """
            Practice Test 11 : Part B - Q(25-30) Answersheet
             25 You hear part of an announcement in a ward. Why is the announcement being given at this time? o AIt is a routine announcement given before every briefing.
            o BIt is being given because of an accident in the hospital.
            o CIt is being because of the rise of a certain infectious disease.
             26 You hear part of a training for nurses on medication errors. What is the overall topic of the training? o ABeing able to explain the causes of most medication errors
            o BBeing able to acquire knowledge and skill in managing errors
            o CBeing able to demonstrate how to investigate medication errors
            """;

        var result = ListeningPartBCSourceParser.Parse(text, [25, 26]);

        Assert.Equal(2, result.Items.Count);
        var q25 = result.Items.Single(i => i.Number == 25);
        Assert.Equal(
            "You hear part of an announcement in a ward. Why is the announcement being given at this time?",
            q25.Stem);
        Assert.Equal("It is a routine announcement given before every briefing.", q25.OptionA);
        Assert.Equal("It is being given because of an accident in the hospital.", q25.OptionB);
        Assert.Equal("It is being because of the rise of a certain infectious disease.", q25.OptionC);
    }

    [Fact]
    public void Reads_the_kaplan_parenthesised_option_layout()
    {
        // Verbatim from the production Kaplan question paper.
        const string text = """
            PART B: QUESTIONS 25 TO 30
            25. You hear two doctors discuss the transfer of care for a patient. The patient's CURB-65 score means that he will (A) be transferred from the Emergency Department. (B) receive additional medication and treatment. (C) be treated as an out-patient.
            26. You hear a speech pathologist talking to the wife of a patient. What does she want to know? (A) how long recovery takes (B) whether communication improves (C) how to speed healing
            """;

        var result = ListeningPartBCSourceParser.Parse(text, [25]);

        var q25 = Assert.Single(result.Items);
        Assert.Equal(
            "You hear two doctors discuss the transfer of care for a patient. The patient's CURB-65 score means that he will",
            q25.Stem);
        Assert.Equal("be transferred from the Emergency Department.", q25.OptionA);
        Assert.Equal("be treated as an out-patient.", q25.OptionC);
    }

    [Fact]
    public void Maps_section_relative_numbering_onto_the_canonical_printed_numbers()
    {
        // Some papers restart numbering per section instead of printing 25-42.
        // Part B item 1 is printed Q25; Part C extract 2 item 1 is printed Q37.
        const string text = """
            E2 language Listening Part B.3
            1. You hear a doctor and a nurse reviewing a coma patient. What is the Doctor checking for? A The symptoms the patient is exhibiting B The severity of the patient's coma C The range of mobility of the patient
            E2 Language Part C.3 Extract 1
            1. What is the stated purpose of the talk? A To evaluate if the audience are feeling burnout B To inform and stimulate discussion about burnout C To describe new research on treatment of burnout
            Extract 2
            1. What does the speaker recommend for new staff? A regular supervision B shorter shifts C peer mentoring
            """;

        var result = ListeningPartBCSourceParser.Parse(text, [25, 31, 37]);

        Assert.Equal(3, result.Items.Count);
        Assert.StartsWith("You hear a doctor and a nurse reviewing a coma patient",
            result.Items.Single(i => i.Number == 25).Stem, StringComparison.Ordinal);
        Assert.Equal("What is the stated purpose of the talk?",
            result.Items.Single(i => i.Number == 31).Stem);
        Assert.Equal("What does the speaker recommend for new staff?",
            result.Items.Single(i => i.Number == 37).Stem);
    }

    [Fact]
    public void Leaves_a_canonically_numbered_paper_alone_when_renumbering()
    {
        // A paper that already prints 25-42 must never be renumbered.
        var result = ListeningPartBCSourceParser.Parse(CleanPartBText, [27, 28]);

        Assert.Equal(2, result.Items.Count);
        Assert.Contains("paediatric ward", result.Items.Single(i => i.Number == 27).Stem, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_a_number_that_is_not_printed_in_the_source_text()
    {
        var result = ListeningPartBCSourceParser.Parse(CleanPartBText, [40]);

        Assert.Empty(result.Items);
        var skip = Assert.Single(result.Skipped);
        Assert.Equal(40, skip.Number);
        Assert.Equal(ListeningPartBCSourceSkipReason.NoQuestionAnchor, skip.Reason);
    }

    [Fact]
    public void Refuses_to_split_an_option_that_contains_clinical_letter_prose()
    {
        // "Hepatitis B" and "vitamin C" are ordinary Listening option content.
        // Splitting on them would silently truncate the real options, so the
        // item has to be reported rather than recovered.
        const string text = """
            33. What does the nurse identify as the main risk for this patient group?
            A incomplete Hepatitis B vaccination among new staff B poor record keeping on the ward C delayed referral to the specialist team
            """;

        var result = ListeningPartBCSourceParser.Parse(text, [33]);

        Assert.Empty(result.Items);
        var skip = Assert.Single(result.Skipped);
        Assert.Equal(ListeningPartBCSourceSkipReason.AmbiguousInterleavedText, skip.Reason);
    }

    [Fact]
    public void Rejects_a_generic_placeholder_heading_recovered_from_a_poisoned_source()
    {
        // The exact heading migration 20261128000000 wrote across every paper.
        // If it ever reaches the source text it must not be re-adopted as a stem.
        const string text = """
            26. What does the speaker identify as the main clinical priority?
            A remove her saline drip B arrange for more tests C monitor her blood pressure
            """;

        var result = ListeningPartBCSourceParser.Parse(text, [26]);

        Assert.Empty(result.Items);
        var skip = Assert.Single(result.Skipped);
        Assert.Equal(ListeningPartBCSourceSkipReason.StemRejected, skip.Reason);
    }

    [Fact]
    public void Ignores_numbers_outside_the_part_b_and_c_range()
    {
        const string text = """
            12. Complete the note about the patient's history.
            A one B two C three
            27. You hear a ward briefing about a newly admitted patient. What does the doctor want to confirm?
            A the reason the patient is in pain. B the reason the condition was triggered. C the reason the patient's eyes are sore.
            """;

        var result = ListeningPartBCSourceParser.Parse(text);

        Assert.Single(result.Items);
        Assert.Equal(27, result.Items[0].Number);
        Assert.DoesNotContain(result.Skipped, skip => skip.Number < ListeningPartBCSourceParser.FirstNumber);
    }

    [Fact]
    public void Selects_the_asset_text_that_actually_contains_part_b_and_c()
    {
        var audioScript = "Presenter: Welcome to the listening test. Extract one begins now.";
        var answerKey = "25 B 26 A 27 C 28 A 29 B 30 C";

        var chosen = ListeningPartBCSourceParser.SelectQuestionPaperText([audioScript, answerKey, CleanPartBText]);

        Assert.Equal(CleanPartBText, chosen);
    }

    [Fact]
    public void Returns_nothing_when_no_asset_text_holds_part_b_or_c_items()
    {
        var chosen = ListeningPartBCSourceParser.SelectQuestionPaperText(["", null, "Extract one transcript only."]);

        Assert.Null(chosen);
    }

    [Fact]
    public void Empty_source_text_produces_no_items_and_no_noise()
    {
        Assert.Empty(ListeningPartBCSourceParser.Parse(null).Items);
        Assert.Empty(ListeningPartBCSourceParser.Parse("   ").Items);
        Assert.Empty(ListeningPartBCSourceParser.Parse("   ").Skipped);
    }
}
