using Meta.Dow.Administration.Localization;
using Volo.Abp.Application.Services;

namespace Meta.Dow.Administration;

public abstract class AdministrationAppService : ApplicationService
{
    protected AdministrationAppService()
    {
        LocalizationResource = typeof(AdministrationResource);
        ObjectMapperContext = typeof(AdministrationApplicationModule);
    }
}
