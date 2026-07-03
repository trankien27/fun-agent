using System.Text.Json;

namespace FunStudio.WindowsMaintenance.Agent.Models;

public sealed class TransactionErrorQueuePushRequest
{
    public List<TransactionErrorQueuePushItem> Items { get; set; } = [];
}

public sealed class TransactionErrorQueuePushItem
{
    public string TransactionId { get; set; } = "";
    public string TransactionCode { get; set; } = "";
    public int? Status { get; set; }
    public JsonElement Values { get; set; }
}
