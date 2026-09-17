using System.ComponentModel.DataAnnotations;

namespace Meta.Dow.SaaS.Orchestration;

public class LogicRunInput
{
    /// <summary>原始 JSON body；优先于 Input 字典。</summary>
    public string? BodyJson { get; set; }
}

public class LogicRunResultDto
{
    public bool Success { get; set; }

    public Guid InstanceId { get; set; }

    public string? DataJson { get; set; }

    public string? Error { get; set; }

    public LogicRunMetaDto Meta { get; set; } = new();
}

public class LogicRunMetaDto
{
    public List<string> VisibleFields { get; set; } = [];

    public int OmittedFieldCount { get; set; }

    public string FlowKey { get; set; } = null!;

    public int Version { get; set; }

    /// <summary>查已发布定义（缓存命中时接近 0）</summary>
    public int LookupMs { get; set; }

    /// <summary>DSL 解释器执行（不含落库）</summary>
    public int ExecuteMs { get; set; }

    /// <summary>写入 FlowInstance</summary>
    public int PersistMs { get; set; }

    /// <summary>服务端总耗时（lookup+execute+persist）</summary>
    public int TotalMs { get; set; }
}

public class SystemParameterCatalogDto
{
    public List<SystemParameterItemDto> FixedKeys { get; set; } = [];

    public List<string> DateExpressionExamples { get; set; } = [];

    public string DateExpressionSyntax { get; set; } = "sys.<Anchor> <+|-> <n><unit> [ | <boundary> ]";
}

public class SystemParameterItemDto
{
    public string Key { get; set; } = null!;

    public string Category { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string? SampleValue { get; set; }
}

public class DateExpressionPreviewInput
{
    [Required]
    public string Expression { get; set; } = null!;

    public string? TimeZone { get; set; }
}

public class DateExpressionPreviewDto
{
    public string Expression { get; set; } = null!;

    public string Value { get; set; } = null!;
}
