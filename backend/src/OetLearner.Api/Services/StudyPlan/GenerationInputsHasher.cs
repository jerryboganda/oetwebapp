using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OetLearner.Api.Services.Planner;

/// <summary>
/// Stable SHA-256 hash over the inputs to the generator so a regeneration that
/// changes nothing material returns SkippedBecauseUnchanged without burning a
/// new plan version.
/// </summary>
public static class GenerationInputsHasher
{
    private static readonly JsonSerializerOptions StableJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Hash(object snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot, StableJsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
