namespace OetLearner.Api.Contracts;

// ══════════════════════════════════════════════════════════════════════════════
//  Vocabulary module — typed response and request DTOs
//  (retire the old anonymous-object responses in VocabularyService)
// ══════════════════════════════════════════════════════════════════════════════

public sealed record VocabularyTermResponse(
    string Id,
    string Term,
    string Definition,
    string ExampleSentence,
    string? ContextNotes,
    string ExamTypeCode,
    string? ProfessionId,
    string Category,
    string Difficulty,
    string? IpaPronunciation,
    string? AudioUrl,
    string? AudioMediaAssetId,
    string? ImageUrl,
    string[] Synonyms,
    string[] Collocations,
    string[] RelatedTerms,
    string? SourceProvenance,
    string Status
);

public sealed record VocabularyTermSummary(
    string Id,
    string Term,
    string Definition,
    string Category,
    string Difficulty,
    string? IpaPronunciation,
    string? AudioUrl,
    string? ExampleSentence
);

public sealed record VocabularyTermsPageResponse(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<VocabularyTermResponse> Terms,
    IReadOnlyList<VocabularyTermResponse> Items      // alias for legacy callers
);

public sealed record MyVocabularyItem(
    Guid Id,
    string TermId,
    string Term,
    string Definition,
    string Mastery,
    double EaseFactor,
    int IntervalDays,
    int ReviewCount,
    int CorrectCount,
    DateOnly? NextReviewDate,
    string? DueAt,
    DateTimeOffset? LastReviewedAt,
    DateTimeOffset AddedAt,
    string? SourceRef
);

public sealed record MyVocabularyAddResponse(
    bool Added,
    MyVocabularyItem Item
);

public sealed record VocabularyFlashcardDto(
    Guid Id,
    string TermId,
    string Term,
    string Definition,
    string? ExampleSentence,
    string? ContextNotes,
    string? IpaPronunciation,
    string? AudioUrl,
    string[] Synonyms,
    string Mastery
);

public sealed record FlashcardReviewResponse(
    Guid Id,
    string Mastery,
    DateOnly NextReviewDate,
    int IntervalDays,
    double EaseFactor,
    int ReviewCount
);

public sealed record VocabularyQuizQuestionDto(
    string TermId,
    string Term,
    string Format,
    string Prompt,
    IReadOnlyList<string> Options,
    int CorrectIndex,
    string CorrectAnswer,
    string? ExampleSentence,
    string? AudioUrl
);

public sealed record VocabularyQuizResponse(
    string Format,
    IReadOnlyList<VocabularyQuizQuestionDto> Questions
);

public sealed record VocabularyQuizSubmissionResponse(
    Guid Id,
    string Format,
    int TermsQuizzed,
    int CorrectCount,
    double Score,
    int DurationSeconds,
    int XpAwarded,
    DateTimeOffset CompletedAt,
    IReadOnlyList<string> NewlyMasteredTermIds
);

public sealed record VocabularyQuizHistoryItem(
    Guid Id,
    string Format,
    int TermsQuizzed,
    int CorrectCount,
    double Score,
    int DurationSeconds,
    DateTimeOffset CompletedAt
);

public sealed record VocabularyQuizHistoryResponse(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<VocabularyQuizHistoryItem> Items
);

public sealed record VocabularyStatsResponse(
    int TotalInList,
    int Mastered,
    int Reviewing,
    int Learning,
    int New,
    int DueToday,
    int DueThisWeek,
    int StreakDays,
    int TotalTermsInCatalog
);

public sealed record VocabularyCategoryItem(
    string Category,
    int TermCount
);

public sealed record VocabularyCategoriesResponse(
    string ExamTypeCode,
    string? ProfessionId,
    IReadOnlyList<VocabularyCategoryItem> Categories
);

public sealed record VocabularyDailySetResponse(
    DateOnly Date,
    int NewCount,
    int DueCount,
    IReadOnlyList<VocabularyFlashcardDto> Cards
);

public sealed record VocabularyLookupResult(
    bool Found,
    VocabularyTermResponse? Term,
    IReadOnlyList<VocabularyTermSummary> Suggestions
);

// ── Request DTOs ──────────────────────────────────────────────────────────

public sealed record AddToMyVocabularyRequest(string? SourceRef, string? Context);

public sealed record FlashcardReviewRequestV2(int Quality);

public sealed record VocabQuizSubmissionV2(
    string? Format,
    IReadOnlyList<VocabQuizAnswerV2> Answers,
    int? DurationSeconds
);

public sealed record VocabQuizAnswerV2(
    string TermId,
    bool Correct,
    string? UserAnswer
);

public sealed record VocabularyGlossRequest(
    string Word,
    string? Context,
    string? LetterType,
    string? Profession
);

public sealed record VocabularyGlossResponse(
    string Term,
    string? IpaPronunciation,
    string ShortDefinition,
    string ExampleSentence,
    string? ContextNotes,
    IReadOnlyList<string> Synonyms,
    string Register,
    IReadOnlyList<string> AppliedRuleIds,
    string RulebookVersion,
    bool MatchedExistingTerm,
    string? ExistingTermId
);

// ── Admin DTOs ────────────────────────────────────────────────────────────

public sealed record AdminVocabularyItemCreateRequestV2(
    string Term,
    string Definition,
    string ExampleSentence,
    string? ContextNotes,
    string ExamTypeCode,
    string? ProfessionId,
    string Category,
    string? Difficulty,
    string? IpaPronunciation,
    string? AudioUrl,
    string? AudioMediaAssetId,
    string? ImageUrl,
    IReadOnlyList<string>? Synonyms,
    IReadOnlyList<string>? Collocations,
    IReadOnlyList<string>? RelatedTerms,
    string? SourceProvenance,
    string? Status
);

public sealed record AdminVocabularyItemUpdateRequestV2(
    string? Term,
    string? Definition,
    string? ExampleSentence,
    string? ContextNotes,
    string? ExamTypeCode,
    string? ProfessionId,
    string? Category,
    string? Difficulty,
    string? IpaPronunciation,
    string? AudioUrl,
    string? AudioMediaAssetId,
    string? ImageUrl,
    IReadOnlyList<string>? Synonyms,
    IReadOnlyList<string>? Collocations,
    IReadOnlyList<string>? RelatedTerms,
    string? SourceProvenance,
    string? Status
);

public sealed record AdminVocabularyImportPreviewRow(
    int LineNumber,
    bool Valid,
    string? Term,
    string? Definition,
    string? Category,
    string? Difficulty,
    string? ProfessionId,
    string? ExampleSentence,
    string? Error
);

public sealed record AdminVocabularyImportPreviewResponse(
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    int DuplicateRows,
    IReadOnlyList<AdminVocabularyImportPreviewRow> Rows,
    IReadOnlyList<string> Warnings
);

public sealed record AdminVocabularyImportResponse(
    int Imported,
    int Skipped,
    int Duplicates,
    int FailedRows,
    IReadOnlyList<string> Errors
);

public sealed record AdminVocabularyAiDraftRequest(
    int Count,
    string ExamTypeCode,
    string? ProfessionId,
    string Category,
    string? Difficulty,
    string? SeedPrompt
);

public sealed record AdminVocabularyDraftTerm(
    string Term,
    string Definition,
    string ExampleSentence,
    string? ContextNotes,
    string Category,
    string Difficulty,
    string? IpaPronunciation,
    IReadOnlyList<string> Synonyms,
    IReadOnlyList<string> Collocations,
    IReadOnlyList<string> RelatedTerms,
    IReadOnlyList<string> AppliedRuleIds
);

public sealed record AdminVocabularyAiDraftResponse(
    string RulebookVersion,
    IReadOnlyList<AdminVocabularyDraftTerm> Drafts,
    string? Warning
);

public sealed record AdminVocabularyAiDraftAcceptRequest(
    string ExamTypeCode,
    string? ProfessionId,
    string SourceProvenance,
    IReadOnlyList<AdminVocabularyDraftTerm> Drafts
);
