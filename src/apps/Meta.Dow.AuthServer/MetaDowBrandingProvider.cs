using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace Meta.Dow;

[Dependency(ReplaceServices = true)]
public class MetaDowBrandingProvider : DefaultBrandingProvider
{
    public override string AppName => "Meta.Dow";
}
