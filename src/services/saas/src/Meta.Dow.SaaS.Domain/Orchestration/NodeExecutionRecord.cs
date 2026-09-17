namespace Meta.Dow.SaaS.Orchestration;

public class NodeExecutionRecord
{
    public string NodeId { get; set; } = null!;

    public string NodeType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? InputJson { get; set; }

    public string? OutputJson { get; set; }

    public string? Error { get; set; }

    public int DurationMs { get; set; }
}
