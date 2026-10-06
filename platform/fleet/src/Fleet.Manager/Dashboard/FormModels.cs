namespace Fleet.Manager.Dashboard;

/// <summary>What the shared SSH-key fields need to render: a suffix that keeps element ids unique on a page and the user to prefill (never a secret).</summary>
public sealed record KeyFieldsModel(string IdSuffix, string? User);
