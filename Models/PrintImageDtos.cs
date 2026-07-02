namespace FunStudio.WindowsMaintenance.Agent.Models;

public sealed class PrintImageRequest
{
    public string TransactionId { get; set; } = "";
    public int LayoutId { get; set; }
    public int NumberOfImage { get; set; }
}

public sealed class TransactionListItem
{
    public string TransactionId { get; set; } = "";
    public Dictionary<string, object?> Values { get; set; } = [];
}
