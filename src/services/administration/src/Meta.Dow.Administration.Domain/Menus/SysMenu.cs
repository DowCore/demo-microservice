using Meta.Dow.Administration.Menus;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.Administration.Menus;

public class SysMenu : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public Guid? ParentId { get; protected set; }

    public string Name { get; protected set; } = null!;

    public string Title { get; protected set; } = null!;

    public string? Path { get; protected set; }

    public string? Component { get; protected set; }

    public string? Redirect { get; protected set; }

    public MenuType Type { get; protected set; }

    public string? Permission { get; protected set; }

    public string? Icon { get; protected set; }

    public string SystemCode { get; protected set; } = MenuConsts.DefaultSystemCode;

    public int Order { get; protected set; }

    public bool IsVisible { get; protected set; } = true;

    public bool IsEnabled { get; protected set; } = true;

    public bool AffixTab { get; protected set; }

    public bool KeepAlive { get; protected set; }

    protected SysMenu() { }

    public SysMenu(
        Guid id,
        string name,
        string title,
        MenuType type,
        Guid? parentId = null,
        string? path = null,
        string? component = null,
        string? permission = null,
        string? icon = null,
        string systemCode = MenuConsts.DefaultSystemCode,
        int order = 0,
        Guid? tenantId = null
    )
        : base(id)
    {
        TenantId = tenantId;
        SetName(name);
        SetTitle(title);
        Type = type;
        ParentId = parentId;
        Path = Check.Length(path, nameof(path), MenuConsts.MaxPathLength);
        Component = Check.Length(component, nameof(component), MenuConsts.MaxComponentLength);
        Permission = Check.Length(permission, nameof(permission), MenuConsts.MaxPermissionLength);
        Icon = Check.Length(icon, nameof(icon), MenuConsts.MaxIconLength);
        SystemCode = Check.NotNullOrWhiteSpace(systemCode, nameof(systemCode), MenuConsts.MaxSystemCodeLength);
        Order = order;
    }

    public void SetName(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MenuConsts.MaxNameLength);
    }

    public void SetTitle(string title)
    {
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), MenuConsts.MaxTitleLength);
    }

    public void Update(
        string name,
        string title,
        MenuType type,
        Guid? parentId,
        string? path,
        string? component,
        string? redirect,
        string? permission,
        string? icon,
        string systemCode,
        int order,
        bool isVisible,
        bool isEnabled,
        bool affixTab,
        bool keepAlive
    )
    {
        SetName(name);
        SetTitle(title);
        Type = type;
        ParentId = parentId;
        Path = Check.Length(path, nameof(path), MenuConsts.MaxPathLength);
        Component = Check.Length(component, nameof(component), MenuConsts.MaxComponentLength);
        Redirect = Check.Length(redirect, nameof(redirect), MenuConsts.MaxRedirectLength);
        Permission = Check.Length(permission, nameof(permission), MenuConsts.MaxPermissionLength);
        Icon = Check.Length(icon, nameof(icon), MenuConsts.MaxIconLength);
        SystemCode = Check.NotNullOrWhiteSpace(systemCode, nameof(systemCode), MenuConsts.MaxSystemCodeLength);
        Order = order;
        IsVisible = isVisible;
        IsEnabled = isEnabled;
        AffixTab = affixTab;
        KeepAlive = keepAlive;
    }
}
