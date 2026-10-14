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

    /// <summary>
    /// 异步作业 Id。契约文档曾承诺过该字段，但模型里一直不存在——
    /// 这正是"异步结果无法回收"的根因：没有可寻址的句柄，Wait 节点无从等待。
    /// </summary>
    public Guid? AsyncJobId { get; set; }
}
