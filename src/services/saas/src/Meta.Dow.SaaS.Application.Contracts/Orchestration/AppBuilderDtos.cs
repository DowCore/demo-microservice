using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace Meta.Dow.SaaS.Orchestration;

public class TableColumnDto
{
    public string Name { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string PlatformType { get; set; } = TablePlatformType.String;

    public int? Length { get; set; }

    public int? Precision { get; set; }

    public int? Scale { get; set; }

    public bool Nullable { get; set; }

    public string? Default { get; set; }

    public bool Unique { get; set; }

    public string? Comment { get; set; }

    public string Origin { get; set; } = TableOrigin.User;

    public string? AppliedName { get; set; }
}

public class TableIndexColumnDto
{
    public string Name { get; set; } = null!;

    public bool Descending { get; set; }
}

public class TableIndexDto
{
    public string Name { get; set; } = null!;

    public bool Unique { get; set; }

    public bool IsPrimary { get; set; }

    public string Origin { get; set; } = TableOrigin.User;

    public List<TableIndexColumnDto> Columns { get; set; } = [];
}

public class TableDefinitionDto : EntityDto<Guid>
{
    public string DataSourceCode { get; set; } = null!;

    public string TableName { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string Origin { get; set; } = null!;

    public string SyncState { get; set; } = null!;

    public DateTime? LastAppliedAt { get; set; }

    public string? Comment { get; set; }

    public List<TableColumnDto> Columns { get; set; } = [];

    public List<TableIndexDto> Indexes { get; set; } = [];

    public DateTime CreationTime { get; set; }
}

public class TableDefinitionGetListInput : PagedAndSortedResultRequestDto
{
    public string? DataSourceCode { get; set; }

    public string? Filter { get; set; }
}

public class CreateTableDefinitionDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxDataSourceCodeLength)]
    public string DataSourceCode { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxTableNameLength)]
    public string TableName { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string DisplayName { get; set; } = null!;
}

public class UpdateTableDefinitionDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string DisplayName { get; set; } = null!;

    public string? Comment { get; set; }

    public List<TableColumnDto> Columns { get; set; } = [];

    public List<TableIndexDto> Indexes { get; set; } = [];
}

public class DdlPreviewItemDto
{
    public string Kind { get; set; } = null!;

    public string Sql { get; set; } = null!;

    public bool Destructive { get; set; }
}

public class AppResourceDto : EntityDto<Guid>
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string DataSourceCode { get; set; } = null!;

    public string TableName { get; set; } = null!;

    public string TitleField { get; set; } = null!;

    public int Status { get; set; }

    public string QueryFlowKey { get; set; } = null!;

    public string GetFlowKey { get; set; } = null!;

    public string CreateFlowKey { get; set; } = null!;

    public string UpdateFlowKey { get; set; } = null!;

    public string DeleteFlowKey { get; set; } = null!;

    public FilterDef Filter { get; set; } = new();

    public ListViewDef ListView { get; set; } = new();

    public FormDef Form { get; set; } = new();

    public DateTime CreationTime { get; set; }
}

public class AppResourceGetListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }

    public int? Status { get; set; }
}

public class CreateAppResourceDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxResourceCodeLength)]
    public string Code { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    public Guid TableDefinitionId { get; set; }
}

public class UpdateAppResourceDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    public string? TitleField { get; set; }

    public FilterDef? Filter { get; set; }

    public ListViewDef? ListView { get; set; }

    public FormDef? Form { get; set; }

    public string? QueryFlowKey { get; set; }

    public string? GetFlowKey { get; set; }

    public string? CreateFlowKey { get; set; }

    public string? UpdateFlowKey { get; set; }

    public string? DeleteFlowKey { get; set; }
}

public class ResourceRuntimeQueryInput
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public string? Sorting { get; set; }

    public object? Filters { get; set; }

    public object? Filter { get; set; }

    public List<SummaryFieldInput> SummaryFields { get; set; } = [];
}

public class SummaryFieldInput
{
    public string Field { get; set; } = null!;

    public string Fn { get; set; } = "sum";
}

public class ResourceRuntimeGetInput
{
    [Required]
    public string Id { get; set; } = null!;
}

public class ResourceRuntimeSaveInput
{
    public string? Id { get; set; }

    public string? ConcurrencyStamp { get; set; }

    public Dictionary<string, object?> Record { get; set; } = [];
}

public class ResourceInvokeResultDto
{
    public bool Success { get; set; }

    public Guid InstanceId { get; set; }

    public object? Data { get; set; }

    public string? Error { get; set; }
}

public class ReportDefinitionDto : EntityDto<Guid>
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Kind { get; set; } = ReportKind.Resource;

    public string? ResourceCode { get; set; }

    public string QueryFlowKey { get; set; } = null!;

    public int Status { get; set; }

    public FilterNode DataScope { get; set; } = new();

    public SearchFormDef SearchForm { get; set; } = new();

    public bool AdvancedFilter { get; set; } = true;

    public List<NamedFilterPreset> Presets { get; set; } = [];

    public List<KpiDef> Kpis { get; set; } = [];

    public List<ListColumnDef> Columns { get; set; } = [];

    public List<ActionDef> Actions { get; set; } = [];

    public string? DefaultSorting { get; set; }

    public int PageSize { get; set; } = 20;

    public bool SelectionEnabled { get; set; }

    public string? Description { get; set; }

    public DateTime CreationTime { get; set; }
}

public class ReportDefinitionGetListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }

    public int? Status { get; set; }
}

public class CreateReportDefinitionDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxResourceCodeLength)]
    public string Code { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    public string Kind { get; set; } = ReportKind.Resource;

    public string? ResourceCode { get; set; }
}

public class CreateReportFromResourceDto
{
    [Required]
    public string ResourceCode { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxResourceCodeLength)]
    public string Code { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string Name { get; set; } = null!;
}

public class UpdateReportDefinitionDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    public string Kind { get; set; } = ReportKind.Resource;

    public string? ResourceCode { get; set; }

    public string? QueryFlowKey { get; set; }

    public FilterNode? DataScope { get; set; }

    public SearchFormDef? SearchForm { get; set; }

    public bool AdvancedFilter { get; set; } = true;

    public List<NamedFilterPreset> Presets { get; set; } = [];

    public List<KpiDef> Kpis { get; set; } = [];

    public List<ListColumnDef> Columns { get; set; } = [];

    public List<ActionDef> Actions { get; set; } = [];

    public string? DefaultSorting { get; set; }

    public int PageSize { get; set; } = 20;

    public bool SelectionEnabled { get; set; }

    public string? Description { get; set; }
}
