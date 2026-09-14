using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Meta.Dow.Administration.DataPermission;
using Meta.Dow.DataPermission;
using Volo.Abp.Application.Dtos;

namespace Meta.Dow.Administration.DataPermission;

public class RoleDataScopeDto : EntityDto<Guid>
{
    public Guid RoleId { get; set; }

    public string Resource { get; set; } = DataPermissionConsts.DefaultResource;

    public DataScope Scope { get; set; }

    public List<Guid> OrganizationIds { get; set; } = [];
}

public class SetRoleDataScopeDto
{
    [Required]
    public Guid RoleId { get; set; }

    [Required]
    [StringLength(DataPermissionConsts.MaxResourceLength)]
    public string Resource { get; set; } = DataPermissionConsts.DefaultResource;

    public DataScope Scope { get; set; }

    public List<Guid> OrganizationIds { get; set; } = [];
}

public class DataScopeDto
{
    public DataScope Scope { get; set; }

    public Guid? UserId { get; set; }

    public List<Guid> OrganizationIds { get; set; } = [];
}
