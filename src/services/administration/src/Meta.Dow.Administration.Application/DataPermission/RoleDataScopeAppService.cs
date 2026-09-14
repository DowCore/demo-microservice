using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Meta.Dow.Administration.Permissions;
using Meta.Dow.DataPermission;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.Administration.DataPermission;

[Authorize]
public class RoleDataScopeAppService : AdministrationAppService, IRoleDataScopeAppService
{
    private readonly IRepository<RoleDataScope, Guid> _repository;
    private readonly IDataScopeResolver _dataScopeResolver;

    public RoleDataScopeAppService(IRepository<RoleDataScope, Guid> repository, IDataScopeResolver dataScopeResolver)
    {
        _repository = repository;
        _dataScopeResolver = dataScopeResolver;
    }

    [Authorize(AdministrationPermissions.DataScopes.Default)]
    public async Task<List<RoleDataScopeDto>> GetListByRoleAsync(Guid roleId)
    {
        var list = await _repository.GetListAsync(x => x.RoleId == roleId);
        return ObjectMapper.Map<List<RoleDataScope>, List<RoleDataScopeDto>>(list);
    }

    [Authorize(AdministrationPermissions.DataScopes.Manage)]
    public async Task<RoleDataScopeDto> SetAsync(SetRoleDataScopeDto input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var existing = await _repository.FirstOrDefaultAsync(x =>
            x.RoleId == input.RoleId && x.Resource == input.Resource
        );

        if (existing is null)
        {
            existing = new RoleDataScope(
                GuidGenerator.Create(),
                input.RoleId,
                input.Resource,
                input.Scope,
                input.OrganizationIds,
                CurrentTenant.Id
            );
            await _repository.InsertAsync(existing, autoSave: true);
        }
        else
        {
            existing.SetScope(input.Scope, input.OrganizationIds);
            await _repository.UpdateAsync(existing, autoSave: true);
        }

        return ObjectMapper.Map<RoleDataScope, RoleDataScopeDto>(existing);
    }

    [Authorize(AdministrationPermissions.DataScopes.Manage)]
    public async Task DeleteAsync(Guid id)
    {
        await _repository.DeleteAsync(id);
    }

    public async Task<DataScopeDto> GetMyAsync(string? resource = null)
    {
        var result = await _dataScopeResolver.ResolveAsync(resource);
        return new DataScopeDto
        {
            Scope = result.Scope,
            UserId = result.UserId,
            OrganizationIds = result.OrganizationIds,
        };
    }
}
