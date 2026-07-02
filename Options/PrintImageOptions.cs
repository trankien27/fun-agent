namespace FunStudio.WindowsMaintenance.Agent.Options;

public sealed class PrintImageOptions
{
    public const string SectionName = "PrintImage";

    public string Url { get; set; } = "http://localhost:8066/api/printimage/printimage";
}
