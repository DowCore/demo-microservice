using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Meta.Dow.Administration.Menus;

public interface IMenuAppService : IApplicationService
{
    Task<List<MenuDto>> GetListAsync(string? systemCode = null);

    Task<MenuDto> GetAsync(Guid id);

    Task<MenuDto> CreateAsync(CreateUpdateMenuDto input);

    Task<MenuDto> UpdateAsync(Guid id, CreateUpdateMenuDto input);

    Task DeleteAsync(Guid id);

    /// <summary>
    /// 当前用户可访问的 Vben 动态路由菜单。
    /// </summary>
    Task<List<VbenRouteDto>> GetCurrentUserRoutesAsync(string? systemCode = null);
}
