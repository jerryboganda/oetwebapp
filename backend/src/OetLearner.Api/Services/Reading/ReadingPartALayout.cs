using System.Text.RegularExpressions;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Reading;

/// <summary>
/// Official OET Reading Part A is always 20 items, but the three task blocks
/// are not fixed at 1-7 / 8-14 / 15-20. Matching is 1-7 or 1-8; 15-20 stays
/// the last heading; the middle block fills the gap and may swap with the
/// last between ShortAnswer and SentenceCompletion.
/// </summary>
public readonly record struct ReadingPartALayout(
    int MatchingEnd,
    ReadingQuestionType MiddleType,
    ReadingQuestionType LastType)
{
    public static readonly ReadingPartALayout Classic = new(
        7,
        ReadingQuestionType.ShortAnswer,
        ReadingQuestionType.SentenceCompletion);

    public ReadingQuestionType? TypeFor(int displayOrder)
    {
        if (displayOrder is < 1 or > 20) return null;
        if (displayOrder <= MatchingEnd) return ReadingQuestionType.MatchingTextReference;
        if (displayOrder < 15) return MiddleType;
        return LastType;
    }

    public string Describe()
    {
        var middle = MiddleType == ReadingQuestionType.ShortAnswer
            ? "answer the questions"
            : "complete the sentences";
        var last = LastType == ReadingQuestionType.ShortAnswer
            ? "answer the questions"
            : "complete the sentences";
        return $"Questions 1-{MatchingEnd} matching A-D; {MatchingEnd + 1}-14 {middle}; 15-20 {last}";
    }
}

public static class ReadingPartALayoutDetector
{
    private static readonly Regex Heading = new(
        @"questions\s+(\d+)\s*(?:-|to)\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryDetectFromQuestions(
        IEnumerable<(int DisplayOrder, ReadingQuestionType QuestionType)> questions,
        out ReadingPartALayout layout,
        out string error)
    {
        layout = ReadingPartALayout.Classic;
        var byOrder = new Dictionary<int, ReadingQuestionType>();
        foreach (var question in questions)
        {
            if (question.DisplayOrder is < 1 or > 20)
            {
                error = $"Part A question {question.DisplayOrder} is outside the official 1-20 range.";
                return false;
            }

            if (byOrder.TryGetValue(question.DisplayOrder, out var existing)
                && existing != question.QuestionType)
            {
                error = $"Part A Q{question.DisplayOrder} has conflicting types.";
                return false;
            }

            byOrder[question.DisplayOrder] = question.QuestionType;
        }

        if (byOrder.Count != 20)
        {
            error = $"Part A must have questions 1-20 before a layout can be confirmed (found {byOrder.Count}).";
            return false;
        }

        var matchingEnd = byOrder.TryGetValue(8, out var q8)
            && q8 == ReadingQuestionType.MatchingTextReference
            ? 8
            : 7;

        for (var order = 1; order <= matchingEnd; order++)
        {
            if (!byOrder.TryGetValue(order, out var type) || type != ReadingQuestionType.MatchingTextReference)
            {
                error = $"Part A Q{order}: expected MatchingTextReference in the opening A-D block (1-{matchingEnd}).";
                return false;
            }
        }

        var middleTypes = Enumerable.Range(matchingEnd + 1, 14 - matchingEnd)
            .Select(order => byOrder[order])
            .Distinct()
            .ToList();
        var lastTypes = Enumerable.Range(15, 6)
            .Select(order => byOrder[order])
            .Distinct()
            .ToList();

        if (middleTypes.Count != 1 || !IsGapType(middleTypes[0]))
        {
            error = $"Part A Q{matchingEnd + 1}-14 must be one block of ShortAnswer or SentenceCompletion.";
            return false;
        }

        if (lastTypes.Count != 1 || !IsGapType(lastTypes[0]))
        {
            error = "Part A Q15-20 must be one block of ShortAnswer or SentenceCompletion.";
            return false;
        }

        if (middleTypes[0] == lastTypes[0])
        {
            error = "Part A middle and last blocks must be different tasks (answer vs complete the sentences).";
            return false;
        }

        layout = new ReadingPartALayout(matchingEnd, middleTypes[0], lastTypes[0]);
        error = string.Empty;
        return true;
    }

    public static bool TryDetectFromBookletText(string? raw, out ReadingPartALayout layout, out string error)
    {
        layout = ReadingPartALayout.Classic;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "No question-paper text was provided.";
            return false;
        }

        var text = Normalize(raw);
        var matches = Heading.Matches(text);
        var blocks = new List<(int Start, int End, string Kind)>();
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            if (!int.TryParse(match.Groups[1].Value, out var start)
                || !int.TryParse(match.Groups[2].Value, out var end)
                || start < 1
                || end > 20
                || start > end)
            {
                continue;
            }

            var chunkStart = match.Index + match.Length;
            var nextIndex = i + 1 < matches.Count
                ? matches[i + 1].Index
                : Math.Min(text.Length, chunkStart + 500);
            var kind = Classify(text[chunkStart..nextIndex]);
            if (kind is null) continue;
            blocks.Add((start, end, kind));
        }

        var matching = blocks.FirstOrDefault(block => block.Kind == "matching" && block.Start == 1 && block.End is 7 or 8);
        var last = blocks.FirstOrDefault(block => block.Start == 15 && block.End == 20 && IsGapKind(block.Kind));
        if (matching == default)
        {
            error = "Could not find a Questions 1-7 or 1-8 matching A-D heading.";
            return false;
        }

        if (last == default)
        {
            error = "Could not find a Questions 15-20 answer/complete heading.";
            return false;
        }

        var matchingEnd = matching.End;
        var expectedMiddleStart = matchingEnd + 1;
        var middle = blocks.FirstOrDefault(block =>
            block.Start == expectedMiddleStart && block.End == 14 && IsGapKind(block.Kind));
        if (middle == default)
        {
            middle = (expectedMiddleStart, 14, OtherGapKind(last.Kind));
        }

        if (!IsGapKind(middle.Kind) || middle.Kind == last.Kind)
        {
            error = "The middle and last Part A blocks must be different (answer vs complete the sentences).";
            return false;
        }

        layout = new ReadingPartALayout(matchingEnd, ParseGap(middle.Kind), ParseGap(last.Kind));
        error = string.Empty;
        return true;
    }

    private static bool IsGapType(ReadingQuestionType type) =>
        type is ReadingQuestionType.ShortAnswer or ReadingQuestionType.SentenceCompletion;

    private static bool IsGapKind(string kind) =>
        kind is "ShortAnswer" or "SentenceCompletion";

    private static string OtherGapKind(string kind) =>
        kind == "ShortAnswer" ? "SentenceCompletion" : "ShortAnswer";

    private static ReadingQuestionType ParseGap(string kind) =>
        kind == "ShortAnswer" ? ReadingQuestionType.ShortAnswer : ReadingQuestionType.SentenceCompletion;

    private static string Normalize(string raw) =>
        Regex.Replace(raw.Replace('\u00a0', ' '), @"[\u2010-\u2015]", "-");

    private static string? Classify(string text)
    {
        var normalized = Regex.Replace(text, @"\s+", " ").Trim().ToLowerInvariant();
        if (normalized.Contains("which text")
            || Regex.IsMatch(normalized, @"a,\s*b,\s*c or d")
            || normalized.Contains("decide which text")
            || normalized.Contains("information comes from"))
        {
            return "matching";
        }

        if (normalized.Contains("complete each of the sentences")
            || normalized.Contains("complete the following sentences")
            || normalized.Contains("complete each of the sentence"))
        {
            return "SentenceCompletion";
        }

        if (normalized.Contains("answer each of the questions")
            || normalized.Contains("answer the following")
            || normalized.Contains("with a word or short phrase"))
        {
            return "ShortAnswer";
        }

        return null;
    }
}
