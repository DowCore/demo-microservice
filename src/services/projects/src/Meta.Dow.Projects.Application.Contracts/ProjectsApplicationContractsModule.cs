using Volo.Abp.Application;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;

namespace Meta.Dow.Projects;

[DependsOn(typeof(ProjectsDomainSharedModule))]
[DependsOn(typeof(AbpDddApplicationContractsModule))]
[DependsOn(typeof(AbpAuthorizationModule))]
public class ProjectsApplicationContractsModule : AbpModule { }
