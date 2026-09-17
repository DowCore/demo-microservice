using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Meta.Dow.IdentityService.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Identity;

namespace Meta.Dow.IdentityService.OrganizationUnits;

[Authorize(IdentityServicePermissions.OrganizationUnits.Default)]
public class OrganizationUnitAppService : IdentityServiceAppService, IOrganizationUnitAppService
{
    private readonly OrganizationUnitManager _organizationUnitManager;
    private readonly IOrganizationUnitRepository _organizationUnitRepository;
    private readonly IdentityUserManager _userManager;

    public OrganizationUnitAppService(
        OrganizationUnitManager organizationUnitManager,
        IOrganizationUnitRepository organizationUnitRepository,
        IdentityUserManager userManager
    )
    {
        _organizationUnitManager = organizationUnitManager;
        _organizationUnitRepository = organizationUnitRepository;
        _userManager = userManager;
    }

    public async Task<ListResultDto<OrganizationUnitDto>> GetListAllAsync()
    {
        var list = await _organizationUnitRepository.GetListAsync(includeDetails: true);
        var dtos = list
            .OrderBy(x => x.Code)
            .Select(MapToDto)
            .ToList();
        return new ListResultDto<OrganizationUnitDto>(dtos);
    }

    public async Task<PagedResultDto<OrganizationUnitDto>> GetListAsync(GetOrganizationUnitListInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var list = await _organizationUnitRepository.GetListAsync(
            sorting: input.Sorting,
            maxResultCount: input.MaxResultCount,
            skipCount: input.SkipCount,
            includeDetails: true
        );

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            list = list
                .Where(x =>
                    x.DisplayName.Contains(input.Filter, StringComparison.OrdinalIgnoreCase)
                    || x.Code.Contains(input.Filter, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
        }

        var total = await _organizationUnitRepository.GetCountAsync();
        return new PagedResultDto<OrganizationUnitDto>(
            total,
            list.Select(MapToDto).ToList()
        );
    }

    public async Task<OrganizationUnitDto> GetAsync(Guid id)
    {
        var ou = await _organizationUnitRepository.GetAsync(id, includeDetails: true);
        return MapToDto(ou);
    }

    [Authorize(IdentityServicePermissions.OrganizationUnits.ManageOU)]
    public async Task<OrganizationUnitDto> CreateAsync(CreateOrganizationUnitDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Check.NotNullOrWhiteSpace(input.DisplayName, nameof(input.DisplayName));

        var ou = new OrganizationUnit(
            GuidGenerator.Create(),
            input.DisplayName.Trim(),
            input.ParentId,
            CurrentTenant.Id
        );
        await _organizationUnitManager.CreateAsync(ou);
        return MapToDto(ou);
    }

    [Authorize(IdentityServicePermissions.OrganizationUnits.ManageOU)]
    public async Task<OrganizationUnitDto> UpdateAsync(Guid id, UpdateOrganizationUnitDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Check.NotNullOrWhiteSpace(input.DisplayName, nameof(input.DisplayName));

        var ou = await _organizationUnitRepository.GetAsync(id);
        ou.DisplayName = input.DisplayName.Trim();
        await _organizationUnitManager.UpdateAsync(ou);
        return MapToDto(ou);
    }

    [Authorize(IdentityServicePermissions.OrganizationUnits.ManageOU)]
    public async Task DeleteAsync(Guid id)
    {
        await _organizationUnitManager.DeleteAsync(id);
    }

    [Authorize(IdentityServicePermissions.OrganizationUnits.ManageOU)]
    public async Task MoveAsync(Guid id, MoveOrganizationUnitDto input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.NewParentId == id)
        {
            throw new BusinessException("IdentityService:CannotMoveOrganizationUnitToItself");
        }

        if (input.NewParentId.HasValue)
        {
            var descendants = await _organizationUnitManager.FindChildrenAsync(id, recursive: true);
            if (descendants.Any(x => x.Id == input.NewParentId.Value))
            {
                throw new BusinessException("IdentityService:CannotMoveOrganizationUnitToDescendant");
            }
        }

        await _organizationUnitManager.MoveAsync(id, input.NewParentId);
    }

    public async Task<PagedResultDto<OrganizationUnitMemberDto>> GetMembersAsync(
        Guid id,
        GetOrganizationUnitMembersInput input
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        var ou = await _organizationUnitRepository.GetAsync(id);
        var members = await _organizationUnitRepository.GetMembersAsync(
            ou,
            sorting: input.Sorting,
            maxResultCount: input.MaxResultCount,
            skipCount: input.SkipCount,
            filter: input.Filter
        );
        var count = await _organizationUnitRepository.GetMembersCountAsync(ou, input.Filter);
        return new PagedResultDto<OrganizationUnitMemberDto>(
            count,
            members.Select(MapMember).ToList()
        );
    }

    [Authorize(IdentityServicePermissions.OrganizationUnits.ManageMembers)]
    public async Task AddMembersAsync(Guid id, OrganizationUnitUserIdsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ou = await _organizationUnitRepository.GetAsync(id);
        foreach (var userId in input.UserIds.Distinct())
        {
            var user = await _userManager.GetByIdAsync(userId);
            await _userManager.AddToOrganizationUnitAsync(user, ou);
        }
    }

    [Authorize(IdentityServicePermissions.OrganizationUnits.ManageMembers)]
    public async Task RemoveMemberAsync(Guid id, Guid memberId)
    {
        var ou = await _organizationUnitRepository.GetAsync(id);
        var user = await _userManager.GetByIdAsync(memberId);
        await _userManager.RemoveFromOrganizationUnitAsync(user, ou);
    }

    public async Task<PagedResultDto<OrganizationUnitRoleDto>> GetRolesAsync(
        Guid id,
        PagedAndSortedResultRequestDto input
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        var ou = await _organizationUnitRepository.GetAsync(id, includeDetails: true);
        var roles = await _organizationUnitRepository.GetRolesAsync(
            ou,
            sorting: input.Sorting,
            maxResultCount: input.MaxResultCount,
            skipCount: input.SkipCount
        );
        var count = await _organizationUnitRepository.GetRolesCountAsync(ou);
        return new PagedResultDto<OrganizationUnitRoleDto>(
            count,
            roles.Select(MapRole).ToList()
        );
    }

    [Authorize(IdentityServicePermissions.OrganizationUnits.ManageRoles)]
    public async Task AddRolesAsync(Guid id, OrganizationUnitRoleIdsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        foreach (var roleId in input.RoleIds.Distinct())
        {
            await _organizationUnitManager.AddRoleToOrganizationUnitAsync(roleId, id);
        }
    }

    [Authorize(IdentityServicePermissions.OrganizationUnits.ManageRoles)]
    public async Task RemoveRoleAsync(Guid id, Guid roleId)
    {
        await _organizationUnitManager.RemoveRoleFromOrganizationUnitAsync(roleId, id);
    }

    [Authorize(IdentityServicePermissions.OrganizationUnits.ManageMembers)]
    public async Task<PagedResultDto<OrganizationUnitMemberDto>> GetAvailableUsersAsync(
        GetAvailableOrganizationUnitUsersInput input
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        var ou = await _organizationUnitRepository.GetAsync(input.Id);
        var users = await _organizationUnitRepository.GetUnaddedUsersAsync(
            ou,
            sorting: input.Sorting,
            maxResultCount: input.MaxResultCount,
            skipCount: input.SkipCount,
            filter: input.Filter
        );
        var count = await _organizationUnitRepository.GetUnaddedUsersCountAsync(ou, input.Filter);
        return new PagedResultDto<OrganizationUnitMemberDto>(
            count,
            users.Select(MapMember).ToList()
        );
    }

    [Authorize(IdentityServicePermissions.OrganizationUnits.ManageRoles)]
    public async Task<PagedResultDto<OrganizationUnitRoleDto>> GetAvailableRolesAsync(
        GetAvailableOrganizationUnitRolesInput input
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        var ou = await _organizationUnitRepository.GetAsync(input.Id);
        var roles = await _organizationUnitRepository.GetUnaddedRolesAsync(
            ou,
            sorting: input.Sorting,
            maxResultCount: input.MaxResultCount,
            skipCount: input.SkipCount,
            filter: input.Filter
        );
        var count = await _organizationUnitRepository.GetUnaddedRolesCountAsync(ou, input.Filter);
        return new PagedResultDto<OrganizationUnitRoleDto>(
            count,
            roles.Select(MapRole).ToList()
        );
    }

    private static OrganizationUnitDto MapToDto(OrganizationUnit ou) =>
        new()
        {
            Id = ou.Id,
            ParentId = ou.ParentId,
            Code = ou.Code,
            DisplayName = ou.DisplayName,
        };

    private static OrganizationUnitMemberDto MapMember(IdentityUser user) =>
        new()
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            Name = user.Name,
            Surname = user.Surname,
        };

    private static OrganizationUnitRoleDto MapRole(IdentityRole role) =>
        new()
        {
            Id = role.Id,
            Name = role.Name,
            IsDefault = role.IsDefault,
            IsPublic = role.IsPublic,
            IsStatic = role.IsStatic,
        };
}
