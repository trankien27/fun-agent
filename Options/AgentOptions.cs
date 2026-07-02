namespace FunStudio.WindowsMaintenance.Agent.Options;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string ServerUrl { get; set; } = "";
    public string MachineCode { get; set; } = "";
    public string AgentKey { get; set; } = "";
    public string AgentVersion { get; set; } = "1.0.0";
}
