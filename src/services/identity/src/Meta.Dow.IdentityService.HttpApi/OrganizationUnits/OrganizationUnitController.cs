using System;
using System.Threading.Tasks;
using Meta.Dow.IdentityService.OrganizationUnits;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace Meta.Dow.IdentityService.OrganizationUnits;

[RemoteService(Name = IdentityServiceRemoteServiceConsts.RemoteServiceName)]
[Area(IdentityServiceRemoteServiceConsts.ModuleName)]
[Route("api/identity/organization-units")]
public class OrganizationUnitController : IdentityServiceController, IOrganizationUnitAppService
{
    private readonly IOrganizationUnitAppService _service;

    public OrganizationUnitController(IOrganizationUnitAppService service)
    {
        _service = service;
    }

    [HttpGet]
    [Route("all")]
    public Task<ListResultDto<OrganizationUnitDto>> GetListAllAsync()
    {
        return _service.GetListAllAsync();
    }

    [HttpGet]
    public Task<PagedResultDto<OrganizationUnitDto>> GetListAsync(GetOrganizationUnitListInput input)
    {
        return _service.GetListAsync(input);
    }

    [HttpGet]
    [Route("{id:guid}")]
    public Task<OrganizationUnitDto> GetAsync(Guid id)
    {
        return _service.GetAsync(id);
    }

    [HttpPost]
    public Task<OrganizationUnitDto> CreateAsync(CreateOrganizationUnitDto input)
    {
        return _service.CreateAsync(input);
    }

    [HttpPut]
    [Route("{id:guid}")]
    public Task<OrganizationUnitDto> UpdateAsync(Guid id, UpdateOrganizationUnitDto input)
    {
        return _service.UpdateAsync(id, input);
    }

    [HttpDelete]
    [Route("{id:guid}")]
    public Task DeleteAsync(Guid id)
    {
        return _service.DeleteAsync(id);
    }

    [HttpPut]
    [Route("{id:guid}/move")]
    public Task MoveAsync(Guid id, MoveOrganizationUnitDto input)
    {
        return _service.MoveAsync(id, input);
    }

    [HttpGet]
    [Route("{id:guid}/members")]
    public Task<PagedResultDto<OrganizationUnitMemberDto>> GetMembersAsync(
        Guid id,
        GetOrganizationUnitMembersInput input
    )
    {
        return _service.GetMembersAsync(id, input);
    }

    [HttpPut]
    [Route("{id:guid}/members")]
    public Task AddMembersAsync(Guid id, OrganizationUnitUserIdsInput input)
    {
        return _service.AddMembersAsync(id, input);
    }

    [HttpDelete]
    [Route("{id:guid}/members/{memberId:guid}")]
    public Task RemoveMemberAsync(Guid id, Guid memberId)
    {
        return _service.RemoveMemberAsync(id, memberId);
    }

    [HttpGet]
    [Route("{id:guid}/roles")]
    public Task<PagedResultDto<OrganizationUnitRoleDto>> GetRolesAsync(
        Guid id,
        PagedAndSortedResultRequestDto input
    )
    {
        return _service.GetRolesAsync(id, input);
    }

    [HttpPut]
    [Route("{id:guid}/roles")]
    public Task AddRolesAsync(Guid id, OrganizationUnitRoleIdsInput input)
    {
        return _service.AddRolesAsync(id, input);
    }

    [HttpDelete]
    [Route("{id:guid}/roles/{roleId:guid}")]
    public Task RemoveRoleAsync(Guid id, Guid roleId)
    {
        return _service.RemoveRoleAsync(id, roleId);
    }

    [HttpGet]
    [Route("available-users")]
    public Task<PagedResultDto<OrganizationUnitMemberDto>> GetAvailableUsersAsync(
        GetAvailableOrganizationUnitUsersInput input
    )
    {
        return _service.GetAvailableUsersAsync(input);
    }

    [HttpGet]
    [Route("available-roles")]
    public Task<PagedResultDto<OrganizationUnitRoleDto>> GetAvailableRolesAsync(
        GetAvailableOrganizationUnitRolesInput input
    )
    {
        return _service.GetAvailableRolesAsync(input);
    }
}
