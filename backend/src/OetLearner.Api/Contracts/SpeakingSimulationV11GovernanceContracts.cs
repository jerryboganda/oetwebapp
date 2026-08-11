namespace OetLearner.Api.Contracts;

public sealed record SpeakingSimulationV11SpecReleaseRequest(
    string SpecVersion,
    string ReleaseVersion);

public sealed record SpeakingSimulationV11RubricReleaseRequest(
    string RubricVersion,
    string CalibrationVersion,
    IReadOnlyList<SpeakingSimulationV11RubricCriterion> Criteria);

public sealed record SpeakingSimulationV11OwnerApprovalRequest(
    string ApprovalKey,
    string ScopeKey,
    string? SpecVersion,
    string? RubricVersion,
    decimal? NumericValue,
    string? EvidenceJson);

public sealed record SpeakingSimulationV11ApprovalActionRequest(string? Note = null);
