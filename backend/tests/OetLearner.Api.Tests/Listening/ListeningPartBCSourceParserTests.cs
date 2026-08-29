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

    // Two printed items interleaved by the watermark column: Q29's stem sits
    // under the "30." anchor and Q30's own stem follows its option set.
    private const string InterleavedText = """
        29. You hear a hospital pharmacist talking to a patient.

        30.

        SAMPLE The patient's main concern about his medication is whether
        A he's been prescribed the most effective dose. B he's likely to experience long-term side effects. C he's been taking it at the most appropriate time.
        You hear a primary-care doctor talking to a patient. The patient is worried that she may have A self-treated her toe in an inappropriate way. B damaged a toe that she'd previously injured. C triggered the resurgence of a health condition.
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
    public void Reports_interleaved_items_instead_of_guessing_an_attribution()
    {
        var result = ListeningPartBCSourceParser.Parse(InterleavedText, [29, 30]);

        Assert.Empty(result.Items);
        Assert.Equal(2, result.Skipped.Count);

        var q29 = result.Skipped.Single(skip => skip.Number == 29);
        Assert.Equal(ListeningPartBCSourceSkipReason.OptionSetIncomplete, q29.Reason);

        var q30 = result.Skipped.Single(skip => skip.Number == 30);
        Assert.Equal(ListeningPartBCSourceSkipReason.AmbiguousInterleavedText, q30.Reason);
        Assert.Contains("source paper", q30.Detail, StringComparison.OrdinalIgnoreCase);
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
