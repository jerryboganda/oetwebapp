using Microsoft.AspNetCore.DataProtection;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>Opaque, conversation-bound provider metadata. Never a public message field.</summary>
public static class OpenCodeProviderState
{
    private static IDataProtector Protector(IDataProtectionProvider protection, string? conversation)
        => protection.CreateProtector("OpenCode.Reasoning.v1", conversation ?? "unscoped");

    public static string Protect(IDataProtectionProvider protection, string? conversation, string reasoning)
        => Protector(protection, conversation).Protect(reasoning);

    public static string Unprotect(IDataProtectionProvider protection, string? conversation, string state)
    {
        try { return Protector(protection, conversation).Unprotect(state); }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new InvalidOperationException("The gateway conversation state could not be resumed. Start a new conversation.");
        }
    }
}
