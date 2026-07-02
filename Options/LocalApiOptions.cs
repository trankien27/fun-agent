namespace FunStudio.WindowsMaintenance.Agent.Options;

public sealed class LocalApiOptions
{
    public const string SectionName = "LocalApi";

    public bool Enabled { get; set; } = true;
    public string[] Urls { get; set; } = ["http://localhost:8787"];
    public bool RequireApiKey { get; set; }
    public string ApiKey { get; set; } = "";
}
