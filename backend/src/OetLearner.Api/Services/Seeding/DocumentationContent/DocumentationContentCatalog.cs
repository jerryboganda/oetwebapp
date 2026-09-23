namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>Aggregates every DOC-01..DOC-15 module for <see cref="DocumentationCenterSeeder"/>. One file per module — see the sibling Doc*.cs files.</summary>
internal static class DocumentationContentCatalog
{
    public static IReadOnlyList<DocumentationModuleSeed> GetAll() =>
    [
        Doc01MasterDossier.Build(),
        Doc02FounderStatement.Build(),
        Doc03AiArchitecture.Build(),
        Doc04LiveSpeakingAgent.Build(),
        Doc05WritingAssessment.Build(),
        Doc06LearningCompanion.Build(),
        Doc07PlacementTest.Build(),
        Doc08ListeningReadingMocks.Build(),
        Doc09SourceOfTruth.Build(),
        Doc10PlatformArchitecture.Build(),
        Doc11SecurityPrivacy.Build(),
        Doc12QaValidation.Build(),
        Doc13InnovationTimeline.Build(),
        Doc14ScalabilityRoadmap.Build(),
        Doc15EvidenceAnnex.Build(),
    ];
}
