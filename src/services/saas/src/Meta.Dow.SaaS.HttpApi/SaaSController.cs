using Meta.Dow.SaaS.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Meta.Dow.SaaS;

public abstract class SaaSController : AbpControllerBase
{
    protected SaaSController()
    {
        LocalizationResource = typeof(SaaSResource);
    }
}
