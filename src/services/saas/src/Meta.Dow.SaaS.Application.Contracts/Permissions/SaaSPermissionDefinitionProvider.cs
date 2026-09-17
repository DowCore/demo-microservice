using System;
using Meta.Dow.SaaS.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Meta.Dow.SaaS.Permissions;

public class SaaSPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var saasGroup = context.AddGroup(SaaSPermissions.GroupName, L("Permission:SaaS"));

        var tenantsPermission = saasGroup.AddPermission(SaaSPermissions.Tenants.Default, L("Permission:SaaS:Tenants"));
        tenantsPermission.AddChild(SaaSPermissions.Tenants.Create, L("Permission:SaaS:Tenants.Create"));
        tenantsPermission.AddChild(SaaSPermissions.Tenants.Update, L("Permission:SaaS:Tenants.Update"));
        tenantsPermission.AddChild(SaaSPermissions.Tenants.Delete, L("Permission:SaaS:Tenants.Delete"));

        var orchestrationGroup = context.AddGroup(OrchestrationPermissions.GroupName, L("Permission:Orchestration"));

        var definitions = orchestrationGroup.AddPermission(
            OrchestrationPermissions.Definitions.Default,
            L("Permission:Orchestration:Definitions")
        );
        definitions.AddChild(OrchestrationPermissions.Definitions.Create, L("Permission:Orchestration:Definitions.Create"));
        definitions.AddChild(OrchestrationPermissions.Definitions.Update, L("Permission:Orchestration:Definitions.Update"));
        definitions.AddChild(OrchestrationPermissions.Definitions.Delete, L("Permission:Orchestration:Definitions.Delete"));
        definitions.AddChild(OrchestrationPermissions.Definitions.Publish, L("Permission:Orchestration:Definitions.Publish"));

        var instances = orchestrationGroup.AddPermission(
            OrchestrationPermissions.Instances.Default,
            L("Permission:Orchestration:Instances")
        );
        instances.AddChild(OrchestrationPermissions.Instances.Run, L("Permission:Orchestration:Instances.Run"));
        instances.AddChild(OrchestrationPermissions.Instances.Cancel, L("Permission:Orchestration:Instances.Cancel"));

        var dataSources = orchestrationGroup.AddPermission(
            OrchestrationPermissions.DataSources.Default,
            L("Permission:Orchestration:DataSources")
        );
        dataSources.AddChild(OrchestrationPermissions.DataSources.Create, L("Permission:Orchestration:DataSources.Create"));
        dataSources.AddChild(OrchestrationPermissions.DataSources.Update, L("Permission:Orchestration:DataSources.Update"));
        dataSources.AddChild(OrchestrationPermissions.DataSources.Delete, L("Permission:Orchestration:DataSources.Delete"));

        orchestrationGroup.AddPermission(
            OrchestrationPermissions.Sql.Write,
            L("Permission:Orchestration:Sql.Write")
        );
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<SaaSResource>(name);
    }
}
