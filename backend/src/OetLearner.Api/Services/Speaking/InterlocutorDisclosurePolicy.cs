using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace OetLearner.Api.Services.Speaking;

public enum InterlocutorRoleClass
{
    Patient,
    RelativeOrCarer,
    AnimalOwner,
    Client,
    Colleague,
    Examiner,
    Interviewer,
}

public static class InterlocutorRoleClassifier
{
    private static readonly (InterlocutorRoleClass RoleClass, string[] Signals)[] OrderedSignals =
    [
        (InterlocutorRoleClass.Examiner, ["examiner", "assessor"]),
        (InterlocutorRoleClass.Interviewer, ["interviewer", "panel member"]),
        (InterlocutorRoleClass.AnimalOwner,
            ["animal owner", "pet owner", "dog owner", "cat owner", "horse owner", "owner of", "pet parent", "veterinary client", "vet client"]),
        (InterlocutorRoleClass.RelativeOrCarer,
            ["relative", "carer", "caregiver", "family", "parent", "mother", "father", "daughter", "son", "sister", "brother", "spouse", "husband", "wife", "partner", "guardian", "next of kin", "friend"]),
        (InterlocutorRoleClass.Colleague,
            ["colleague", "coworker", "co worker", "team member", "nurse", "doctor", "physician", "pharmacist", "physiotherapist", "therapist", "manager", "supervisor", "health professional", "healthcare professional"]),
        (InterlocutorRoleClass.Client, ["client", "customer", "service user"]),
        (InterlocutorRoleClass.Patient, ["patient", "resident"]),
    ];

    public static InterlocutorRoleClass Resolve(string? authoredRole)
    {
        if (string.IsNullOrWhiteSpace(authoredRole))
            throw new InvalidOperationException("Interlocutor role is required.");

        var normalized = Normalize(authoredRole);
        foreach (var (roleClass, signals) in OrderedSignals)
        {
            if (signals.Any(signal => normalized.Contains(signal, StringComparison.Ordinal)))
                return roleClass;
        }

        throw new InvalidOperationException($"Unsupported interlocutor role '{authoredRole}'.");
    }

    public static string ToCode(InterlocutorRoleClass roleClass) => roleClass switch
    {
        InterlocutorRoleClass.Patient => "patient",
        InterlocutorRoleClass.RelativeOrCarer => "relative_or_carer",
        InterlocutorRoleClass.AnimalOwner => "animal_owner",
        InterlocutorRoleClass.Client => "client",
        InterlocutorRoleClass.Colleague => "colleague",
        InterlocutorRoleClass.Examiner => "examiner",
        InterlocutorRoleClass.Interviewer => "interviewer",
        _ => throw new InvalidOperationException("Unknown interlocutor role class."),
    };

    private static string Normalize(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray();
        return string.Join(' ', new string(chars)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

public enum InterlocutorFactDisclosure
{
    AlwaysEligible,
    Conditional,
    NeverDisclose,
}

public enum InterlocutorNonFactualResponseKind
{
    Acknowledgement,
    ClarificationRequest,
    NeutralPause,
    Closing,
}

public sealed record InterlocutorApprovedStatement(string VariantId, string Text);

public sealed record InterlocutorFactDefinition(
    string FactId,
    InterlocutorFactDisclosure Disclosure,
    IReadOnlyList<InterlocutorApprovedStatement> ApprovedStatements,
    string? ConditionId = null);

public sealed record InterlocutorDisclosureSnapshot(
    string PolicyVersion,
    string CaseIdentity,
    InterlocutorRoleClass RoleClass,
    IReadOnlyList<InterlocutorFactDefinition> Facts);

public sealed record InterlocutorModelFact(
    string FactId,
    IReadOnlyList<InterlocutorApprovedStatement> ApprovedStatements);

/// <summary>
/// Outbound-only projection. Keep the original instance on the server for rendering;
/// never reconstruct it from a learner or provider payload. Construction is limited
/// to the server assembly and projection supplies defensively copied collections.
/// This type does not replace session ownership or per-turn disclosure authorization.
/// </summary>
public sealed class InterlocutorModelSafeContext
{
    internal InterlocutorModelSafeContext(
        string policyVersion,
        string caseIdentity,
        InterlocutorRoleClass roleClass,
        IReadOnlyList<InterlocutorModelFact> facts)
    {
        PolicyVersion = policyVersion;
        CaseIdentity = caseIdentity;
        RoleClass = roleClass;
        Facts = facts;
    }

    public string PolicyVersion { get; }
    public string CaseIdentity { get; }
    public InterlocutorRoleClass RoleClass { get; }
    public IReadOnlyList<InterlocutorModelFact> Facts { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InterlocutorFactSelection(string FactId, string StatementVariantId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InterlocutorModelResponse(
    IReadOnlyList<InterlocutorFactSelection> FactSelections,
    InterlocutorNonFactualResponseKind? NonFactualResponseKind);

public sealed class InterlocutorSatisfiedConditions
{
    private readonly HashSet<string> ids;

    private InterlocutorSatisfiedConditions(HashSet<string> ids)
    {
        this.ids = ids;
    }

    // Only trusted server code may supply condition decisions; never bind client/model claims here.
    internal static InterlocutorSatisfiedConditions FromServerState(params string[] conditionIds)
    {
        ArgumentNullException.ThrowIfNull(conditionIds);
        var copy = new HashSet<string>(StringComparer.Ordinal);
        foreach (var conditionId in conditionIds)
        {
            var validated = InterlocutorDisclosurePolicy.RequireStableId(conditionId, "conditionId");
            if (!copy.Add(validated))
                throw new InvalidOperationException($"Duplicate satisfied condition id '{validated}'.");
        }

        return new InterlocutorSatisfiedConditions(copy);
    }

    internal bool Contains(string conditionId) => ids.Contains(conditionId);
}

public static class InterlocutorDisclosurePolicy
{
    private static readonly IReadOnlyDictionary<InterlocutorNonFactualResponseKind, string> NonFactualResponses =
        new ReadOnlyDictionary<InterlocutorNonFactualResponseKind, string>(
            new Dictionary<InterlocutorNonFactualResponseKind, string>
            {
                [InterlocutorNonFactualResponseKind.Acknowledgement] = "I understand.",
                [InterlocutorNonFactualResponseKind.ClarificationRequest] = "Could you explain that a little more?",
                [InterlocutorNonFactualResponseKind.NeutralPause] = "Okay.",
                [InterlocutorNonFactualResponseKind.Closing] = "Thank you.",
            });

    public static InterlocutorModelSafeContext ProjectForModel(
        InterlocutorDisclosureSnapshot snapshot,
        InterlocutorSatisfiedConditions satisfiedConditions)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(satisfiedConditions);

        var policyVersion = RequireStableId(snapshot.PolicyVersion, "policyVersion");
        var caseIdentity = RequireStableId(snapshot.CaseIdentity, "caseIdentity");
        if (!Enum.IsDefined(snapshot.RoleClass))
            throw new InvalidOperationException("Unknown interlocutor role class.");
        if (snapshot.Facts is null)
            throw new InvalidOperationException("Fact collection is required.");

        var seenFactIds = new HashSet<string>(StringComparer.Ordinal);
        var eligibleFacts = new List<InterlocutorModelFact>();

        foreach (var fact in snapshot.Facts)
        {
            if (fact is null)
                throw new InvalidOperationException("Fact definitions cannot contain null entries.");

            var factId = RequireStableId(fact.FactId, "factId");
            if (!seenFactIds.Add(factId))
                throw new InvalidOperationException($"Duplicate fact id '{factId}'.");

            var approvedStatements = CopyApprovedStatements(fact.ApprovedStatements, factId);
            var eligible = fact.Disclosure switch
            {
                InterlocutorFactDisclosure.AlwaysEligible when fact.ConditionId is null => true,
                InterlocutorFactDisclosure.AlwaysEligible => throw new InvalidOperationException(
                    $"Always-eligible fact '{factId}' cannot declare a condition."),
                InterlocutorFactDisclosure.Conditional => IsConditionalFactEligible(
                    factId, fact.ConditionId, satisfiedConditions),
                InterlocutorFactDisclosure.NeverDisclose => false,
                _ => throw new InvalidOperationException($"Unknown disclosure policy for fact '{factId}'."),
            };

            if (eligible)
                eligibleFacts.Add(new InterlocutorModelFact(factId, approvedStatements));
        }

        return new InterlocutorModelSafeContext(
            policyVersion,
            caseIdentity,
            snapshot.RoleClass,
            Array.AsReadOnly(eligibleFacts.ToArray()));
    }

    public static string Render(
        InterlocutorModelSafeContext context,
        InterlocutorModelResponse response)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);
        if (context.Facts is null || response.FactSelections is null)
            throw new InvalidOperationException("Model context and response selections are required.");

        var eligible = new Dictionary<string, InterlocutorModelFact>(StringComparer.Ordinal);
        foreach (var fact in context.Facts)
        {
            var factId = RequireStableId(fact.FactId, "factId");
            if (!eligible.TryAdd(factId, fact))
                throw new InvalidOperationException($"Duplicate eligible fact id '{factId}'.");
        }

        var selectedFactIds = new HashSet<string>(StringComparer.Ordinal);
        var rendered = new List<string>();
        foreach (var selection in response.FactSelections)
        {
            if (selection is null)
                throw new InvalidOperationException("Fact selections cannot contain null entries.");

            var factId = RequireStableId(selection.FactId, "factId");
            var variantId = RequireStableId(selection.StatementVariantId, "statementVariantId");
            if (!selectedFactIds.Add(factId))
                throw new InvalidOperationException($"Duplicate selected fact id '{factId}'.");
            if (!eligible.TryGetValue(factId, out var fact))
                throw new InvalidOperationException($"Fact '{factId}' is unknown or ineligible.");

            var statement = fact.ApprovedStatements.SingleOrDefault(x =>
                string.Equals(x.VariantId, variantId, StringComparison.Ordinal));
            if (statement is null)
                throw new InvalidOperationException(
                    $"Statement variant '{variantId}' is not approved for fact '{factId}'.");
            rendered.Add(statement.Text);
        }

        if (response.NonFactualResponseKind is { } responseKind)
        {
            if (!NonFactualResponses.TryGetValue(responseKind, out var approvedResponse))
                throw new InvalidOperationException("Unknown non-factual response kind.");
            rendered.Add(approvedResponse);
        }

        if (rendered.Count == 0)
            throw new InvalidOperationException("Model response selected no approved output.");

        return string.Join(" ", rendered);
    }

    internal static string RequireStableId(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.' or ':')))
        {
            throw new InvalidOperationException($"{fieldName} must be a non-empty culture-stable identifier.");
        }

        return value;
    }

    private static bool IsConditionalFactEligible(
        string factId,
        string? conditionId,
        InterlocutorSatisfiedConditions satisfiedConditions)
    {
        if (conditionId is null)
            throw new InvalidOperationException($"Conditional fact '{factId}' requires an explicit condition id.");
        var validatedConditionId = RequireStableId(conditionId, "conditionId");
        return satisfiedConditions.Contains(validatedConditionId);
    }

    private static IReadOnlyList<InterlocutorApprovedStatement> CopyApprovedStatements(
        IReadOnlyList<InterlocutorApprovedStatement> statements,
        string factId)
    {
        if (statements is null || statements.Count == 0)
            throw new InvalidOperationException($"Fact '{factId}' requires at least one approved statement.");

        var seenVariantIds = new HashSet<string>(StringComparer.Ordinal);
        var copy = new List<InterlocutorApprovedStatement>(statements.Count);
        foreach (var statement in statements)
        {
            if (statement is null || string.IsNullOrWhiteSpace(statement.Text))
                throw new InvalidOperationException($"Fact '{factId}' contains an invalid approved statement.");
            var variantId = RequireStableId(statement.VariantId, "statementVariantId");
            if (!seenVariantIds.Add(variantId))
                throw new InvalidOperationException(
                    $"Fact '{factId}' contains duplicate statement variant id '{variantId}'.");
            copy.Add(new InterlocutorApprovedStatement(variantId, statement.Text));
        }

        return Array.AsReadOnly(copy.ToArray());
    }
}
