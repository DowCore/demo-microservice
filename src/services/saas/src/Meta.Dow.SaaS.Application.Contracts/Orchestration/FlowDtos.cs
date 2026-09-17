using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Meta.Dow.SaaS.Orchestration;
using Volo.Abp.Application.Dtos;

namespace Meta.Dow.SaaS.Orchestration;

public class FlowDefinitionDto : FullAuditedEntityDto<Guid>
{
    public string Name { get; set; } = null!;

    public string Code { get; set; } = null!;

    public string? Category { get; set; }

    public FlowDefinitionStatus Status { get; set; }

    public string? GraphJson { get; set; }

    public string? DslJson { get; set; }

    public int? PublishedVersion { get; set; }
}

public class CreateFlowDefinitionDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxCodeLength)]
    public string Code { get; set; } = null!;

    [StringLength(OrchestrationConsts.MaxCategoryLength)]
    public string? Category { get; set; }

    public string? GraphJson { get; set; }

    public string? DslJson { get; set; }
}

public class UpdateFlowDefinitionDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    [StringLength(OrchestrationConsts.MaxCategoryLength)]
    public string? Category { get; set; }

    public string? GraphJson { get; set; }

    public string? DslJson { get; set; }
}

public class FlowDefinitionGetListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }

    public FlowDefinitionStatus? Status { get; set; }
}

public class FlowVersionDto : EntityDto<Guid>
{
    public Guid DefinitionId { get; set; }

    public int Version { get; set; }

    public string GraphJson { get; set; } = null!;

    public string DslJson { get; set; } = null!;

    public DateTime CreationTime { get; set; }

    public Guid? CreatorId { get; set; }
}

public class NodeExecutionDto
{
    public string NodeId { get; set; } = null!;

    public string NodeType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? InputJson { get; set; }

    public string? OutputJson { get; set; }

    public string? Error { get; set; }

    public int DurationMs { get; set; }
}

public class FlowInstanceDto : CreationAuditedEntityDto<Guid>
{
    public Guid DefinitionId { get; set; }

    public string DefinitionName { get; set; } = null!;

    public int Version { get; set; }

    public FlowInstanceStatus Status { get; set; }

    public string? VariablesJson { get; set; }

    public string? Error { get; set; }

    public bool IsDryRun { get; set; }

    public List<NodeExecutionDto> Nodes { get; set; } = [];
}

public class FlowInstanceGetListInput : PagedAndSortedResultRequestDto
{
    public Guid? DefinitionId { get; set; }

    public FlowInstanceStatus? Status { get; set; }
}

public class StartFlowInstanceDto
{
    [Required]
    public Guid DefinitionId { get; set; }

    public int? Version { get; set; }

    public string? VariablesJson { get; set; }
}

public class DryRunFlowInstanceDto
{
    [Required]
    public Guid DefinitionId { get; set; }

    public string? GraphJson { get; set; }

    public string? DslJson { get; set; }

    public string? VariablesJson { get; set; }
}
