using System;
using System.ComponentModel.DataAnnotations;
using Meta.Dow.Administration.Menus;

namespace Meta.Dow.Administration.Menus;

public class CreateUpdateMenuDto
{
    public Guid? ParentId { get; set; }

    [Required]
    [StringLength(MenuConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    [Required]
    [StringLength(MenuConsts.MaxTitleLength)]
    public string Title { get; set; } = null!;

    [StringLength(MenuConsts.MaxPathLength)]
    public string? Path { get; set; }

    [StringLength(MenuConsts.MaxComponentLength)]
    public string? Component { get; set; }

    [StringLength(MenuConsts.MaxRedirectLength)]
    public string? Redirect { get; set; }

    public MenuType Type { get; set; } = MenuType.Menu;

    [StringLength(MenuConsts.MaxPermissionLength)]
    public string? Permission { get; set; }

    [StringLength(MenuConsts.MaxIconLength)]
    public string? Icon { get; set; }

    [Required]
    [StringLength(MenuConsts.MaxSystemCodeLength)]
    public string SystemCode { get; set; } = MenuConsts.DefaultSystemCode;

    public int Order { get; set; }

    public bool IsVisible { get; set; } = true;

    public bool IsEnabled { get; set; } = true;

    public bool AffixTab { get; set; }

    public bool KeepAlive { get; set; }
}
