using System;
using System.Collections.Generic;
using Meta.Dow.Administration.Menus;
using Volo.Abp.Application.Dtos;

namespace Meta.Dow.Administration.Menus;

public class MenuDto : ExtensibleFullAuditedEntityDto<Guid>
{
    public Guid? ParentId { get; set; }

    public string Name { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Path { get; set; }

    public string? Component { get; set; }

    public string? Redirect { get; set; }

    public MenuType Type { get; set; }

    public string? Permission { get; set; }

    public string? Icon { get; set; }

    public string SystemCode { get; set; } = MenuConsts.DefaultSystemCode;

    public int Order { get; set; }

    public bool IsVisible { get; set; }

    public bool IsEnabled { get; set; }

    public bool AffixTab { get; set; }

    public bool KeepAlive { get; set; }

    public List<MenuDto> Children { get; set; } = [];
}
