using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Meta.Dow.Administration.Menus;
using Meta.Dow.Administration.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.Administration.Menus;

[Authorize]
public class MenuAppService : AdministrationAppService, IMenuAppService
{
    private readonly IRepository<SysMenu, Guid> _menuRepository;
    private readonly IPermissionChecker _permissionChecker;

    public MenuAppService(IRepository<SysMenu, Guid> menuRepository, IPermissionChecker permissionChecker)
    {
        _menuRepository = menuRepository;
        _permissionChecker = permissionChecker;
    }

    private bool IsAdminUser() =>
        CurrentUser.Roles.Any(r => string.Equals(r, "admin", StringComparison.OrdinalIgnoreCase));

    [Authorize(AdministrationPermissions.Menus.Default)]
    public async Task<List<MenuDto>> GetListAsync(string? systemCode = null)
    {
        var query = await _menuRepository.GetQueryableAsync();
        if (!string.IsNullOrWhiteSpace(systemCode))
        {
            query = query.Where(x => x.SystemCode == systemCode);
        }

        var list = query.OrderBy(x => x.Order).ThenBy(x => x.Name).ToList();
        var dtos = ObjectMapper.Map<List<SysMenu>, List<MenuDto>>(list);
        return BuildTree(dtos, null);
    }

    [Authorize(AdministrationPermissions.Menus.Default)]
    public async Task<MenuDto> GetAsync(Guid id)
    {
        var entity = await _menuRepository.GetAsync(id);
        return ObjectMapper.Map<SysMenu, MenuDto>(entity);
    }

    [Authorize(AdministrationPermissions.Menus.Create)]
    public async Task<MenuDto> CreateAsync(CreateUpdateMenuDto input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var entity = new SysMenu(
            GuidGenerator.Create(),
            input.Name,
            input.Title,
            input.Type,
            input.ParentId,
            input.Path,
            input.Component,
            input.Permission,
            input.Icon,
            input.SystemCode,
            input.Order,
            CurrentTenant.Id
        );
        entity.Update(
            input.Name,
            input.Title,
            input.Type,
            input.ParentId,
            input.Path,
            input.Component,
            input.Redirect,
            input.Permission,
            input.Icon,
            input.SystemCode,
            input.Order,
            input.IsVisible,
            input.IsEnabled,
            input.AffixTab,
            input.KeepAlive
        );

        await _menuRepository.InsertAsync(entity, autoSave: true);
        return ObjectMapper.Map<SysMenu, MenuDto>(entity);
    }

    [Authorize(AdministrationPermissions.Menus.Update)]
    public async Task<MenuDto> UpdateAsync(Guid id, CreateUpdateMenuDto input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var entity = await _menuRepository.GetAsync(id);
        entity.Update(
            input.Name,
            input.Title,
            input.Type,
            input.ParentId,
            input.Path,
            input.Component,
            input.Redirect,
            input.Permission,
            input.Icon,
            input.SystemCode,
            input.Order,
            input.IsVisible,
            input.IsEnabled,
            input.AffixTab,
            input.KeepAlive
        );
        await _menuRepository.UpdateAsync(entity, autoSave: true);
        return ObjectMapper.Map<SysMenu, MenuDto>(entity);
    }

    [Authorize(AdministrationPermissions.Menus.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        var children = await _menuRepository.GetListAsync(x => x.ParentId == id);
        if (children.Count > 0)
        {
            throw new BusinessException("Administration:MenuHasChildren").WithData("Id", id);
        }

        await _menuRepository.DeleteAsync(id);
    }

    public async Task<List<VbenRouteDto>> GetCurrentUserRoutesAsync(string? systemCode = null)
    {
        systemCode ??= MenuConsts.DefaultSystemCode;
        var query = await _menuRepository.GetQueryableAsync();
        var menus = query
            .Where(x => x.SystemCode == systemCode && x.IsEnabled && x.Type != MenuType.Button)
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Name)
            .ToList();

        var isAdmin = IsAdminUser();
        var allowed = new List<SysMenu>();
        foreach (var menu in menus)
        {
            if (!menu.IsVisible && string.IsNullOrWhiteSpace(menu.Permission))
            {
                continue;
            }

            // admin 默认可见全部启用菜单（与数据权限 All、权限种子全量授予一致）
            if (
                isAdmin
                || string.IsNullOrWhiteSpace(menu.Permission)
                || await _permissionChecker.IsGrantedAsync(menu.Permission)
            )
            {
                allowed.Add(menu);
            }
        }

        // 保留有权限子节点所需的父目录
        var allowedIds = allowed.Select(x => x.Id).ToHashSet();
        var byId = menus.ToDictionary(x => x.Id);
        foreach (var menu in allowed.ToList())
        {
            var parentId = menu.ParentId;
            while (parentId.HasValue && byId.TryGetValue(parentId.Value, out var parent))
            {
                if (allowedIds.Add(parent.Id))
                {
                    allowed.Add(parent);
                }

                parentId = parent.ParentId;
            }
        }

        return BuildVbenRoutes(allowed.OrderBy(x => x.Order).ThenBy(x => x.Name).ToList(), null);
    }

    private static List<MenuDto> BuildTree(List<MenuDto> all, Guid? parentId)
    {
        return all.Where(x => x.ParentId == parentId)
            .Select(x =>
            {
                x.Children = BuildTree(all, x.Id);
                return x;
            })
            .ToList();
    }

    private static List<VbenRouteDto> BuildVbenRoutes(List<SysMenu> all, Guid? parentId)
    {
        var parents = all.Where(x => x.ParentId == parentId).ToList();
        return parents
            .Select(x =>
            {
                var children = BuildVbenRoutes(all, x.Id);
                var path = ToVbenPath(x, parentId.HasValue ? all.FirstOrDefault(p => p.Id == parentId) : null);
                var route = new VbenRouteDto
                {
                    Name = x.Name,
                    Path = path,
                    Component = string.IsNullOrWhiteSpace(x.Component) ? null : x.Component,
                    Redirect = string.IsNullOrWhiteSpace(x.Redirect)
                        ? (children.Count > 0 ? JoinParentChildPath(path, children[0].Path) : null)
                        : x.Redirect,
                    Meta = new VbenRouteMetaDto
                    {
                        Title = x.Title,
                        Icon = x.Icon,
                        Order = x.Order,
                        AffixTab = x.AffixTab ? true : null,
                        KeepAlive = x.KeepAlive ? true : null,
                        HideInMenu = x.IsVisible ? null : true,
                        Authority = string.IsNullOrWhiteSpace(x.Permission) ? null : [x.Permission],
                    },
                    Children = children.Count > 0 ? children : null,
                };
                return route;
            })
            .ToList();
    }

    /// <summary>
    /// Vben backend 约定：顶级绝对路径，子级相对路径（如 workspace），否则侧栏/路由嵌套会异常。
    /// </summary>
    private static string ToVbenPath(SysMenu menu, SysMenu? parent)
    {
        var raw = string.IsNullOrWhiteSpace(menu.Path)
            ? menu.Name.ToLowerInvariant()
            : menu.Path.Trim();

        if (parent == null)
        {
            return raw.StartsWith('/') ? raw : "/" + raw;
        }

        var parentPath = (parent.Path ?? string.Empty).TrimEnd('/');
        if (
            !string.IsNullOrEmpty(parentPath)
            && raw.StartsWith(parentPath + "/", StringComparison.OrdinalIgnoreCase)
        )
        {
            return raw[(parentPath.Length + 1)..];
        }

        if (raw.StartsWith('/'))
        {
            var segments = raw.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 0 ? segments[^1] : menu.Name.ToLowerInvariant();
        }

        return raw;
    }

    private static string JoinParentChildPath(string parentPath, string childPath)
    {
        if (childPath.StartsWith('/'))
        {
            return childPath;
        }

        return $"{parentPath.TrimEnd('/')}/{childPath.TrimStart('/')}";
    }
}
