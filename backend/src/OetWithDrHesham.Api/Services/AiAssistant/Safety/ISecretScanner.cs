namespace OetWithDrHesham.Api.Services.AiAssistant.Safety;

/// <summary>
/// Scans content for leaked secrets/credentials before allowing writes.
/// </summary>
public interface ISecretScanner
{
    SecretScanResult Scan(string content);
}

public sealed record SecretScanResult(bool HasSecrets, IReadOnlyList<SecretFinding> Findings);

public sealed record SecretFinding(string Type, int Line, string Snippet);
