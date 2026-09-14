using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Meta.Dow.IdentityService.OrganizationUnits;

public interface IOrganizationUnitAppService : IApplicationService
{
    Task<ListResultDto<OrganizationUnitDto>> GetListAllAsync();

    Task<PagedResultDto<OrganizationUnitDto>> GetListAsync(GetOrganizationUnitListInput input);

    Task<OrganizationUnitDto> GetAsync(Guid id);

    Task<OrganizationUnitDto> CreateAsync(CreateOrganizationUnitDto input);

    Task<OrganizationUnitDto> UpdateAsync(Guid id, UpdateOrganizationUnitDto input);

    Task DeleteAsync(Guid id);

    Task MoveAsync(Guid id, MoveOrganizationUnitDto input);

    Task<PagedResultDto<OrganizationUnitMemberDto>> GetMembersAsync(
        Guid id,
        GetOrganizationUnitMembersInput input
    );

    Task AddMembersAsync(Guid id, OrganizationUnitUserIdsInput input);

    Task RemoveMemberAsync(Guid id, Guid memberId);

    Task<PagedResultDto<OrganizationUnitRoleDto>> GetRolesAsync(
        Guid id,
        PagedAndSortedResultRequestDto input
    );

    Task AddRolesAsync(Guid id, OrganizationUnitRoleIdsInput input);

    Task RemoveRoleAsync(Guid id, Guid roleId);

    Task<PagedResultDto<OrganizationUnitMemberDto>> GetAvailableUsersAsync(
        GetAvailableOrganizationUnitUsersInput input
    );

    Task<PagedResultDto<OrganizationUnitRoleDto>> GetAvailableRolesAsync(
        GetAvailableOrganizationUnitRolesInput input
    );
}

public class OrganizationUnitDto : ExtensibleEntityDto<Guid>
{
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public List<OrganizationUnitDto>? Children { get; set; }
}

public class CreateOrganizationUnitDto
{
    public Guid? ParentId { get; set; }
    public string DisplayName { get; set; } = null!;
}

public class UpdateOrganizationUnitDto
{
    public string DisplayName { get; set; } = null!;
}

public class MoveOrganizationUnitDto
{
    public Guid? NewParentId { get; set; }
}

public class GetOrganizationUnitListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }
}

public class GetOrganizationUnitMembersInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }
}

public class OrganizationUnitUserIdsInput
{
    public List<Guid> UserIds { get; set; } = [];
}

public class OrganizationUnitRoleIdsInput
{
    public List<Guid> RoleIds { get; set; } = [];
}

public class OrganizationUnitMemberDto : EntityDto<Guid>
{
    public string UserName { get; set; } = null!;
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Surname { get; set; }
}

public class OrganizationUnitRoleDto : EntityDto<Guid>
{
    public string Name { get; set; } = null!;
    public bool IsDefault { get; set; }
    public bool IsPublic { get; set; }
    public bool IsStatic { get; set; }
}

public class GetAvailableOrganizationUnitUsersInput : PagedAndSortedResultRequestDto
{
    public Guid Id { get; set; }
    public string? Filter { get; set; }
}

public class GetAvailableOrganizationUnitRolesInput : PagedAndSortedResultRequestDto
{
    public Guid Id { get; set; }
    public string? Filter { get; set; }
}
