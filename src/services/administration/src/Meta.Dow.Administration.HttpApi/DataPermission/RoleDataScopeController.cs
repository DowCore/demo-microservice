using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.Dow.Administration.DataPermission;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace Meta.Dow.Administration.DataPermission;

[Area(AdministrationRemoteServiceConsts.ModuleName)]
[RemoteService(Name = AdministrationRemoteServiceConsts.RemoteServiceName)]
[Route("api/administration/role-data-scopes")]
[Authorize]
public class RoleDataScopeController(IRoleDataScopeAppService appService)
    : AdministrationController,
        IRoleDataScopeAppService
{
    private readonly IRoleDataScopeAppService _appService = appService;

    [HttpGet]
    [Route("by-role/{roleId}")]
    public Task<List<RoleDataScopeDto>> GetListByRoleAsync(Guid roleId)
    {
        return _appService.GetListByRoleAsync(roleId);
    }

    [HttpPut]
    public Task<RoleDataScopeDto> SetAsync(SetRoleDataScopeDto input)
    {
        return _appService.SetAsync(input);
    }

    [HttpDelete]
    [Route("{id}")]
    public Task DeleteAsync(Guid id)
    {
        return _appService.DeleteAsync(id);
    }

    [HttpGet]
    [Route("my")]
    public Task<DataScopeDto> GetMyAsync(string? resource = null)
    {
        return _appService.GetMyAsync(resource);
    }
}
