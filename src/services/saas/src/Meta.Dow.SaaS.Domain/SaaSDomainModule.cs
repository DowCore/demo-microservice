using Volo.Abp.Domain;
using Volo.Abp.Modularity;
using Volo.Abp.TenantManagement;

namespace Meta.Dow.SaaS;

[DependsOn(typeof(AbpDddDomainModule))]
[DependsOn(typeof(SaaSDomainSharedModule))]
[DependsOn(typeof(AbpTenantManagementDomainModule))]
public class SaaSDomainModule : AbpModule { }
