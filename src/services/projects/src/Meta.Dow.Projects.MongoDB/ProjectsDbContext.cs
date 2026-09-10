using Microsoft.Extensions.Hosting;
using Volo.Abp.Data;
using Volo.Abp.MongoDB;

namespace Meta.Dow.Projects.MongoDB;

[ConnectionStringName(MetaDowNames.ProjectsDb)]
public class ProjectsDbContext : AbpMongoDbContext, IProjectsDbContext
{
}
