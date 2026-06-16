namespace OetLearner.Api.Services.Conversation.Tts;

/// <summary>
/// Builds the <c>pronunciation_dictionary_locators</c> entry sent on ElevenLabs
/// text-to-speech requests. Shared by the conversation and listening providers.
/// </summary>
internal static class ElevenLabsPronunciationLocator
{
    /// <summary>
    /// Returns a single locator object for the configured pronunciation
    /// dictionary, or <c>null</c> when none is configured. The
    /// <c>version_id</c> key is omitted entirely when no version is stored —
    /// ElevenLabs rejects an explicit <c>null</c> version, which previously
    /// broke every generation that relied on a dictionary without a version.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? Build(string? dictionaryId, string? versionId)
    {
        if (string.IsNullOrWhiteSpace(dictionaryId)) return null;
        var locator = new Dictionary<string, string>
        {
            ["pronunciation_dictionary_id"] = dictionaryId,
        };
        if (!string.IsNullOrWhiteSpace(versionId))
        {
            locator["version_id"] = versionId;
        }
        return locator;
    }
}
