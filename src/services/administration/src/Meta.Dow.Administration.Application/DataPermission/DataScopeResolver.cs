using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meta.Dow.Administration.DataPermission;
using Meta.Dow.DataPermission;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace Meta.Dow.Administration.DataPermission;

public interface IDataScopeResolver
{
    Task<DataScopeResult> ResolveAsync(string? resource = null, CancellationToken cancellationToken = default);
}

public class DataScopeResolver : IDataScopeResolver, ITransientDependency
{
    private readonly IRepository<RoleDataScope, Guid> _roleDataScopeRepository;
    private readonly IIdentityUserRepository _userRepository;
    private readonly IOrganizationUnitRepository _organizationUnitRepository;
    private readonly ICurrentUser _currentUser;

    public DataScopeResolver(
        IRepository<RoleDataScope, Guid> roleDataScopeRepository,
        IIdentityUserRepository userRepository,
        IOrganizationUnitRepository organizationUnitRepository,
        ICurrentUser currentUser
    )
    {
        _roleDataScopeRepository = roleDataScopeRepository;
        _userRepository = userRepository;
        _organizationUnitRepository = organizationUnitRepository;
        _currentUser = currentUser;
    }

    public async Task<DataScopeResult> ResolveAsync(
        string? resource = null,
        CancellationToken cancellationToken = default
    )
    {
        var userId = _currentUser.Id;
        if (userId is null)
        {
            return DataScopeResult.Deny(null);
        }

        resource = string.IsNullOrWhiteSpace(resource) ? DataPermissionConsts.DefaultResource : resource.Trim();

        if (_currentUser.Roles.Any(r => string.Equals(r, "admin", StringComparison.OrdinalIgnoreCase)))
        {
            return DataScopeResult.AllowAll(userId);
        }

        var user = await _userRepository.FindAsync(userId.Value, includeDetails: true, cancellationToken: cancellationToken);
        if (user is null)
        {
            return DataScopeResult.Deny(userId);
        }

        var roleIds = user.Roles.Select(r => r.RoleId).ToHashSet();
        var policies = await _roleDataScopeRepository.GetListAsync(
            x =>
                roleIds.Contains(x.RoleId)
                && (x.Resource == resource || x.Resource == DataPermissionConsts.DefaultResource),
            cancellationToken: cancellationToken
        );

        if (policies.Count == 0)
        {
            return DataScopeResult.Deny(userId);
        }

        // 精确资源优先于默认 *
        var resourcePolicies = policies.Where(p => p.Resource == resource).ToList();
        if (resourcePolicies.Count == 0)
        {
            resourcePolicies = policies.Where(p => p.Resource == DataPermissionConsts.DefaultResource).ToList();
        }

        var widest = resourcePolicies.OrderByDescending(p => (int)p.Scope).First();

        if (widest.Scope == DataScope.All)
        {
            return DataScopeResult.AllowAll(userId);
        }

        if (widest.Scope == DataScope.Self)
        {
            return new DataScopeResult { Scope = DataScope.Self, UserId = userId };
        }

        if (widest.Scope == DataScope.Custom)
        {
            var orgIds = resourcePolicies
                .Where(p => p.Scope == DataScope.Custom)
                .SelectMany(p => p.OrganizationIds)
                .Distinct()
                .ToList();
            return new DataScopeResult
            {
                Scope = DataScope.Custom,
                UserId = userId,
                OrganizationIds = orgIds,
            };
        }

        var userOuIds = user.OrganizationUnits.Select(x => x.OrganizationUnitId).ToList();
        if (userOuIds.Count == 0)
        {
            return DataScopeResult.Deny(userId);
        }

        if (widest.Scope == DataScope.Department)
        {
            return new DataScopeResult
            {
                Scope = DataScope.Department,
                UserId = userId,
                OrganizationIds = userOuIds,
            };
        }

        var allUnits = await _organizationUnitRepository.GetListAsync(cancellationToken: cancellationToken);
        var rootCodes = allUnits.Where(u => userOuIds.Contains(u.Id)).Select(u => u.Code).ToList();
        var expanded = allUnits
            .Where(u => rootCodes.Any(code => u.Code.StartsWith(code, StringComparison.Ordinal)))
            .Select(u => u.Id)
            .Distinct()
            .ToList();

        return new DataScopeResult
        {
            Scope = DataScope.DepartmentAndChildren,
            UserId = userId,
            OrganizationIds = expanded,
        };
    }
}
