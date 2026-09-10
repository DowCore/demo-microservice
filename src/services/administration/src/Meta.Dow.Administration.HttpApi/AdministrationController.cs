using Meta.Dow.Administration.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Meta.Dow.Administration;

public abstract class AdministrationController : AbpControllerBase
{
    protected AdministrationController()
    {
        LocalizationResource = typeof(AdministrationResource);
    }
}
