using Meta.Dow.IdentityService.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Meta.Dow.IdentityService;

public abstract class IdentityServiceController : AbpControllerBase
{
    protected IdentityServiceController()
    {
        LocalizationResource = typeof(IdentityServiceResource);
    }
}
