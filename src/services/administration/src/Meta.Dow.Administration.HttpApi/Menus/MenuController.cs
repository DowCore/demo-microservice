using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.Dow.Administration.Menus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace Meta.Dow.Administration.Menus;

[Area(AdministrationRemoteServiceConsts.ModuleName)]
[RemoteService(Name = AdministrationRemoteServiceConsts.RemoteServiceName)]
[Route("api/administration/menus")]
[Authorize]
public class MenuController(IMenuAppService menuAppService) : AdministrationController, IMenuAppService
{
    private readonly IMenuAppService _menuAppService = menuAppService;

    [HttpGet]
    public Task<List<MenuDto>> GetListAsync(string? systemCode = null)
    {
        return _menuAppService.GetListAsync(systemCode);
    }

    [HttpGet]
    [Route("{id}")]
    public Task<MenuDto> GetAsync(Guid id)
    {
        return _menuAppService.GetAsync(id);
    }

    [HttpPost]
    public Task<MenuDto> CreateAsync(CreateUpdateMenuDto input)
    {
        return _menuAppService.CreateAsync(input);
    }

    [HttpPut]
    [Route("{id}")]
    public Task<MenuDto> UpdateAsync(Guid id, CreateUpdateMenuDto input)
    {
        return _menuAppService.UpdateAsync(id, input);
    }

    [HttpDelete]
    [Route("{id}")]
    public Task DeleteAsync(Guid id)
    {
        return _menuAppService.DeleteAsync(id);
    }

    [HttpGet]
    [Route("routes")]
    public Task<List<VbenRouteDto>> GetCurrentUserRoutesAsync(string? systemCode = null)
    {
        return _menuAppService.GetCurrentUserRoutesAsync(systemCode);
    }
}

/// <summary>
/// Vben 约定入口：GET /api/menu/all
/// </summary>
[Area(AdministrationRemoteServiceConsts.ModuleName)]
[RemoteService(Name = AdministrationRemoteServiceConsts.RemoteServiceName)]
[Route("api/menu")]
[Authorize]
public class MenuRouteController(IMenuAppService menuAppService) : AdministrationController
{
    private readonly IMenuAppService _menuAppService = menuAppService;

    [HttpGet]
    [Route("all")]
    public Task<List<VbenRouteDto>> GetAllAsync(string? systemCode = null)
    {
        return _menuAppService.GetCurrentUserRoutesAsync(systemCode);
    }
}
