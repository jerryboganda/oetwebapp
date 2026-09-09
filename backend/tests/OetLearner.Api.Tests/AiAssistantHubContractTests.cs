using System.Text.RegularExpressions;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// The SignalR wire contract between <c>AiAssistantHub</c> and the browser client
/// in <c>lib/ai-assistant/signalr.ts</c>.
///
/// <para>
/// This test exists because the two sides silently disagreed. The hub sent
/// <c>MessageDelta</c> and <c>MessageComplete</c>, each with a leading
/// <c>threadId</c>; the client listened for <c>TextDelta</c> and
/// <c>TurnComplete</c> with no <c>threadId</c>. Nothing threw and nothing logged
/// — the learner simply watched an empty panel while a perfectly good answer
/// streamed into a handler no one had registered. Tool-call arguments were
/// shifted by one position for the same reason.
/// </para>
///
/// <para>
/// Neither a backend test nor a frontend test can catch that alone, because the
/// bug lives in the gap between them. So this one reads both sources and asserts
/// the event names agree. It is deliberately a name-level check: it will not
/// catch an argument-order change, but it catches the failure that actually
/// happened, and it runs in the suite that actually runs.
/// </para>
/// </summary>
public sealed class AiAssistantHubContractTests
{
    /// <summary>Events the hub sends that the browser client must handle.</summary>
    private static readonly string[] RequiredClientHandlers =
    [
        "MessageDelta",
        "MessageComplete",
        "ToolCallStart",
        "ToolCallResult",
        "TurnError",
        "Citations",
        "VoiceTranscript",
    ];

    [Fact]
    public void EveryEventTheHubSends_IsHandledByTheBrowserClient()
    {
        var root = FindRepositoryRoot();
        var hub = File.ReadAllText(Path.Combine(root, "backend/src/OetLearner.Api/Hubs/AiAssistantHub.cs"));
        var client = File.ReadAllText(Path.Combine(root, "lib/ai-assistant/signalr.ts"));

        var sent = Regex.Matches(hub, @"SendAsync\(""(?<name>[A-Za-z]+)""")
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var handled = Regex.Matches(client, @"on\('(?<name>[A-Za-z]+)'")
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        // Sanity: if either regex stops matching, the test would pass vacuously.
        Assert.Contains("MessageDelta", sent);
        Assert.Contains("MessageDelta", handled);

        foreach (var name in RequiredClientHandlers)
        {
            Assert.True(
                sent.Contains(name),
                $"AiAssistantHub no longer sends '{name}'. If it was renamed, rename it in "
                + "lib/ai-assistant/signalr.ts too and update RequiredClientHandlers.");

            Assert.True(
                handled.Contains(name),
                $"lib/ai-assistant/signalr.ts does not handle '{name}', which AiAssistantHub sends. "
                + "An unhandled streaming event is silent: the UI just shows nothing.");
        }

        // Any handler the client registers must correspond to something the hub
        // actually sends, otherwise it is dead code that reads as working wiring.
        foreach (var name in handled)
        {
            Assert.True(
                sent.Contains(name),
                $"lib/ai-assistant/signalr.ts handles '{name}', which AiAssistantHub never sends.");
        }
    }

    [Fact]
    public void ClientDoesNotListenForTheOldPreContractEventNames()
    {
        var root = FindRepositoryRoot();
        var client = File.ReadAllText(Path.Combine(root, "lib/ai-assistant/signalr.ts"));

        Assert.DoesNotContain("on('TextDelta'", client, StringComparison.Ordinal);
        Assert.DoesNotContain("on('TurnComplete'", client, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend", "src", "OetLearner.Api")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
