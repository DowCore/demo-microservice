using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace Meta.Dow.SaaS.Orchestration;

public class DataSourceDto : EntityDto<Guid>
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Provider { get; set; } = null!;

    public string Family { get; set; } = null!;

    public DataSourceAccessMode AccessMode { get; set; }

    public List<string> AllowedOps { get; set; } = [];

    public bool IsEnabled { get; set; }

    public string? Description { get; set; }

    /// <summary>连接串已配置（不回传明文）</summary>
    public bool HasConnectionString { get; set; }

    /// <summary>掩码后的连接串提示，如 Host=***;Database=ord</summary>
    public string? ConnectionStringHint { get; set; }

    public DateTime CreationTime { get; set; }

    public DateTime? LastModificationTime { get; set; }
}

public class DataSourceGetListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }

    public bool? EnabledOnly { get; set; }
}

public class CreateDataSourceDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxDataSourceCodeLength)]
    public string Code { get; set; } = null!;

    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    [Required]
    [StringLength(32)]
    public string Provider { get; set; } = DataSourceProvider.Postgres;

    [Required]
    [StringLength(OrchestrationConsts.MaxConnectionStringLength)]
    public string ConnectionString { get; set; } = null!;

    public DataSourceAccessMode AccessMode { get; set; } = DataSourceAccessMode.ReadWrite;

    public List<string>? AllowedOps { get; set; }

    [StringLength(512)]
    public string? Description { get; set; }
}

public class UpdateDataSourceDto
{
    [Required]
    [StringLength(OrchestrationConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    [Required]
    [StringLength(32)]
    public string Provider { get; set; } = DataSourceProvider.Postgres;

    /// <summary>为空表示不修改连接串</summary>
    [StringLength(OrchestrationConsts.MaxConnectionStringLength)]
    public string? ConnectionString { get; set; }

    public DataSourceAccessMode AccessMode { get; set; } = DataSourceAccessMode.ReadWrite;

    public List<string>? AllowedOps { get; set; }

    public bool IsEnabled { get; set; } = true;

    [StringLength(512)]
    public string? Description { get; set; }
}

public class DataSourceLookupDto
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Provider { get; set; } = null!;

    public string Family { get; set; } = null!;

    public DataSourceAccessMode AccessMode { get; set; }
}

public class DataSourceProviderOptionDto
{
    public string Provider { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string Family { get; set; } = null!;

    public string ConnectionHint { get; set; } = null!;
}
