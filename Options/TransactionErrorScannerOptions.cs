namespace FunStudio.WindowsMaintenance.Agent.Options;

public sealed class TransactionErrorScannerOptions
{
    public const string SectionName = "TransactionErrorScanner";

    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 300;
    public int SuccessStatus { get; set; } = 4;
    public int Limit { get; set; } = 200;
}
