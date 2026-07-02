namespace FunStudio.WindowsMaintenance.Agent.Options;

public sealed class PowerShellOptions
{
    public const string SectionName = "PowerShell";

    public int MaxTimeoutSeconds { get; set; } = 300;
    public int MaxOutputLength { get; set; } = 100_000;
    public bool AllowAdminPowerShell { get; set; } = true;
    public bool AllowUserPowerShell { get; set; } = true;
}
