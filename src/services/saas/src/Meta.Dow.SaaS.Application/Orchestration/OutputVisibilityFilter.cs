using System.Text.Json.Nodes;
using Volo.Abp.Users;

namespace Meta.Dow.SaaS.Orchestration;

public static class OutputVisibilityFilter
{
    public static JsonObject Filter(
        IEnumerable<FlowOutputParameterDsl> outputs,
        FlowRuntimeContext ctx,
        ICurrentUser currentUser,
        IReadOnlyCollection<string>? extraPermissions = null,
        bool bypass = false
    )
    {
        var data = new JsonObject();
        var roles = new HashSet<string>(currentUser.Roles ?? [], StringComparer.OrdinalIgnoreCase);
        var permissions = new HashSet<string>(extraPermissions ?? [], StringComparer.OrdinalIgnoreCase);

        foreach (var output in outputs)
        {
            if (string.IsNullOrWhiteSpace(output.Name))
            {
                continue;
            }

            if (!bypass && !IsVisible(output.VisibleTo, roles, permissions))
            {
                continue;
            }

            var value = string.IsNullOrWhiteSpace(output.From)
                ? FlowContextResolver.GetByPath(ctx.Vars, output.Name)
                : FlowContextResolver.ToJsonNode(FlowContextResolver.ResolvePath(output.From, ctx));

            data[output.Name] = value?.DeepClone();
        }

        return data;
    }

    public static List<string> ListVisibleFields(
        IEnumerable<FlowOutputParameterDsl> outputs,
        ICurrentUser currentUser,
        IReadOnlyCollection<string>? extraPermissions = null,
        bool bypass = false
    )
    {
        var roles = new HashSet<string>(currentUser.Roles ?? [], StringComparer.OrdinalIgnoreCase);
        var permissions = new HashSet<string>(extraPermissions ?? [], StringComparer.OrdinalIgnoreCase);
        return outputs
            .Where(o => !string.IsNullOrWhiteSpace(o.Name) && (bypass || IsVisible(o.VisibleTo, roles, permissions)))
            .Select(o => o.Name)
            .ToList();
    }

    public static bool IsVisible(
        FlowVisibleToDsl? visibleTo,
        ISet<string> roles,
        ISet<string> permissions
    )
    {
        if (visibleTo == null || string.IsNullOrWhiteSpace(visibleTo.Mode) ||
            visibleTo.Mode.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (visibleTo.Mode.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (visibleTo.Mode.Equals("roles", StringComparison.OrdinalIgnoreCase))
        {
            if (visibleTo.RoleNames is { Count: > 0 } && visibleTo.RoleNames.Any(roles.Contains))
            {
                return true;
            }

            if (visibleTo.RoleIds is { Count: > 0 } && visibleTo.RoleIds.Any(roles.Contains))
            {
                return true;
            }

            return false;
        }

        if (visibleTo.Mode.Equals("permissions", StringComparison.OrdinalIgnoreCase))
        {
            return visibleTo.Permissions is { Count: > 0 } &&
                   visibleTo.Permissions.Any(permissions.Contains);
        }

        return true;
    }
}
