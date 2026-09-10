using Meta.Dow.SaaS.Localization;
using Volo.Abp.Application.Services;

namespace Meta.Dow.SaaS;

public abstract class SaaSAppService : ApplicationService
{
    protected SaaSAppService()
    {
        LocalizationResource = typeof(SaaSResource);
        ObjectMapperContext = typeof(SaaSApplicationModule);
    }
}
