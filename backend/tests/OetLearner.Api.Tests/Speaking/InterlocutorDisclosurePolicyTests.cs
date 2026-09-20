using System.Text.Json;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

public sealed class InterlocutorDisclosurePolicyTests
{
    [Theory]
    [InlineData("Patient", InterlocutorRoleClass.Patient, "patient")]
    [InlineData("Patient's daughter", InterlocutorRoleClass.RelativeOrCarer, "relative_or_carer")]
    [InlineData("Dog owner", InterlocutorRoleClass.AnimalOwner, "animal_owner")]
    [InlineData("Client", InterlocutorRoleClass.Client, "client")]
    [InlineData("Nurse colleague", InterlocutorRoleClass.Colleague, "colleague")]
    [InlineData("OET examiner", InterlocutorRoleClass.Examiner, "examiner")]
    [InlineData("Panel interviewer", InterlocutorRoleClass.Interviewer, "interviewer")]
    public void Role_classifier_supports_all_universal_role_classes(
        string authoredRole,
        InterlocutorRoleClass expectedClass,
        string expectedCode)
    {
        var resolved = InterlocutorRoleClassifier.Resolve(authoredRole);

        Assert.Equal(expectedClass, resolved);
        Assert.Equal(expectedCode, InterlocutorRoleClassifier.ToCode(resolved));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown actor")]
    public void Role_classifier_fails_closed_for_missing_or_unknown_roles(string authoredRole)
        => Assert.Throws<InvalidOperationException>(() => InterlocutorRoleClassifier.Resolve(authoredRole));

    [Theory]
    [InlineData(InterlocutorRoleClass.Patient)]
    [InlineData(InterlocutorRoleClass.RelativeOrCarer)]
    [InlineData(InterlocutorRoleClass.AnimalOwner)]
    [InlineData(InterlocutorRoleClass.Client)]
    [InlineData(InterlocutorRoleClass.Colleague)]
    [InlineData(InterlocutorRoleClass.Examiner)]
    [InlineData(InterlocutorRoleClass.Interviewer)]
    public void Projects_all_supported_role_classes(InterlocutorRoleClass roleClass)
    {
        var projected = InterlocutorDisclosurePolicy.ProjectForModel(
            Snapshot(roleClass, Fact("safe.fact", InterlocutorFactDisclosure.AlwaysEligible, "safe")),
            InterlocutorSatisfiedConditions.FromServerState());

        Assert.Equal(roleClass, projected.RoleClass);
        Assert.Equal("safe.fact", Assert.Single(projected.Facts).FactId);
    }

    [Fact]
    public void Never_disclose_wins_even_when_condition_is_satisfied()
    {
        var hidden = Fact(
            "hidden.fact",
            InterlocutorFactDisclosure.NeverDisclose,
            "secret",
            conditionId: "candidate.asked");

        var projected = InterlocutorDisclosurePolicy.ProjectForModel(
            Snapshot(InterlocutorRoleClass.Patient, hidden),
            InterlocutorSatisfiedConditions.FromServerState("candidate.asked"));

        Assert.Empty(projected.Facts);
    }

    [Fact]
    public void Conditional_fact_requires_explicit_server_condition_state()
    {
        var conditional = Fact(
            "conditional.fact",
            InterlocutorFactDisclosure.Conditional,
            "eligible now",
            conditionId: "server.condition.met");
        var snapshot = Snapshot(InterlocutorRoleClass.RelativeOrCarer, conditional);

        var before = InterlocutorDisclosurePolicy.ProjectForModel(
            snapshot,
            InterlocutorSatisfiedConditions.FromServerState());
        var after = InterlocutorDisclosurePolicy.ProjectForModel(
            snapshot,
            InterlocutorSatisfiedConditions.FromServerState("server.condition.met"));

        Assert.Empty(before.Facts);
        Assert.Equal("conditional.fact", Assert.Single(after.Facts).FactId);
    }

    [Fact]
    public void Incomplete_conditional_policy_fails_closed()
    {
        var malformed = Fact(
            "conditional.fact",
            InterlocutorFactDisclosure.Conditional,
            "not eligible",
            conditionId: null);

        Assert.Throws<InvalidOperationException>(() =>
            InterlocutorDisclosurePolicy.ProjectForModel(
                Snapshot(InterlocutorRoleClass.Client, malformed),
                InterlocutorSatisfiedConditions.FromServerState()));
    }

    [Fact]
    public void Duplicate_or_malformed_fact_ids_fail_closed()
    {
        var duplicateSnapshot = Snapshot(
            InterlocutorRoleClass.Colleague,
            Fact("same.fact", InterlocutorFactDisclosure.AlwaysEligible, "one"),
            Fact("same.fact", InterlocutorFactDisclosure.AlwaysEligible, "two"));
        var malformedSnapshot = Snapshot(
            InterlocutorRoleClass.Colleague,
            Fact("bad fact id", InterlocutorFactDisclosure.AlwaysEligible, "bad"));

        Assert.Throws<InvalidOperationException>(() =>
            InterlocutorDisclosurePolicy.ProjectForModel(
                duplicateSnapshot,
                InterlocutorSatisfiedConditions.FromServerState()));
        Assert.Throws<InvalidOperationException>(() =>
            InterlocutorDisclosurePolicy.ProjectForModel(
                malformedSnapshot,
                InterlocutorSatisfiedConditions.FromServerState()));
    }

    [Fact]
    public void Model_context_serialization_contains_no_withheld_metadata()
    {
        var snapshot = Snapshot(
            InterlocutorRoleClass.Examiner,
            Fact("visible.fact", InterlocutorFactDisclosure.AlwaysEligible, "visible statement"),
            Fact("never.fact", InterlocutorFactDisclosure.NeverDisclose, "never value"),
            Fact("later.fact", InterlocutorFactDisclosure.Conditional, "later value", "later.condition"));

        var projected = InterlocutorDisclosurePolicy.ProjectForModel(
            snapshot,
            InterlocutorSatisfiedConditions.FromServerState());
        var json = JsonSerializer.Serialize(projected);

        Assert.Contains("visible.fact", json, StringComparison.Ordinal);
        Assert.DoesNotContain("never.fact", json, StringComparison.Ordinal);
        Assert.DoesNotContain("never value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("later.fact", json, StringComparison.Ordinal);
        Assert.DoesNotContain("later.condition", json, StringComparison.Ordinal);
        Assert.DoesNotContain("NeverDisclose", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ConditionId", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Renderer_rejects_unknown_or_withheld_fact_ids()
    {
        var projected = InterlocutorDisclosurePolicy.ProjectForModel(
            Snapshot(
                InterlocutorRoleClass.AnimalOwner,
                Fact("visible.fact", InterlocutorFactDisclosure.AlwaysEligible, "visible"),
                Fact("hidden.fact", InterlocutorFactDisclosure.NeverDisclose, "hidden")),
            InterlocutorSatisfiedConditions.FromServerState());

        Assert.Throws<InvalidOperationException>(() =>
            InterlocutorDisclosurePolicy.Render(
                projected,
                new InterlocutorModelResponse(
                    [new InterlocutorFactSelection("hidden.fact", "default")],
                    null)));
        Assert.Throws<InvalidOperationException>(() =>
            InterlocutorDisclosurePolicy.Render(
                projected,
                new InterlocutorModelResponse(
                    [new InterlocutorFactSelection("unknown.fact", "default")],
                    null)));
    }

    [Fact]
    public void Renderer_uses_only_exact_approved_statements_and_rejects_duplicate_fact_selection()
    {
        var projected = InterlocutorDisclosurePolicy.ProjectForModel(
            Snapshot(
                InterlocutorRoleClass.Interviewer,
                Fact("visible.fact", InterlocutorFactDisclosure.AlwaysEligible, "Exact approved words.")),
            InterlocutorSatisfiedConditions.FromServerState());

        var rendered = InterlocutorDisclosurePolicy.Render(
            projected,
            new InterlocutorModelResponse(
                [new InterlocutorFactSelection("visible.fact", "default")],
                null));
        Assert.Equal("Exact approved words.", rendered);

        Assert.Throws<InvalidOperationException>(() =>
            InterlocutorDisclosurePolicy.Render(
                projected,
                new InterlocutorModelResponse(
                    [
                        new InterlocutorFactSelection("visible.fact", "default"),
                        new InterlocutorFactSelection("visible.fact", "default"),
                    ],
                    null)));
    }

    [Fact]
    public void Model_response_contract_rejects_arbitrary_freeform_text()
    {
        const string payload = """
            {"FactSelections":[],"NonFactualResponseKind":0,"Text":"Take this medication immediately."}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InterlocutorModelResponse>(payload));
    }

    [Fact]
    public void Model_context_is_outbound_only_and_cannot_be_deserialized_as_render_authority()
    {
        var projected = InterlocutorDisclosurePolicy.ProjectForModel(
            Snapshot(InterlocutorRoleClass.Patient,
                Fact("visible.fact", InterlocutorFactDisclosure.AlwaysEligible, "Approved words.")),
            InterlocutorSatisfiedConditions.FromServerState());
        var payload = JsonSerializer.Serialize(projected);

        Assert.Contains("Approved words.", payload, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() =>
            JsonSerializer.Deserialize<InterlocutorModelSafeContext>(payload));
    }

    [Fact]
    public void Source_collection_mutation_cannot_change_an_existing_projection_or_rendered_statement()
    {
        var statements = new List<InterlocutorApprovedStatement>
        {
            new("default", "Original approved words."),
        };
        var facts = new List<InterlocutorFactDefinition>
        {
            new("visible.fact", InterlocutorFactDisclosure.AlwaysEligible, statements),
        };
        var projected = InterlocutorDisclosurePolicy.ProjectForModel(
            new InterlocutorDisclosureSnapshot("policy-v1", "case-001", InterlocutorRoleClass.Client, facts),
            InterlocutorSatisfiedConditions.FromServerState());

        statements[0] = new("default", "Changed after projection.");
        facts.Clear();
        facts.Add(Fact("hidden.fact", InterlocutorFactDisclosure.NeverDisclose, "Withheld value."));

        Assert.Equal("Original approved words.", InterlocutorDisclosurePolicy.Render(projected,
            new InterlocutorModelResponse([new("visible.fact", "default")], null)));
        var json = JsonSerializer.Serialize(projected);
        Assert.DoesNotContain("Changed after projection.", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden.fact", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Withheld value.", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_provider_cannot_override_an_approved_statement_inside_a_selection()
    {
        const string payload = """
            {"FactSelections":[{"FactId":"visible.fact","StatementVariantId":"default","Text":"Unapproved words"}]}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InterlocutorModelResponse>(payload));
    }

    private static InterlocutorDisclosureSnapshot Snapshot(
        InterlocutorRoleClass roleClass,
        params InterlocutorFactDefinition[] facts)
        => new("live-interlocutor-v11", "case-001", roleClass, facts);

    private static InterlocutorFactDefinition Fact(
        string factId,
        InterlocutorFactDisclosure disclosure,
        string statement,
        string? conditionId = null)
        => new(
            factId,
            disclosure,
            [new InterlocutorApprovedStatement("default", statement)],
            conditionId);
}
