using Meta.Dow.Administration.DataPermission;
using Meta.Dow.Administration.Menus;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using Volo.Abp.Data;
using Volo.Abp.MongoDB;

namespace Meta.Dow.Administration.MongoDB;

[ConnectionStringName(MetaDowNames.AdministrationDb)]
public class AdministrationDbContext : AbpMongoDbContext
{
    public IMongoCollection<SysMenu> Menus => Collection<SysMenu>();

    public IMongoCollection<RoleDataScope> RoleDataScopes => Collection<RoleDataScope>();
}
