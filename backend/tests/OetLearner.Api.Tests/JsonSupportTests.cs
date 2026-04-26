using System.Text.Json.Serialization;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public class JsonSupportTests
{
    private record SamplePayload(string Name, int Score, [property: JsonPropertyName("is_active")] bool IsActive);

    [Fact]
    public void Options_uses_web_defaults_with_camelCase_property_names()
    {
        var json = JsonSupport.Serialize(new { firstName = "Jane", lastName = "Doe" });
        Assert.Contains("\"firstName\":\"Jane\"", json);
        Assert.Contains("\"lastName\":\"Doe\"", json);
    }

    [Fact]
    public void Options_does_not_indent_output()
    {
        var json = JsonSupport.Serialize(new { a = 1, b = 2 });
        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("  ", json);
    }

    [Fact]
    public void Serialize_round_trips_through_Deserialize()
    {
        var original = new SamplePayload("Alice", 42, true);
        var json = JsonSupport.Serialize(original);
        var roundTripped = JsonSupport.Deserialize(json, default(SamplePayload)!);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void Serialize_handles_null_values()
    {
        var json = JsonSupport.Serialize<string?>(null);
        Assert.Equal("null", json);
    }

    [Fact]
    public void Deserialize_returns_fallback_for_null_input()
    {
        var fallback = new SamplePayload("fallback", 0, false);
        var result = JsonSupport.Deserialize<SamplePayload>(null, fallback);
        Assert.Equal(fallback, result);
    }

    [Fact]
    public void Deserialize_returns_fallback_for_empty_input()
    {
        var fallback = new SamplePayload("fb", 1, true);
        Assert.Equal(fallback, JsonSupport.Deserialize("", fallback));
    }

    [Fact]
    public void Deserialize_returns_fallback_for_whitespace_input()
    {
        var fallback = new SamplePayload("fb", 1, true);
        Assert.Equal(fallback, JsonSupport.Deserialize("   \t\n", fallback));
    }

    [Fact]
    public void Deserialize_returns_fallback_for_malformed_json()
    {
        var fallback = new SamplePayload("fb", 1, true);
        Assert.Equal(fallback, JsonSupport.Deserialize("{ not json", fallback));
    }

    [Fact]
    public void Deserialize_returns_fallback_for_invalid_shape()
    {
        var fallback = new SamplePayload("fb", 1, true);
        // String value cannot deserialize into SamplePayload -> exception swallowed.
        Assert.Equal(fallback, JsonSupport.Deserialize("\"just a string\"", fallback));
    }

    [Fact]
    public void Deserialize_returns_fallback_when_deserializer_yields_null()
    {
        var fallback = new SamplePayload("fb", 1, true);
        Assert.Equal(fallback, JsonSupport.Deserialize<SamplePayload?>("null", fallback));
    }

    [Fact]
    public void Deserialize_supports_camelCase_input()
    {
        var fallback = new SamplePayload("fb", 0, false);
        var result = JsonSupport.Deserialize(
            "{\"name\":\"Bob\",\"score\":7,\"is_active\":true}",
            fallback);
        Assert.Equal(new SamplePayload("Bob", 7, true), result);
    }

    [Fact]
    public void Deserialize_supports_simple_collections()
    {
        var fallback = new List<int>();
        var result = JsonSupport.Deserialize("[1,2,3]", fallback);
        Assert.Equal(new List<int> { 1, 2, 3 }, result);
    }

    [Fact]
    public void Serialize_supports_simple_collections()
    {
        Assert.Equal("[1,2,3]", JsonSupport.Serialize(new[] { 1, 2, 3 }));
    }

    [Fact]
    public void Serialize_uses_string_enum_or_numeric_consistent_with_web_defaults()
    {
        // System.Text.Json Web defaults serialize enums as numbers by default; verify that contract.
        var json = JsonSupport.Serialize(SampleEnum.Second);
        Assert.Equal("1", json);
    }

    private enum SampleEnum
    {
        First = 0,
        Second = 1
    }
}
