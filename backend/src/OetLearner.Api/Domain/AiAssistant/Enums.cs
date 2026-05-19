// TODO Phase 1: confirm enum values against AI-ASSISTANT-PLAN.md.
namespace OetLearner.Api.Domain.AiAssistant;

public enum AiProviderKind
{
    GitHubCopilot = 1,
    OpenAi = 2,
    Anthropic = 3,
    AzureOpenAi = 4,
    GitHubModels = 5,
    OpenAiCompatible = 6,
}

public enum AiChatMessageRole
{
    System = 1,
    User = 2,
    Assistant = 3,
    Tool = 4,
}

public enum AiToolApprovalPolicy
{
    Auto = 1,         // safe read-only
    RequireAdmin = 2, // mutates repo / runs commands
    Never = 3,        // kill-switch
}

public enum AiAuditAction
{
    ThreadCreated = 1,
    MessageSent = 2,
    ToolInvoked = 3,
    ToolApproved = 4,
    ToolRejected = 5,
    ProviderConfigChanged = 6,
    KillSwitchToggled = 7,
    ReindexTriggered = 8,
    SettingsChanged = 9,
}
