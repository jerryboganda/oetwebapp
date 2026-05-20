using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.AiAssistant.Safety;

/// <summary>
/// Regex-based secret scanner. Detects API keys, JWTs, connection strings,
/// private keys, and passwords in common formats.
/// </summary>
public sealed class SecretScanner : ISecretScanner
{
    private static readonly (string Type, Regex Pattern)[] Patterns =
    [
        ("OpenAI API Key", new Regex(@"sk-[A-Za-z0-9]{20,}", RegexOptions.Compiled)),
        ("GitHub PAT", new Regex(@"ghp_[A-Za-z0-9]{36,}", RegexOptions.Compiled)),
        ("GitHub OAuth", new Regex(@"gho_[A-Za-z0-9]{36,}", RegexOptions.Compiled)),
        ("AWS Access Key", new Regex(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled)),
        ("AWS Secret Key", new Regex(@"(?i)aws.{0,20}secret.{0,10}['\""=:\s]+([A-Za-z0-9/+=]{40})", RegexOptions.Compiled)),
        ("JWT", new Regex(@"eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}", RegexOptions.Compiled)),
        ("Connection String", new Regex(@"(?i)(Server|Data Source|Host)=[^;]+;.*(Password|Pwd)=[^;]+", RegexOptions.Compiled)),
        ("Private Key", new Regex(@"-----BEGIN (RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----", RegexOptions.Compiled)),
        ("Generic Secret Assignment", new Regex(@"(?i)(password|secret|token|api[_-]?key)\s*[:=]\s*['""][^'""]{8,}['""]", RegexOptions.Compiled)),
        ("Stripe Key", new Regex(@"(sk|pk)_(test|live)_[A-Za-z0-9]{20,}", RegexOptions.Compiled)),
        ("Slack Token", new Regex(@"xox[baprs]-[0-9]{10,13}-[A-Za-z0-9-]+", RegexOptions.Compiled)),
        ("Azure Storage Key", new Regex(@"DefaultEndpointsProtocol=https;AccountName=[^;]+;AccountKey=[A-Za-z0-9+/=]{44,}", RegexOptions.Compiled)),
    ];

    public SecretScanResult Scan(string content)
    {
        if (string.IsNullOrEmpty(content))
            return new SecretScanResult(false, []);

        var findings = new List<SecretFinding>();
        var lines = content.Split('\n');

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            foreach (var (type, pattern) in Patterns)
            {
                var match = pattern.Match(line);
                if (match.Success)
                {
                    var snippet = MaskSecret(match.Value);
                    findings.Add(new SecretFinding(type, lineIndex + 1, snippet));
                }
            }
        }

        return new SecretScanResult(findings.Count > 0, findings);
    }

    private static string MaskSecret(string value)
    {
        if (value.Length <= 8)
            return new string('*', value.Length);

        // Show first 4 and last 4, mask the rest
        var prefixLen = Math.Min(4, value.Length / 4);
        var suffixLen = Math.Min(4, value.Length / 4);
        var masked = value.Length - prefixLen - suffixLen;
        return $"{value[..prefixLen]}{new string('*', Math.Max(masked, 4))}{value[^suffixLen..]}";
    }
}
