namespace OetWithDrHesham.Api.Configuration;

public sealed class SoketiOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6001;
    public string AppId { get; set; } = "oet-app";
    public string AppKey { get; set; } = "oet-key";
    public string AppSecret { get; set; } = "oet-secret";
    public bool UseTls { get; set; }
    public bool Enabled { get; set; } = true;
}
