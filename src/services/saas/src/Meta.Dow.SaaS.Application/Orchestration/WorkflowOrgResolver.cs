using System;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>按组织架构解析 OA 流程里的「直属部门主管」。</summary>
public interface IWorkflowOrgResolver
{
    /// <summary>
    /// 解析某用户的直属部门主管用户名；无组织数据或找不到主管时返回 null。
    /// </summary>
    /// <param name="userName">发起人用户名。</param>
    /// <param name="managerRoleName">标识「主管」的角色码；留空默认 <c>manager</c>。</param>
    Task<string?> ResolveDirectManagerAsync(string userName, string? managerRoleName);
}

/// <summary>
/// 直属部门主管解析。依托 ABP Identity 的组织架构（AbpOrganizationUnits）——
/// SaaS 宿主已加载 Identity MongoDB 模块，故可在进程内查询，无需新建跨服务管道。
/// 约定：某组织单元的主管 = 该单元（或其上级）中拥有指定角色（默认 <c>manager</c>）的成员。
/// </summary>
public class WorkflowOrgResolver : IWorkflowOrgResolver, ITransientDependency
{
    private const string DefaultManagerRole = "manager";

    private readonly IdentityUserManager _userManager;
    private readonly IOrganizationUnitRepository _ouRepository;

    public WorkflowOrgResolver(IdentityUserManager userManager, IOrganizationUnitRepository ouRepository)
    {
        _userManager = userManager;
        _ouRepository = ouRepository;
    }

    public async Task<string?> ResolveDirectManagerAsync(string userName, string? managerRoleName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName.Trim());
        if (user == null)
        {
            return null;
        }

        var units = await _userManager.GetOrganizationUnitsAsync(user);
        if (units == null || units.Count == 0)
        {
            return null;
        }

        var role = string.IsNullOrWhiteSpace(managerRoleName) ? DefaultManagerRole : managerRoleName.Trim();

        // 直属部门 = Code 最长的那个（最具体）；本级找不到主管就沿父级往上找。
        OrganizationUnit? current = units.OrderByDescending(u => u.Code?.Length ?? 0).FirstOrDefault();
        var guard = 0;
        while (current != null && guard++ < 32)
        {
            var members = await _ouRepository.GetMembersAsync(current);
            foreach (var member in members)
            {
                var roles = await _userManager.GetRolesAsync(member);
                if (roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)))
                {
                    return member.UserName;
                }
            }

            current = current.ParentId.HasValue
                ? await _ouRepository.FindAsync(current.ParentId.Value)
                : null;
        }

        return null;
    }
}
