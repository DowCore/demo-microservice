using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Meta.Dow.Administration.DataPermission;

public interface IRoleDataScopeAppService : IApplicationService
{
    Task<List<RoleDataScopeDto>> GetListByRoleAsync(Guid roleId);

    Task<RoleDataScopeDto> SetAsync(SetRoleDataScopeDto input);

    Task DeleteAsync(Guid id);

    /// <summary>
    /// 解析当前用户对指定资源的数据范围。
    /// </summary>
    Task<DataScopeDto> GetMyAsync(string? resource = null);
}
