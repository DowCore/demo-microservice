using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace Meta.Dow.SaaS.Orchestration;

public class MessageSourceDto : EntityDto<Guid>
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public bool IsEnabled { get; set; }
    public string? Description { get; set; }
    public bool HasConnectionString { get; set; }
    public string? ConnectionStringHint { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

public class MessageSourceGetListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }
    public bool? EnabledOnly { get; set; }
}

public class CreateMessageSourceDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxMessageSourceCodeLength)]
    public string Code { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    [Required]
    [StringLength(32)]
    public string Provider { get; set; } = MessageSourceProvider.Rabbit;

    [StringLength(OrchestrationConsts.MaxConnectionStringLength)]
    public string? ConnectionString { get; set; }

    [StringLength(512)]
    public string? Description { get; set; }
}

public class UpdateMessageSourceDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    [Required]
    [StringLength(32)]
    public string Provider { get; set; } = MessageSourceProvider.Rabbit;

    /// <summary>null=不改；空串=改回平台默认</summary>
    [StringLength(OrchestrationConsts.MaxConnectionStringLength)]
    public string? ConnectionString { get; set; }

    public bool ClearConnectionString { get; set; }

    public bool IsEnabled { get; set; } = true;

    [StringLength(512)]
    public string? Description { get; set; }
}

public class MessageSourceLookupDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Provider { get; set; } = null!;
}

public class MessageSourceProviderOptionDto
{
    public string Provider { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string ConnectionHint { get; set; } = null!;
    public bool ConsumeSupported { get; set; }
}

public class FlowTriggerDto : EntityDto<Guid>
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public FlowTriggerType TriggerType { get; set; }
    public string FlowKey { get; set; } = null!;
    public string MessageSourceCode { get; set; } = null!;
    public string Queue { get; set; } = null!;
    public string? Exchange { get; set; }
    public string? ExchangeType { get; set; }
    public string? RoutingKey { get; set; }
    public bool IsEnabled { get; set; }
    public string? Description { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

public class FlowTriggerGetListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }
    public bool? EnabledOnly { get; set; }
}

public class CreateFlowTriggerDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxTriggerCodeLength)]
    public string Code { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxCodeLength)]
    public string FlowKey { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxMessageSourceCodeLength)]
    public string MessageSourceCode { get; set; } = null!;

    [Required]
    [StringLength(128)]
    public string Queue { get; set; } = null!;

    [StringLength(128)]
    public string? Exchange { get; set; }

    [StringLength(32)]
    public string? ExchangeType { get; set; } = "fanout";

    [StringLength(128)]
    public string? RoutingKey { get; set; }

    [StringLength(512)]
    public string? Description { get; set; }
}

public class UpdateFlowTriggerDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxCodeLength)]
    public string FlowKey { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxMessageSourceCodeLength)]
    public string MessageSourceCode { get; set; } = null!;

    [Required]
    [StringLength(128)]
    public string Queue { get; set; } = null!;

    [StringLength(128)]
    public string? Exchange { get; set; }

    [StringLength(32)]
    public string? ExchangeType { get; set; }

    [StringLength(128)]
    public string? RoutingKey { get; set; }

    public bool IsEnabled { get; set; } = true;

    [StringLength(512)]
    public string? Description { get; set; }
}

public class FlowScheduleDto : EntityDto<Guid>
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string FlowKey { get; set; } = null!;
    public string Cron { get; set; } = null!;
    public string TimeZone { get; set; } = null!;
    public string? VariablesJson { get; set; }
    public bool IsEnabled { get; set; }
    public string? Description { get; set; }
    public DateTime? LastFiredAt { get; set; }
    public DateTime? NextFireAt { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

public class FlowScheduleGetListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }
    public bool? EnabledOnly { get; set; }
}

public class CreateFlowScheduleDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxScheduleCodeLength)]
    public string Code { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxCodeLength)]
    public string FlowKey { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxCronLength)]
    public string Cron { get; set; } = null!;

    [StringLength(OrchestrationConsts.MaxTimeZoneLength)]
    public string? TimeZone { get; set; } = "UTC";

    public string? VariablesJson { get; set; }

    [StringLength(512)]
    public string? Description { get; set; }
}

public class UpdateFlowScheduleDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxCodeLength)]
    public string FlowKey { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxCronLength)]
    public string Cron { get; set; } = null!;

    [StringLength(OrchestrationConsts.MaxTimeZoneLength)]
    public string? TimeZone { get; set; }

    public string? VariablesJson { get; set; }

    public bool IsEnabled { get; set; } = true;

    [StringLength(512)]
    public string? Description { get; set; }
}
