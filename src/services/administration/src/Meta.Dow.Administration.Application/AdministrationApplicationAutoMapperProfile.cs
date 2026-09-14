using AutoMapper;
using Meta.Dow.Administration.DataPermission;
using Meta.Dow.Administration.Menus;

namespace Meta.Dow.Administration;

public class AdministrationApplicationAutoMapperProfile : Profile
{
    public AdministrationApplicationAutoMapperProfile()
    {
        CreateMap<SysMenu, MenuDto>().ForMember(d => d.Children, opt => opt.Ignore());

        CreateMap<RoleDataScope, RoleDataScopeDto>();
    }
}
