namespace FunStudio.WindowsMaintenance.Agent.Options;

public sealed class TransactionOptions
{
    public const string SectionName = "Transactions";

    public string DatabasePath { get; set; } = @"D:\Work\PhotoBooth\Data\Funstudio.db";
    public string TableName { get; set; } = "Transactions";
    public int Limit { get; set; } = 200;
    public string Query { get; set; } = "";
}
