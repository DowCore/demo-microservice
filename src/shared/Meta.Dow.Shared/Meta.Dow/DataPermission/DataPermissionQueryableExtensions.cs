using System.Linq.Expressions;

namespace Meta.Dow.DataPermission;

public static class DataPermissionQueryableExtensions
{
    /// <summary>
    /// 按数据范围过滤。Self 要求实体同时实现 <see cref="IHasCreatorId"/>。
    /// </summary>
    public static IQueryable<T> ApplyDataPermission<T>(this IQueryable<T> query, DataScopeResult scope)
        where T : class, IHasOrganizationId
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(scope);

        return scope.Scope switch
        {
            DataScope.All => query,
            DataScope.Self => ApplySelf(query, scope.UserId),
            DataScope.Department or DataScope.DepartmentAndChildren or DataScope.Custom =>
                ApplyOrganizations(query, scope.OrganizationIds),
            _ => ApplySelf(query, scope.UserId),
        };
    }

    private static IQueryable<T> ApplySelf<T>(IQueryable<T> query, Guid? userId)
        where T : class, IHasOrganizationId
    {
        if (userId is null)
        {
            return query.Where(_ => false);
        }

        if (!typeof(IHasCreatorId).IsAssignableFrom(typeof(T)))
        {
            return query.Where(_ => false);
        }

        var parameter = Expression.Parameter(typeof(T), "x");
        var property = Expression.Property(parameter, nameof(IHasCreatorId.CreatorId));
        var constant = Expression.Constant(userId, typeof(Guid?));
        var equal = Expression.Equal(property, constant);
        var lambda = Expression.Lambda<Func<T, bool>>(equal, parameter);
        return query.Where(lambda);
    }

    private static IQueryable<T> ApplyOrganizations<T>(IQueryable<T> query, List<Guid> organizationIds)
        where T : class, IHasOrganizationId
    {
        if (organizationIds.Count == 0)
        {
            return query.Where(_ => false);
        }

        return query.Where(x => x.OrganizationId != null && organizationIds.Contains(x.OrganizationId.Value));
    }
}
