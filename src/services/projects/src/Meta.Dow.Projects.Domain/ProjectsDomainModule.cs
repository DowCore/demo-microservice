using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Meta.Dow.Projects;

[DependsOn(typeof(AbpDddDomainModule))]
[DependsOn(typeof(ProjectsDomainSharedModule))]
[DependsOn(typeof(MetaDowSharedModule))]
public class ProjectsDomainModule : AbpModule { }
